using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.PasswordHash)
            .HasColumnName("PasswordHash")
            .HasMaxLength(512);

        builder.Property(user => user.FirstName)
            .HasColumnName("FirstName")
            .HasMaxLength(User.FirstNameMaxLength);

        builder.Property(user => user.LastName)
            .HasColumnName("LastName")
            .HasMaxLength(User.LastNameMaxLength);

        builder.Property(user => user.DisplayName)
            .HasColumnName("DisplayName")
            .HasMaxLength(User.DisplayNameMaxLength);

        builder.Property(user => user.PhoneNumber)
            .HasColumnName("PhoneNumber")
            .HasMaxLength(User.PhoneNumberMaxLength);

        builder.Property(user => user.IsPhoneVerified)
            .HasColumnName("IsPhoneVerified");

        builder.Property(user => user.AvatarUrl)
            .HasColumnName("AvatarUrl")
            .HasMaxLength(User.AvatarUrlMaxLength);

        builder.Property(user => user.PreferredLanguage)
            .HasColumnName("PreferredLanguage")
            .HasMaxLength(User.PreferredLanguageMaxLength);

        builder.Property(user => user.TimeZone)
            .HasColumnName("TimeZone")
            .HasMaxLength(User.TimeZoneMaxLength);

        builder.Property(user => user.Status)
            .HasColumnName("Status");

        builder.Property(user => user.IsEmailVerified)
            .HasColumnName("IsEmailVerified");

        builder.Property(user => user.EmailVerifiedAtUtc)
            .HasColumnName("EmailVerifiedAtUtc");

        builder.Property(user => user.LastLoginAtUtc)
            .HasColumnName("LastLoginAtUtc");

        builder.Property(user => user.RegisteredAtUtc)
            .HasColumnName("RegisteredAtUtc");

        builder.OwnsOne(user => user.Email, email =>
        {
            email.Property(e => e.Value)
                .HasColumnName("Email")
                .HasMaxLength(Email.MaxLength);

            email.HasIndex(e => e.Value)
                .IsUnique();
        });

        builder.OwnsOne(user => user.Lockout, lockout =>
        {
            lockout.Property(l => l.FailedAccessAttemptCount)
                .HasColumnName("FailedAccessAttemptCount");

            lockout.Property(l => l.LockoutEndUtc)
                .HasColumnName("LockoutEndUtc");
        });

        builder.Property(typeof(List<string>), "_roles")
            .HasColumnName("Roles");

        builder.OwnsMany(user => user.RefreshTokens, tokens =>
        {
            tokens.ToTable("RefreshTokens");
            tokens.WithOwner().HasForeignKey(token => token.UserId);
            tokens.HasKey(token => token.Id);
            tokens.Property(token => token.Value).HasMaxLength(512);
            tokens.Property(token => token.CreatedAtUtc);
            tokens.Property(token => token.ExpiresAtUtc);
            tokens.Property(token => token.ReplacedByTokenValue).HasMaxLength(512);
        });

        builder.OwnsOne(user => user.VerificationToken, token =>
        {
            token.ToTable("EmailVerificationTokens");
            token.WithOwner().HasForeignKey(t => t.UserId);
            token.HasKey(t => t.Id);
            token.Property(t => t.Value).HasMaxLength(512);
            token.Property(t => t.CreatedAtUtc);
            token.Property(t => t.ExpiresAtUtc);
        });

        builder.OwnsOne(user => user.ResetToken, token =>
        {
            token.ToTable("PasswordResetTokens");
            token.WithOwner().HasForeignKey(t => t.UserId);
            token.HasKey(t => t.Id);
            token.Property(t => t.Value).HasMaxLength(512);
            token.Property(t => t.CreatedAtUtc);
            token.Property(t => t.ExpiresAtUtc);
        });
    }
}
