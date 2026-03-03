namespace TypedRequestContext.Propagation;

/// <summary>
/// Lazy provider that serializes the current typed request context into
/// propagation headers at the moment of an outbound call.
/// Injected by accessor clients to forward context across service boundaries.
/// </summary>
public interface IPropagationHeadersProvider
{
    /// <summary>
    /// Returns the current typed context serialized as header key/value pairs.
    /// Returns an empty dictionary if no context is set or no headers are configured.
    /// </summary>
    IReadOnlyDictionary<string, string> GetCurrentHeaders();
}
