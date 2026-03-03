namespace TypedRequestContext;

/// <summary>
/// Marks a property to be populated from a JWT claim by the default
/// attribute-based request context factory.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FromClaimAttribute(string claimName) : Attribute
{
    /// <summary>
    /// The name of the JWT claim to extract the value from.
    /// </summary>
    public string ClaimName { get; } = claimName;
}
