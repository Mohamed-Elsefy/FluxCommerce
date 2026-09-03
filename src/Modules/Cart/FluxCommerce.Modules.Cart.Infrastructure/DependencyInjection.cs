using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.MergeCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Interfaces;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
using FluxCommerce.Modules.Cart.Application.Services;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Cart.Infrastructure.Configuration;
using FluxCommerce.Modules.Cart.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Cart.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCartInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CartOptions>()
            .Bind(configuration.GetSection(CartOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<CartKeyGenerator>();
        services.AddSingleton<CartStateSerializer>();
        services.AddSingleton<CartStateMapper>();
        services.AddSingleton<ICartStore, RedisCartStore>();

        services.AddScoped<ICartCheckoutSnapshotProvider, CartCheckoutSnapshotProvider>();
        services.AddScoped<ICartCheckoutConsumption, CartCheckoutConsumption>();

        services.AddScoped<ICommandHandler<CreateGuestCartCommand, Result<CartState>>, CreateGuestCartCommandHandler>();
        services.AddScoped<IQueryHandler<GetGuestCartQuery, Result<CartState>>, GetGuestCartQueryHandler>();
        services.AddScoped<ICommandHandler<AddGuestCartItemCommand, Result<CartState>>, AddGuestCartItemCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateGuestCartItemQuantityCommand, Result<CartState>>, UpdateGuestCartItemQuantityCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveGuestCartItemCommand, Result<CartState>>, RemoveGuestCartItemCommandHandler>();
        services.AddScoped<ICommandHandler<ClearGuestCartCommand, Result<CartState>>, ClearGuestCartCommandHandler>();

        services.AddScoped<ICommandHandler<CreateAuthenticatedCartCommand, Result<CartState>>, CreateAuthenticatedCartCommandHandler>();
        services.AddScoped<IQueryHandler<GetAuthenticatedCartQuery, Result<CartState>>, GetAuthenticatedCartQueryHandler>();
        services.AddScoped<ICommandHandler<AddAuthenticatedCartItemCommand, Result<CartState>>, AddAuthenticatedCartItemCommandHandler>();
        services.AddScoped<ICommandHandler<UpdateAuthenticatedCartItemQuantityCommand, Result<CartState>>, UpdateAuthenticatedCartItemQuantityCommandHandler>();
        services.AddScoped<ICommandHandler<RemoveAuthenticatedCartItemCommand, Result<CartState>>, RemoveAuthenticatedCartItemCommandHandler>();
        services.AddScoped<ICommandHandler<ClearAuthenticatedCartCommand, Result<CartState>>, ClearAuthenticatedCartCommandHandler>();
        services.AddScoped<ICommandHandler<MergeCartCommand, Result<CartState>>, MergeCartCommandHandler>();

        return services;
    }
}

