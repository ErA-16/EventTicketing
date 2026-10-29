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

        private static string GenerateRefreshToken()
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        }

        private static string HashRefreshToken(string token)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        }

        private async Task<string> IssueRefreshTokenAsync(Guid userId)
        {
            var token = GenerateRefreshToken();
            var now = DateTime.UtcNow;

            _db.RefreshTokens.Add(new RefreshToken
            {
                UserId = userId,
                TokenHash = HashRefreshToken(token),
                CreatedAt = now,
                ExpiresAt = now.AddMinutes(GetRefreshTokenIdleMinutes())
            });

            await _db.SaveChangesAsync();
            return token;
        }

        private async Task<string> GenerateTokenAsync(EventTicketingUser user)
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

            var expiryMinutes = GetAccessTokenExpiryMinutes();
            var now = DateTime.UtcNow;
            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"],
                audience: _configuration["Jwt:Audience"],
                claims: claims,
                notBefore: now,
                expires: now.AddMinutes(expiryMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
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

            var token = await GenerateTokenAsync(user);
            var refreshToken = await IssueRefreshTokenAsync(user.Id);
            return AuthResponse.Success(token, refreshToken, GetAccessTokenExpiryMinutes() * 60);
        }

        public async Task<AuthResponse> RefreshAsync(RefreshTokenRequest request)
        {
            var now = DateTime.UtcNow;
            var tokenHash = HashRefreshToken(request.RefreshToken);
            var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
            {
                return AuthResponse.FailureWithCode("refresh_failed", "Session expired. Please log in again.");
            }

            var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
            if (user is null)
            {
                return AuthResponse.FailureWithCode("refresh_failed", "Session expired. Please log in again.");
            }

            if (user.IsSuspended)
            {
                stored.RevokedAt = now;
                await _db.SaveChangesAsync();
                return AuthResponse.FailureWithCode("account_suspended", "This account has been suspended.");
            }

            stored.ExpiresAt = now.AddMinutes(GetRefreshTokenIdleMinutes());
            await _db.SaveChangesAsync();

            var token = await GenerateTokenAsync(user);
            return AuthResponse.Success(token, null, GetAccessTokenExpiryMinutes() * 60);
        }

        public async Task LogoutAsync(RefreshTokenRequest request)
        {
            var tokenHash = HashRefreshToken(request.RefreshToken);
            var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

            if (stored is null || stored.RevokedAt is not null)
            {
                return;
            }

            stored.RevokedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
    }
}
