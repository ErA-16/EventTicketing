using System;

namespace EventTicketing.Domain.Enums
{
    public enum ReservationStatus
    {
        Held = 1,
        Paid = 2,
        Expired = 3,
        Cancelled = 4
    }
}
