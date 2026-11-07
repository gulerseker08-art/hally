namespace Hally.Domain.Common;

/// <summary>A business rule was violated. Mapped to RFC 9457 problem details by the API.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
