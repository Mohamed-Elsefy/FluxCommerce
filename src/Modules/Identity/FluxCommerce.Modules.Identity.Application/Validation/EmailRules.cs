using System.Net.Mail;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class EmailRules
{
    public static void Validate(string? email, Dictionary<string, string[]> errors, ILocalizer localizer)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            ValidationErrors.Add(errors, "Email", localizer["Validation.Email.Required"]);
            return;
        }

        var value = email.Trim();

        if (value.Length > Email.MaxLength)
            ValidationErrors.Add(errors, "Email", localizer.Get("Validation.Email.MaxLength", Email.MaxLength));

        if (!MailAddress.TryCreate(value, out _))
            ValidationErrors.Add(errors, "Email", localizer["Validation.Email.Invalid"]);
    }
}
