using Asp.Versioning;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Linq;

namespace EventTicketing.Api.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/auth")]
    public class AuthController : ControllerBase
    {
        private readonly IIdentityService _identityService;

        public AuthController(IIdentityService identityService)
        {
            _identityService = identityService;
        }

        [HttpPost("register")]
        [EnableRateLimiting("register")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            var result = await _identityService.RegisterAsync(request);
            if (!result.Succeeded)
            {
                return BadRequest(new
                {
                    code = "registration_failed",
                    message = "Registration failed.",
                    details = result.Errors
                });
            }

            return Created();
        }

        [HttpPost("login")]
        [EnableRateLimiting("login")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login(LoginRequest request)
        {
            var result = await _identityService.LoginAsync(request);
            if (!result.Succeeded)
            {
                return Unauthorized(new
                {
                    code = "login_failed",
                    message = "Login failed.",
                    details = result.Errors
                });
            }

            return Ok(new
            {
                token = result.Token,
                refreshToken = result.RefreshToken,
                expiresIn = result.ExpiresIn
            });
        }

        [HttpPost("refresh")]
        [EnableRateLimiting("refresh")]
        [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Refresh(RefreshTokenRequest request)
        {
            var result = await _identityService.RefreshAsync(request);
            if (!result.Succeeded)
            {
                return Unauthorized(new
                {
                    code = result.ErrorCode ?? "refresh_failed",
                    message = result.Errors.FirstOrDefault() ?? "Session expired. Please log in again.",
                    details = (string?)null
                });
            }

            return Ok(new { token = result.Token, expiresIn = result.ExpiresIn });
        }

        [HttpPost("logout")]
        [EnableRateLimiting("refresh")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Logout(RefreshTokenRequest request)
        {
            await _identityService.LogoutAsync(request);
            return NoContent();
        }
    }
}
