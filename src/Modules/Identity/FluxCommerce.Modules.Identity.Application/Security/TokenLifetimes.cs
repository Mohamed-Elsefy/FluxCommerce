namespace FluxCommerce.Modules.Identity.Application.Security;

public static class TokenLifetimes
{
    public static readonly TimeSpan RefreshToken = TimeSpan.FromDays(7);

    public static readonly TimeSpan EmailVerification = TimeSpan.FromHours(24);

    public static readonly TimeSpan PasswordReset = TimeSpan.FromHours(1);
}
