namespace FluxCommerce.Modules.Identity.Application.Security;

public static class TokenLifetimes
{
    public static readonly TimeSpan RefreshToken = TimeSpan.FromDays(7);
}
