using System;

namespace EventTicketing.Application.DTOs
{
    public class RejectEventRequest
    {
        public string Reason { get; set; } = null!;
    }
}
