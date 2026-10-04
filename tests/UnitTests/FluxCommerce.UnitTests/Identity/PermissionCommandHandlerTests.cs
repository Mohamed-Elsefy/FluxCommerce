using FluxCommerce.Modules.Identity.Application.Commands.GrantPermission;
using FluxCommerce.Modules.Identity.Application.Commands.RevokePermission;
using FluxCommerce.Modules.Identity.Application.Queries.GetPermissionCatalog;
using FluxCommerce.Modules.Identity.Application.Queries.GetUserPermissions;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Identity;

public sealed class PermissionCommandHandlerTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private static Guid SeedUser(FakeUserRepository users)
    {
        var user = TestUsers.CreateVerified();
        users.AddAsync(user).GetAwaiter().GetResult();
        return user.Id;
    }

    private static FakeCurrentUser SuperAdminCaller() =>
        new() { UserId = Guid.NewGuid(), IsAuthenticated = true, Roles = [Roles.SuperAdmin] };

    private static FakeCurrentUser ScopedCaller(params string[] permissions) =>
        new() { UserId = Guid.NewGuid(), IsAuthenticated = true, Roles = [Roles.Admin], Permissions = permissions };

    [Fact]
    public async Task GrantPermission_KnownPermission_GrantsAndPersists()
    {
        var userId = SeedUser(_users);
        var handler = new GrantPermissionCommandHandler(_users, SuperAdminCaller(), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new GrantPermissionCommand(userId, PermissionCatalog.OrdersRead));

        Assert.True(result.IsSuccess);
        Assert.Contains(PermissionCatalog.OrdersRead, result.Value.Permissions);
        Assert.Contains(PermissionCatalog.OrdersRead, _users.Users.Single().Permissions);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task GrantPermission_Duplicate_ReturnsConflict()
    {
        var userId = SeedUser(_users);
        _users.Users.Single().GrantPermission(PermissionCatalog.OrdersRead);
        var handler = new GrantPermissionCommandHandler(_users, SuperAdminCaller(), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new GrantPermissionCommand(userId, PermissionCatalog.OrdersRead));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.PermissionAlreadyGranted", result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task GrantPermission_UnknownPermission_ReturnsValidation()
    {
        var userId = SeedUser(_users);
        var handler = new GrantPermissionCommandHandler(_users, SuperAdminCaller(), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new GrantPermissionCommand(userId, "unknown.permission"));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.ValidationFailed", result.Error.Code);
    }

    [Fact]
    public async Task GrantPermission_MissingUser_ReturnsNotFound()
    {
        var handler = new GrantPermissionCommandHandler(_users, SuperAdminCaller(), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new GrantPermissionCommand(Guid.NewGuid(), PermissionCatalog.OrdersRead));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.UserNotFound", result.Error.Code);
    }

    [Fact]
    public async Task RevokePermission_Granted_RemovesAndPersists()
    {
        var userId = SeedUser(_users);
        _users.Users.Single().GrantPermission(PermissionCatalog.InventoryManage);
        var handler = new RevokePermissionCommandHandler(_users, SuperAdminCaller(), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RevokePermissionCommand(userId, PermissionCatalog.InventoryManage));

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(PermissionCatalog.InventoryManage, result.Value.Permissions);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task RevokePermission_Absent_IsBenignSuccess()
    {
        var userId = SeedUser(_users);
        var handler = new RevokePermissionCommandHandler(_users, SuperAdminCaller(), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RevokePermissionCommand(userId, PermissionCatalog.OrdersRead));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task RevokePermission_MissingUser_ReturnsNotFound()
    {
        var handler = new RevokePermissionCommandHandler(_users, SuperAdminCaller(), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RevokePermissionCommand(Guid.NewGuid(), PermissionCatalog.OrdersRead));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.UserNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetUserPermissions_ReturnsGrantedPermissions()
    {
        var userId = SeedUser(_users);
        _users.Users.Single().GrantPermission(PermissionCatalog.PaymentsStats);
        var handler = new GetUserPermissionsQueryHandler(_users);

        var result = await handler.HandleAsync(new GetUserPermissionsQuery(userId));

        Assert.True(result.IsSuccess);
        Assert.Contains(PermissionCatalog.PaymentsStats, result.Value!);
    }

    [Fact]
    public async Task GetUserPermissions_MissingUser_ReturnsNotFound()
    {
        var handler = new GetUserPermissionsQueryHandler(_users);

        var result = await handler.HandleAsync(new GetUserPermissionsQuery(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.UserNotFound", result.Error.Code);
    }

    [Fact]
    public async Task GetPermissionCatalog_ReturnsAllPermissions()
    {
        var handler = new GetPermissionCatalogQueryHandler();

        var result = await handler.HandleAsync(new GetPermissionCatalogQuery());

        Assert.True(result.IsSuccess);
        Assert.Equal(PermissionCatalog.All.Count, result.Value!.Count);
        Assert.All(PermissionCatalog.All, permission => Assert.Contains(permission, result.Value));
    }

    [Fact]
    public async Task GrantPermission_NonSuperAdminHoldingPermission_Succeeds()
    {
        var userId = SeedUser(_users);
        var handler = new GrantPermissionCommandHandler(
            _users, ScopedCaller(PermissionCatalog.OrdersRead), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new GrantPermissionCommand(userId, PermissionCatalog.OrdersRead));

        Assert.True(result.IsSuccess);
        Assert.Contains(PermissionCatalog.OrdersRead, _users.Users.Single(u => u.Id == userId).Permissions);
    }

    [Fact]
    public async Task GrantPermission_NonSuperAdminWithoutHeldPermission_ReturnsForbidden()
    {
        var userId = SeedUser(_users);
        var handler = new GrantPermissionCommandHandler(
            _users, ScopedCaller(PermissionCatalog.InventoryRead), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new GrantPermissionCommand(userId, PermissionCatalog.OrdersRead));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.Forbidden", result.Error.Code);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task GrantPermission_TargetIsSuperAdmin_NonSuperAdminCaller_ReturnsForbidden()
    {
        var superAdmin = TestUsers.CreateVerified(roles: [Roles.SuperAdmin]);
        await _users.AddAsync(superAdmin);
        var handler = new GrantPermissionCommandHandler(
            _users, ScopedCaller(PermissionCatalog.OrdersRead), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new GrantPermissionCommand(superAdmin.Id, PermissionCatalog.OrdersRead));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.Forbidden", result.Error.Code);
    }

    [Fact]
    public async Task RevokePermission_TargetIsSuperAdmin_NonSuperAdminCaller_ReturnsForbidden()
    {
        var superAdmin = TestUsers.CreateVerified(roles: [Roles.SuperAdmin]);
        await _users.AddAsync(superAdmin);
        var handler = new RevokePermissionCommandHandler(
            _users, ScopedCaller(PermissionCatalog.OrdersRead), _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RevokePermissionCommand(superAdmin.Id, PermissionCatalog.OrdersRead));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.Forbidden", result.Error.Code);
    }
}
