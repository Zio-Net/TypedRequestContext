using TypedRequestContext.Infrastructure;

namespace TypedRequestContext.Propagation.Infrastructure;

/// <summary>
/// Default <see cref="IRequestContextPropagator{T}"/> implementation that delegates
/// deserialization to <see cref="IRequestContextDeserializer{T}"/> and manages
/// accessor state via <see cref="RequestContextScopeFactory"/>.
/// </summary>
/// <typeparam name="T">The typed request context to propagate.</typeparam>
/// <remarks>
/// Initializes a new instance of <see cref="RequestContextPropagator{T}"/>.
/// </remarks>
public sealed class RequestContextPropagator<T>(
    IRequestContextDeserializer<T> deserializer,
    RequestContextScopeFactory scopeFactory) : IRequestContextPropagator<T>
    where T : class, ITypedRequestContext
{
    /// <inheritdoc />
    public IDisposable Propagate(IReadOnlyDictionary<string, string> metadata)
    {
        var context = deserializer.Deserialize(metadata);
        return scopeFactory.Begin(context);
    }

    /// <inheritdoc />
    public IDisposable Propagate(IReadOnlyDictionary<string, string> metadata, IServiceProvider serviceProvider)
    {
        var context = deserializer.Deserialize(metadata);
        return scopeFactory.Begin(context, serviceProvider);
    }
}
