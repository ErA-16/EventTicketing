using EventTicketing.Application.DTOs;

namespace EventTicketing.Application.Interfaces
{
    public interface IIdentityService
    {
        Task<AuthResponse> RegisterAsync(RegisterRequest request);
        Task<AuthResponse> LoginAsync(LoginRequest request);
        Task<AuthResponse> RefreshAsync(RefreshTokenRequest request);
        Task LogoutAsync(RefreshTokenRequest request);
    }
}
