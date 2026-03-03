namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Thrown when context extraction cannot create a context
/// due to missing required values.
/// </summary>
public sealed class RequestContextCreationException(int statusCode, string message)
    : Exception(message)
{
    /// <summary>
    /// The HTTP status code the middleware should return (401 or 403).
    /// </summary>
    public int StatusCode { get; } = statusCode;
}
