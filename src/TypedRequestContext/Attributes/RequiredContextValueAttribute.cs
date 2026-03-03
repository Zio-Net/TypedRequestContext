namespace TypedRequestContext;

/// <summary>
/// Marks a property as required. When the source value is missing or empty,
/// the middleware will short-circuit with 401 (claim source) or 403 (header source).
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class RequiredContextValueAttribute : Attribute;
