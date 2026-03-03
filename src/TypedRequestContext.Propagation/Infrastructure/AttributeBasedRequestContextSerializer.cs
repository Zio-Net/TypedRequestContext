using System.Reflection;
using System.Globalization;
using TypedRequestContext;
using TypedRequestContext.Propagation;

namespace TypedRequestContext.Propagation.Infrastructure;

/// <summary>
/// Default <see cref="IRequestContextSerializer{T}"/> implementation that uses
/// <see cref="PropagationKeyAttribute"/> to serialize typed contexts into propagation headers.
/// </summary>
/// <remarks>
/// Properties without <see cref="PropagationKeyAttribute"/> are not serialized — they remain local
/// to the manager. Properties with a <see langword="null"/> value at runtime are omitted.
/// Reflection cost is paid once per context type at class initialization.
/// </remarks>
/// <typeparam name="T">The typed request context to serialize.</typeparam>
public sealed class AttributeBasedRequestContextSerializer<T> : IRequestContextSerializer<T>
    where T : class, ITypedRequestContext
{
    private static readonly (PropertyInfo Property, string HeaderName)[] _map =
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (p, p.GetCustomAttribute<PropagationKeyAttribute>()?.Key))
            .Where(x => x.Key is not null)
            .Select(x => (x.p, x.Key!))
            .ToArray();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, string> Serialize(T context)
    {
        var result = new Dictionary<string, string>(_map.Length);

        foreach (var (property, headerName) in _map)
        {
            var value = property.GetValue(context);
            if (value is not null)
                result[headerName] = value is IFormattable formattable
                    ? formattable.ToString(format: null, CultureInfo.InvariantCulture)
                    : value.ToString()!;
        }

        return result;
    }
}
