using System.Text;
using FluxCommerce.Api.Authorization;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Infrastructure.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace FluxCommerce.Api.Extensions;

public static class AuthenticationConfiguration
{
    public const string CustomerPolicy = "Customer";

    public const string AdminPolicy = "Admin";

    public const string SuperAdminPolicy = "SuperAdmin";

    // Permission policies (ADR-016 D2). The policy name is the permission value itself, so
    // [Authorize(Policy = ...)] stays self-describing. SuperAdmin bypasses every one of them
    // through PermissionOrSuperAdminHandler.
    public const string OrdersReadPolicy = PermissionCatalog.OrdersRead;
    public const string OrdersCancelPolicy = PermissionCatalog.OrdersCancel;
    public const string OrdersRefundPolicy = PermissionCatalog.OrdersRefund;
    public const string PaymentsReadPolicy = PermissionCatalog.PaymentsRead;
    public const string PaymentsStatsPolicy = PermissionCatalog.PaymentsStats;
    public const string InventoryReadPolicy = PermissionCatalog.InventoryRead;
    public const string InventoryManagePolicy = PermissionCatalog.InventoryManage;
    public const string CatalogManagePolicy = PermissionCatalog.CatalogManage;

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "JWT issuer is not configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "JWT audience is not configured.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.SigningKey), "JWT signing key is not configured.")
            .ValidateOnStart();

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
            // Role policies flow through the supremacy handler (ADR-016 D1): SuperAdmin satisfies
            // every role policy. SuperAdminPolicy itself stays an exact role check.
            options.AddPolicy(CustomerPolicy, policy => policy.AddRequirements(new RoleOrSuperAdminRequirement(Roles.Customer)));
            options.AddPolicy(AdminPolicy, policy => policy.AddRequirements(new RoleOrSuperAdminRequirement(Roles.Admin)));
            options.AddPolicy(SuperAdminPolicy, policy => policy.RequireRole(Roles.SuperAdmin));

            // Permission policies (ADR-016 D2): each requires its permission claim, with the same
            // SuperAdmin bypass.
            options.AddPolicy(OrdersReadPolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.OrdersRead)));
            options.AddPolicy(OrdersCancelPolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.OrdersCancel)));
            options.AddPolicy(OrdersRefundPolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.OrdersRefund)));
            options.AddPolicy(PaymentsReadPolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.PaymentsRead)));
            options.AddPolicy(PaymentsStatsPolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.PaymentsStats)));
            options.AddPolicy(InventoryReadPolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.InventoryRead)));
            options.AddPolicy(InventoryManagePolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.InventoryManage)));
            options.AddPolicy(CatalogManagePolicy, policy => policy.AddRequirements(new PermissionRequirement(PermissionCatalog.CatalogManage)));
        });

        services.AddSingleton<IAuthorizationHandler, RoleOrSuperAdminHandler>();
        services.AddSingleton<IAuthorizationHandler, PermissionOrSuperAdminHandler>();

        return services;
    }
}
