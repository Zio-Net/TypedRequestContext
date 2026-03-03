using System.Reflection;
using TypedRequestContext;

namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Default <see cref="IRequestContextExtractor{T}"/> implementation that uses
/// <see cref="FromClaimAttribute"/> and <see cref="FromHeaderAttribute"/>
/// to build typed contexts via cached reflection.
/// </summary>
/// <remarks>
/// Because the class is generic, the mapper array is a static field per closed generic type —
/// no <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> needed.
/// Reflection cost is paid once per context type at class initialization.
/// </remarks>
/// <typeparam name="T">The typed request context to extract.</typeparam>
public sealed class AttributeBasedRequestContextExtractor<T> : IRequestContextExtractor<T>
    where T : class, ITypedRequestContext
{
    private static readonly PropertyMapper[] _mappers = BuildMappers();
    private static readonly Func<T> _factory = BuildFactory();

    /// <inheritdoc />
    /// <exception cref="RequestContextCreationException">
    /// Thrown when a required value is missing from the HTTP context.
    /// </exception>
    public T Extract(HttpContext httpContext)
    {
        var instance = _factory();

        foreach (var mapper in _mappers)
        {
            var result = mapper.Apply(instance, httpContext);
            if (!result.IsSuccess)
            {
                var reason = result.FailureKind == RequestContextFailureKind.Invalid ? "is invalid" : "is missing";
                throw new RequestContextCreationException(
                    result.StatusCode,
                    $"Required context value '{result.PropertyName}' {reason}.");
            }
        }

        return instance;
    }

    private static Func<T> BuildFactory()
    {
        var ctor = typeof(T).GetConstructor(Type.EmptyTypes);
        if (ctor is null || !ctor.IsPublic)
        {
            throw new InvalidOperationException(
                $"Type '{typeof(T).Name}' must have a public parameterless constructor when using '{nameof(AttributeBasedRequestContextExtractor<T>)}'. " +
                $"Provide one, or register a custom extractor via AddTypedRequestContext<{typeof(T).Name}>(b => b.UseExtractor<...>()).");
        }

        return Activator.CreateInstance<T>;
    }

    private static PropertyMapper[] BuildMappers()
        => typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(PropertyMapper.From)
            .Where(m => m is not null)
            .ToArray()!;
}
