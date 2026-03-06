namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Fluent builder for configuring a typed request context registration.
/// Allows overriding the default extractor per context type.
/// </summary>
/// <typeparam name="TContext">The typed request context being configured.</typeparam>
public sealed class RequestContextBuilder<TContext>
    where TContext : class, ITypedRequestContext
{
    /// <summary>
    /// The custom extractor type, or null to use the default attribute-based extractor.
    /// </summary>
    internal Type? ExtractorType { get; private set; }

    /// <summary>
    /// The custom deserializer type configured for this context type.
    /// This is consumed by optional extension packages.
    /// </summary>
    internal Type? DeserializerType { get; private set; }

    /// <summary>
    /// The custom serializer type configured for this context type.
    /// This is consumed by optional extension packages.
    /// </summary>
    internal Type? SerializerType { get; private set; }

    /// <summary>
    /// Whether validation is enabled for this context type.
    /// </summary>
    internal bool ValidationEnabled { get; private set; }

    /// <summary>
    /// The custom validator type, or null to use the default DataAnnotations validator.
    /// </summary>
    internal Type? ValidatorType { get; private set; }

    /// <summary>
    /// Registers a custom extractor for this context type.
    /// If not called, the default <see cref="AttributeBasedRequestContextExtractor{T}"/> is used.
    /// </summary>
    public RequestContextBuilder<TContext> UseExtractor<TExtractor>()
        where TExtractor : class, IRequestContextExtractor<TContext>
    {
        ExtractorType = typeof(TExtractor);
        return this;
    }

    /// <summary>
    /// Registers a custom deserializer type for this context type.
    /// </summary>
    public RequestContextBuilder<TContext> UseDeserializer(Type deserializerType)
    {
        ArgumentNullException.ThrowIfNull(deserializerType);
        DeserializerType = deserializerType;
        return this;
    }

    /// <summary>
    /// Registers a custom deserializer type for this context type.
    /// </summary>
    public RequestContextBuilder<TContext> UseDeserializer<TDeserializer>()
        => UseDeserializer(typeof(TDeserializer));

    /// <summary>
    /// Registers a custom serializer type for this context type.
    /// </summary>
    public RequestContextBuilder<TContext> UseSerializer(Type serializerType)
    {
        ArgumentNullException.ThrowIfNull(serializerType);
        SerializerType = serializerType;
        return this;
    }

    /// <summary>
    /// Registers a custom serializer type for this context type.
    /// </summary>
    public RequestContextBuilder<TContext> UseSerializer<TSerializer>()
        => UseSerializer(typeof(TSerializer));

    /// <summary>
    /// Enables validation using the default DataAnnotations validator.
    /// </summary>
    public RequestContextBuilder<TContext> EnableValidation()
    {
        ValidationEnabled = true;
        return this;
    }

    /// <summary>
    /// Enables validation using DataAnnotations attributes.
    /// Equivalent to <see cref="EnableValidation"/>.
    /// </summary>
    public RequestContextBuilder<TContext> UseDataAnnotationsValidation()
        => EnableValidation();

    /// <summary>
    /// Enables validation using a custom validator implementation.
    /// </summary>
    public RequestContextBuilder<TContext> UseValidation<TValidator>()
        where TValidator : class, IRequestContextValidator<TContext>
    {
        ValidationEnabled = true;
        ValidatorType = typeof(TValidator);
        return this;
    }
}
