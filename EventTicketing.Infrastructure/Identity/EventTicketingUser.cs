using System;
using EventTicketing.Domain.Enums;
using Microsoft.AspNetCore.Identity;

namespace EventTicketing.Infrastructure.Identity
{
    public class EventTicketingUser : IdentityUser<Guid>
    {
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string? OrganizationName { get; set; }
        public bool IsSuspended { get; set; }

        public EventTicketing.Domain.Entities.Wallet? Wallet { get; set; }
    }
}
