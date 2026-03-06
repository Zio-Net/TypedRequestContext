using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using TypedRequestContext.Infrastructure;

namespace TypedRequestContext.UnitTests;

public class DataAnnotationsRequestContextValidatorTests
{
    [Fact]
    public void Validate_ReturnsNoErrors_WhenContextIsValid()
    {
        var validator = new DataAnnotationsRequestContextValidator<ValidatedContext>();
        var context = new ValidatedContext
        {
            Name = "Alice",
            Code = "AB12"
        };

        var errors = validator.Validate(context);

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReturnsErrors_WhenMaxLengthExceeded()
    {
        var validator = new DataAnnotationsRequestContextValidator<ValidatedContext>();
        var context = new ValidatedContext
        {
            Name = "Alice",
            Code = "TOOLONG"
        };

        var errors = validator.Validate(context);

        Assert.Single(errors);
        Assert.Equal("Code", errors[0].MemberName);
    }

    [Fact]
    public void Validate_ReturnsErrors_WhenRegexFails()
    {
        var validator = new DataAnnotationsRequestContextValidator<RegexContext>();
        var context = new RegexContext
        {
            Eori = "invalid"
        };

        var errors = validator.Validate(context);

        Assert.Single(errors);
        Assert.Equal("Eori", errors[0].MemberName);
    }

    [Fact]
    public void Validate_ReturnsMultipleErrors()
    {
        var validator = new DataAnnotationsRequestContextValidator<ValidatedContext>();
        var context = new ValidatedContext
        {
            Name = null!,
            Code = "TOOLONG"
        };

        var errors = validator.Validate(context);

        Assert.True(errors.Count >= 2);
        Assert.Contains(errors, e => e.MemberName == "Name");
        Assert.Contains(errors, e => e.MemberName == "Code");
    }

    [Fact]
    public void Validate_SupportsIValidatableObject()
    {
        var validator = new DataAnnotationsRequestContextValidator<ValidatableContext>();
        var context = new ValidatableContext
        {
            Start = 10,
            End = 5
        };

        var errors = validator.Validate(context);

        Assert.Single(errors);
        Assert.Contains("End must be greater than Start", errors[0].ErrorMessage);
    }

    public sealed class ValidatedContext : ITypedRequestContext
    {
        [FromHeader("x-name"), Required]
        public string Name { get; init; } = default!;

        [FromHeader("x-code"), MaxLength(4)]
        public string? Code { get; init; }
    }

    public sealed class RegexContext : ITypedRequestContext
    {
        [FromHeader("x-eori"), RegularExpression(@"^[A-Z]{2}[A-Z0-9]{8,15}$")]
        public string? Eori { get; init; }
    }

    public sealed class ValidatableContext : ITypedRequestContext, IValidatableObject
    {
        [FromHeader("x-start")]
        public int Start { get; init; }

        [FromHeader("x-end")]
        public int End { get; init; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (End <= Start)
                yield return new ValidationResult("End must be greater than Start", [nameof(End)]);
        }
    }
}

public class RequestContextScopeFactoryValidationTests
{
    [Fact]
    public void Begin_ThrowsValidationException_WhenValidatorFails()
    {
        var sp = BuildProvider(b => b.AddTypedRequestContext<InvalidContext>(
            cfg => cfg.UseValidation<AlwaysFailValidator>()));

        var factory = sp.GetRequiredService<RequestContextScopeFactory>();
        var accessor = sp.GetRequiredService<IRequestContextAccessor>();
        var context = new InvalidContext();

        Assert.Throws<RequestContextValidationException>(() => factory.Begin(context));
        Assert.Null(accessor.Current);
    }

    [Fact]
    public void Begin_SetsContext_WhenNoValidatorRegistered()
    {
        var sp = BuildProvider(b => b.AddTypedRequestContext<InvalidContext>());

        var factory = sp.GetRequiredService<RequestContextScopeFactory>();
        var accessor = sp.GetRequiredService<IRequestContextAccessor>();
        var context = new InvalidContext();

        using var scope = factory.Begin(context);

        Assert.Same(context, accessor.Current);
    }

    [Fact]
    public void Begin_SetsContext_WhenValidatorPasses()
    {
        var sp = BuildProvider(b => b.AddTypedRequestContext<InvalidContext>(
            cfg => cfg.UseValidation<AlwaysPassValidator>()));

        var factory = sp.GetRequiredService<RequestContextScopeFactory>();
        var accessor = sp.GetRequiredService<IRequestContextAccessor>();
        var context = new InvalidContext();

        using var scope = factory.Begin(context);

        Assert.Same(context, accessor.Current);
    }

    [Fact]
    public void Begin_RestoresPreviousContext_OnNestedScopeDispose()
    {
        var sp = BuildProvider(b => b.AddTypedRequestContext<InvalidContext>());

        var factory = sp.GetRequiredService<RequestContextScopeFactory>();
        var accessor = sp.GetRequiredService<IRequestContextAccessor>();

        var outer = new InvalidContext();
        var inner = new InvalidContext();

        using var outerScope = factory.Begin(outer);
        Assert.Same(outer, accessor.Current);

        using (var innerScope = factory.Begin(inner))
        {
            Assert.Same(inner, accessor.Current);
        }

        Assert.Same(outer, accessor.Current);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddTypedRequestContext();
        configure(services);
        return services.BuildServiceProvider();
    }

    public sealed class InvalidContext : ITypedRequestContext;

    public sealed class AlwaysFailValidator : IRequestContextValidator<InvalidContext>
    {
        public IReadOnlyList<RequestContextValidationError> Validate(InvalidContext context)
            => [new RequestContextValidationError("Prop", "bad value")];
    }

    public sealed class AlwaysPassValidator : IRequestContextValidator<InvalidContext>
    {
        public IReadOnlyList<RequestContextValidationError> Validate(InvalidContext context) => [];
    }
}

public class RequestContextValidationRegistrationTests
{
    [Fact]
    public void AddTypedRequestContext_WithValidation_RegistersValidator()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContext<ValidatedCtx>(b => b.EnableValidation());

        var provider = services.BuildServiceProvider();

        var validator = provider.GetService<IRequestContextValidator<ValidatedCtx>>();
        Assert.NotNull(validator);
        Assert.IsType<DataAnnotationsRequestContextValidator<ValidatedCtx>>(validator);
    }

    [Fact]
    public void AddTypedRequestContext_WithCustomValidator_RegistersCustom()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContext<ValidatedCtx>(b => b.UseValidation<AlwaysPassValidator>());

        var provider = services.BuildServiceProvider();

        var validator = provider.GetService<IRequestContextValidator<ValidatedCtx>>();
        Assert.NotNull(validator);
        Assert.IsType<AlwaysPassValidator>(validator);
    }

    [Fact]
    public void AddTypedRequestContext_WithoutValidation_DoesNotRegisterValidator()
    {
        var services = new ServiceCollection();
        services.AddTypedRequestContext();
        services.AddTypedRequestContext<ValidatedCtx>();

        var provider = services.BuildServiceProvider();

        var validator = provider.GetService<IRequestContextValidator<ValidatedCtx>>();
        Assert.Null(validator);
    }

    public sealed class ValidatedCtx : ITypedRequestContext
    {
        [FromHeader("x-val")]
        public string? Value { get; init; }
    }

    public sealed class AlwaysPassValidator : IRequestContextValidator<ValidatedCtx>
    {
        public IReadOnlyList<RequestContextValidationError> Validate(ValidatedCtx context) => [];
    }
}
