namespace TypedRequestContext;

/// <summary>
/// Per-type extractor: creates a typed request context from the HTTP context.
/// Pairs with propagation serializers in the optional propagation extension package.
/// </summary>
/// <typeparam name="T">The typed request context to extract.</typeparam>
public interface IRequestContextExtractor<out T> where T : class, ITypedRequestContext
{
    /// <summary>
    /// Extracts the typed context from the HTTP request (claims, headers, etc.).
    /// </summary>
    T Extract(HttpContext httpContext);
}
