using System;

namespace ParvNegar.Domain
{
    /// <summary>Raised when a domain invariant is violated. Codes are stable identifiers for UI mapping.</summary>
    public sealed class DomainException : Exception
    {
        public DomainException(string code, string message) : base(message) { Code = code; }
        public string Code { get; }
    }

    internal static class Guard
    {
        public static void NotEmpty(Guid id, string name)
        {
            if (id == Guid.Empty) throw new DomainException("id.empty", name + " must not be empty.");
        }

        public static string RequiredText(string value, string name, int maxLength)
        {
            if (value == null || value.Trim().Length == 0) throw new DomainException("text.required", name + " is required.");
            if (value.Length > maxLength) throw new DomainException("text.too_long", name + " exceeds " + maxLength + " characters.");
            return value;
        }

        public static string OptionalText(string value, string name, int maxLength)
        {
            if (value == null) return null;
            if (value.Length > maxLength) throw new DomainException("text.too_long", name + " exceeds " + maxLength + " characters.");
            return value;
        }
    }
}
