using FluxCommerce.Modules.Identity.Application.Emailing;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace FluxCommerce.Modules.Identity.Infrastructure.Emailing;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    private readonly ILogger<SmtpEmailSender> _logger;

    public SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var socketOptions = _options.UseSsl ? SecureSocketOptions.Auto : SecureSocketOptions.None;

        using var client = new SmtpClient();
        await client.ConnectAsync(_options.Host, _options.Port, socketOptions, cancellationToken);

        if (!string.IsNullOrWhiteSpace(_options.Username) || !string.IsNullOrWhiteSpace(_options.Password))
        {
            await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
        }

        var mimeMessage = new MimeMessage
        {
            From = { new MailboxAddress(_options.FromName, _options.FromEmail) },
            To = { new MailboxAddress(message.ToName, message.ToEmail) },
            Subject = message.Subject,
            Body = new BodyBuilder
            {
                HtmlBody = message.HtmlBody,
                TextBody = message.TextBody
            }.ToMessageBody()
        };

        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);

        _logger.LogInformation("Email sent to {Email} with subject {Subject}", message.ToEmail, message.Subject);
    }
}
