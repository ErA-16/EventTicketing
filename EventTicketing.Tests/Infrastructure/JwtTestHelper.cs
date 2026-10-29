using System;
using System.Text;
using System.Text.Json;

namespace EventTicketing.Tests.Infrastructure
{
    public static class JwtTestHelper
    {
        private static byte[] DecodePayload(string token)
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                throw new InvalidOperationException("Token is not a JWT.");
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            return Convert.FromBase64String(payload);
        }

        public static string GetUserId(string token)
        {
            using var document = JsonDocument.Parse(DecodePayload(token));
            return document.RootElement.GetProperty("sub").GetString()
                ?? throw new InvalidOperationException("Token has no subject.");
        }

        public static JsonDocument ReadPayload(string token)
        {
            return JsonDocument.Parse(DecodePayload(token));
        }

        public static string GetClaim(string token, string claim)
        {
            using var document = ReadPayload(token);
            return document.RootElement.GetProperty(claim).GetString() ?? string.Empty;
        }

        public static string DecodeText(string token)
        {
            return Encoding.UTF8.GetString(DecodePayload(token));
        }
    }
}
