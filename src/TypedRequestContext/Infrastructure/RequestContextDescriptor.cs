namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Endpoint metadata declaring which <see cref="ITypedRequestContext"/>
/// type is required for the endpoint.
/// </summary>
public sealed record RequestContextDescriptor(Type ContextType);
