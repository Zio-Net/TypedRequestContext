namespace TypedRequestContext;

/// <summary>
/// Provides ambient access to the current typed request context.
/// Set by middleware, read by handlers and domain services.
/// </summary>
public interface IRequestContextAccessor
{
    /// <summary>
    /// Gets or sets the current request context for the executing async flow.
    /// </summary>
    ITypedRequestContext? Current { get; set; }

    /// <summary>
    /// Gets the current context cast to <typeparamref name="T"/>,
    /// or throws if the context is not available or is a different type.
    /// </summary>
    T GetRequired<T>() where T : class, ITypedRequestContext;
}
