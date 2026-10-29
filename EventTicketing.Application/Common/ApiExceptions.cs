using System;
using System.Collections.Generic;

namespace EventTicketing.Application.Common
{
    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message) { }
    }

    public class ForbiddenException : Exception
    {
        public ForbiddenException(string message) : base(message) { }
    }

    public class ConflictException : Exception
    {
        public ConflictException(string message) : base(message) { }
    }

    public class GoneException : Exception
    {
        public GoneException(string message) : base(message) { }
    }

    public class IdempotencyKeyExpiredException : Exception
    {
        public IdempotencyKeyExpiredException(string message) : base(message) { }
    }

    public class RequestValidationException : Exception
    {
        public RequestValidationException(IReadOnlyList<string> errors)
            : base("One or more fields are invalid.")
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }
}
