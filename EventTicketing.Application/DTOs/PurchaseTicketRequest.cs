using System;
using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class PurchaseTicketRequest
    {
        [Required]
        public Guid TicketTypeId { get; set; }
    }
}
