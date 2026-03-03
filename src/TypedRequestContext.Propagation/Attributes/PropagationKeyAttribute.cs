namespace TypedRequestContext.Propagation;

/// <summary>
/// Marks a property with the wire-format key used for context propagation.
/// The default serializer writes this key outbound, and the default deserializer
/// reads this same key inbound from transport metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PropagationKeyAttribute(string key) : Attribute
{
    /// <summary>
    /// The wire-format key used for propagation.
    /// </summary>
    public string Key { get; } = key;
}
