using TypedRequestContext;

namespace TypedRequestContext.Propagation;

/// <summary>
/// Serializes a typed request context to key/value pairs
/// for propagation across service boundaries.
/// </summary>
/// <typeparam name="T">The typed request context to serialize.</typeparam>
public interface IRequestContextSerializer<in T> where T : class, ITypedRequestContext
{
    /// <summary>
    /// Converts the typed context into header key/value pairs.
    /// Properties decorated with <c>[PropagationKey]</c> are propagated; others are local-only.
    /// </summary>
    IReadOnlyDictionary<string, string> Serialize(T context);
}
