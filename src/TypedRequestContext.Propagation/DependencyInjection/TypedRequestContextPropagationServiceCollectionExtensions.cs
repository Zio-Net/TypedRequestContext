using TypedRequestContext;
using TypedRequestContext.Propagation;
using TypedRequestContext.Propagation.Infrastructure;

namespace TypedRequestContext.Propagation;

/// <summary>
/// Extension methods for registering propagation components.
/// </summary>
public static class TypedRequestContextPropagationServiceCollectionExtensions
{
    /// <summary>
    /// Registers propagation services and default serializer/deserializer implementations.
    /// </summary>
    public static IServiceCollection AddTypedRequestContextPropagation(this IServiceCollection services)
    {
        services.AddSingleton<IPropagationHeadersProvider, PropagationHeadersProvider>();

        services.AddSingleton(
            typeof(IRequestContextDeserializer<>),
            typeof(AttributeBasedRequestContextDeserializer<>));

        services.AddSingleton(
            typeof(IRequestContextSerializer<>),
            typeof(AttributeBasedRequestContextSerializer<>));

        return services;
    }
}
