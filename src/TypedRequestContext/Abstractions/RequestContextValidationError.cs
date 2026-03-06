namespace TypedRequestContext;

/// <summary>
/// Represents a single validation error on a request context property.
/// </summary>
/// <param name="MemberName">The property name that failed validation, or null for object-level errors.</param>
/// <param name="ErrorMessage">A human-readable description of the validation failure.</param>
public sealed record RequestContextValidationError(string? MemberName, string ErrorMessage);
