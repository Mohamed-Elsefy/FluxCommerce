using FluxCommerce.Modules.Payments.Domain.Options;
using FluxCommerce.Modules.Payments.Infrastructure.Webhook;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class StripeWebhookServiceTests
{
    private const string WebhookSecret = "whsec_test_1234567890_TestSecretForUnitTests_Only";

    private static IOptions<StripeOptions> Options(string secret = WebhookSecret)
        => Microsoft.Extensions.Options.Options.Create(new StripeOptions { SecretKey = "sk_test_dummy", WebhookSecret = secret, PublishableKey = "pk_test_dummy" });

    private static string GenerateStripeSignature(string payload, string secret, long? timestamp = null)
    {
        var t = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Prefer Stripe's own computation via reflection for exact compatibility
        var utilType = typeof(Stripe.EventUtility);
        var method = utilType.GetMethod("ComputeSignature", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        // Stripe has overload ComputeSignature(string payload, string secret) where payload = $"{t}.{json}"
        string hex;
        if (method != null)
        {
            var signed = $"{t}.{payload}";
            // Try 2-arg overload
            var twoArg = utilType.GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "ComputeSignature" && m.GetParameters().Length == 2);
            if (twoArg != null)
            {
                hex = (string)twoArg.Invoke(null, new object[] { signed, secret })!;
            }
            else
            {
                // fallback to manual
                using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signed));
                hex = Convert.ToHexString(hash).ToLowerInvariant();
            }
        }
        else
        {
            var signedPayload = $"{t}.{payload}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload));
            hex = Convert.ToHexString(hash).ToLowerInvariant();
        }
        return $"t={t},v1={hex}";
    }

    private static string SamplePayload() =>
        """{"id":"evt_test_123","object":"event","api_version":"2023-08-16","created":1326853478,"data":{"object":{"id":"pi_test_123","object":"payment_intent","amount":1999,"currency":"usd","status":"succeeded"}},"livemode":false,"pending_webhooks":1,"request":{"id":"req_test","idempotency_key":null},"type":"payment_intent.succeeded"}""";

    [Fact]
    public void VerifyAndParse_ValidSignature_ReturnsIsValidTrueAndEventId()
    {
        var payload = SamplePayload();
        var header = GenerateStripeSignature(payload, WebhookSecret);
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse(payload, header);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal("evt_test_123", result.EventId);
        Assert.Equal("payment_intent.succeeded", result.EventType);
        Assert.Equal("pi_test_123", result.PaymentIntentId);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void VerifyAndParse_InvalidSignature_ReturnsIsValidFalse()
    {
        var payload = SamplePayload();
        var header = GenerateStripeSignature(payload, WebhookSecret);
        // Tamper payload
        var tamperedPayload = payload.Replace("succeeded", "failed");
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse(tamperedPayload, header);

        Assert.False(result.IsValid);
        Assert.Null(result.EventId);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void VerifyAndParse_TamperedSignature_ReturnsIsValidFalse()
    {
        var payload = SamplePayload();
        var header = "t=1234567890,v1=invalidhexsignature000000000000000000000000000000";
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse(payload, header);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void VerifyAndParse_MissingSignature_ReturnsIsValidFalse()
    {
        var payload = SamplePayload();
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse(payload, "");

        Assert.False(result.IsValid);
        Assert.Contains("missing", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VerifyAndParse_EmptyPayload_ReturnsIsValidFalse()
    {
        var header = GenerateStripeSignature("", WebhookSecret);
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse("", header);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void VerifyAndParse_WrongSecret_ReturnsIsValidFalse()
    {
        var payload = SamplePayload();
        var header = GenerateStripeSignature(payload, "whsec_wrong_secret");
        var svc = new StripeWebhookService(Options(WebhookSecret));

        var result = svc.VerifyAndParse(payload, header);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void VerifyAndParse_ValidSignature_ExtractsAmountAndCurrency()
    {
        var payload = SamplePayload();
        var header = GenerateStripeSignature(payload, WebhookSecret);
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse(payload, header);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(1999, result.AmountMinor);
        Assert.Equal("usd", result.Currency);
    }

    [Fact]
    public void VerifyAndParse_PrefersAmountReceivedOverAmount()
    {
        var payload = SamplePayload().Replace("\"amount\":1999", "\"amount\":1999,\"amount_received\":1500");
        var header = GenerateStripeSignature(payload, WebhookSecret);
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse(payload, header);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal(1500, result.AmountMinor);
    }

    [Fact]
    public void VerifyAndParse_ExtractsDeclineReason()
    {
        var payload = SamplePayload()
            .Replace("\"status\":\"succeeded\"", "\"status\":\"requires_payment_method\",\"last_payment_error\":{\"code\":\"card_declined\",\"decline_code\":\"insufficient_funds\",\"message\":\"Your card has insufficient funds.\"}");
        var header = GenerateStripeSignature(payload, WebhookSecret);
        var svc = new StripeWebhookService(Options());

        var result = svc.VerifyAndParse(payload, header);

        Assert.True(result.IsValid, result.ErrorMessage);
        Assert.Equal("insufficient_funds", result.FailureReason);
    }
}
