using FluxCommerce.Modules.Identity.Infrastructure.Emailing;
using FluxCommerce.Modules.Identity.Infrastructure.Emailing.Templates;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class EmailTemplateTests
{
    private static EmailOptions CreateOptions(string frontendBaseUrl = "https://localhost:4200") =>
        new()
        {
            Host = "localhost",
            Port = 1025,
            FromEmail = "noreply@fluxcommerce.local",
            FromName = "FluxCommerce",
            FrontendBaseUrl = frontendBaseUrl
        };

    [Fact]
    public void Verification_Template_ContainsVerifyUrlWithToken()
    {
        var message = EmailTemplates.Verification(CreateOptions(), "user@example.com", "tok-123");

        Assert.Equal("user@example.com", message.ToEmail);
        Assert.Equal("Verify your FluxCommerce account", message.Subject);
        Assert.Contains("https://localhost:4200/verify-email?token=tok-123", message.HtmlBody);
        Assert.Contains("https://localhost:4200/verify-email?token=tok-123", message.TextBody);
        Assert.Contains("24 hours", message.HtmlBody);
    }

    [Fact]
    public void Verification_Template_EscapesTokenAndNormalizesBaseUrl()
    {
        var message = EmailTemplates.Verification(
            CreateOptions("https://localhost:4200/"),
            "user@example.com",
            "a+b/c==");

        Assert.Contains("https://localhost:4200/verify-email?token=a%2Bb%2Fc%3D%3D", message.HtmlBody);
    }

    [Fact]
    public void PasswordReset_Template_ContainsResetUrlWithToken()
    {
        var message = EmailTemplates.PasswordReset(CreateOptions(), "user@example.com", "reset-456");

        Assert.Equal("user@example.com", message.ToEmail);
        Assert.Equal("Reset your FluxCommerce password", message.Subject);
        Assert.Contains("https://localhost:4200/reset-password?token=reset-456", message.HtmlBody);
        Assert.Contains("https://localhost:4200/reset-password?token=reset-456", message.TextBody);
        Assert.Contains("1 hour", message.HtmlBody);
    }
}
