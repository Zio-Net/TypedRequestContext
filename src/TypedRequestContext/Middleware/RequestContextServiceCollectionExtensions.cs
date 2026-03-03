using System.Reflection;
using TypedRequestContext;
using TypedRequestContext.Infrastructure;
using Microsoft.Extensions.Options;

namespace TypedRequestContext;

/// <summary>
/// Extension methods for registering the request context framework.
/// </summary>
public static class RequestContextServiceCollectionExtensions
{
    /// <summary>
    /// Registers core framework singletons and default open-generic extractor.
    /// Call once during service configuration.
    /// </summary>
    public static IServiceCollection AddTypedRequestContext(this IServiceCollection services)
    {
        services.AddSingleton<IRequestContextAccessor, RequestContextAccessor>();

        services.AddSingleton(
            typeof(IRequestContextExtractor<>),
            typeof(AttributeBasedRequestContextExtractor<>));

        return services;
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="AddTypedRequestContext(IServiceCollection)"/>.
    /// </summary>
    public static IServiceCollection AddRequestContextFramework(this IServiceCollection services)
        => services.AddTypedRequestContext();

    /// <summary>
    /// Registers the always-on CorrelationId module. Once registered, CorrelationId flows
    /// on every request unconditionally — no endpoint metadata required.
    /// </summary>
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
    {
        services.AddSingleton<CorrelationContext>();
        services.AddSingleton<ICorrelationContext>(sp =>
            sp.GetRequiredService<CorrelationContext>());

        return services;
    }

    /// <summary>
    /// Registers a typed request context with optional custom extractor/serializer via fluent builder.
    /// Also registers a scoped DI shortcut so handlers can inject <typeparamref name="TContext"/> directly.
    /// Tracks the context type so <see cref="UseRequestContext"/> can build delegate maps at startup.
    /// </summary>
    public static IServiceCollection AddTypedRequestContext<TContext>(
        this IServiceCollection services,
        Action<RequestContextBuilder<TContext>>? configure = null)
        where TContext : class, ITypedRequestContext
    {
        var builder = new RequestContextBuilder<TContext>();
        configure?.Invoke(builder);

        if (builder.ExtractorType is not null)
            services.AddTransient(typeof(IRequestContextExtractor<TContext>), builder.ExtractorType);

        services.AddScoped<TContext>(sp =>
            sp.GetRequiredService<IRequestContextAccessor>()
              .GetRequired<TContext>());

        services.Configure<RequestContextOptions>(opts =>
        {
            opts.ContextTypes.Add(typeof(TContext));

            if (builder.SerializerType is not null)
                opts.SerializerTypes[typeof(TContext)] = builder.SerializerType;

            if (builder.DeserializerType is not null)
                opts.DeserializerTypes[typeof(TContext)] = builder.DeserializerType;
        });

        return services;
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="AddTypedRequestContext{TContext}(IServiceCollection, Action{RequestContextBuilder{TContext}}?)"/>.
    /// </summary>
    public static IServiceCollection AddRequestContext<TContext>(
        this IServiceCollection services,
        Action<RequestContextBuilder<TContext>>? configure = null)
        where TContext : class, ITypedRequestContext
        => services.AddTypedRequestContext(configure);

    /// <summary>
    /// Builds the internal extractor delegate map, detects correlation registration, and adds the middleware.
    /// Must be called AFTER <c>UseAuthentication()</c> and <c>UseAuthorization()</c>.
    /// </summary>
    public static IApplicationBuilder UseTypedRequestContext(this IApplicationBuilder app)
    {
        var options = app.ApplicationServices
            .GetRequiredService<IOptions<RequestContextOptions>>().Value;

        // Build internal extractor delegate map — one entry per registered context type.
        // MakeGenericMethod used once per type at startup to bridge Type → generic.
        var extractors = new Dictionary<Type, Func<HttpContext, ITypedRequestContext>>();

        var buildExtractorMethod = typeof(RequestContextServiceCollectionExtensions)
            .GetMethod(nameof(BuildExtractorDelegate), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var contextType in options.ContextTypes)
        {
            var extractorDelegate = buildExtractorMethod
                .MakeGenericMethod(contextType)
                .Invoke(null, null);
            extractors[contextType] = (Func<HttpContext, ITypedRequestContext>)extractorDelegate!;
        }

        // Detect whether AddCorrelationId() was called
        var correlationEnabled = app.ApplicationServices
            .GetService<CorrelationContext>() is not null;

        // Pass the pre-built extractor map and correlation flag to the middleware
        app.UseMiddleware<RequestContextMiddleware>(extractors, correlationEnabled);

        return app;
    }

    /// <summary>
    /// Backward-compatible alias for <see cref="UseTypedRequestContext(IApplicationBuilder)"/>.
    /// </summary>
    public static IApplicationBuilder UseRequestContext(this IApplicationBuilder app)
        => app.UseTypedRequestContext();

    private static Func<HttpContext, ITypedRequestContext> BuildExtractorDelegate<TContext>()
        where TContext : class, ITypedRequestContext
    {
        return httpContext =>
            httpContext.RequestServices
                .GetRequiredService<IRequestContextExtractor<TContext>>()
                .Extract(httpContext);
    }
}

/// <summary>
/// Options tracking which context types have been registered via
/// <see cref="RequestContextServiceCollectionExtensions.AddRequestContext{TContext}"/>.
/// </summary>
public sealed class RequestContextOptions
{
    /// <summary>
    /// The set of registered context types.
    /// </summary>
    public HashSet<Type> ContextTypes { get; } = [];

    /// <summary>
    /// Optional context-type to serializer-type mapping configured at registration time.
    /// </summary>
    public Dictionary<Type, Type> SerializerTypes { get; } = [];

    /// <summary>
    /// Optional context-type to deserializer-type mapping configured at registration time.
    /// </summary>
    public Dictionary<Type, Type> DeserializerTypes { get; } = [];
}
