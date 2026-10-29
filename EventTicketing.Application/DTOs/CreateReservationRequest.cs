using System;
using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class CreateReservationRequest
    {
        [Required]
        public Guid TicketTypeId { get; set; }
    }
}
