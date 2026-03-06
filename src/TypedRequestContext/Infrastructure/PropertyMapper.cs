using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;

namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Internal helper that maps a single property from a claim or header source.
/// Built once per property via reflection, then reused for every request.
/// </summary>
internal sealed class PropertyMapper
{
    private enum ContextValueSource
    {
        Claim,
        Header
    }

    private readonly string _propertyName;
    private readonly Type _propertyType;
    private readonly Func<HttpContext, string?> _extract;
    private readonly Action<object, object?> _setter;
    private readonly bool _required;
    private readonly ContextValueSource _source;

    private PropertyMapper(
        string propertyName,
        Type propertyType,
        Func<HttpContext, string?> extract,
        Action<object, object?> setter,
        bool required,
        ContextValueSource source)
    {
        _propertyName = propertyName;
        _propertyType = propertyType;
        _extract = extract;
        _setter = setter;
        _required = required;
        _source = source;
    }

    /// <summary>
    /// Builds a mapper for the given property, or returns null if
    /// the property has no <see cref="FromClaimAttribute"/> or <see cref="FromHeaderAttribute"/>.
    /// </summary>
    public static PropertyMapper? From(PropertyInfo property)
    {
        var fromClaim = property.GetCustomAttribute<FromClaimAttribute>();
        var fromHeader = property.GetCustomAttribute<FromHeaderAttribute>();

        if (fromClaim is not null && fromHeader is not null)
        {
            throw new InvalidOperationException(
                $"Property '{property.Name}' on '{property.DeclaringType?.Name}' cannot have both {nameof(FromClaimAttribute)} and {nameof(FromHeaderAttribute)}.");
        }

        if (fromClaim is null && fromHeader is null)
            return null;

        var required = property.GetCustomAttribute<RequiredContextValueAttribute>() is not null;

        Func<HttpContext, string?> extract = fromClaim is not null
            ? http => http.User.FindFirstValue(fromClaim.ClaimName)
            : http => http.Request.Headers.TryGetValue(fromHeader!.HeaderName, out var val)
                ? val.FirstOrDefault()
                : null;

        var source = fromClaim is not null ? ContextValueSource.Claim : ContextValueSource.Header;
        var setter = BuildSetter(property);

        return new PropertyMapper(property.Name, property.PropertyType, extract, setter, required, source);
    }

    /// <summary>
    /// Extracts the value from the HTTP context and sets it on the target instance.
    /// </summary>
    /// <returns>
    /// A <see cref="PropertyMapperResult"/> indicating success or the kind of failure.
    /// </returns>
    public PropertyMapperResult Apply(object instance, HttpContext http)
    {
        var raw = _extract(http);

        if (string.IsNullOrEmpty(raw))
        {
            if (_required)
            {
                return _source == ContextValueSource.Claim
                    ? PropertyMapperResult.MissingClaim(_propertyName)
                    : PropertyMapperResult.MissingHeader(_propertyName);
            }

            return PropertyMapperResult.Success;
        }

        var converted = ConvertValue(raw, _propertyType);
        if (converted is null)
        {
            if (_required)
            {
                return _source == ContextValueSource.Claim
                    ? PropertyMapperResult.InvalidClaim(_propertyName)
                    : PropertyMapperResult.InvalidHeader(_propertyName);
            }

            return PropertyMapperResult.Success;
        }

        _setter(instance, converted);
        return PropertyMapperResult.Success;
    }

    /// <summary>
    /// Builds a compiled setter delegate for the given property.
    /// Falls back to reflection-based SetValue for init-only properties
    /// where the set method cannot be compiled into a delegate.
    /// </summary>
    internal static Action<object, object?> BuildSetter(PropertyInfo property)
    {
        try
        {
            var instanceParam = Expression.Parameter(typeof(object), "instance");
            var valueParam = Expression.Parameter(typeof(object), "value");

            var castInstance = Expression.Convert(instanceParam, property.DeclaringType!);
            var castValue = Expression.Convert(valueParam, property.PropertyType);
            var propertyAccess = Expression.Property(castInstance, property);
            var assign = Expression.Assign(propertyAccess, castValue);

            return Expression.Lambda<Action<object, object?>>(assign, instanceParam, valueParam).Compile();
        }
        catch
        {
            return property.SetValue;
        }
    }

    internal static object? ConvertValue(string raw, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying == typeof(string))
            return raw;

        if (underlying == typeof(Guid))
            return Guid.TryParse(raw, out var guid) ? guid : null;

        if (underlying.IsEnum)
            return Enum.TryParse(underlying, raw, ignoreCase: true, out var enumVal) ? enumVal : null;

        var converter = TypeDescriptor.GetConverter(underlying);
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

/// <summary>
/// Result of applying a property mapper to an HTTP context.
/// </summary>
internal readonly record struct PropertyMapperResult
{
    /// <summary>Successful extraction (or optional value was absent).</summary>
    public static readonly PropertyMapperResult Success = new() { IsSuccess = true };

    /// <summary>A required claim was missing — should return 401.</summary>
    public static PropertyMapperResult MissingClaim(string propertyName)
        => new() { IsSuccess = false, StatusCode = 401, PropertyName = propertyName, FailureKind = RequestContextFailureKind.Missing };

    /// <summary>A required header was missing — should return 400.</summary>
    public static PropertyMapperResult MissingHeader(string propertyName)
        => new() { IsSuccess = false, StatusCode = 400, PropertyName = propertyName, FailureKind = RequestContextFailureKind.Missing };

    /// <summary>A required claim exists but has invalid format — should return 401.</summary>
    public static PropertyMapperResult InvalidClaim(string propertyName)
        => new() { IsSuccess = false, StatusCode = 401, PropertyName = propertyName, FailureKind = RequestContextFailureKind.Invalid };

    /// <summary>A required header exists but has invalid format — should return 400.</summary>
    public static PropertyMapperResult InvalidHeader(string propertyName)
        => new() { IsSuccess = false, StatusCode = 400, PropertyName = propertyName, FailureKind = RequestContextFailureKind.Invalid };

    /// <summary>Whether the extraction succeeded.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>HTTP status code to return on failure (401 or 400).</summary>
    public int StatusCode { get; init; }

    /// <summary>The name of the property that failed extraction.</summary>
    public string? PropertyName { get; init; }

    /// <summary>Whether the value was missing or invalid.</summary>
    public RequestContextFailureKind FailureKind { get; init; }
}

internal enum RequestContextFailureKind
{
    Missing,
    Invalid
}
