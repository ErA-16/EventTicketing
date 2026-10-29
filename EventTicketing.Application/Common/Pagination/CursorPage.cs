using System;
using System.Collections.Generic;

namespace EventTicketing.Application.Common.Pagination
{
    public class CursorPage<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
        public string? NextCursor { get; init; }
        public bool HasMore { get; init; }
    }
}
