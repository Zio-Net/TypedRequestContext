using System.Reflection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace TypedRequestContext.Propagation.Infrastructure;

/// <summary>
/// Internal implementation of <see cref="IPropagationHeadersProvider"/>.
/// Reads the current typed context from <see cref="IRequestContextAccessor"/>,
/// resolves the correct serializer via a lazily-built delegate map,
/// and serializes on the spot — no eager pre-serialization, no second AsyncLocal.
/// </summary>
internal sealed class PropagationHeadersProvider(
    IRequestContextAccessor contextAccessor,
    IOptions<RequestContextOptions> options,
    IServiceScopeFactory scopeFactory,
    ICorrelationContext? correlationContext = null) : IPropagationHeadersProvider
{
    private readonly Lazy<Dictionary<Type, SerializerRegistration>> _serializerRegistrations = new(
            () => BuildSerializerMap(options.Value));
    private readonly ConcurrentDictionary<Type, object> _singletonSerializerCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, ITypedRequestContext, IReadOnlyDictionary<string, string>>> _invokerCache = new();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetCurrentHeaders()
    {
        var headers = new Dictionary<string, string>();

        if (correlationContext is not null)
            headers["x-correlation-id"] = correlationContext.CorrelationId;

        var context = contextAccessor.Current;
        if (context is not null && _serializerRegistrations.Value.TryGetValue(context.GetType(), out var registration))
        {
            var serializer = ResolveSerializer(context.GetType(), registration);
            var serialize = _invokerCache.GetOrAdd(context.GetType(), CreateInvoker);

            foreach (var (key, value) in serialize(serializer, context))
                headers[key] = value;
        }

        return headers;
    }

    private object ResolveSerializer(Type contextType, SerializerRegistration registration)
    {
        if (registration.IsSingleton)
        {
            return _singletonSerializerCache.GetOrAdd(contextType, _ =>
            {
                using var scope = scopeFactory.CreateScope();
                return registration.Resolve(scope.ServiceProvider);
            });
        }

        // Custom serializers may have scoped dependencies — create a scope each time
        using var scopeForCustom = scopeFactory.CreateScope();
        return registration.Resolve(scopeForCustom.ServiceProvider);
    }

    private static Dictionary<Type, SerializerRegistration> BuildSerializerMap(
        RequestContextOptions options)
    {
        var map = new Dictionary<Type, SerializerRegistration>();

        foreach (var contextType in options.ContextTypes)
        {
            if (options.SerializerTypes.TryGetValue(contextType, out var customSerializerType))
            {
                map[contextType] = new SerializerRegistration(
                    serviceProvider => ActivatorUtilities.CreateInstance(serviceProvider, customSerializerType),
                    IsSingleton: false);
            }
            else
            {
                var serializerType = typeof(IRequestContextSerializer<>).MakeGenericType(contextType);
                map[contextType] = new SerializerRegistration(
                    serviceProvider => serviceProvider.GetRequiredService(serializerType),
                    IsSingleton: true);
            }
        }

        return map;
    }

    private static Func<object, ITypedRequestContext, IReadOnlyDictionary<string, string>> CreateInvoker(Type contextType)
    {
        var createMethod = typeof(PropagationHeadersProvider)
            .GetMethod(nameof(CreateInvokerGeneric), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(contextType);

        return (Func<object, ITypedRequestContext, IReadOnlyDictionary<string, string>>)createMethod.Invoke(null, null)!;
    }

    private static Func<object, ITypedRequestContext, IReadOnlyDictionary<string, string>> CreateInvokerGeneric<T>()
        where T : class, ITypedRequestContext
    {
        return (serializer, context) => ((IRequestContextSerializer<T>)serializer).Serialize((T)context);
    }

    private sealed record SerializerRegistration(Func<IServiceProvider, object> Resolve, bool IsSingleton);
}
