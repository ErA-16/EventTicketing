namespace EventTicketing.Infrastructure.Caching
{
    public class RedisOptions
    {
        public const string SectionName = "Redis";

        public string Configuration { get; set; } = null!;

        public string InstanceName { get; set; } = "eventticketing:";

        public int TtlSeconds { get; set; } = 120;
    }
}
