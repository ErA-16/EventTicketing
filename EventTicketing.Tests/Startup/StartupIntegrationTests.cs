using System;
using System.Linq;
using System.Threading.Tasks;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using EventTicketing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EventTicketing.Tests.Startup
{
    public sealed class StartupIntegrationTests
    {
        [Fact]
        public async Task Database_HasNoPendingMigrations()
        {
            await using var db = TestDatabase.CreateDbContext();
            var pending = await db.Database.GetPendingMigrationsAsync(
                TestContext.Current.CancellationToken);
            Assert.Empty(pending);
        }

        [Fact]
        public async Task Startup_SeedsRolesAndTheConfiguredAdminWithoutPlaintextPassword()
        {
            await using var db = TestDatabase.CreateDbContext();

            var roleNames = await db.Roles
                .Select(role => role.Name!)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Contains(Role.Admin.ToString(), roleNames);
            Assert.Contains(Role.Attendee.ToString(), roleNames);
            Assert.Contains(Role.Organizer.ToString(), roleNames);

            var admin = await db.Users.SingleOrDefaultAsync(
                user => user.Email == TestRun.AdminEmail,
                TestContext.Current.CancellationToken);
            Assert.NotNull(admin);
            Assert.NotEqual(ApiFactory.AdminPassword, admin.PasswordHash);
            Assert.False(string.IsNullOrWhiteSpace(admin.PasswordHash));

            var adminRoles = await (
                from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == admin.Id
                select role.Name!)
                .ToListAsync(TestContext.Current.CancellationToken);
            Assert.Contains(Role.Admin.ToString(), adminRoles);
        }
    }
}
