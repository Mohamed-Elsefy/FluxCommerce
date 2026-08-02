using System.Net.Mail;
using FluxCommerce.BuildingBlocks.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Domain.ValueObjects;

public sealed class Email : ValueObject
{
    public const int MaxLength = 254;

    public string Value { get; }

    public Email(string? value)
    {
        var email = value?.Trim();

        if (string.IsNullOrEmpty(email))
            throw new ArgumentException("Email is required.", nameof(value));

        if (email.Length > MaxLength)
            throw new ArgumentException($"Email must not exceed {MaxLength} characters.", nameof(value));

        if (!MailAddress.TryCreate(email, out var parsed))
            throw new ArgumentException("Email has an invalid format.", nameof(value));

        Value = parsed.Address.ToLowerInvariant();
    }

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }
}
