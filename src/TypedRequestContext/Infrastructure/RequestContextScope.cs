namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Disposable scope that sets a typed request context on creation
/// and clears it on disposal. Used by both HTTP middleware and
/// queue/event propagation channels via <see cref="RequestContextScopeFactory"/>.
/// </summary>
internal sealed class RequestContextScope : IDisposable
{
    private readonly IRequestContextAccessor _accessor;

    internal RequestContextScope(IRequestContextAccessor accessor, ITypedRequestContext context)
    {
        _accessor = accessor;
        _accessor.Current = context;
    }

    public void Dispose() => _accessor.Current = null;
}
