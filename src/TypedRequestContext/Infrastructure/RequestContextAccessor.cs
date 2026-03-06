namespace TypedRequestContext.Infrastructure;

/// <summary>
/// AsyncLocal-backed implementation of <see cref="IRequestContextAccessor"/>.
/// Set by middleware, read by handlers and domain services.
/// </summary>
public sealed class RequestContextAccessor : IRequestContextAccessor
{
    private static readonly AsyncLocal<ITypedRequestContext?> _current = new();

    /// <inheritdoc />
    public ITypedRequestContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <inheritdoc />
    public T GetRequired<T>() where T : class, ITypedRequestContext
    {
        if (_current.Value is T typed)
            return typed;

        throw new InvalidOperationException(
            $"RequestContext of type {typeof(T).Name} is not available for the current request.");
    }
}
