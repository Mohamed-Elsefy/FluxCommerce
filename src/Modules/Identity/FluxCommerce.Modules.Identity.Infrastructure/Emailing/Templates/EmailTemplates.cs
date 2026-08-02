using FluxCommerce.Modules.Identity.Application.Emailing;

namespace FluxCommerce.Modules.Identity.Infrastructure.Emailing.Templates;

public static class EmailTemplates
{
    public static EmailMessage Verification(EmailOptions options, string toEmail, string token)
    {
        var verifyUrl = BuildUrl(options.FrontendBaseUrl, "/verify-email", token);
        var subject = "Verify your FluxCommerce account";
        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
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
                          <h1 style="margin:0 0 16px;font-size:20px;color:#111827;">Verify your email address</h1>
                          <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#374151;">
                            Thanks for creating an account with FluxCommerce. Please confirm your email address by clicking the button below.
                          </p>
                          <p style="margin:0 0 24px;">
                            <a href="{verifyUrl}" style="display:inline-block;background-color:#1e3a8a;color:#ffffff;text-decoration:none;font-size:14px;font-weight:bold;padding:12px 24px;border-radius:6px;">Verify email</a>
                          </p>
                          <p style="margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;">
                            This link is valid for 24 hours. If you did not create this account, you can safely ignore this email.
                          </p>
                          <p style="margin:0;font-size:13px;line-height:1.6;color:#6b7280;">
                            If the button does not work, copy and paste this link into your browser:<br />
                            <a href="{verifyUrl}" style="color:#1e3a8a;word-break:break-all;">{verifyUrl}</a>
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

        var text = $"""
            Verify your FluxCommerce account

            Thanks for creating an account with FluxCommerce. Please confirm your email address by opening the link below:

            {verifyUrl}

            This link is valid for 24 hours. If you did not create this account, you can safely ignore this email.
            """;

        return new EmailMessage(toEmail, string.Empty, subject, html, text);
    }

    public static EmailMessage PasswordReset(EmailOptions options, string toEmail, string token)
    {
        var resetUrl = BuildUrl(options.FrontendBaseUrl, "/reset-password", token);
        var subject = "Reset your FluxCommerce password";
        var html = $"""
            <!DOCTYPE html>
            <html lang="en">
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
                          <h1 style="margin:0 0 16px;font-size:20px;color:#111827;">Reset your password</h1>
                          <p style="margin:0 0 24px;font-size:14px;line-height:1.6;color:#374151;">
                            We received a request to reset the password for your FluxCommerce account. Click the button below to choose a new password.
                          </p>
                          <p style="margin:0 0 24px;">
                            <a href="{resetUrl}" style="display:inline-block;background-color:#1e3a8a;color:#ffffff;text-decoration:none;font-size:14px;font-weight:bold;padding:12px 24px;border-radius:6px;">Reset password</a>
                          </p>
                          <p style="margin:0 0 8px;font-size:13px;line-height:1.6;color:#6b7280;">
                            This link is valid for 1 hour. If you did not request a password reset, you can safely ignore this email.
                          </p>
                          <p style="margin:0;font-size:13px;line-height:1.6;color:#6b7280;">
                            If the button does not work, copy and paste this link into your browser:<br />
                            <a href="{resetUrl}" style="color:#1e3a8a;word-break:break-all;">{resetUrl}</a>
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

        var text = $"""
            Reset your FluxCommerce password

            We received a request to reset the password for your FluxCommerce account. Open the link below to choose a new password:

            {resetUrl}

            This link is valid for 1 hour. If you did not request a password reset, you can safely ignore this email.
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
