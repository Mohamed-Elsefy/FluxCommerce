using FluxCommerce.Modules.Identity.Infrastructure.Emailing;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Api.Extensions;

public static class EmailConfiguration
{
    public static IServiceCollection AddEmailOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Host), "SMTP host is not configured.")
            .Validate(options => options.Port is > 0 and <= 65535, "SMTP port is not valid.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.FromEmail), "SMTP sender email is not configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.FrontendBaseUrl), "Frontend base URL is not configured.")
            .ValidateOnStart();

        return services;
    }
}
