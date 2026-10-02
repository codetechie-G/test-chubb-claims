namespace Claims.Api.Domain;

/// <summary>A business rule was violated (e.g. an illegal status transition). Maps to HTTP 409.</summary>
public sealed class DomainException(string message) : Exception(message);
