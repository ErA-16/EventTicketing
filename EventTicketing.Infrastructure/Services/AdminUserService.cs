using EventTicketing.Application.Common;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Identity;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Infrastructure.Services
{
    public class AdminUserService : IAdminUserService
    {
        private readonly AppDbContext _db;

        public AdminUserService(AppDbContext db)
        {
            _db = db;
        }

        private static string GetDisplayName(EventTicketingUser user)
        {
            if (!string.IsNullOrWhiteSpace(user.OrganizationName))
            {
                return user.OrganizationName.Trim();
            }

            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            return string.IsNullOrWhiteSpace(fullName) ? (user.Email ?? string.Empty) : fullName;
        }

        private class CursorData
        {
            public Guid Id { get; set; }
        }

        public async Task<CursorPage<AdminUserResponse>> GetUsersAsync(AdminUserQuery query, CancellationToken cancellationToken = default)
        {
            if (query is null) query = new AdminUserQuery();

            var limit = query.Limit;
            if (limit < 1) limit = 20;
            if (limit > 50) limit = 50;

            string? roleFilter = null;
            if (!string.IsNullOrWhiteSpace(query.Role)
                && !string.Equals(query.Role, "All", StringComparison.OrdinalIgnoreCase))
            {
                if (!Enum.TryParse<Role>(query.Role, true, out var role) || !Enum.IsDefined(role))
                {
                    throw new RequestValidationException(new[] { "Invalid role." });
                }

                roleFilter = role.ToString();
            }

            IQueryable<EventTicketingUser> queryable = _db.Users;

            if (roleFilter is not null)
            {
                queryable =
                    from user in _db.Users
                    join userRole in _db.UserRoles on user.Id equals userRole.UserId
                    join role in _db.Roles on userRole.RoleId equals role.Id
                    where role.Name == roleFilter
                    select user;
            }

            if (!string.IsNullOrWhiteSpace(query.Q))
            {
                var q = query.Q.Trim();
                queryable = queryable.Where(u =>
                    EF.Functions.ILike(u.FirstName, "%" + q + "%")
                    || EF.Functions.ILike(u.LastName, "%" + q + "%")
                    || (u.OrganizationName != null && EF.Functions.ILike(u.OrganizationName, "%" + q + "%"))
                    || (u.Email != null && EF.Functions.ILike(u.Email, "%" + q + "%")));
            }

            queryable = queryable.OrderByDescending(u => u.Id);

            if (!string.IsNullOrWhiteSpace(query.Cursor))
            {
                try
                {
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(query.Cursor));
                    var cursorData = JsonSerializer.Deserialize<CursorData>(json);
                    if (cursorData is null || cursorData.Id == Guid.Empty)
                        throw new RequestValidationException(new[] { "Invalid cursor." });

                    var cursorId = cursorData.Id;
                    queryable = queryable.Where(u => u.Id.CompareTo(cursorId) < 0);
                }
                catch (RequestValidationException) { throw; }
                catch { throw new RequestValidationException(new[] { "Invalid cursor." }); }
            }

            var users = await queryable.Take(limit + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
            var hasMore = users.Count > limit;
            if (hasMore) users = users.Take(limit).ToList();

            string? nextCursor = null;
            if (hasMore && users.Count > 0)
            {
                var last = users[users.Count - 1];
                var json = JsonSerializer.Serialize(new CursorData { Id = last.Id });
                nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            }

            var userIds = users.Select(u => u.Id).ToList();
            var roleRows = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                where userIds.Contains(userRole.UserId)
                select new { userRole.UserId, RoleName = role.Name })
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var rolesByUser = roleRows
                .GroupBy(row => row.UserId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(row => row.RoleName ?? string.Empty)
                        .Where(name => name.Length > 0)
                        .OrderBy(name => name)
                        .ToList());

            var responses = users.Select(user => new AdminUserResponse
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                OrganizationName = user.OrganizationName,
                DisplayName = GetDisplayName(user),
                Email = user.Email ?? string.Empty,
                Roles = rolesByUser.GetValueOrDefault(user.Id) ?? new List<string>(),
                IsSuspended = user.IsSuspended
            }).ToList();

            return new CursorPage<AdminUserResponse>
            {
                Items = responses,
                NextCursor = nextCursor,
                HasMore = hasMore
            };
        }
    }
}
