namespace TypedRequestContext;

/// <summary>
/// Validates a typed request context after extraction.
/// Implement this interface to provide custom validation logic.
/// </summary>
/// <typeparam name="TContext">The typed request context to validate.</typeparam>
public interface IRequestContextValidator<in TContext>
    where TContext : class, ITypedRequestContext
{
    /// <summary>
    /// Validates the given context and returns any validation errors.
    /// An empty list indicates successful validation.
    /// </summary>
    IReadOnlyList<RequestContextValidationError> Validate(TContext context);
}
