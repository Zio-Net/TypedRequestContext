namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Disposable scope that sets a typed request context on creation
/// and restores the previous context on disposal. Supports nested scopes
/// (e.g., inner propagation within an active HTTP request context).
/// </summary>
internal sealed class RequestContextScope : IDisposable
{
    private readonly IRequestContextAccessor _accessor;
    private readonly ITypedRequestContext? _previous;

    internal RequestContextScope(IRequestContextAccessor accessor, ITypedRequestContext context)
    {
        _accessor = accessor;
        _previous = accessor.Current;
        _accessor.Current = context;
    }

    public void Dispose() => _accessor.Current = _previous;
}
