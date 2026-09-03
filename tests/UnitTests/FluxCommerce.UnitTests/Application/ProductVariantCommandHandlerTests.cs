using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.CreateProductVariants;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductVariant;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductVariant;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductVariantCommandHandlerTests
{
    private readonly FakeProductRepository _products = new();
    private readonly FakeVariantRepository _variants = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    // ---- CreateProductVariant ----

    [Fact]
    public async Task CreateVariant_Succeeds_AndCreatesVariant()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new CreateProductVariantCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantCommand(product.Id, " sku-123 ", "128GB", 999.99m, "USD"));

        Assert.True(result.IsSuccess);
        var variant = Assert.Single(_variants.Variants);
        Assert.Equal(result.Value, variant.Id);
        Assert.Equal(product.Id, variant.ProductId);
        Assert.Equal("SKU-123", variant.Sku.Value);
        Assert.Equal("128GB", variant.Name);
        Assert.Equal(999.99m, variant.UnitPrice.Amount);
        Assert.Equal("USD", variant.UnitPrice.Currency);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task CreateVariant_ProductNotFound_ReturnsNotFound()
    {
        var handler = new CreateProductVariantCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantCommand(Guid.NewGuid(), "SKU-123", "128GB", 999.99m, "USD"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Empty(_variants.Variants);
    }

    [Fact]
    public async Task CreateVariant_EmptyName_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new CreateProductVariantCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantCommand(Guid.NewGuid(), "SKU-123", "", 999.99m, "USD"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
        Assert.Empty(_variants.Variants);
    }

    [Fact]
    public async Task CreateVariant_InvalidSkuFormat_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new CreateProductVariantCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantCommand(Guid.NewGuid(), "bad sku!", "128GB", 999.99m, "USD"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidSku", result.Error.Code);
        Assert.Empty(_variants.Variants);
    }

    [Fact]
    public async Task CreateVariant_DuplicateSku_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        await _variants.AddAsync(TestVariants.Create(productId: product.Id, sku: "SKU-123"));
        var handler = new CreateProductVariantCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantCommand(product.Id, "sku-123", "128GB", 999.99m, "USD"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateVariantSku", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Single(_variants.Variants);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task CreateVariant_InvalidUnitPriceCurrency_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new CreateProductVariantCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantCommand(Guid.NewGuid(), "SKU-123", "128GB", 999.99m, "US"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidUnitPrice", result.Error.Code);
        Assert.Empty(_variants.Variants);
    }

    // ---- CreateProductVariants ----

    [Fact]
    public async Task CreateVariants_Succeeds_AndCreatesAllVariants()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new CreateProductVariantsCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantsCommand(product.Id, new[]
        {
            new CreateProductVariantItemCommand("SKU-1", "128GB", 999.99m, "USD"),
            new CreateProductVariantItemCommand("SKU-2", "256GB", 1099.99m, "USD"),
        }));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(2, _variants.Variants.Count);
        Assert.Equal(2, _variants.Variants.Select(v => v.Id).Intersect(result.Value).Count());
        Assert.All(_variants.Variants, variant => Assert.Equal("USD", variant.UnitPrice.Currency));
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task CreateVariants_ProductNotFound_ReturnsNotFound()
    {
        var handler = new CreateProductVariantsCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantsCommand(Guid.NewGuid(), new[]
        {
            new CreateProductVariantItemCommand("SKU-1", "128GB", 999.99m, "USD"),
        }));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ProductNotFound", result.Error.Code);
        Assert.Empty(_variants.Variants);
    }

    [Fact]
    public async Task CreateVariants_DuplicateSkuWithinBatch_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        var handler = new CreateProductVariantsCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantsCommand(product.Id, new[]
        {
            new CreateProductVariantItemCommand("SKU-1", "128GB", 999.99m, "USD"),
            new CreateProductVariantItemCommand("SKU-1", "256GB", 999.99m, "USD"),
        }));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateVariantSku", result.Error.Code);
        Assert.Empty(_variants.Variants);
    }

    [Fact]
    public async Task CreateVariants_DuplicateSkuInRepository_ReturnsConflict()
    {
        var product = TestProducts.Create();
        await _products.AddAsync(product);
        await _variants.AddAsync(TestVariants.Create(productId: product.Id, sku: "SKU-1"));
        var handler = new CreateProductVariantsCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantsCommand(product.Id, new[]
        {
            new CreateProductVariantItemCommand("SKU-1", "128GB", 999.99m, "USD"),
        }));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateVariantSku", result.Error.Code);
        Assert.Single(_variants.Variants);
    }

    [Fact]
    public async Task CreateVariants_EmptyName_ReturnsValidation()
    {
        await _products.AddAsync(TestProducts.Create());
        var handler = new CreateProductVariantsCommandHandler(_products, _variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new CreateProductVariantsCommand(Guid.NewGuid(), new[]
        {
            new CreateProductVariantItemCommand("SKU-1", " ", 999.99m, "USD"),
        }));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
        Assert.Empty(_variants.Variants);
    }

    // ---- UpdateProductVariant ----

    [Fact]
    public async Task UpdateVariant_Succeeds_AndAppliesChanges()
    {
        var variant = TestVariants.Create(sku: "SKU-1", name: "128GB");
        await _variants.AddAsync(variant);
        var handler = new UpdateProductVariantCommandHandler(_variants, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductVariantCommand(variant.Id, "SKU-2", "256GB"));

        Assert.True(result.IsSuccess);
        Assert.Equal("SKU-2", variant.Sku.Value);
        Assert.Equal("256GB", variant.Name);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateVariant_VariantNotFound_ReturnsNotFound()
    {
        var handler = new UpdateProductVariantCommandHandler(_variants, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductVariantCommand(Guid.NewGuid(), "SKU-2", "256GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.VariantNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task UpdateVariant_InvalidSku_ReturnsValidation()
    {
        var variant = TestVariants.Create();
        await _variants.AddAsync(variant);
        var handler = new UpdateProductVariantCommandHandler(_variants, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductVariantCommand(variant.Id, "bad sku!", "256GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.InvalidSku", result.Error.Code);
    }

    [Fact]
    public async Task UpdateVariant_DuplicateSku_ReturnsConflict()
    {
        var variant = TestVariants.Create(sku: "SKU-1");
        await _variants.AddAsync(variant);
        await _variants.AddAsync(TestVariants.Create(sku: "SKU-2"));
        var handler = new UpdateProductVariantCommandHandler(_variants, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductVariantCommand(variant.Id, "SKU-2", "256GB"));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DuplicateVariantSku", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.Equal("SKU-1", variant.Sku.Value);
    }

    [Fact]
    public async Task UpdateVariant_SameSku_DoesNotTriggerDuplicateCheck()
    {
        var variant = TestVariants.Create(sku: "SKU-1", name: "128GB");
        await _variants.AddAsync(variant);
        var handler = new UpdateProductVariantCommandHandler(_variants, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new UpdateProductVariantCommand(variant.Id, "sku-1", "256GB"));

        Assert.True(result.IsSuccess);
        Assert.Equal("SKU-1", variant.Sku.Value);
        Assert.Equal("256GB", variant.Name);
    }

    // ---- DeleteProductVariant ----

    [Fact]
    public async Task DeleteVariant_MarksVariantDeleted()
    {
        var variant = TestVariants.Create();
        await _variants.AddAsync(variant);
        var handler = new DeleteProductVariantCommandHandler(_variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductVariantCommand(variant.Id));

        Assert.True(result.IsSuccess);
        Assert.True(variant.IsDeleted);
        Assert.Equal(_clock.UtcNow, variant.DeletedAtUtc);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task DeleteVariant_VariantNotFound_ReturnsNotFound()
    {
        var handler = new DeleteProductVariantCommandHandler(_variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductVariantCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.VariantNotFound", result.Error.Code);
    }

    [Fact]
    public async Task DeleteVariant_EmptyId_ReturnsValidation()
    {
        var handler = new DeleteProductVariantCommandHandler(_variants, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeleteProductVariantCommand(Guid.Empty));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.ValidationFailed", result.Error.Code);
    }
}
