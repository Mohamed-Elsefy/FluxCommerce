using System.Net.Mail;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Application.Validation;

public static class EmailRules
{
    public static void Validate(string? email, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            ValidationErrors.Add(errors, "Email", "Email is required.");
            return;
        }

        var value = email.Trim();

        if (value.Length > Email.MaxLength)
            ValidationErrors.Add(errors, "Email", $"Email must not exceed {Email.MaxLength} characters.");

        if (!MailAddress.TryCreate(value, out _))
            ValidationErrors.Add(errors, "Email", "Email has an invalid format.");
    }
}
