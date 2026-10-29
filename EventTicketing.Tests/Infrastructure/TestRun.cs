using System;
using System.Threading;

namespace EventTicketing.Tests.Infrastructure
{
    public static class TestRun
    {
        private static int _emailCounter;
        private static int _ipCounter;

        public const string Password = "TestPass123!";

        public static string Id { get; } = Guid.NewGuid().ToString("N")[..10];

        public static string Prefix => $"test-{Id}-";

        public static string AdminEmail => $"seed-admin-{Id}@example.invalid";

        public static string NewEmail(string label)
        {
            var number = Interlocked.Increment(ref _emailCounter);
            return $"{Prefix}{label}-{number}@example.invalid";
        }

        public static string NewTitle(string label)
        {
            var number = Interlocked.Increment(ref _emailCounter);
            return $"{Prefix}{label}-{number}";
        }

        public static string NextIp()
        {
            var number = Interlocked.Increment(ref _ipCounter);
            return $"10.{(number >> 16) & 255}.{(number >> 8) & 255}.{number & 255}";
        }
    }
}
