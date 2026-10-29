using System;
using System.Collections.Generic;

namespace EventTicketing.Application.DTOs
{
    public class AuthResponse
    {
        public bool Succeeded { get; init; }
        public string? Token { get; init; }
        public string? RefreshToken { get; init; }
        public int ExpiresIn { get; init; }
        public string? ErrorCode { get; init; }
        public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

        public static AuthResponse Success(string? token = null, string? refreshToken = null, int expiresIn = 0) =>
            new() { Succeeded = true, Token = token, RefreshToken = refreshToken, ExpiresIn = expiresIn };

        public static AuthResponse Failure(params string[] errors) =>
            new() { Succeeded = false, Errors = errors };

        public static AuthResponse FailureWithCode(string code, params string[] errors) =>
            new() { Succeeded = false, ErrorCode = code, Errors = errors };
    }
}
