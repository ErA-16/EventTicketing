using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EventTicketing.Application.Common;
using EventTicketing.Application.DTOs;
using EventTicketing.Application.Interfaces;
using EventTicketing.Application.Mapping;
using EventTicketing.Domain.Entities;
using EventTicketing.Domain.Enums;
using EventTicketing.Infrastructure.Persistence;
using EventTicketing.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventTicketing.Infrastructure.Services
{
    public class OrganizerEventService : IOrganizerEventService
    {
        private static readonly byte[] JpegMagicBytes = { 0xFF, 0xD8, 0xFF };

        private readonly AppDbContext _db;
        private readonly EventMapper _mapper;
        private readonly IImageStorage _imageStorage;
        private readonly R2Options _r2Options;
        private readonly long _maxImageBytes;
        private readonly ICacheService _cache;
        private readonly IStockQueryService _stockQuery;

        public OrganizerEventService(
            AppDbContext db,
            EventMapper mapper,
            IImageStorage imageStorage,
            IOptions<R2Options> r2Options,
            ICacheService cache,
            IStockQueryService stockQuery)
        {
            _db = db;
            _mapper = mapper;
            _imageStorage = imageStorage;
            _r2Options = r2Options.Value;
            _maxImageBytes = r2Options.Value.MaxImageBytes;
            _cache = cache;
            _stockQuery = stockQuery;
        }

        private async Task InvalidateEventAsync(Guid eventId, CancellationToken cancellationToken)
        {
            try
            {
                await _cache.RemoveAsync(CacheKeys.EventDetail(eventId), cancellationToken).ConfigureAwait(false);
                await _cache.RemoveByPrefixAsync(CacheKeys.ListPrefix, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Cache invalidation must never break the request.
            }
        }

        private string? BuildImageUrl(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                return null;
            }

            var baseUrl = (_r2Options.PublicBaseUrl ?? string.Empty).TrimEnd('/');
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return imagePath;
            }

            return baseUrl + "/" + imagePath.TrimStart('/');
        }

        private async Task<string> GetOrganizerNameAsync(Guid organizerId, CancellationToken cancellationToken)
        {
            var organizer = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == organizerId)
                .Select(u => new { u.FirstName, u.LastName, u.OrganizationName })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (organizer is null)
            {
                return string.Empty;
            }

            return !string.IsNullOrWhiteSpace(organizer.OrganizationName)
                ? organizer.OrganizationName.Trim()
                : $"{organizer.FirstName} {organizer.LastName}".Trim();
        }

        private static void ApplyStock(EventResponse response, IReadOnlyDictionary<Guid, TicketTypeStock> stock)
        {
            var total = 0;
            foreach (var ticketType in response.TicketTypes)
            {
                if (stock.TryGetValue(ticketType.Id, out var value))
                {
                    ticketType.Sold = value.Sold;
                    ticketType.Held = value.Held;
                    ticketType.Remaining = value.Remaining;
                }
                else
                {
                    ticketType.Sold = 0;
                    ticketType.Held = 0;
                    ticketType.Remaining = ticketType.Quantity;
                }

                total += ticketType.Remaining;
            }

            response.TicketsLeft = total;
        }

        private async Task ApplyStockAsync(IReadOnlyList<EventResponse> responses, CancellationToken cancellationToken)
        {
            var ids = responses
                .SelectMany(r => r.TicketTypes)
                .Select(t => t.Id)
                .Distinct()
                .ToList();

            var stock = await _stockQuery.GetTicketTypeStockAsync(ids, cancellationToken).ConfigureAwait(false);
            foreach (var response in responses)
            {
                ApplyStock(response, stock);
            }
        }

        private async Task<EventResponse> HydrateAsync(EventResponse response, CancellationToken cancellationToken)
        {
            response.ImageUrl = BuildImageUrl(response.ImagePath);
            response.OrganizerName = await GetOrganizerNameAsync(response.OrganizerId, cancellationToken).ConfigureAwait(false);
            await ApplyStockAsync(new[] { response }, cancellationToken).ConfigureAwait(false);
            return response;
        }

        private async Task<Event> LoadOwnedAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken)
        {
            var entity = await _db.Events
                .Include(e => e.TicketTypes)
                .FirstOrDefaultAsync(e => e.Id == eventId && e.OrganizerId == organizerId, cancellationToken);

            if (entity is null)
            {
                throw new NotFoundException("Event not found.");
            }

            return entity;
        }

        private async Task<Event> LoadOwnedDraftAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken)
        {
            var entity = await LoadOwnedAsync(organizerId, eventId, cancellationToken);

            if (!entity.IsEditable)
            {
                throw new ConflictException("An event can only be changed while it is a draft.");
            }

            return entity;
        }

        private async Task<(Event Event, TicketType TicketType)> LoadOwnedTicketTypeAsync(
            Guid organizerId,
            Guid eventId,
            Guid ticketTypeId,
            CancellationToken cancellationToken)
        {
            var entity = await LoadOwnedDraftAsync(organizerId, eventId, cancellationToken).ConfigureAwait(false);
            var ticketType = entity.TicketTypes.FirstOrDefault(t => t.Id == ticketTypeId);
            if (ticketType is null)
            {
                throw new NotFoundException("Ticket type not found.");
            }

            return (entity, ticketType);
        }

        private static void ValidateStartsAt(DateTimeOffset startsAt)
        {
            if (startsAt <= DateTimeOffset.UtcNow)
            {
                throw new RequestValidationException(new[] { "StartsAt must be in the future." });
            }
        }

        private static void ValidateTicketTypeRequest(CreateTicketTypeRequest request, Event entity)
        {
            var errors = new List<string>();

            if (request.EarlyBirdPrice.HasValue ^ request.EarlyBirdEndsAt.HasValue)
            {
                errors.Add("EarlyBirdPrice and EarlyBirdEndsAt must be provided together.");
            }

            if (request.EarlyBirdPrice.HasValue && request.EarlyBirdPrice.Value >= request.Price)
            {
                errors.Add("EarlyBirdPrice must be lower than Price.");
            }

            if (request.EarlyBirdEndsAt.HasValue && request.EarlyBirdEndsAt.Value <= request.SalesOpenAt)
            {
                errors.Add("EarlyBirdEndsAt must be after SalesOpenAt.");
            }

            if (request.SalesOpenAt.UtcDateTime >= entity.StartsAt)
            {
                errors.Add("SalesOpenAt must be before the event starts.");
            }

            if (request.EarlyBirdEndsAt.HasValue && request.EarlyBirdEndsAt.Value.UtcDateTime >= entity.StartsAt)
            {
                errors.Add("EarlyBirdEndsAt must be before the event starts.");
            }

            if (errors.Count > 0)
            {
                throw new RequestValidationException(errors);
            }
        }

        private static void ValidateExistingTicketTypes(Event entity)
        {
            var errors = new List<string>();

            foreach (var ticketType in entity.TicketTypes)
            {
                if (ticketType.SalesOpenAt >= entity.StartsAt)
                {
                    errors.Add($"Ticket type '{ticketType.Name}': sales open at or after the event starts.");
                }

                if (ticketType.EarlyBirdEndsAt.HasValue && ticketType.EarlyBirdEndsAt.Value >= entity.StartsAt)
                {
                    errors.Add($"Ticket type '{ticketType.Name}': early bird must end before the event starts.");
                }
            }

            if (errors.Count > 0)
            {
                throw new RequestValidationException(errors);
            }
        }

        private void ValidateImage(MemoryStream buffer, string contentType)
        {
            var errors = new List<string>();

            var isJpegContentType =
                string.Equals(contentType, "image/jpeg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(contentType, "image/jpg", StringComparison.OrdinalIgnoreCase);

            if (!isJpegContentType)
            {
                errors.Add("The image must be a JPEG.");
            }

            if (buffer.Length > _maxImageBytes)
            {
                errors.Add($"The image must be {_maxImageBytes / 1_048_576.0:0.#} MB or smaller.");
            }

            var header = buffer.GetBuffer();
            var hasJpegMagicBytes =
                buffer.Length >= 3 &&
                header[0] == JpegMagicBytes[0] &&
                header[1] == JpegMagicBytes[1] &&
                header[2] == JpegMagicBytes[2];

            if (!hasJpegMagicBytes)
            {
                errors.Add("The uploaded file is not a real JPEG. Only genuine JPEG files are accepted.");
            }

            if (errors.Count > 0)
            {
                throw new RequestValidationException(errors);
            }
        }

        public async Task<EventResponse> CreateAsync(Guid organizerId, CreateEventRequest request, CancellationToken cancellationToken = default)
        {
            ValidateStartsAt(request.StartsAt);

            var entity = _mapper.ToEntity(request);
            entity.OrganizerId = organizerId;
            entity.Status = EventStatus.Draft;
            entity.CreatedAt = DateTime.UtcNow;

            _db.Events.Add(entity);
            await _db.SaveChangesAsync(cancellationToken);

            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }

        public async Task<IReadOnlyList<EventResponse>> GetMineAsync(Guid organizerId, CancellationToken cancellationToken = default)
        {
            var events = await _db.Events
                .Include(e => e.TicketTypes)
                .Where(e => e.OrganizerId == organizerId)
                .OrderByDescending(e => e.StartsAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var organizerName = await GetOrganizerNameAsync(organizerId, cancellationToken).ConfigureAwait(false);
            var responses = events.Select(entity =>
            {
                var response = _mapper.ToResponse(entity);
                response.ImageUrl = BuildImageUrl(entity.ImagePath);
                response.OrganizerName = organizerName;
                return response;
            }).ToList();

            await ApplyStockAsync(responses, cancellationToken).ConfigureAwait(false);
            return responses;
        }

        public async Task<EventResponse> GetAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken = default)
        {
            var entity = await LoadOwnedAsync(organizerId, eventId, cancellationToken);

            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }

        public async Task<EventResponse> UpdateAsync(Guid organizerId, Guid eventId, UpdateEventRequest request, CancellationToken cancellationToken = default)
        {
            var entity = await LoadOwnedDraftAsync(organizerId, eventId, cancellationToken);

            ValidateStartsAt(request.StartsAt);

            _mapper.Apply(request, entity);
            ValidateExistingTicketTypes(entity);

            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);

            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }

        public async Task<TicketTypeResponse> AddTicketTypeAsync(Guid organizerId, Guid eventId, CreateTicketTypeRequest request, CancellationToken cancellationToken = default)
        {
            var entity = await LoadOwnedDraftAsync(organizerId, eventId, cancellationToken);
            ValidateTicketTypeRequest(request, entity);

            var ticketType = _mapper.ToEntity(request);
            ticketType.EventId = entity.Id;

            _db.TicketTypes.Add(ticketType);
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);

            var response = _mapper.ToResponse(ticketType);
            response.Sold = 0;
            response.Held = 0;
            response.Remaining = ticketType.Quantity;
            return response;
        }

        public async Task<TicketTypeResponse> UpdateTicketTypeAsync(
            Guid organizerId,
            Guid eventId,
            Guid ticketTypeId,
            UpdateTicketTypeRequest request,
            CancellationToken cancellationToken = default)
        {
            var (entity, ticketType) = await LoadOwnedTicketTypeAsync(organizerId, eventId, ticketTypeId, cancellationToken).ConfigureAwait(false);
            ValidateTicketTypeRequest(request, entity);

            _mapper.Apply(request, ticketType);
            ValidateExistingTicketTypes(entity);

            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);

            var response = _mapper.ToResponse(ticketType);
            response.Sold = 0;
            response.Held = 0;
            response.Remaining = ticketType.Quantity;
            return response;
        }

        public async Task DeleteTicketTypeAsync(
            Guid organizerId,
            Guid eventId,
            Guid ticketTypeId,
            CancellationToken cancellationToken = default)
        {
            var (entity, ticketType) = await LoadOwnedTicketTypeAsync(organizerId, eventId, ticketTypeId, cancellationToken).ConfigureAwait(false);

            var hasReservations = await _db.Reservations
                .AnyAsync(r => r.TicketTypeId == ticketType.Id, cancellationToken)
                .ConfigureAwait(false);
            if (hasReservations)
            {
                throw new ConflictException("Ticket type cannot be deleted after reservations exist.");
            }

            _db.TicketTypes.Remove(ticketType);
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ImageUploadResponse> SetImageAsync(Guid organizerId, Guid eventId, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            var entity = await LoadOwnedDraftAsync(organizerId, eventId, cancellationToken);

            await using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);

            ValidateImage(buffer, contentType);

            var key = $"events/{entity.Id}/cover.jpg";
            buffer.Position = 0;
            await _imageStorage.SaveAsync(key, buffer, "image/jpeg", cancellationToken);

            entity.ImagePath = key;
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);

            return new ImageUploadResponse
            {
                ImagePath = key,
                ImageUrl = $"{_r2Options.PublicBaseUrl.TrimEnd('/')}/{key}"
            };
        }

        public async Task<EventResponse> SubmitAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken = default)
        {
            var entity = await LoadOwnedDraftAsync(organizerId, eventId, cancellationToken);
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(entity.ImagePath))
            {
                errors.Add("A cover image is required before the event can be submitted for review.");
            }

            if (entity.TicketTypes.Count == 0)
            {
                errors.Add("At least one ticket type is required before the event can be submitted for review.");
            }

            if (errors.Count == 0)
            {
                try
                {
                    ValidateStartsAt(entity.StartsAt);
                }
                catch (RequestValidationException ex)
                {
                    errors.AddRange(ex.Errors);
                }
            }

            if (errors.Count == 0 && entity.TicketTypes.Count > 0)
            {
                try
                {
                    ValidateExistingTicketTypes(entity);
                }
                catch (RequestValidationException ex)
                {
                    errors.AddRange(ex.Errors);
                }
            }

            if (errors.Count > 0)
            {
                throw new RequestValidationException(errors);
            }

            entity.RejectionReason = null;
            entity.Status = EventStatus.PendingApproval;
            entity.RejectionReason = null;
            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);

            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }

        public async Task<EventResponse> PublishAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken = default)
        {
            var entity = await LoadOwnedAsync(organizerId, eventId, cancellationToken);

            if (entity.Status != EventStatus.Approved)
            {
                throw new ConflictException("Only approved events can be published.");
            }

            entity.Status = EventStatus.Published;

            await _db.SaveChangesAsync(cancellationToken);
            await InvalidateEventAsync(entity.Id, cancellationToken).ConfigureAwait(false);

            return await HydrateAsync(_mapper.ToResponse(entity), cancellationToken).ConfigureAwait(false);
        }
    }
}
