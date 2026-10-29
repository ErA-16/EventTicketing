using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventTicketing.Infrastructure.Seed
{
    public static class IdentitySeeder
    {
        private static async Task SeedAdminAsync(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var userManager = serviceProvider.GetRequiredService<UserManager<EventTicketingUser>>();
            var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("IdentitySeeder");

            var adminEmail = configuration["Seed:AdminEmail"];
            var adminPassword = configuration["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.LogWarning("Admin seeding skipped: Seed:AdminEmail / Seed:AdminPassword are not configured.");
                return;
            }

            var adminRole = Role.Admin.ToString();
            var admin = await userManager.FindByEmailAsync(adminEmail);

            if (admin == null)
            {
                admin = new EventTicketingUser
                {
                    FirstName = "Admin",
                    LastName = "Account",
                    UserName = adminEmail,
                    Email = adminEmail,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(admin, adminPassword);
                if (!createResult.Succeeded)
                {
                    var errors = string.Join("; ", createResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Admin seeding failed: {errors}");
                }

                logger.LogInformation("Seeded admin account {Email}.", adminEmail);
            }

            if (!await userManager.IsInRoleAsync(admin, adminRole))
            {
                await userManager.AddToRoleAsync(admin, adminRole);
            }
        }

        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            foreach (var roleName in Enum.GetNames<Role>())
            {
                if (!await roleManager.RoleExistsAsync(roleName))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid> { Name = roleName });
                }
            }

            await SeedAdminAsync(serviceProvider);
        }
    }
}
