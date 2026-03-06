namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Creates <see cref="RequestContextScope"/> instances that manage the lifecycle
/// of a typed request context on <see cref="IRequestContextAccessor"/>.
/// Both HTTP middleware and queue/event propagation channels use this factory,
/// ensuring a single place to manage and validate context activation.
/// </summary>
public sealed class RequestContextScopeFactory(IRequestContextAccessor accessor)
{
    private Dictionary<Type, Action<ITypedRequestContext>>? _validators;

    /// <summary>
    /// Configures the validator delegate map. Called once at startup by
    /// <see cref="RequestContextServiceCollectionExtensions.UseTypedRequestContext"/>.
    /// </summary>
    internal void SetValidators(Dictionary<Type, Action<ITypedRequestContext>> validators)
        => _validators = validators;

    /// <summary>
    /// Validates (if configured) and sets the given <paramref name="context"/> as the
    /// current request context. Returns a scope that clears it on disposal.
    /// </summary>
    /// <exception cref="RequestContextValidationException">
    /// Thrown when validation is enabled for the context type and the context is invalid.
    /// </exception>
    public IDisposable Begin(ITypedRequestContext context)
    {
        _validators?.GetValueOrDefault(context.GetType())?.Invoke(context);
        return new RequestContextScope(accessor, context);
    }
}
