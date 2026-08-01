using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Commands.Login;
using FluxCommerce.Modules.Identity.Application.Commands.Logout;
using FluxCommerce.Modules.Identity.Application.Commands.Refresh;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContextPool<IdentityDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Identity");
                sqlOptions.MigrationsAssembly(typeof(IdentityDbContext).Assembly.GetName().Name);
            }));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ICommandHandler<RegisterCustomerCommand, Result<UserResponse>>, RegisterCustomerCommandHandler>();
        services.AddScoped<ICommandHandler<RegisterAdminCommand, Result<UserResponse>>, RegisterAdminCommandHandler>();
        services.AddScoped<ICommandHandler<LoginCommand, Result<LoginResponse>>, LoginCommandHandler>();
        services.AddScoped<ICommandHandler<RefreshCommand, Result<LoginResponse>>, RefreshCommandHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand, Result>, LogoutCommandHandler>();

        return services;
    }
}
