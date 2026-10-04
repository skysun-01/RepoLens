namespace RepoLens.Domain.Common;

/// <summary>Raised when an operation would break a domain rule, such as an invalid state transition.</summary>
public sealed class DomainException(string message) : Exception(message);
