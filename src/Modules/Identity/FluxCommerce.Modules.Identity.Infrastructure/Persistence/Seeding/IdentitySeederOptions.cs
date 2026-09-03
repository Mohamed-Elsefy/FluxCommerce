using System.ComponentModel.DataAnnotations;

namespace FluxCommerce.Modules.Identity.Infrastructure.Persistence.Seeding;

public sealed class IdentitySeederOptions
{
    public const string SectionName = "Identity:Seeder";

    public bool Enabled { get; set; } = false;

    public SuperAdminOptions SuperAdmin { get; set; } = new();

    public sealed class SuperAdminOptions
    {
        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string FirstName { get; set; } = "System";

        public string LastName { get; set; } = "Admin";
    }
}
