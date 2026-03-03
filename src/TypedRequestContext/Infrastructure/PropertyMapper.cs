using System.ComponentModel;
using System.Reflection;
using System.Security.Claims;
using TypedRequestContext;

namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Internal helper that maps a single property from a claim or header source.
/// Built once per property via reflection, then reused for every request.
/// </summary>
internal sealed class PropertyMapper
{
    private readonly PropertyInfo _property;
    private readonly Func<HttpContext, string?> _extract;
    private readonly bool _required;

    private PropertyMapper(
        PropertyInfo property,
        Func<HttpContext, string?> extract,
        bool required)
    {
        _property = property;
        _extract = extract;
        _required = required;
    }

    /// <summary>
    /// Builds a mapper for the given property, or returns null if
    /// the property has no <see cref="FromClaimAttribute"/> or <see cref="FromHeaderAttribute"/>.
    /// </summary>
    public static PropertyMapper? From(PropertyInfo property)
    {
        var fromClaim = property.GetCustomAttribute<FromClaimAttribute>();
        var fromHeader = property.GetCustomAttribute<FromHeaderAttribute>();

        if (fromClaim is null && fromHeader is null)
            return null;

        var required = property.GetCustomAttribute<RequiredContextValueAttribute>() is not null;

        Func<HttpContext, string?> extract = fromClaim is not null
            ? http => http.User.FindFirstValue(fromClaim.ClaimName)
            : http => http.Request.Headers.TryGetValue(fromHeader!.HeaderName, out var val)
                ? val.FirstOrDefault()
                : null;

        return new PropertyMapper(property, extract, required);
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
                var isClaim = _property.GetCustomAttribute<FromClaimAttribute>() is not null;
                return isClaim
                    ? PropertyMapperResult.MissingClaim(_property.Name)
                    : PropertyMapperResult.MissingHeader(_property.Name);
            }

            return PropertyMapperResult.Success;
        }

        var converted = ConvertValue(raw, _property.PropertyType);
        if (converted is null)
        {
            if (_required)
            {
                var isClaim = _property.GetCustomAttribute<FromClaimAttribute>() is not null;
                return isClaim
                    ? PropertyMapperResult.InvalidClaim(_property.Name)
                    : PropertyMapperResult.InvalidHeader(_property.Name);
            }

            return PropertyMapperResult.Success;
        }

        _property.SetValue(instance, converted);
        return PropertyMapperResult.Success;
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
        => new() { IsSuccess = false, StatusCode = 401, PropertyName = propertyName };

    /// <summary>A required header was missing — should return 403.</summary>
    public static PropertyMapperResult MissingHeader(string propertyName)
        => new() { IsSuccess = false, StatusCode = 403, PropertyName = propertyName };

    /// <summary>A required claim exists but has invalid format — should return 401.</summary>
    public static PropertyMapperResult InvalidClaim(string propertyName)
        => new() { IsSuccess = false, StatusCode = 401, PropertyName = propertyName };

    /// <summary>A required header exists but has invalid format — should return 403.</summary>
    public static PropertyMapperResult InvalidHeader(string propertyName)
        => new() { IsSuccess = false, StatusCode = 403, PropertyName = propertyName };

    /// <summary>Whether the extraction succeeded.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>HTTP status code to return on failure (401 or 403).</summary>
    public int StatusCode { get; init; }

    /// <summary>The name of the property that failed extraction.</summary>
    public string? PropertyName { get; init; }
}
