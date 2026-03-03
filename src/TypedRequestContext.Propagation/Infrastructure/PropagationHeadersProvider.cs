using System.Reflection;
using TypedRequestContext;
using TypedRequestContext.Propagation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TypedRequestContext.Propagation.Infrastructure;

/// <summary>
/// Internal implementation of <see cref="IPropagationHeadersProvider"/>.
/// Reads the current typed context from <see cref="IRequestContextAccessor"/>,
/// resolves the correct serializer via a lazily-built delegate map,
/// and serializes on the spot — no eager pre-serialization, no second AsyncLocal.
/// </summary>
internal sealed class PropagationHeadersProvider : IPropagationHeadersProvider
{
    private readonly IRequestContextAccessor _contextAccessor;
    private readonly ICorrelationContext? _correlationContext;
    private readonly Lazy<Dictionary<Type, Func<ITypedRequestContext, IReadOnlyDictionary<string, string>>>> _serializers;

    public PropagationHeadersProvider(
        IRequestContextAccessor contextAccessor,
        IOptions<RequestContextOptions> options,
        IServiceProvider serviceProvider,
        ICorrelationContext? correlationContext = null)
    {
        _contextAccessor = contextAccessor;
        _correlationContext = correlationContext;

        _serializers = new Lazy<Dictionary<Type, Func<ITypedRequestContext, IReadOnlyDictionary<string, string>>>>(
            () => BuildSerializerMap(options.Value, serviceProvider));
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> GetCurrentHeaders()
    {
        var headers = new Dictionary<string, string>();

        if (_correlationContext is not null)
            headers["x-correlation-id"] = _correlationContext.CorrelationId;

        var context = _contextAccessor.Current;
        if (context is not null && _serializers.Value.TryGetValue(context.GetType(), out var serialize))
        {
            foreach (var (key, value) in serialize(context))
                headers[key] = value;
        }

        return headers;
    }

    private static Dictionary<Type, Func<ITypedRequestContext, IReadOnlyDictionary<string, string>>> BuildSerializerMap(
        RequestContextOptions options,
        IServiceProvider serviceProvider)
    {
        var map = new Dictionary<Type, Func<ITypedRequestContext, IReadOnlyDictionary<string, string>>>();

        foreach (var contextType in options.ContextTypes)
        {
            object serializer;
            if (options.SerializerTypes.TryGetValue(contextType, out var customSerializerType))
            {
                serializer = ActivatorUtilities.CreateInstance(serviceProvider, customSerializerType);
            }
            else
            {
                var serializerType = typeof(IRequestContextSerializer<>).MakeGenericType(contextType);
                serializer = serviceProvider.GetRequiredService(serializerType);
            }

            var serializeMethod = typeof(PropagationHeadersProvider)
                .GetMethod(nameof(CreateDelegate), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(contextType);

            var del = (Func<ITypedRequestContext, IReadOnlyDictionary<string, string>>)
                serializeMethod.Invoke(null, [serializer])!;

            map[contextType] = del;
        }

        return map;
    }

    private static Func<ITypedRequestContext, IReadOnlyDictionary<string, string>> CreateDelegate<T>(
        IRequestContextSerializer<T> serializer)
        where T : class, ITypedRequestContext
    {
        return context => serializer.Serialize((T)context);
    }
}
