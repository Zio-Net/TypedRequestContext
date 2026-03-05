using TypedRequestContext;

namespace TypedRequestContext.Propagation;

/// <summary>
/// Combines deserialization and accessor management into a single scoped operation.
/// Intended for non-HTTP flows such as queues, events, or background processing,
/// where context arrives as a metadata dictionary rather than HTTP headers or claims.
/// </summary>
/// <typeparam name="T">The typed request context to propagate.</typeparam>
/// <remarks>
/// Calling <see cref="Propagate"/> deserializes the metadata, sets the context on
/// <see cref="IRequestContextAccessor"/>, and returns an <see cref="IDisposable"/>
/// that clears the accessor when disposed. Use a <c>using</c> statement or
/// <c>await using</c> to guarantee cleanup even if the handler throws.
/// <code>
/// using var _ = propagator.Propagate(metadata);
/// // CustomerRequestContext is now accessible via DI or GetRequired&lt;T&gt;()
/// await DoWorkAsync(ct);
/// </code>
/// </remarks>
public interface IRequestContextPropagator<T> where T : class, ITypedRequestContext
{
    /// <summary>
    /// Deserializes <paramref name="metadata"/> into a <typeparamref name="T"/> context,
    /// sets it as the current context on <see cref="IRequestContextAccessor"/>, and returns
    /// a scope that clears the accessor when disposed.
    /// </summary>
    /// <param name="metadata">The key/value metadata (e.g. message headers) to deserialize from.</param>
    /// <returns>
    /// An <see cref="IDisposable"/> scope. Dispose it (or use a <c>using</c> block) to
    /// clear the context from the accessor after the handler finishes.
    /// </returns>
    /// <exception cref="Infrastructure.RequestContextDeserializationException">
    /// Thrown when a required propagation key is missing or cannot be converted.
    /// </exception>
    IDisposable Propagate(IReadOnlyDictionary<string, string> metadata);
}
