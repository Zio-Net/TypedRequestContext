namespace TypedRequestContext;

/// <summary>
/// Marks a property to be populated from an HTTP request header by the
/// default attribute-based request context factory.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromHeaderAttribute(string headerName) : Attribute
{
    /// <summary>
    /// The name of the HTTP request header to extract the value from.
    /// </summary>
    public string HeaderName { get; } = headerName;
}
