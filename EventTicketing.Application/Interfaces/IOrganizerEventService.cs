using EventTicketing.Application.DTOs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EventTicketing.Application.Interfaces
{
    public interface IOrganizerEventService
    {
        Task<EventResponse> CreateAsync(Guid organizerId, CreateEventRequest request, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<EventResponse>> GetMineAsync(Guid organizerId, CancellationToken cancellationToken = default);
        Task<EventResponse> GetAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken = default);
        Task<EventResponse> UpdateAsync(Guid organizerId, Guid eventId, UpdateEventRequest request, CancellationToken cancellationToken = default);
        Task<TicketTypeResponse> AddTicketTypeAsync(Guid organizerId, Guid eventId, CreateTicketTypeRequest request, CancellationToken cancellationToken = default);
        Task<TicketTypeResponse> UpdateTicketTypeAsync(Guid organizerId, Guid eventId, Guid ticketTypeId, UpdateTicketTypeRequest request, CancellationToken cancellationToken = default);
        Task DeleteTicketTypeAsync(Guid organizerId, Guid eventId, Guid ticketTypeId, CancellationToken cancellationToken = default);
        Task<ImageUploadResponse> SetImageAsync(Guid organizerId, Guid eventId, Stream content, string contentType, CancellationToken cancellationToken = default);
        Task<EventResponse> SubmitAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken = default);
        Task<EventResponse> PublishAsync(Guid organizerId, Guid eventId, CancellationToken cancellationToken = default);
    }
}
