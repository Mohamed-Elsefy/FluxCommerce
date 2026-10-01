using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.Errors;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public sealed class UserPermissionTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private static User CreateUser()
    {
        var user = User.Register(
            Guid.NewGuid(),
            new Email("user@example.com"),
            "password-hash",
            "John",
            "Doe",
            UtcNow,
            [Roles.Customer]);

        Assert.Empty(user.Permissions);
        return user;
    }

    [Fact]
    public void GrantPermission_KnownPermission_AddsIt()
    {
        var user = CreateUser();

        var result = user.GrantPermission(PermissionCatalog.OrdersRead);

        Assert.True(result.IsSuccess);
        Assert.Contains(PermissionCatalog.OrdersRead, user.Permissions);
    }

    [Fact]
    public void GrantPermission_Duplicate_ReturnsPermissionAlreadyGranted()
    {
        var user = CreateUser();
        user.GrantPermission(PermissionCatalog.InventoryManage);

        var result = user.GrantPermission(PermissionCatalog.InventoryManage);

        Assert.True(result.IsFailure);
        Assert.Equal(UserErrors.PermissionAlreadyGranted, result.Error);
        Assert.Single(user.Permissions);
    }

    [Fact]
    public void GrantPermission_TrimsInput()
    {
        var user = CreateUser();

        var result = user.GrantPermission($"  {PermissionCatalog.PaymentsRead}  ");

        Assert.True(result.IsSuccess);
        Assert.Contains(PermissionCatalog.PaymentsRead, user.Permissions);
    }

    [Theory]
    [InlineData("unknown.permission")]
    [InlineData("ADMIN")]
    public void GrantPermission_UnknownPermission_Throws(string permission)
    {
        var user = CreateUser();

        Assert.Throws<ArgumentException>(() => user.GrantPermission(permission));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GrantPermission_InvalidInput_Throws(string? permission)
    {
        var user = CreateUser();

        Assert.Throws<ArgumentException>(() => user.GrantPermission(permission!));
    }

    [Fact]
    public void RevokePermission_Granted_RemovesIt()
    {
        var user = CreateUser();
        user.GrantPermission(PermissionCatalog.OrdersRead);
        user.GrantPermission(PermissionCatalog.OrdersCancel);

        var result = user.RevokePermission(PermissionCatalog.OrdersRead);

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(PermissionCatalog.OrdersRead, user.Permissions);
        Assert.Contains(PermissionCatalog.OrdersCancel, user.Permissions);
    }

    [Fact]
    public void RevokePermission_Absent_IsBenignNoOp()
    {
        var user = CreateUser();

        var result = user.RevokePermission(PermissionCatalog.OrdersRead);

        Assert.True(result.IsSuccess);
        Assert.Empty(user.Permissions);
    }

    [Fact]
    public void PermissionCatalog_AdminBundle_ContainsAllPermissions()
    {
        Assert.Equal(PermissionCatalog.All.Count, PermissionCatalog.AdminBundle.Count);
        Assert.All(PermissionCatalog.All, permission => Assert.Contains(permission, PermissionCatalog.AdminBundle));
    }
}
