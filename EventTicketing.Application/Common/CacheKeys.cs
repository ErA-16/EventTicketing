namespace EventTicketing.Application.Common
{
    public static class CacheKeys
    {
        public const string ListPrefix = "events:list:";
        public const string DetailPrefix = "events:detail:";

        public static string EventList(string hash)
        {
            return ListPrefix + hash;
        }

        public static string EventDetail(string id)
        {
            return DetailPrefix + id;
        }

        public static string EventDetail(System.Guid id)
        {
            return DetailPrefix + id.ToString();
        }
    }
}
