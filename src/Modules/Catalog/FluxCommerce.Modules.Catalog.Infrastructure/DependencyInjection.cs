using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.ArchiveProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariants;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateCategory;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProduct;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;
using FluxCommerce.Modules.Catalog.Application.Interfaces;
using FluxCommerce.Modules.Catalog.Application.Queries.GetCategory;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProduct;
using FluxCommerce.Modules.Catalog.Application.Queries.GetProductVariant;
using FluxCommerce.Modules.Catalog.Application.Queries.ListCategories;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProducts;
using FluxCommerce.Modules.Catalog.Application.Queries.ListProductVariants;
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

        services.AddScoped<ICommandHandler<CreateProductCommand, Result<Guid>>, CreateProductCommandHandler>();

        services.AddScoped<ICommandHandler<UpdateProductCommand, Result>, UpdateProductCommandHandler>();

        services.AddScoped<ICommandHandler<CreateProductVariantCommand, Result<Guid>>, CreateProductVariantCommandHandler>();

        services.AddScoped<ICommandHandler<CreateProductVariantsCommand, Result<IReadOnlyCollection<Guid>>>, CreateProductVariantsCommandHandler>();

        services.AddScoped<ICommandHandler<UpdateProductVariantCommand, Result>, UpdateProductVariantCommandHandler>();

        services.AddScoped<ICommandHandler<DeleteProductVariantCommand, Result>, DeleteProductVariantCommandHandler>();

        services.AddScoped<ICommandHandler<ActivateCategoryCommand, Result>, ActivateCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<DeactivateCategoryCommand, Result>, DeactivateCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<SoftDeleteCategoryCommand, Result>, SoftDeleteCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<RestoreCategoryCommand, Result>, RestoreCategoryCommandHandler>();

        services.AddScoped<ICommandHandler<ActivateProductCommand, Result>, ActivateProductCommandHandler>();

        services.AddScoped<ICommandHandler<DeactivateProductCommand, Result>, DeactivateProductCommandHandler>();

        services.AddScoped<ICommandHandler<ArchiveProductCommand, Result>, ArchiveProductCommandHandler>();

        services.AddScoped<ICommandHandler<SoftDeleteProductCommand, Result>, SoftDeleteProductCommandHandler>();

        services.AddScoped<ICommandHandler<RestoreProductCommand, Result>, RestoreProductCommandHandler>();

        services.AddScoped<IQueryHandler<GetCategoryQuery, Result<CategoryResponse>>, GetCategoryQueryHandler>();

        services.AddScoped<IQueryHandler<ListCategoriesQuery, Result<IReadOnlyCollection<CategoryResponse>>>, ListCategoriesQueryHandler>();

        services.AddScoped<IQueryHandler<GetProductQuery, Result<ProductResponse>>, GetProductQueryHandler>();

        services.AddScoped<IQueryHandler<ListProductsQuery, Result<IReadOnlyCollection<ProductResponse>>>, ListProductsQueryHandler>();

        services.AddScoped<IQueryHandler<GetProductVariantQuery, Result<VariantResponse>>, GetProductVariantQueryHandler>();

        services.AddScoped<IQueryHandler<ListProductVariantsQuery, Result<IReadOnlyCollection<VariantResponse>>>, ListProductVariantsQueryHandler>();

        return services;
    }
}
