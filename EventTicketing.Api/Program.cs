using Amazon.Runtime;
using Amazon.S3;
using Asp.Versioning;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using EventTicketing.Api.Extensions;
using EventTicketing.Application.Common;
using EventTicketing.Application.Interfaces;
using EventTicketing.Application.Mapping;
using StackExchange.Redis;
using EventTicketing.Infrastructure.Identity;
using EventTicketing.Infrastructure.Jobs;
using EventTicketing.Infrastructure.Persistence;
using EventTicketing.Infrastructure.Seed;
using EventTicketing.Infrastructure.Services;
using EventTicketing.Infrastructure.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Jwt:Key is missing or too short. Run: dotnet user-secrets set \"Jwt:Key\" <value> (32+ characters).");

var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];
if (string.IsNullOrWhiteSpace(jwtIssuer) || string.IsNullOrWhiteSpace(jwtAudience))
    throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be set in appsettings.json.");

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("ConnectionStrings:DefaultConnection is missing.");

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
if (corsOrigins.Length == 0)
    throw new InvalidOperationException("Cors:AllowedOrigins must contain at least one frontend origin.");

var r2Endpoint = builder.Configuration["R2:Endpoint"];
var r2BucketName = builder.Configuration["R2:BucketName"];
var r2PublicBaseUrl = builder.Configuration["R2:PublicBaseUrl"];
var r2AccessKeyId = builder.Configuration["R2:AccessKeyId"];
var r2SecretAccessKey = builder.Configuration["R2:SecretAccessKey"];
if (string.IsNullOrWhiteSpace(r2Endpoint) || string.IsNullOrWhiteSpace(r2BucketName)
    || string.IsNullOrWhiteSpace(r2PublicBaseUrl) || string.IsNullOrWhiteSpace(r2AccessKeyId)
    || string.IsNullOrWhiteSpace(r2SecretAccessKey))
    throw new InvalidOperationException(
        "R2 settings are incomplete. Endpoint, BucketName and PublicBaseUrl belong in appsettings.json; " +
        "AccessKeyId and SecretAccessKey belong in user secrets (dotnet user-secrets set \"R2:AccessKeyId\" ...).");

var maxImageBytes = builder.Configuration.GetValue<long>("R2:MaxImageBytes");
if (maxImageBytes <= 0)
    throw new InvalidOperationException("R2:MaxImageBytes must be greater than zero.");

// --- Services ---

builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// The framework cap sits slightly above our own limit so our validation
// returns a friendly 400 instead of the form reader failing first.
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxImageBytes + 64 * 1024;
});

// One error shape everywhere: { code, message, details }.
// Even automatic model validation answers in it, not in ProblemDetails.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(entry => entry.Value?.Errors.Count > 0)
            .SelectMany(entry => entry.Value!.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                ? "A value is invalid."
                : error.ErrorMessage)
            .ToList();

        return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(new
        {
            code = "validation_failed",
            message = "One or more fields are invalid.",
            details = errors
        });
    };
});

builder.Services.AddOpenApi();

builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1.0);
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc();

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services
    .AddIdentity<EventTicketingUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = false;

        options.Password.RequiredLength = 8;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;

        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
        options.Lockout.AllowedForNewUsers = true;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = true;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal is null || !principal.IsInRole("Organizer"))
                {
                    return;
                }

                var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                    ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
                if (!Guid.TryParse(userIdValue, out var userId))
                {
                    context.Fail("Invalid user identifier.");
                    return;
                }

                var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
                var suspended = await db.Users
                    .AsNoTracking()
                    .AnyAsync(u => u.Id == userId && u.IsSuspended, context.HttpContext.RequestAborted);
                if (suspended)
                {
                    context.Fail("This account has been suspended.");
                }
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<IIdentityService, IdentityService>();

// Slice 2: events, ticket types, images in R2.
builder.Services.Configure<R2Options>(builder.Configuration.GetSection(R2Options.SectionName));

// Slice 4: Redis cache
var redisConfig = builder.Configuration["Redis:Configuration"];
if (string.IsNullOrWhiteSpace(redisConfig))
{
    throw new InvalidOperationException("Redis:Configuration is missing. Set via user secrets (dotnet user-secrets set \"Redis:Configuration\" ...).");
}
builder.Services.Configure<EventTicketing.Infrastructure.Caching.RedisOptions>(builder.Configuration.GetSection(EventTicketing.Infrastructure.Caching.RedisOptions.SectionName));
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EventTicketing.Infrastructure.Caching.RedisOptions>>().Value;
    var config = ConfigurationOptions.Parse(string.IsNullOrWhiteSpace(options.Configuration) ? redisConfig : options.Configuration);
    config.AbortOnConnectFail = false;
    return ConnectionMultiplexer.Connect(config);
});
builder.Services.AddSingleton<EventTicketing.Application.Interfaces.ICacheService, EventTicketing.Infrastructure.Caching.RedisCacheService>();

builder.Services.AddSingleton<IAmazonS3>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<R2Options>>().Value;

    // R2 speaks the S3 protocol: point the SDK at Cloudflare's endpoint.
    var s3Config = new AmazonS3Config
    {
        ServiceURL = options.Endpoint,
        ForcePathStyle = true,
        AuthenticationRegion = "auto",
        // SDK v4 defaults add an aws-chunked CRC32 trailer that R2 rejects.
        RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
        ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
    };

    var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
    return new AmazonS3Client(credentials, s3Config);
});

builder.Services.AddSingleton<IImageStorage, R2ImageStorage>();
builder.Services.AddSingleton<EventMapper>();
builder.Services.AddScoped<IOrganizerEventService, OrganizerEventService>();
builder.Services.AddScoped<IAdminEventService, AdminEventService>();
builder.Services.AddScoped<IAdminOrganizerService, AdminOrganizerService>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();
builder.Services.AddScoped<IEventQueryService, EventTicketing.Infrastructure.Services.EventQueryService>();
builder.Services.AddScoped<IStockQueryService, EventTicketing.Infrastructure.Services.StockQueryService>();
builder.Services.AddScoped<IWalletService, EventTicketing.Infrastructure.Services.WalletService>();
builder.Services.AddScoped<IReservationService, EventTicketing.Infrastructure.Services.ReservationService>();
builder.Services.AddScoped<ITicketService, EventTicketing.Infrastructure.Services.TicketService>();
builder.Services.AddScoped<IDataRetentionService, DataRetentionService>();
builder.Services.AddHostedService<ReservationExpiryJob>();
builder.Services.AddHostedService<DataRetentionJob>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            code = "too_many_requests",
            message = "Too many requests. Please slow down and try again shortly.",
            details = (string?)null
        }, cancellationToken);
    };

    options.AddPolicy("login", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("register", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 3,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("refresh", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("public", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(ip, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("reserve", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(userId, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("wallet", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(userId, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("pay", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(userId, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("checkin", context =>
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? context.User.FindFirstValue("sub") ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(userId, _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

// --- Pipeline ---

// Must run first: Render terminates TLS at its proxy, so without this
// every request looks like it came from the proxy's own IP address.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
// Trust Render's proxy. If we listed nothing, the middleware would ignore
// the forwarded headers entirely and keep the proxy IP.
forwardedHeadersOptions.KnownIPNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

// One error shape for anything unhandled: { code, message, details }.
// The full exception is logged; the caller never sees a stack trace.
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GlobalExceptionHandler");
        logger.LogError(exception, "Unhandled exception: {Message}", exception?.Message);

        // Known exception types carry the right status code and a safe message.
        int statusCode;
        string code;
        string message;
        object? details;

        if (exception is NotFoundException notFound)
        {
            statusCode = StatusCodes.Status404NotFound;
            code = "not_found";
            message = notFound.Message;
            details = null;
        }
        else if (exception is ForbiddenException forbidden)
        {
            statusCode = StatusCodes.Status403Forbidden;
            code = "forbidden";
            message = forbidden.Message;
            details = null;
        }
        else if (exception is ConflictException conflict)
        {
            statusCode = StatusCodes.Status409Conflict;
            code = "conflict";
            message = conflict.Message;
            details = null;
        }
        else if (exception is GoneException gone)
        {
            statusCode = StatusCodes.Status410Gone;
            code = "reservation_expired";
            message = gone.Message;
            details = null;
        }
        else if (exception is IdempotencyKeyExpiredException expiredKey)
        {
            statusCode = StatusCodes.Status409Conflict;
            code = "idempotency_key_expired";
            message = expiredKey.Message;
            details = null;
        }
        else if (exception is RequestValidationException validation)
        {
            statusCode = StatusCodes.Status400BadRequest;
            code = "validation_failed";
            message = validation.Message;
            details = validation.Errors;
        }
        else
        {
            statusCode = StatusCodes.Status500InternalServerError;
            code = "internal_error";
            message = "Something went wrong. Please try again.";
            details = app.Environment.IsDevelopment() ? exception?.Message : null;
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new { code, message, details });
    });
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseCors("Frontend");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Role rows and the seeded admin must exist before anyone registers.
using (var scope = app.Services.CreateScope())
{
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);

    try
    {
        await DemoDataSeeder.SeedAsync(scope.ServiceProvider);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Demo data seeding failed.");
    }
}

app.Run();

public partial class Program { }
