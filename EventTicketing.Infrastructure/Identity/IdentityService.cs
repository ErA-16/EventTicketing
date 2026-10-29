using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace EventTicketing.Infrastructure.Identity
{
    public class IdentityService : IIdentityService
    {
        private readonly UserManager<EventTicketingUser> _userManager;
        private readonly SignInManager<EventTicketingUser> _signInManager;
        private readonly IConfiguration _configuration;
        private readonly AppDbContext _db;
        private readonly ILogger<IdentityService> _logger;

        public IdentityService(
            UserManager<EventTicketingUser> userManager,
            SignInManager<EventTicketingUser> signInManager,
            IConfiguration configuration,
            AppDbContext db,
            ILogger<IdentityService> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _configuration = configuration;
            _db = db;
            _logger = logger;
        }

        private static string GetDisplayName(EventTicketingUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.OrganizationName))
            {
                return user.OrganizationName.Trim();
            }

            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            return string.IsNullOrWhiteSpace(fullName) ? (user.Email ?? string.Empty) : fullName;
        }

        private int GetAccessTokenExpiryMinutes()
        {
            if (!int.TryParse(_configuration["Jwt:ExpiryMinutes"], out var expiryMinutes) || expiryMinutes <= 0)
            {
                expiryMinutes = 60;
            }

            return expiryMinutes;
        }

        private int GetRefreshTokenIdleMinutes()
        {
            if (!int.TryParse(_configuration["RefreshToken:IdleMinutes"], out var idleMinutes) || idleMinutes <= 0)
            {
                idleMinutes = 480;
            }

            return idleMinutes;
        }

        private int GetRefreshTokenAbsoluteMinutes()
        {
            if (!int.TryParse(_configuration["RefreshToken:AbsoluteMinutes"], out var absoluteMinutes) || absoluteMinutes <= 0)
            {
                absoluteMinutes = 10080;
            }

            return absoluteMinutes;
        }

        private int GetRotationGraceSeconds()
        {
            if (!int.TryParse(_configuration["RefreshToken:RotationGraceSeconds"], out var graceSeconds) || graceSeconds < 0)
            {
                graceSeconds = 10;
            }

            return graceSeconds;
        }

        private static string GenerateRefreshToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        private static string HashRefreshToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private (RefreshToken Entity, string RawToken) CreateRefreshToken(
            Guid userId,
            Guid familyId,
            DateTime absoluteExpiresAt,
            int idleMinutes,
            DateTime now)
        {
            var rawToken = GenerateRefreshToken();
            var expiresAt = now.AddMinutes(idleMinutes);
            if (absoluteExpiresAt < expiresAt)
            {
                expiresAt = absoluteExpiresAt;
            }

            return (new RefreshToken
            {
                UserId = userId,
                FamilyId = familyId,
                TokenHash = HashRefreshToken(rawToken),
                CreatedAt = now,
                ExpiresAt = expiresAt,
                AbsoluteExpiresAt = absoluteExpiresAt
            }, rawToken);
        }

        private async Task RevokeFamilyAsync(Guid familyId, DateTime now)
        {
            await _db.RefreshTokens
                .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAt, now));
        }

        private async Task<(string Token, int ExpiresIn)> GenerateTokenAsync(
            EventTicketingUser user,
            DateTime sessionExpiresAt)
        {
            var roles = await _userManager.GetRolesAsync(user);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.Name, GetDisplayName(user))
            };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

            var keyText = _configuration["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is not configured.");
            var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
            var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

            var now = DateTime.UtcNow;
            var accessExpiresAt = now.AddMinutes(GetAccessTokenExpiryMinutes());
            if (sessionExpiresAt < accessExpiresAt)
            {
                accessExpiresAt = sessionExpiresAt;
            }

            var expiresIn = (int)Math.Ceiling((accessExpiresAt - now).TotalSeconds);
            if (expiresIn < 1)
            {
                expiresIn = 1;
            }

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                notBefore: now,
                expires: accessExpiresAt,
                signingCredentials: credentials);

            return (new JwtSecurityTokenHandler().WriteToken(token), expiresIn);
        }

        public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
        {
            var firstName = request.FirstName?.Trim() ?? string.Empty;
            var lastName = request.LastName?.Trim() ?? string.Empty;
            var organizationName = request.OrganizationName?.Trim();

            if (request.IsOrganizer && string.IsNullOrWhiteSpace(organizationName))
            {
                return AuthResponse.Failure("OrganizationName is required for organizer accounts.");
            }

            if (!request.IsOrganizer && (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)))
            {
                return AuthResponse.Failure("FirstName and LastName are required for attendee accounts.");
            }

            if (await _userManager.FindByEmailAsync(request.Email) != null)
            {
                return AuthResponse.Failure("An account with this email already exists.");
            }

            var user = new EventTicketingUser
            {
                FirstName = request.IsOrganizer ? string.Empty : firstName,
                LastName = request.IsOrganizer ? string.Empty : lastName,
                OrganizationName = request.IsOrganizer ? organizationName : null,
                UserName = request.Email,
                Email = request.Email
            };

            var createResult = await _userManager.CreateAsync(user, request.Password);
            if (!createResult.Succeeded)
            {
                return AuthResponse.Failure(createResult.Errors.Select(e => e.Description).ToArray());
            }

            var roleName = request.IsOrganizer ? Role.Organizer.ToString() : Role.Attendee.ToString();

            var roleResult = await _userManager.AddToRoleAsync(user, roleName);
            if (!roleResult.Succeeded)
            {
                await _userManager.DeleteAsync(user);
                return AuthResponse.Failure(roleResult.Errors.Select(e => e.Description).ToArray());
            }

            try
            {
                if (!await _db.Wallets.AnyAsync(w => w.UserId == user.Id))
                {
                    _db.Wallets.Add(new Wallet
                    {
                        Id = Guid.NewGuid(),
                        UserId = user.Id,
                        Balance = 0m
                    });
                    await _db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create wallet for user {UserId} during registration.", user.Id);
            }

            return AuthResponse.Success();
        }

        public async Task<AuthResponse> LoginAsync(LoginRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user == null)
            {
                return AuthResponse.Failure("Invalid email or password.");
            }

            var signInResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (signInResult.IsLockedOut)
            {
                return AuthResponse.Failure("Account is locked out. Try again later.");
            }
            if (!signInResult.Succeeded)
            {
                return AuthResponse.Failure("Invalid email or password.");
            }

            if (user.IsSuspended)
            {
                return AuthResponse.Failure("This account has been suspended.");
            }

            var now = DateTime.UtcNow;
            var absoluteExpiresAt = now.AddMinutes(GetRefreshTokenAbsoluteMinutes());
            var refresh = CreateRefreshToken(user.Id, Guid.NewGuid(), absoluteExpiresAt, GetRefreshTokenIdleMinutes(), now);
            _db.RefreshTokens.Add(refresh.Entity);

            var access = await GenerateTokenAsync(user, absoluteExpiresAt);
            await _db.SaveChangesAsync();

            return AuthResponse.Success(access.Token, refresh.RawToken, access.ExpiresIn);
        }

        public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request)
        {
            var now = DateTime.UtcNow;
            var tokenHash = HashRefreshToken(request.RefreshToken);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            var stored = await _db.RefreshTokens
                .FromSqlInterpolated($"SELECT * FROM \"RefreshTokens\" WHERE \"TokenHash\" = {tokenHash} FOR UPDATE")
                .FirstOrDefaultAsync();

            now = DateTime.UtcNow;

            if (stored is null)
            {
                return AuthResponse.FailureWithCode("refresh_failed", "Session expired. Please log in again.");
            }

            if (stored.ExpiresAt <= now || stored.AbsoluteExpiresAt <= now)
            {
                await RevokeFamilyAsync(stored.FamilyId, now);
                await transaction.CommitAsync();
                return AuthResponse.FailureWithCode("refresh_failed", "Session expired. Please log in again.");
            }

            if (stored.RevokedAt is not null)
            {
                var grace = TimeSpan.FromSeconds(GetRotationGraceSeconds());
                var withinGrace = stored.ReplacedByTokenHash is not null
                    && stored.RevokedAt.Value > now.Subtract(grace);

                if (!withinGrace)
                {
                    await RevokeFamilyAsync(stored.FamilyId, now);
                    await transaction.CommitAsync();
                    return AuthResponse.FailureWithCode("refresh_failed", "Session expired. Please log in again.");
                }
            }

            var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
            if (user is null)
            {
                await RevokeFamilyAsync(stored.FamilyId, now);
                await transaction.CommitAsync();
                return AuthResponse.FailureWithCode("refresh_failed", "Session expired. Please log in again.");
            }

            if (user.IsSuspended)
            {
                await RevokeFamilyAsync(stored.FamilyId, now);
                await transaction.CommitAsync();
                return AuthResponse.FailureWithCode("account_suspended", "This account has been suspended.");
            }

            var refresh = CreateRefreshToken(
                stored.UserId,
                stored.FamilyId,
                stored.AbsoluteExpiresAt,
                GetRefreshTokenIdleMinutes(),
                now);

            stored.RevokedAt ??= now;
            stored.ReplacedByTokenHash = refresh.Entity.TokenHash;
            _db.RefreshTokens.Add(refresh.Entity);

            var access = await GenerateTokenAsync(user, stored.AbsoluteExpiresAt);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return AuthResponse.Success(access.Token, refresh.RawToken, access.ExpiresIn);
        }

        public async Task LogoutAsync(RefreshTokenRequest request)
        {
            var tokenHash = HashRefreshToken(request.RefreshToken);
            var stored = await _db.RefreshTokens
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (stored is null)
            {
                return;
            }

            await RevokeFamilyAsync(stored.FamilyId, DateTime.UtcNow);
        }
    }
}
