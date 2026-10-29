using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace EventTicketing.Application.DTOs
{
    public class RegisterRequest : IValidatableObject
    {
        [StringLength(100)]
        public string? FirstName { get; set; }

        [StringLength(100)]
        public string? LastName { get; set; }

        [StringLength(200)]
        public string? OrganizationName { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; } = null!;

        [Required]
        [MinLength(8)]
        public string Password { get; set; } = null!;

        public bool IsOrganizer { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (IsOrganizer)
            {
                if (string.IsNullOrWhiteSpace(OrganizationName))
                {
                    yield return new ValidationResult(
                        "OrganizationName is required for organizer accounts.",
                        new[] { nameof(OrganizationName) });
                }

                yield break;
            }

            if (string.IsNullOrWhiteSpace(FirstName))
            {
                yield return new ValidationResult(
                    "FirstName is required for attendee accounts.",
                    new[] { nameof(FirstName) });
            }

            if (string.IsNullOrWhiteSpace(LastName))
            {
                yield return new ValidationResult(
                    "LastName is required for attendee accounts.",
                    new[] { nameof(LastName) });
            }
        }
    }
}
