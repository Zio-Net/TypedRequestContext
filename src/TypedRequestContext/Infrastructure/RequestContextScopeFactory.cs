namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Creates <see cref="RequestContextScope"/> instances that manage the lifecycle
/// of a typed request context on <see cref="IRequestContextAccessor"/>.
/// Both HTTP middleware and queue/event propagation channels use this factory,
/// ensuring a single place to manage (and later validate) context activation.
/// </summary>
public sealed class RequestContextScopeFactory(IRequestContextAccessor accessor)
{
    /// <summary>
    /// Sets the given <paramref name="context"/> as the current request context
    /// and returns a scope that clears it on disposal.
    /// </summary>
    public IDisposable Begin(ITypedRequestContext context)
    {
        return new RequestContextScope(accessor, context);
    }
}
