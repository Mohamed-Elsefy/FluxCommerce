using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Validation;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Commands.UpdateProfile;

public sealed class UpdateProfileCommandValidator(ILocalizer localizer)
{
    public ValidationResult Validate(UpdateProfileCommand command)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.FirstName is not null)
            NameRules.Validate(command.FirstName, "FirstName", localizer["Validation.Fields.FirstName"], User.FirstNameMaxLength, errors, localizer);

        if (command.LastName is not null)
            NameRules.Validate(command.LastName, "LastName", localizer["Validation.Fields.LastName"], User.LastNameMaxLength, errors, localizer);

        ValidateOptionalMaxLength(command.PhoneNumber, "PhoneNumber", localizer["Validation.Fields.PhoneNumber"], User.PhoneNumberMaxLength, errors);
        ValidateOptionalMaxLength(command.AvatarUrl, "AvatarUrl", localizer["Validation.Fields.AvatarUrl"], User.AvatarUrlMaxLength, errors);
        ValidateRequiredMaxLength(command.PreferredLanguage, "PreferredLanguage", localizer["Validation.Fields.PreferredLanguage"], User.PreferredLanguageMaxLength, errors);
        ValidateRequiredMaxLength(command.TimeZone, "TimeZone", localizer["Validation.Fields.TimeZone"], User.TimeZoneMaxLength, errors);

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }

    private void ValidateOptionalMaxLength(
        string? value,
        string propertyName,
        string displayName,
        int maxLength,
        Dictionary<string, string[]> errors)
    {
        if (value is not null && value.Trim().Length > maxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, maxLength));
    }

    private void ValidateRequiredMaxLength(
        string? value,
        string propertyName,
        string displayName,
        int maxLength,
        Dictionary<string, string[]> errors)
    {
        if (value is null)
            return;

        if (string.IsNullOrWhiteSpace(value))
        {
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.Required", displayName));
            return;
        }

        if (value.Trim().Length > maxLength)
            ValidationErrors.Add(errors, propertyName, localizer.Get("Validation.Field.MaxLength", displayName, maxLength));
    }
}
