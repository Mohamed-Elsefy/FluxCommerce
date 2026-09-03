using System.Security.Cryptography;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;

namespace FluxCommerce.Modules.Identity.Infrastructure.Security;

public sealed class TokenGenerator : ITokenGenerator
{
    private const int RefreshTokenBytes = 64;

    private const int VerificationTokenBytes = 32;

    private const int ResetTokenBytes = 32;

    public string GenerateRefreshToken() => GenerateToken(RefreshTokenBytes);

    public string GenerateEmailVerificationToken() => GenerateToken(VerificationTokenBytes);

    public string GeneratePasswordResetToken() => GenerateToken(ResetTokenBytes);

    private static string GenerateToken(int byteCount) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
