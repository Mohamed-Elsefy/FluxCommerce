using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.UnitTests.TestInfrastructure;

public sealed class FakeUserRepository : IUserRepository
{
    private readonly Dictionary<Guid, User> _users = new();

    public IReadOnlyCollection<User> Users => _users.Values;

    public Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.TryGetValue(id, out var user) ? user : null);

    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.FirstOrDefault(user => user.Email.Value == email.Value));

    public Task<bool> ExistsByEmailAsync(Email email, CancellationToken cancellationToken = default) =>
        Task.FromResult(_users.Values.Any(user => user.Email.Value == email.Value));

    public Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        _users[user.Id] = user;

        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        _users[user.Id] = user;

        return Task.CompletedTask;
    }
}
