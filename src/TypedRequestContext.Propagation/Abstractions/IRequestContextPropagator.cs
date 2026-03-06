namespace TypedRequestContext.Propagation;

/// <summary>
/// Combines deserialization and accessor management into a single scoped operation.
/// Intended for non-HTTP flows such as queues, events, or background processing,
/// where context arrives as a metadata dictionary rather than HTTP headers or claims.
/// </summary>
/// <typeparam name="T">The typed request context to propagate.</typeparam>
/// <remarks>
/// Calling <see cref="Propagate(IReadOnlyDictionary{string, string})"/> deserializes the metadata,
/// validates and sets the context on <see cref="IRequestContextAccessor"/>, and returns an
/// <see cref="IDisposable"/> that restores the previous context when disposed.
/// Use a <c>using</c> statement to guarantee cleanup even if the handler throws.
/// <code>
/// using var _ = propagator.Propagate(metadata);
/// // CustomerRequestContext is now accessible via DI or GetRequired&lt;T&gt;()
/// await DoWorkAsync(ct);
/// </code>
/// For validators with scoped dependencies, use the overload accepting <see cref="IServiceProvider"/>:
/// <code>
/// using var scope = serviceProvider.CreateScope();
/// using var _ = propagator.Propagate(metadata, scope.ServiceProvider);
/// </code>
/// </remarks>
public interface IRequestContextPropagator<T> where T : class, ITypedRequestContext
{
    /// <summary>
    /// Deserializes <paramref name="metadata"/> into a <typeparamref name="T"/> context,
    /// validates (if configured), sets it as the current context on
    /// <see cref="IRequestContextAccessor"/>, and returns a scope that restores
    /// the previous context when disposed.
    /// </summary>
    /// <param name="metadata">The key/value metadata (e.g. message headers) to deserialize from.</param>
    /// <returns>
    /// An <see cref="IDisposable"/> scope. Dispose it (or use a <c>using</c> block) to
    /// restore the previous context after the handler finishes.
    /// </returns>
    /// <exception cref="Infrastructure.RequestContextDeserializationException">
    /// Thrown when a required propagation key is missing or cannot be converted.
    /// </exception>
    IDisposable Propagate(IReadOnlyDictionary<string, string> metadata);

    /// <summary>
    /// Deserializes <paramref name="metadata"/> into a <typeparamref name="T"/> context,
    /// validates (if configured) using the provided <paramref name="serviceProvider"/>
    /// (enabling scoped validator dependencies), sets it as the current context,
    /// and returns a scope that restores the previous context when disposed.
    /// </summary>
    /// <param name="metadata">The key/value metadata (e.g. message headers) to deserialize from.</param>
    /// <param name="serviceProvider">
    /// The <see cref="IServiceProvider"/> to use for resolving validators.
    /// Pass a scoped provider when validators depend on scoped services.
    /// </param>
    /// <returns>
    /// An <see cref="IDisposable"/> scope. Dispose it (or use a <c>using</c> block) to
    /// restore the previous context after the handler finishes.
    /// </returns>
    /// <exception cref="Infrastructure.RequestContextDeserializationException">
    /// Thrown when a required propagation key is missing or cannot be converted.
    /// </exception>
    IDisposable Propagate(IReadOnlyDictionary<string, string> metadata, IServiceProvider serviceProvider);
}
