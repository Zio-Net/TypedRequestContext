using System.Reflection;
using Microsoft.Extensions.Options;

namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Creates <see cref="RequestContextScope"/> instances that manage the lifecycle
/// of a typed request context on <see cref="IRequestContextAccessor"/>.
/// Both HTTP middleware and queue/event propagation channels use this factory,
/// ensuring a single place to manage and validate context activation.
/// Validation works automatically in both HTTP and non-HTTP hosts.
/// </summary>
/// <remarks>
/// Initializes a new instance of <see cref="RequestContextScopeFactory"/>.
/// Automatically builds the validator factory map from registered options.
/// </remarks>
public sealed class RequestContextScopeFactory(
    IRequestContextAccessor accessor,
    IServiceProvider rootProvider,
    IOptions<RequestContextOptions> options)
{
    private readonly Dictionary<Type, Func<IServiceProvider, Action<ITypedRequestContext>>> _validatorFactories = BuildValidatorFactories(options.Value);

    /// <summary>
    /// Validates (if configured) and sets the given <paramref name="context"/> as the
    /// current request context. Returns a scope that restores the previous context on disposal.
    /// Uses the root <see cref="IServiceProvider"/> to resolve validators.
    /// </summary>
    /// <exception cref="RequestContextValidationException">
    /// Thrown when validation is enabled for the context type and the context is invalid.
    /// </exception>
    public IDisposable Begin(ITypedRequestContext context)
        => Begin(context, rootProvider);

    /// <summary>
    /// Validates (if configured) and sets the given <paramref name="context"/> as the
    /// current request context. Returns a scope that restores the previous context on disposal.
    /// Uses the provided <paramref name="serviceProvider"/> to resolve validators,
    /// enabling scoped validator dependencies.
    /// </summary>
    /// <exception cref="RequestContextValidationException">
    /// Thrown when validation is enabled for the context type and the context is invalid.
    /// </exception>
    public IDisposable Begin(ITypedRequestContext context, IServiceProvider serviceProvider)
    {
        if (_validatorFactories.TryGetValue(context.GetType(), out var factory))
        {
            factory(serviceProvider)(context);
        }

        return new RequestContextScope(accessor, context);
    }

    private static Dictionary<Type, Func<IServiceProvider, Action<ITypedRequestContext>>>
        BuildValidatorFactories(RequestContextOptions options)
    {
        var factories = new Dictionary<Type, Func<IServiceProvider, Action<ITypedRequestContext>>>();

        if (options.ValidatorTypes.Count == 0)
            return factories;

        var buildMethod = typeof(RequestContextScopeFactory)
            .GetMethod(nameof(CreateValidatorFactory), BindingFlags.NonPublic | BindingFlags.Static)!;

        foreach (var (contextType, _) in options.ValidatorTypes)
        {
            var factory = buildMethod
                .MakeGenericMethod(contextType)
                .Invoke(null, null);
            factories[contextType] =
                (Func<IServiceProvider, Action<ITypedRequestContext>>)factory!;
        }

        return factories;
    }

    private static Func<IServiceProvider, Action<ITypedRequestContext>> CreateValidatorFactory<TContext>()
        where TContext : class, ITypedRequestContext
    {
        return sp =>
        {
            var validator = sp.GetRequiredService<IRequestContextValidator<TContext>>();
            return context =>
            {
                var errors = validator.Validate((TContext)context);
                if (errors is { Count: > 0 })
                    throw new RequestContextValidationException(errors);
            };
        };
    }
}
