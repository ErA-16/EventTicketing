namespace EventTicketing.Application.DTOs
{
    public class AdminOrganizerQuery
    {
        public string? Cursor { get; set; }
        public int Limit { get; set; } = 20;
        public string? Q { get; set; }
        public bool? Suspended { get; set; }
    }
}
