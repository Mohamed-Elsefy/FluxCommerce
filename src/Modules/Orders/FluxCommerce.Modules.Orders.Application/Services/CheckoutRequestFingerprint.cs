using System.Security.Cryptography;
using System.Text;
using FluxCommerce.Modules.Orders.Application.Commands.Checkout;

namespace FluxCommerce.Modules.Orders.Application.Services;

/// <summary>
/// Computes a stable fingerprint for a Checkout request (the Cart id plus the normalized billing and
/// shipping addresses). The fingerprint is stored next to the (Customer, Idempotency-Key) record and
/// tells whether a later request reusing the same key describes the same checkout (safe retry, the
/// existing Order is returned) or a different one (idempotency conflict).
/// </summary>
public static class CheckoutRequestFingerprint
{
    public static string Compute(
        Guid cartId,
        CheckoutAddressData shippingAddress,
        CheckoutAddressData billingAddress)
    {
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);

        var canonical = new StringBuilder()
            .Append(cartId.ToString("D"))
            .Append('|')
            .Append(Canonicalize(shippingAddress))
            .Append('|')
            .Append(Canonicalize(billingAddress))
            .ToString();

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Canonicalize(CheckoutAddressData address)
    {
        return string.Join(
            '|',
            Normalize(address.RecipientName),
            Normalize(address.Line1),
            Normalize(address.Line2),
            Normalize(address.City),
            Normalize(address.Region),
            Normalize(address.PostalCode),
            Normalize(address.CountryCode));
    }

    private static string Normalize(string? value) =>
        value?.Trim().ToUpperInvariant() ?? string.Empty;
}