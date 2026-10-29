using System;
using EventTicketing.Application.DTOs;
using EventTicketing.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace EventTicketing.Application.Mapping
{
    // Requests only carry the fields an organizer may edit. OrganizerId, Status,
    // ImagePath and EventId are set by the service, so "some members unmapped"
    // is expected here, not a mistake.
    [Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
    public partial class EventMapper
    {
        public partial Event ToEntity(CreateEventRequest request);

        public partial void Apply(UpdateEventRequest request, Event entity);

        public partial TicketType ToEntity(CreateTicketTypeRequest request);

        public partial void Apply(UpdateTicketTypeRequest request, TicketType entity);

        [MapperIgnoreTarget(nameof(EventResponse.ImageUrl))]
        [MapperIgnoreTarget(nameof(EventResponse.OrganizerName))]
        [MapperIgnoreTarget(nameof(EventResponse.TicketsLeft))]
        public partial EventResponse ToResponse(Event entity);

        [MapperIgnoreTarget(nameof(EventSummaryResponse.ImageUrl))]
        [MapperIgnoreTarget(nameof(EventSummaryResponse.MinPrice))]
        [MapperIgnoreTarget(nameof(EventSummaryResponse.TicketsLeft))]
        public partial EventSummaryResponse ToSummary(Event entity);

        [MapperIgnoreTarget(nameof(EventDetailsResponse.ImageUrl))]
        [MapperIgnoreTarget(nameof(EventDetailsResponse.OrganizerName))]
        public partial EventDetailsResponse ToDetails(Event entity);

        // Hand-written because the current price depends on the current time.
        public TicketTypeResponse ToResponse(TicketType entity)
        {
            return new TicketTypeResponse
            {
                Id = entity.Id,
                Name = entity.Name,
                Price = entity.Price,
                EarlyBirdPrice = entity.EarlyBirdPrice,
                EarlyBirdEndsAt = entity.EarlyBirdEndsAt,
                CurrentPrice = entity.GetCurrentPrice(DateTime.UtcNow),
                Quantity = entity.Quantity,
                SalesOpenAt = entity.SalesOpenAt
            };
        }

        // Store everything as UTC. Mapperly uses these automatically for the
        // DateTimeOffset -> DateTime conversions.
        private static DateTime ToUtc(DateTimeOffset value)
        {
            return value.UtcDateTime;
        }

        private static DateTime? ToUtc(DateTimeOffset? value)
        {
            return value.HasValue ? value.Value.UtcDateTime : null;
        }
    }
}
