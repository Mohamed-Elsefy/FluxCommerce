namespace FluxCommerce.Modules.Identity.Application.Interfaces.Security;

public interface ITokenGenerator
{
    string GenerateRefreshToken();

    string GenerateEmailVerificationToken();

    string GeneratePasswordResetToken();
}
