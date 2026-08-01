using System.Text;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace FluxCommerce.Api.Extensions;

public static class AuthenticationConfiguration
{
    public const string CustomerPolicy = "Customer";

    public const string AdminPolicy = "Admin";

    public const string SuperAdminPolicy = "SuperAdmin";

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
                    ?? throw new InvalidOperationException("JWT configuration is not defined.");

                if (string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
                    throw new InvalidOperationException("JWT signing key is not configured.");

                if (string.IsNullOrWhiteSpace(jwtOptions.Issuer))
                    throw new InvalidOperationException("JWT issuer is not configured.");

                if (string.IsNullOrWhiteSpace(jwtOptions.Audience))
                    throw new InvalidOperationException("JWT audience is not configured.");

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                    ClockSkew = TimeSpan.FromMinutes(jwtOptions.ClockSkewMinutes)
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(CustomerPolicy, policy => policy.RequireRole(Roles.Customer));
            options.AddPolicy(AdminPolicy, policy => policy.RequireRole(Roles.Admin));
            options.AddPolicy(SuperAdminPolicy, policy => policy.RequireRole(Roles.SuperAdmin));
        });

        return services;
    }
}
