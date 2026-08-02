namespace FluxCommerce.Modules.Identity.Infrastructure.Emailing;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string FromEmail { get; set; } = string.Empty;

    public string FromName { get; set; } = string.Empty;

    public bool UseSsl { get; set; } = true;

    public string FrontendBaseUrl { get; set; } = string.Empty;
}
