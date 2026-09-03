using FluxCommerce.Modules.Identity.Application.Emailing;

namespace FluxCommerce.Modules.Identity.Application.Interfaces.Services;

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
