using TypedRequestContext;

namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Internal implementation of <see cref="ICorrelationContext"/> backed by <see cref="AsyncLocal{T}"/>.
/// Set by middleware and read by consumers.
/// </summary>
internal sealed class CorrelationContext : ICorrelationContext
{
    private static readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc />
    public string CorrelationId => _current.Value ?? "unknown";

    /// <summary>
    /// Sets the correlation ID for the current async flow. Called only by the middleware.
    /// </summary>
    internal static void Set(string id) => _current.Value = id;

    /// <summary>
    /// Clears the correlation ID for the current async flow.
    /// </summary>
    internal static void Clear() => _current.Value = null;
}
