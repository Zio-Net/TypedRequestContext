namespace TypedRequestContext;

/// <summary>
/// Always-on per-request correlation ID; independent of typed business contexts.
/// Opt-in via <c>AddCorrelationId()</c>.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>
    /// The correlation ID for the current request.
    /// Returns "unknown" if not set.
    /// </summary>
    string CorrelationId { get; }
}
