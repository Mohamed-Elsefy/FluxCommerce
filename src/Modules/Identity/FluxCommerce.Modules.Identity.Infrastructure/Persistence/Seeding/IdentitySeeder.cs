using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Identity.Infrastructure.Persistence.Seeding;

public sealed class IdentitySeeder
{
    private readonly IdentityDbContext _dbContext;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IdentitySeederOptions _options;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        IdentityDbContext dbContext,
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        IOptions<IdentitySeederOptions> options,
        ILogger<IdentitySeeder> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
        _passwordHasher = passwordHasher ?? throw new ArgumentNullException(nameof(passwordHasher));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await _dbContext.Database.MigrateAsync(cancellationToken);

        // Seed Roles table (lookup) — idempotent via RoleRepository.AddAsync duplicate check + unique index
        foreach (var roleName in new[] { Roles.Customer, Roles.Admin, Roles.SuperAdmin })
        {
            await _roleRepository.AddAsync(roleName, cancellationToken);
        }

        // Save roles if any were added (RoleRepository.AddAsync only adds to DbSet, not SaveChanges)
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!_options.Enabled)
        {
            _logger.LogDebug("Identity seeder is disabled (Identity:Seeder:Enabled=false). Skipping SuperAdmin seeding.");
            return;
        }

        var emailValue = _options.SuperAdmin.Email?.Trim();
        var passwordValue = _options.SuperAdmin.Password;
        var firstName = string.IsNullOrWhiteSpace(_options.SuperAdmin.FirstName) ? "System" : _options.SuperAdmin.FirstName.Trim();
        var lastName = string.IsNullOrWhiteSpace(_options.SuperAdmin.LastName) ? "Admin" : _options.SuperAdmin.LastName.Trim();

        if (string.IsNullOrWhiteSpace(emailValue) || string.IsNullOrWhiteSpace(passwordValue))
        {
            _logger.LogWarning("Identity seeder is enabled but SuperAdmin Email or Password is missing. Skipping seeding. Provide via UserSecrets: Identity:Seeder:SuperAdmin:Password");
            return;
        }

        Email email;
        try
        {
            email = new Email(emailValue);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Identity seeder SuperAdmin Email is invalid: {Email}. Skipping.", emailValue);
            return;
        }

        var exists = await _userRepository.ExistsByEmailAsync(email, cancellationToken);
        if (exists)
        {
            _logger.LogInformation("SuperAdmin already exists, skipping seeding. Email: {Email}", emailValue);
            return;
        }

        var hash = _passwordHasher.Hash(passwordValue);
        var utcNow = DateTimeOffset.UtcNow;
        var verificationToken = Guid.NewGuid().ToString("N");
        var tokenExpiresAt = utcNow.AddHours(24);

        var user = User.Register(
            Guid.NewGuid(),
            email,
            hash,
            firstName,
            lastName,
            utcNow.UtcDateTime,
            [Roles.SuperAdmin],
            verificationToken,
            tokenExpiresAt.UtcDateTime);

        // Mark as verified and active for bootstrap SuperAdmin (bypass email verification flow)
        var verifyResult = user.VerifyEmail(verificationToken, utcNow.UtcDateTime);
        if (verifyResult.IsFailure)
        {
            _logger.LogWarning("Failed to verify SuperAdmin email during seeding: {Error}", verifyResult.Error);
        }

        await _userRepository.AddAsync(user, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Seeded SuperAdmin {Email}", emailValue);
    }
}
