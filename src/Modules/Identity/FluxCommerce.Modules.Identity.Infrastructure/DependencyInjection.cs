using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;
using FluxCommerce.Modules.Identity.Application.Commands.ForgotPassword;
using FluxCommerce.Modules.Identity.Application.Commands.GetCurrentUser;
using FluxCommerce.Modules.Identity.Application.Commands.Login;
using FluxCommerce.Modules.Identity.Application.Commands.Logout;
using FluxCommerce.Modules.Identity.Application.Commands.Refresh;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;
using FluxCommerce.Modules.Identity.Application.Commands.ResendVerification;
using FluxCommerce.Modules.Identity.Application.Commands.ResetPassword;
using FluxCommerce.Modules.Identity.Application.Commands.VerifyEmail;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Application.Interfaces.Services;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Infrastructure.Authentication;
using FluxCommerce.Modules.Identity.Infrastructure.DbContexts;
using FluxCommerce.Modules.Identity.Infrastructure.Repositories;
using FluxCommerce.Modules.Identity.Infrastructure.Security;
using FluxCommerce.Modules.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<IdentityDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Identity");
                sqlOptions.MigrationsAssembly(typeof(IdentityDbContext).Assembly.GetName().Name);
            }));
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ITokenRepository, TokenRepository>();

        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.AddSingleton<ITokenGenerator, TokenGenerator>();
        services.AddSingleton<IJwtProvider, JwtProvider>();

        services.AddScoped<IEmailVerificationService, EmailVerificationService>();
        services.AddScoped<IPasswordResetService, PasswordResetService>();

        services.AddOptions<JwtOptions>();

        services.AddScoped<ICommandHandler<RegisterCustomerCommand, Result<UserResponse>>, RegisterCustomerCommandHandler>();
        services.AddScoped<ICommandHandler<RegisterAdminCommand, Result<UserResponse>>, RegisterAdminCommandHandler>();
        services.AddScoped<ICommandHandler<LoginCommand, Result<LoginResponse>>, LoginCommandHandler>();
        services.AddScoped<ICommandHandler<RefreshCommand, Result<LoginResponse>>, RefreshCommandHandler>();
        services.AddScoped<ICommandHandler<LogoutCommand, Result>, LogoutCommandHandler>();
        services.AddScoped<ICommandHandler<VerifyEmailCommand, Result<UserResponse>>, VerifyEmailCommandHandler>();
        services.AddScoped<ICommandHandler<ResendVerificationCommand, Result<ResendVerificationResponse>>, ResendVerificationCommandHandler>();
        services.AddScoped<ICommandHandler<ForgotPasswordCommand, Result>, ForgotPasswordCommandHandler>();
        services.AddScoped<ICommandHandler<ResetPasswordCommand, Result>, ResetPasswordCommandHandler>();
        services.AddScoped<ICommandHandler<ChangePasswordCommand, Result>, ChangePasswordCommandHandler>();
        services.AddScoped<ICommandHandler<GetCurrentUserCommand, Result<UserResponse>>, GetCurrentUserCommandHandler>();

        return services;
    }
}
