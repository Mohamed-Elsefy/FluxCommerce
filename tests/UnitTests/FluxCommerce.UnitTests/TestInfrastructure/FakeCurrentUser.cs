using FluxCommerce.BuildingBlocks.Application.Abstractions;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public string? Email { get; set; }

    public string[] Roles { get; set; } = [];

    public IReadOnlyCollection<string> Permissions { get; set; } = [];

    public bool IsAuthenticated { get; set; }
}
