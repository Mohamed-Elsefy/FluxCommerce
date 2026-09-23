using System.Security.Cryptography;
using System.Text;
using FluxCommerce.Modules.Orders.Application.Commands.Checkout;

namespace FluxCommerce.Modules.Orders.Application.Services;

/// <summary>
/// Computes a stable fingerprint for a Checkout request. Two schemes exist:
/// <list type="bullet">
/// <item>v1 (legacy): the Cart id plus the normalized billing and shipping addresses.
/// Rows written before cart contents were fingerprinted carry a bare hex hash.</item>
/// <item>v2: v1 inputs plus the sorted cart lines (variant + quantity), stored as
/// <c>"v2:&lt;hex&gt;"</c>. This closes the hole where the same key with a mutated
/// cart returned the stale order instead of an idempotency conflict.</item>
/// </list>
/// The fingerprint is stored next to the (Customer, Idempotency-Key) record and
/// tells whether a later request reusing the same key describes the same checkout
/// (safe retry, the existing Order is returned) or a different one (idempotency conflict).
/// </summary>
public static class CheckoutRequestFingerprint
{
    /// <summary>
    /// Prefix marking v2 fingerprints. Absence of a prefix implies a legacy v1 row,
    /// so no schema change was needed to introduce v2.
    /// </summary>
    public const string V2Prefix = "v2:";

    /// <summary>
    /// Minimal cart line projection used by v2 (variant + quantity only).
    /// </summary>
    public sealed record CartLine(Guid VariantId, int Quantity);

    public static string Compute(
        Guid cartId,
        CheckoutAddressData shippingAddress,
        CheckoutAddressData billingAddress)
    {
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            CanonicalV1(cartId, shippingAddress, billingAddress)))).ToLowerInvariant();
    }

    /// <summary>
    /// Computes a v2 fingerprint covering cart lines. Items are sorted by variant id
    /// so line order never affects the hash. Catalog prices are deliberately excluded:
    /// a price move between attempts must not turn a legitimate retry into a conflict.
    /// The result embeds both hashes as <c>"v2:&lt;lineHash&gt;:&lt;v1Hash&gt;"</c> so a
    /// replay after the cart was consumed can still verify the v1 half.
    /// </summary>
    public static string ComputeV2(
        Guid cartId,
        CheckoutAddressData shippingAddress,
        CheckoutAddressData billingAddress,
        IEnumerable<CartLine> lines)
    {
        ArgumentNullException.ThrowIfNull(shippingAddress);
        ArgumentNullException.ThrowIfNull(billingAddress);
        ArgumentNullException.ThrowIfNull(lines);

        var canonical = new StringBuilder()
            .Append(CanonicalV1(cartId, shippingAddress, billingAddress))
            .Append('|')
            .Append(string.Join(',', lines
                .Select(l => (VariantId: l.VariantId.ToString("D"), l.Quantity))
                .OrderBy(l => l.VariantId, StringComparer.Ordinal)
                .Select(l => $"{l.VariantId}:{l.Quantity}")));

        var lineHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
        var v1Hash = Compute(cartId, shippingAddress, billingAddress);

        return $"{V2Prefix}{lineHash}:{v1Hash}";
    }

    /// <summary>
    /// Compares a stored fingerprint against the current request. Comparison runs in the
    /// stored record's scheme: v2 rows compare the line hash when the cart is available,
    /// and the embedded v1 half when the cart was already consumed (post-commit replay).
    /// Legacy bare-hex rows compare v1 exactly as before.
    /// </summary>
    public static bool Matches(string? stored, string currentV1, string? currentV2)
    {
        if (TrySplitV2(stored, out var storedLineHash, out var storedV1))
        {
            if (currentV2 is not null && TrySplitV2(currentV2, out var currentLineHash, out _))
                return string.Equals(storedLineHash, currentLineHash, StringComparison.Ordinal);

            return string.Equals(storedV1, currentV1, StringComparison.Ordinal);
        }

        return string.Equals(stored, currentV1, StringComparison.Ordinal);
    }

    public static bool IsV2(string? stored) =>
        stored?.StartsWith(V2Prefix, StringComparison.Ordinal) == true;

    private static bool TrySplitV2(string? value, out string lineHash, out string v1Hash)
    {
        lineHash = string.Empty;
        v1Hash = string.Empty;

        if (value is null || !IsV2(value))
            return false;

        var parts = value.Split(':');
        if (parts.Length != 3 || string.IsNullOrEmpty(parts[1]) || string.IsNullOrEmpty(parts[2]))
            return false;

        lineHash = parts[1];
        v1Hash = parts[2];
        return true;
    }

    private static string CanonicalV1(
        Guid cartId,
        CheckoutAddressData shippingAddress,
        CheckoutAddressData billingAddress) =>
        new StringBuilder()
            .Append(cartId.ToString("D"))
            .Append('|')
            .Append(Canonicalize(shippingAddress))
            .Append('|')
            .Append(Canonicalize(billingAddress))
            .ToString();

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