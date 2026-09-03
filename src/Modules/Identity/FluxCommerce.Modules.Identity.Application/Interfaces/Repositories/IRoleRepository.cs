namespace FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<IReadOnlyCollection<string>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string role, CancellationToken cancellationToken = default);

    Task AddAsync(string role, CancellationToken cancellationToken = default);
}
