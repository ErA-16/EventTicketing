namespace EventTicketing.Application.DTOs
{
    public class AdminUserQuery
    {
        public string? Cursor { get; set; }
        public int Limit { get; set; } = 20;
        public string? Q { get; set; }
        public string? Role { get; set; }
    }
}
