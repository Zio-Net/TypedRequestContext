using System.Reflection;

namespace TypedRequestContext.Propagation.Infrastructure;

/// <summary>
/// Default <see cref="IRequestContextDeserializer{T}"/> implementation that uses
/// <see cref="PropagationKeyAttribute"/> on context properties.
/// </summary>
/// <typeparam name="T">The typed request context to deserialize.</typeparam>
public sealed class AttributeBasedRequestContextDeserializer<T> : IRequestContextDeserializer<T>
    where T : class, ITypedRequestContext
{
    private static readonly PropagationKeyMapper[] _mappers = BuildMappers();
    private static readonly Func<T> _factory = BuildFactory();

    /// <inheritdoc />
    public T Deserialize(IReadOnlyDictionary<string, string> metadata)
    {
        var instance = _factory();

        foreach (var mapper in _mappers)
            mapper.Apply(instance, metadata);

        return instance;
    }

    private static Func<T> BuildFactory()
    {
        var ctor = typeof(T).GetConstructor(Type.EmptyTypes);
        if (ctor is null || !ctor.IsPublic)
        {
            throw new InvalidOperationException(
                $"Type '{typeof(T).Name}' must have a public parameterless constructor when using '{nameof(AttributeBasedRequestContextDeserializer<T>)}'. " +
                $"Provide one, or register a custom deserializer via AddTypedRequestContext<{typeof(T).Name}>(b => b.UseDeserializer<...>()).");
        }

        return Activator.CreateInstance<T>;
    }

    private static PropagationKeyMapper[] BuildMappers()
        => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(PropagationKeyMapper.From)
            .Where(m => m is not null)
            .ToArray()!;

    private sealed class PropagationKeyMapper
    {
        private readonly PropertyInfo _property;
        private readonly string _key;
        private readonly bool _required;

        private PropagationKeyMapper(
            PropertyInfo property,
            string key,
            bool required)
        {
            _property = property;
            _key = key;
            _required = required;
        }

        public static PropagationKeyMapper? From(PropertyInfo property)
        {
            var propagationKey = property.GetCustomAttribute<PropagationKeyAttribute>();
            if (propagationKey is null)
                return null;

            var required = property.GetCustomAttribute<RequiredContextValueAttribute>() is not null;
            return new PropagationKeyMapper(property, propagationKey.Key, required);
        }

        public void Apply(object instance, IReadOnlyDictionary<string, string> metadata)
        {
            metadata.TryGetValue(_key, out var raw);

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (_required)
                {
                    throw new RequestContextDeserializationException(
                        $"Required context value '{_property.Name}' is missing in metadata key '{_key}'.");
                }

                return;
            }

            var converted = ConvertValue(raw, _property.PropertyType);
            if (converted is null)
            {
                if (_required)
                {
                    throw new RequestContextDeserializationException(
                        $"Context value for '{_property.Name}' from metadata key '{_key}' is invalid.");
                }

                return;
            }

            _property.SetValue(instance, converted);
        }

        private static object? ConvertValue(string raw, Type targetType)
        {
            var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (underlying == typeof(string))
                return raw;

            if (underlying == typeof(Guid))
                return Guid.TryParse(raw, out var guid) ? guid : null;

            if (underlying.IsEnum)
                return Enum.TryParse(underlying, raw, ignoreCase: true, out var enumVal) ? enumVal : null;

            var converter = System.ComponentModel.TypeDescriptor.GetConverter(underlying);
            if (converter.CanConvertFrom(typeof(string)))
            {
                try
                {
                    return converter.ConvertFromInvariantString(raw);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }
}
