using FluxCommerce.Modules.Identity.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Name)
            .HasColumnName("Name")
            .HasMaxLength(Role.NameMaxLength)
            .IsRequired();

        builder.Property(role => role.NormalizedName)
            .HasColumnName("NormalizedName")
            .HasMaxLength(Role.NameMaxLength)
            .IsRequired();

        builder.Property(role => role.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc")
            .IsRequired();

        builder.HasIndex(role => role.NormalizedName)
            .IsUnique()
            .HasDatabaseName("IX_Roles_NormalizedName");
    }
}
