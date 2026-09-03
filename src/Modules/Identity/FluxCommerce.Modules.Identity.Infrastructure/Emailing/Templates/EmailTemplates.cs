using System.Globalization;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Emailing;

namespace FluxCommerce.Modules.Identity.Infrastructure.Emailing.Templates;

public static class EmailTemplates
{
    public static EmailMessage Verification(ILocalizer localizer, EmailOptions options, string toEmail, string token)
    {
        var verifyUrl = BuildUrl(options.FrontendBaseUrl, "/verify-email", token);

        var subject = localizer["Email.Verification.Subject"];
        var heading = localizer["Email.Verification.Heading"];
        var body = localizer["Email.Verification.Body"];
        var textIntro = localizer["Email.Verification.TextIntro"];
        var buttonText = localizer["Email.Verification.ButtonText"];
        var validityNote = localizer["Email.Verification.ValidityNote"];
        var copyLink = localizer["Email.CopyLinkInstruction"];

        var html = $$"""
            <!DOCTYPE html>
            <html lang="{{CultureInfo.CurrentUICulture.Name}}">
            <body style="margin:0;padding:0;background-color:#f4f6f8;font-family:Arial,Helvetica,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f8;padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                      <tr>
                        <td style="background-color:#1e3a8a;padding:24px 32px;">
                          <span style="color:#ffffff;font-size:20px;font-weight:bold;">FluxCommerce</span>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 16px;font-size:20px;color:#111827;">{{heading}}</h1>
                          <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#374151;">
                            {{body}}
                          </p>
                          <p style="margin:0 0 24px;">
                            <a href="{{verifyUrl}}" style="display:inline-block;background-color:#1e3a8a;color:#ffffff;text-decoration:none;font-size:14px;font-weight:bold;padding:12px 24px;border-radius:6px;">{{buttonText}}</a>
                          </p>
                          <p style="margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;">
                            {{validityNote}}
                          </p>
                          <p style="margin:0;font-size:13px;line-height:1.6;color:#6b7280;">
                            {{copyLink}}<br />
                            <a href="{{verifyUrl}}" style="color:#1e3a8a;word-break:break-all;">{{verifyUrl}}</a>
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        var text = $$"""
            {{subject}}

            {{textIntro}}

            {{verifyUrl}}

            {{validityNote}}
            """;

        return new EmailMessage(toEmail, string.Empty, subject, html, text);
    }

    public static EmailMessage PasswordReset(ILocalizer localizer, EmailOptions options, string toEmail, string token)
    {
        var resetUrl = BuildUrl(options.FrontendBaseUrl, "/reset-password", token);

        var subject = localizer["Email.PasswordReset.Subject"];
        var heading = localizer["Email.PasswordReset.Heading"];
        var body = localizer["Email.PasswordReset.Body"];
        var textIntro = localizer["Email.PasswordReset.TextIntro"];
        var buttonText = localizer["Email.PasswordReset.ButtonText"];
        var validityNote = localizer["Email.PasswordReset.ValidityNote"];
        var copyLink = localizer["Email.CopyLinkInstruction"];

        var html = $$"""
            <!DOCTYPE html>
            <html lang="{{CultureInfo.CurrentUICulture.Name}}">
            <body style="margin:0;padding:0;background-color:#f4f6f8;font-family:Arial,Helvetica,sans-serif;">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f6f8;padding:32px 16px;">
                <tr>
                  <td align="center">
                    <table role="presentation" width="480" cellpadding="0" cellspacing="0" style="background-color:#ffffff;border-radius:8px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.1);">
                      <tr>
                        <td style="background-color:#1e3a8a;padding:24px 32px;">
                          <span style="color:#ffffff;font-size:20px;font-weight:bold;">FluxCommerce</span>
                        </td>
                      </tr>
                      <tr>
                        <td style="padding:32px;">
                          <h1 style="margin:0 0 16px;font-size:20px;color:#111827;">{{heading}}</h1>
                          <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#374151;">
                            {{body}}
                          </p>
                          <p style="margin:0 0 24px;">
                            <a href="{{resetUrl}}" style="display:inline-block;background-color:#1e3a8a;color:#ffffff;text-decoration:none;font-size:14px;font-weight:bold;padding:12px 24px;border-radius:6px;">{{buttonText}}</a>
                          </p>
                          <p style="margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;">
                            {{validityNote}}
                          </p>
                          <p style="margin:0;font-size:13px;line-height:1.6;color:#6b7280;">
                            {{copyLink}}<br />
                            <a href="{{resetUrl}}" style="color:#1e3a8a;word-break:break-all;">{{resetUrl}}</a>
                          </p>
                        </td>
                      </tr>
                    </table>
                  </td>
                </tr>
              </table>
            </body>
            </html>
            """;

        var text = $$"""
            {{subject}}

            {{textIntro}}

            {{resetUrl}}

            {{validityNote}}
            """;

        return new EmailMessage(toEmail, string.Empty, subject, html, text);
    }

    private static string BuildUrl(string baseUrl, string path, string token)
    {
        var normalizedBase = baseUrl.TrimEnd('/');
        var escapedToken = Uri.EscapeDataString(token);

        return $"{normalizedBase}{path}?token={escapedToken}";
    }
}
