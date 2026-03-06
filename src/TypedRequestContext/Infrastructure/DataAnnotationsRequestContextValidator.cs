using System.ComponentModel.DataAnnotations;

namespace TypedRequestContext.Infrastructure;

/// <summary>
/// Validates a typed request context using <see cref="System.ComponentModel.DataAnnotations"/>
/// attributes. Supports standard attributes (Required, MaxLength, RegularExpression, etc.),
/// custom <see cref="ValidationAttribute"/> subclasses, and <see cref="IValidatableObject"/>.
/// </summary>
/// <typeparam name="TContext">The typed request context to validate.</typeparam>
public sealed class DataAnnotationsRequestContextValidator<TContext>
    : IRequestContextValidator<TContext>
    where TContext : class, ITypedRequestContext
{
    /// <inheritdoc />
    public IReadOnlyList<RequestContextValidationError> Validate(TContext context)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(context);

        if (Validator.TryValidateObject(context, validationContext, validationResults, validateAllProperties: true))
            return [];

        return [.. validationResults.SelectMany(ToErrors)];
    }

    private static IEnumerable<RequestContextValidationError> ToErrors(ValidationResult result)
    {
        var message = result.ErrorMessage ?? "Validation failed.";
        var members = result.MemberNames?.Where(m => !string.IsNullOrEmpty(m)).ToArray();

        if (members is null or { Length: 0 })
        {
            yield return new RequestContextValidationError(null, message);
            yield break;
        }

        foreach (var member in members)
            yield return new RequestContextValidationError(member, message);
    }
}
