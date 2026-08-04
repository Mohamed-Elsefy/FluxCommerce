using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateCategory;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Application.Queries.GetCategory;
using FluxCommerce.Modules.Catalog.Application.Queries.ListCategories;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Catalog.Domain.Repositories;
using FluxCommerce.Modules.Catalog.Infrastructure.DbContexts;
using FluxCommerce.Modules.Catalog.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCommerce.Modules.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure(3);
                sqlOptions.CommandTimeout(30);
                sqlOptions.MigrationsHistoryTable("__EFMigrationsHistory_Catalog");
                sqlOptions.MigrationsAssembly(typeof(CatalogDbContext).Assembly.GetName().Name);
            }));

        services.AddScoped<ICategoryRepository, CategoryRepository>();

        services.AddScoped<IProductRepository, ProductRepository>();

        services.AddScoped<IVariantRepository, VariantRepository>();

        services.AddScoped<ICatalogUnitOfWork, CatalogUnitOfWork>();

        services.AddScoped<ICommandHandler<CreateCategoryCommand, Result<Guid>>, CreateCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<UpdateCategoryCommand, Result>, UpdateCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<ActivateCategoryCommand, Result>, ActivateCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<DeactivateCategoryCommand, Result>, DeactivateCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<SoftDeleteCategoryCommand, Result>, SoftDeleteCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<RestoreCategoryCommand, Result>, RestoreCategoryCommandHandler>();

        services.AddScoped<IQueryHandler<GetCategoryQuery, Result<CategoryResponse>>, GetCategoryQueryHandler>();

        services.AddScoped<IQueryHandler<ListCategoriesQuery, Result<IReadOnlyCollection<CategoryResponse>>>, ListCategoriesQueryHandler>();

        return services;
    }
}
