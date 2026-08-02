using FluxCommerce.Modules.Identity.Domain.Entities;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class RefreshTokenTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_RequiresValidArguments()
    {
        Assert.Throws<ArgumentException>(() =>
            new RefreshToken(Guid.Empty, Guid.NewGuid(), "value", UtcNow, UtcNow.AddDays(7)));

        Assert.Throws<ArgumentException>(() =>
            new RefreshToken(Guid.NewGuid(), Guid.Empty, "value", UtcNow, UtcNow.AddDays(7)));

        Assert.Throws<ArgumentException>(() =>
            new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "", UtcNow, UtcNow.AddDays(7)));

        Assert.Throws<ArgumentException>(() =>
            new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow));
    }

    [Fact]
    public void IsActive_IsTrue_WhenNotRevokedAndNotExpired()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddDays(7));

        Assert.True(token.IsActiveAt(UtcNow));
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public void IsExpiredAt_ReturnsTrue_AfterExpiry()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddDays(7));

        Assert.True(token.IsExpiredAt(UtcNow.AddDays(8)));
        Assert.False(token.IsActiveAt(UtcNow.AddDays(8)));
    }

    [Fact]
    public void Revoke_MarksTokenAsRevoked_AndRecordsReplacement()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddDays(7));

        token.Revoke(UtcNow, "replacement");

        Assert.True(token.IsRevoked);
        Assert.Equal(UtcNow, token.RevokedAtUtc);
        Assert.Equal("replacement", token.ReplacedByTokenValue);
        Assert.False(token.IsActiveAt(UtcNow));
    }

    [Fact]
    public void Revoke_Twice_Throws()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddDays(7));

        token.Revoke(UtcNow, null);

        Assert.Throws<InvalidOperationException>(() => token.Revoke(UtcNow, null));
    }

    [Fact]
    public void Matches_IsCaseSensitive()
    {
        var token = new RefreshToken(Guid.NewGuid(), Guid.NewGuid(), "TokenValue", UtcNow, UtcNow.AddDays(7));

        Assert.True(token.Matches("TokenValue"));
        Assert.False(token.Matches("tokenvalue"));
    }
}
