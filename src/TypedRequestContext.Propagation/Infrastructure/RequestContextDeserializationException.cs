namespace TypedRequestContext.Propagation.Infrastructure;

/// <summary>
/// Thrown when metadata-based context deserialization fails due to missing or invalid required values.
/// </summary>
public sealed class RequestContextDeserializationException : Exception
{
    /// <summary>
    /// Initializes a new exception with a deserialization failure message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public RequestContextDeserializationException(string message)
        : base(message) { }

    /// <summary>
    /// Initializes a new exception with a deserialization failure message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public RequestContextDeserializationException(string message, Exception innerException)
        : base(message, innerException) { }
}
