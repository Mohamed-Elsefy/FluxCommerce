using FluxCommerce.Modules.Identity.Application.Interfaces.Security;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeTokenGenerator : ITokenGenerator
{
    private readonly Queue<string> _tokens = new();

    public FakeTokenGenerator(params string[] tokens)
    {
        foreach (var token in tokens)
        {
            _tokens.Enqueue(token);
        }
    }

    public string GenerateRefreshToken() => Next("refresh-token");

    public string GenerateEmailVerificationToken() => Next("verification-token");

    public string GeneratePasswordResetToken() => Next("reset-token");

    private string Next(string fallback) =>
        _tokens.Count > 0 ? _tokens.Dequeue() : fallback;
}
