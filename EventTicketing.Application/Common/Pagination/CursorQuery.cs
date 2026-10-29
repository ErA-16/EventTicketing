namespace EventTicketing.Application.Common.Pagination
{
    public class CursorQuery
    {
        public string? Cursor { get; set; }
        public int Limit { get; set; } = 20;
        public string? Q { get; set; }
        public string? Status { get; set; }
    }
}
