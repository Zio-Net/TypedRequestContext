namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Thrown when a typed request context fails validation.
/// Contains the validation errors that caused the failure.
/// </summary>
public sealed class RequestContextValidationException(
    IReadOnlyList<RequestContextValidationError> errors)
    : Exception("Request context validation failed.")
{
    /// <summary>
    /// The validation errors that caused the failure.
    /// </summary>
    public IReadOnlyList<RequestContextValidationError> Errors { get; } = errors;
}
