using TypedRequestContext;
using TypedRequestContext.Propagation;

namespace TypedRequestContext.Propagation.Infrastructure;

/// <summary>
/// Default <see cref="IRequestContextPropagator{T}"/> implementation that delegates
/// deserialization to <see cref="IRequestContextDeserializer{T}"/> and manages
/// accessor state via a disposable scope.
/// </summary>
/// <typeparam name="T">The typed request context to propagate.</typeparam>
public sealed class RequestContextPropagator<T> : IRequestContextPropagator<T>
    where T : class, ITypedRequestContext
{
    private readonly IRequestContextDeserializer<T> _deserializer;
    private readonly IRequestContextAccessor _accessor;

    /// <summary>
    /// Initializes a new instance of <see cref="RequestContextPropagator{T}"/>.
    /// </summary>
    public RequestContextPropagator(
        IRequestContextDeserializer<T> deserializer,
        IRequestContextAccessor accessor)
    {
        _deserializer = deserializer;
        _accessor = accessor;
    }

    /// <inheritdoc />
    public IDisposable Propagate(IReadOnlyDictionary<string, string> metadata)
    {
        _accessor.Current = _deserializer.Deserialize(metadata);
        return new PropagationScope(_accessor);
    }

    private sealed class PropagationScope : IDisposable
    {
        private readonly IRequestContextAccessor _accessor;

        internal PropagationScope(IRequestContextAccessor accessor)
            => _accessor = accessor;

        public void Dispose() => _accessor.Current = null;
    }
}
