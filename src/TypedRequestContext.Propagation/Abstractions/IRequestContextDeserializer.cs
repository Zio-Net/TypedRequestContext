using TypedRequestContext;

namespace TypedRequestContext.Propagation;

/// <summary>
/// Per-type deserializer that creates a typed request context from a metadata dictionary.
/// Intended for non-HTTP flows such as queues, events, or background processing.
/// </summary>
/// <typeparam name="T">The typed request context to deserialize.</typeparam>
public interface IRequestContextDeserializer<out T> where T : class, ITypedRequestContext
{
    /// <summary>
    /// Deserializes the typed context from metadata.
    /// </summary>
    T Deserialize(IReadOnlyDictionary<string, string> metadata);
}
