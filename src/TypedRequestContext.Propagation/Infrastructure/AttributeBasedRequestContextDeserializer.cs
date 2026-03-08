using System.Reflection;
using TypedRequestContext.Infrastructure;

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
        private readonly string _propertyName;
        private readonly Type _propertyType;
        private readonly string _key;
        private readonly Action<object, object?> _setter;
        private readonly bool _required;

        private PropagationKeyMapper(
            string propertyName,
            Type propertyType,
            string key,
            Action<object, object?> setter,
            bool required)
        {
            _propertyName = propertyName;
            _propertyType = propertyType;
            _key = key;
            _setter = setter;
            _required = required;
        }

        public static PropagationKeyMapper? From(PropertyInfo property)
        {
            var propagationKey = property.GetCustomAttribute<PropagationKeyAttribute>();
            if (propagationKey is null)
                return null;

            var required = property.GetCustomAttribute<RequiredContextValueAttribute>() is not null;
            var setter = PropertyMapper.BuildSetter(property);
            return new PropagationKeyMapper(property.Name, property.PropertyType, propagationKey.Key, setter, required);
        }

        public void Apply(object instance, IReadOnlyDictionary<string, string> metadata)
        {
            metadata.TryGetValue(_key, out var raw);

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (_required)
                {
                    throw new RequestContextDeserializationException(
                        $"Required context value '{_propertyName}' is missing in metadata key '{_key}'.");
                }

                return;
            }

            var converted = PropertyMapper.ConvertValue(raw, _propertyType);
            if (converted is null)
            {
                if (_required)
                {
                    throw new RequestContextDeserializationException(
                        $"Context value for '{_propertyName}' from metadata key '{_key}' is invalid.");
                }

                return;
            }

            _setter(instance, converted);
        }
    }
}
