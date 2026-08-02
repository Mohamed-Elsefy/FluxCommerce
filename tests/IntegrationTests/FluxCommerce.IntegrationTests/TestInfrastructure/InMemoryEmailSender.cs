using FluxCommerce.Modules.Identity.Application.Emailing;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

public sealed class InMemoryEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Sent.Add(message);

        return Task.CompletedTask;
    }
}
