using System;
using System.Collections.Generic;

namespace EventTicketing.Application.DTOs
{
    public class AdminUserResponse
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? OrganizationName { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = new();
        public bool IsSuspended { get; set; }
    }
}
