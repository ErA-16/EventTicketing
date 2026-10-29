using System;

namespace EventTicketing.Application.DTOs
{
    public class AdminOrganizerResponse
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = null!;
        public string LastName { get; set; } = null!;
        public string? OrganizationName { get; set; }
        public string Email { get; set; } = null!;
        public bool IsSuspended { get; set; }
    }
}
