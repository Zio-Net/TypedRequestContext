using TypedRequestContext;
using TypedRequestContext.Infrastructure;

namespace TypedRequestContext;

/// <summary>
/// Endpoint extensions for declaring the required request context type.
/// </summary>
public static class RequestContextEndpointExtensions
{
    /// <summary>
    /// Declares the required context type for a single endpoint.
    /// </summary>
    public static RouteHandlerBuilder WithRequestContext<TContext>(this RouteHandlerBuilder builder)
        where TContext : class, ITypedRequestContext
    {
        return builder.WithMetadata(new RequestContextDescriptor(typeof(TContext)));
    }

    /// <summary>
    /// Declares the default context type for all endpoints in a group.
    /// Individual endpoints can override by calling <see cref="WithRequestContext{TContext}(RouteHandlerBuilder)"/>
    /// on the endpoint.
    /// </summary>
    public static RouteGroupBuilder WithRequestContext<TContext>(this RouteGroupBuilder builder)
        where TContext : class, ITypedRequestContext
    {
        return builder.WithMetadata(new RequestContextDescriptor(typeof(TContext)));
    }
}
