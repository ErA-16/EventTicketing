namespace EventTicketing.Infrastructure.Storage
{
    public class R2Options
    {
        public const string SectionName = "R2";

        public string Endpoint { get; set; } = null!;
        public string AccessKeyId { get; set; } = null!;
        public string SecretAccessKey { get; set; } = null!;
        public string BucketName { get; set; } = null!;
        public string PublicBaseUrl { get; set; } = null!;

        // Upload limit for event cover images. Lives here so all image storage
        // settings stay in one place.
        public long MaxImageBytes { get; set; } = 2_621_440;
    }
}
