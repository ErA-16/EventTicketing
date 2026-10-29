using EventTicketing.Application.Common;
using EventTicketing.Application.Common.Pagination;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Identity;
using EventTicketing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Infrastructure.Services
{
    public class AdminOrganizerService : IAdminOrganizerService
    {
        private readonly AppDbContext _db;
        private readonly ICacheService _cache;

        public AdminOrganizerService(AppDbContext db, ICacheService cache)
        {
            _db = db;
            _cache = cache;
        }

        private async Task InvalidatePublicEventsAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _cache.RemoveByPrefixAsync(CacheKeys.ListPrefix, cancellationToken).ConfigureAwait(false);
                await _cache.RemoveByPrefixAsync(CacheKeys.DetailPrefix, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        private async Task<EventTicketingUser> FindOrganizerAsync(Guid organizerId, CancellationToken cancellationToken)
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == organizerId, cancellationToken).ConfigureAwait(false);
            if (user is null)
            {
                throw new NotFoundException("Organizer not found.");
            }

            var organizerRole = Role.Organizer.ToString();
            var isOrganizer = await (
                from userRole in _db.UserRoles
                join role in _db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == organizerId && role.Name == organizerRole
                select userRole
            ).AnyAsync(cancellationToken).ConfigureAwait(false);

            if (!isOrganizer)
            {
                throw new ConflictException("User is not an organizer.");
            }

            return user;
        }

        private static AdminOrganizerResponse ToResponse(EventTicketingUser user)
        {
            return new AdminOrganizerResponse
            {
                Id = user.Id,
                FirstName = user.FirstName,
                LastName = user.LastName,
                OrganizationName = user.OrganizationName,
                Email = user.Email ?? string.Empty,
                IsSuspended = user.IsSuspended
            };
        }

        private class CursorData
        {
            public Guid Id { get; set; }
        }

        public async Task<CursorPage<AdminOrganizerResponse>> GetOrganizersAsync(AdminOrganizerQuery query, CancellationToken cancellationToken = default)
        {
            if (query is null) query = new AdminOrganizerQuery();

            var limit = query.Limit;
            if (limit < 1) limit = 20;
            if (limit > 50) limit = 50;

            var organizerRole = Role.Organizer.ToString();
            IQueryable<EventTicketingUser> queryable =
                from user in _db.Users
                join userRole in _db.UserRoles on user.Id equals userRole.UserId
                join role in _db.Roles on userRole.RoleId equals role.Id
                where role.Name == organizerRole
                select user;

            if (query.Suspended.HasValue)
            {
                queryable = queryable.Where(u => u.IsSuspended == query.Suspended.Value);
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

            return new CursorPage<AdminOrganizerResponse>
            {
                Items = users.Select(ToResponse).ToList(),
                NextCursor = nextCursor,
                HasMore = hasMore
            };
        }

        public async Task SuspendAsync(Guid organizerId, CancellationToken cancellationToken = default)
        {
            var user = await FindOrganizerAsync(organizerId, cancellationToken).ConfigureAwait(false);
            if (user.IsSuspended)
            {
                throw new ConflictException("Organizer is already suspended.");
            }

            user.IsSuspended = true;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidatePublicEventsAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task UnsuspendAsync(Guid organizerId, CancellationToken cancellationToken = default)
        {
            var user = await FindOrganizerAsync(organizerId, cancellationToken).ConfigureAwait(false);
            if (!user.IsSuspended)
            {
                throw new ConflictException("Organizer is not suspended.");
            }

            user.IsSuspended = false;
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await InvalidatePublicEventsAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
