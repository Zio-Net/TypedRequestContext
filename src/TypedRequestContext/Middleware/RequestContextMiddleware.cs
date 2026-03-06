using System.Diagnostics;
using TypedRequestContext.Infrastructure;

namespace TypedRequestContext;

/// <summary>
/// Middleware that handles per-request correlation ID (when enabled) and
/// reads <see cref="RequestContextDescriptor"/> from endpoint metadata,
/// invokes the registered extractor to create the typed request context,
/// and stores it in <see cref="IRequestContextAccessor"/>.
/// </summary>
/// <remarks>
/// Must be registered AFTER authentication and authorization middleware.
/// Endpoints without a <see cref="RequestContextDescriptor"/> are passed through (no-op).
/// Serialization into propagation headers (when the propagation package is used)
/// happens lazily at outbound call time.
/// </remarks>
public sealed class RequestContextMiddleware(
    RequestDelegate next,
    Dictionary<Type, Func<HttpContext, ITypedRequestContext>> extractors,
    bool correlationEnabled,
    RequestContextScopeFactory scopeFactory,
    ILogger<RequestContextMiddleware> logger)
{
    private readonly RequestDelegate _next = next;
    private readonly Dictionary<Type, Func<HttpContext, ITypedRequestContext>> _extractors = extractors;
    private readonly bool _correlationEnabled = correlationEnabled;
    private readonly RequestContextScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<RequestContextMiddleware> _logger = logger;

    /// <summary>
    /// Processes the HTTP request: (1) correlation ID handling, (2) typed context extraction.
    /// </summary>
    public async Task InvokeAsync(HttpContext httpContext)
    {
        // Step 1: Unconditional CorrelationId handling (when enabled via AddCorrelationId())
        if (_correlationEnabled)
        {
            var correlationId =
                httpContext.Request.Headers["x-correlation-id"].FirstOrDefault()
                ?? Activity.Current?.TraceId.ToString()
                ?? Guid.NewGuid().ToString("N");

            CorrelationContext.Set(correlationId);
            httpContext.Response.Headers["x-correlation-id"] = correlationId;
        }

        // Step 2: Per-endpoint typed context extraction
        var endpoint = httpContext.GetEndpoint();

        // Last-wins: endpoint-level descriptor overrides group-level
        var descriptors = endpoint?.Metadata.GetOrderedMetadata<RequestContextDescriptor>();
        var descriptor = descriptors is { Count: > 0 } ? descriptors[^1] : null;

        if (descriptor is null)
        {
            await _next(httpContext);
            return;
        }

        try
        {
            if (!_extractors.TryGetValue(descriptor.ContextType, out var extract))
            {
                throw new InvalidOperationException(
                    $"No extractor registered for context type '{descriptor.ContextType.Name}'. " +
                    $"Ensure AddRequestContext<{descriptor.ContextType.Name}>() was called during service registration.");
            }

            // Invoke the pre-built extractor delegate to create the typed context
            var requestContext = extract(httpContext);

            // Store in typed accessor via scope — automatically cleared on dispose.
            // Pass request-scoped provider so validators can use scoped dependencies.
            using var scope = _scopeFactory.Begin(requestContext, httpContext.RequestServices);

            _logger.LogDebug(
                "Request context set: Type={ContextType}",
                descriptor.ContextType.Name);

            await _next(httpContext);
        }
        catch (RequestContextValidationException ex)
        {
            _logger.LogWarning(
                "Request context validation failed: {ErrorCount} error(s)",
                ex.Errors.Count);

            httpContext.Response.StatusCode = 400;
            await httpContext.Response.WriteAsJsonAsync(new
            {
                message = ex.Message,
                errors = ex.Errors
                    .GroupBy(e => e.MemberName ?? "$")
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray())
            });
        }
        catch (RequestContextCreationException ex)
        {
            _logger.LogWarning(
                "Request context creation failed: {Message}",
                ex.Message);

            httpContext.Response.StatusCode = ex.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(new { message = ex.Message, });
        }
        finally
        {
            if (_correlationEnabled)
                CorrelationContext.Clear();
        }
    }
}
