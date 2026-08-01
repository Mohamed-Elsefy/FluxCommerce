using FluxCommerce.Modules.Identity.Domain.Entities;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class PasswordResetTokenTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Constructor_RequiresValidArguments()
    {
        Assert.Throws<ArgumentException>(() =>
            new PasswordResetToken(Guid.Empty, Guid.NewGuid(), "value", UtcNow, UtcNow.AddHours(1)));

        Assert.Throws<ArgumentException>(() =>
            new PasswordResetToken(Guid.NewGuid(), Guid.Empty, "value", UtcNow, UtcNow.AddHours(1)));

        Assert.Throws<ArgumentException>(() =>
            new PasswordResetToken(Guid.NewGuid(), Guid.NewGuid(), "", UtcNow, UtcNow.AddHours(1)));

        Assert.Throws<ArgumentException>(() =>
            new PasswordResetToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow));
    }

    [Fact]
    public void IsActive_IsTrue_WhenNotUsedAndNotExpired()
    {
        var token = new PasswordResetToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddHours(1));

        Assert.True(token.IsActiveAt(UtcNow));
        Assert.False(token.IsUsed);
    }

    [Fact]
    public void IsExpiredAt_ReturnsTrue_AfterExpiry()
    {
        var token = new PasswordResetToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddHours(1));

        Assert.True(token.IsExpiredAt(UtcNow.AddMinutes(61)));
        Assert.False(token.IsActiveAt(UtcNow.AddMinutes(61)));
    }

    [Fact]
    public void MarkUsed_MarksTokenAsUsed()
    {
        var token = new PasswordResetToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddHours(1));

        token.MarkUsed(UtcNow);

        Assert.True(token.IsUsed);
        Assert.Equal(UtcNow, token.UsedAtUtc);
        Assert.False(token.IsActiveAt(UtcNow));
    }

    [Fact]
    public void MarkUsed_Twice_Throws()
    {
        var token = new PasswordResetToken(Guid.NewGuid(), Guid.NewGuid(), "value", UtcNow, UtcNow.AddHours(1));

        token.MarkUsed(UtcNow);

        Assert.Throws<InvalidOperationException>(() => token.MarkUsed(UtcNow));
    }
}
