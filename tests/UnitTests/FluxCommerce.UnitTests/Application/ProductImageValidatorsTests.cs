using FluxCommerce.Modules.Catalog.Application.Commands.AddProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.DeleteProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.ReorderProductImages;
using FluxCommerce.Modules.Catalog.Application.Commands.SetPrimaryProductImage;
using FluxCommerce.Modules.Catalog.Application.Commands.UpdateProductImage;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class ProductImageValidatorsTests
{
    private static readonly FakeLocalizer Localizer = new();

    // ---- AddProductImageCommandValidator ----

    [Fact]
    public void AddImage_ValidCommand_IsValid()
    {
        var validator = new AddProductImageCommandValidator(Localizer);

        var result = validator.Validate(new AddProductImageCommand(
            Guid.NewGuid(),
            "https://example.com/a.jpg",
            "Alt",
            false,
            0,
            null));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void AddImage_EmptyProductId_IsInvalid()
    {
        var validator = new AddProductImageCommandValidator(Localizer);

        var result = validator.Validate(new AddProductImageCommand(Guid.Empty, "https://example.com/a.jpg", null, false, 0, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }

    [Fact]
    public void AddImage_EmptyUrl_IsInvalid()
    {
        var validator = new AddProductImageCommandValidator(Localizer);

        var result = validator.Validate(new AddProductImageCommand(Guid.NewGuid(), "", null, false, 0, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ImageUrl"));
    }

    [Fact]
    public void AddImage_UrlOverMaxLength_IsInvalid()
    {
        var validator = new AddProductImageCommandValidator(Localizer);

        var result = validator.Validate(new AddProductImageCommand(
            Guid.NewGuid(),
            string.Concat(Enumerable.Repeat("a", 2049)),
            null,
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ImageUrl"));
    }

    [Fact]
    public void AddImage_AltTextOverMaxLength_IsInvalid()
    {
        var validator = new AddProductImageCommandValidator(Localizer);

        var result = validator.Validate(new AddProductImageCommand(
            Guid.NewGuid(),
            "https://example.com/a.jpg",
            string.Concat(Enumerable.Repeat("a", 501)),
            false,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("AltText"));
    }

    [Fact]
    public void AddImage_NegativeDisplayOrder_IsInvalid()
    {
        var validator = new AddProductImageCommandValidator(Localizer);

        var result = validator.Validate(new AddProductImageCommand(Guid.NewGuid(), "https://example.com/a.jpg", null, false, -1, null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("DisplayOrder"));
    }

    [Fact]
    public void AddImage_NullAltText_IsValid()
    {
        var validator = new AddProductImageCommandValidator(Localizer);

        var result = validator.Validate(new AddProductImageCommand(Guid.NewGuid(), "https://example.com/a.jpg", null, false, 0, null));

        Assert.True(result.IsValid);
    }

    // ---- UpdateProductImageCommandValidator ----

    [Fact]
    public void UpdateImage_ValidCommand_IsValid()
    {
        var validator = new UpdateProductImageCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductImageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/a.jpg",
            null,
            0,
            null));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void UpdateImage_EmptyImageId_IsInvalid()
    {
        var validator = new UpdateProductImageCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductImageCommand(
            Guid.NewGuid(),
            Guid.Empty,
            "https://example.com/a.jpg",
            null,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ImageId"));
    }

    [Fact]
    public void UpdateImage_EmptyUrl_IsInvalid()
    {
        var validator = new UpdateProductImageCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductImageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            " ",
            null,
            0,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ImageUrl"));
    }

    [Fact]
    public void UpdateImage_NegativeDisplayOrder_IsInvalid()
    {
        var validator = new UpdateProductImageCommandValidator(Localizer);

        var result = validator.Validate(new UpdateProductImageCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "https://example.com/a.jpg",
            null,
            -1,
            null));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("DisplayOrder"));
    }

    // ---- DeleteProductImageCommandValidator ----

    [Fact]
    public void DeleteImage_ValidCommand_IsValid()
    {
        var validator = new DeleteProductImageCommandValidator(Localizer);

        var result = validator.Validate(new DeleteProductImageCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DeleteImage_EmptyImageId_IsInvalid()
    {
        var validator = new DeleteProductImageCommandValidator(Localizer);

        var result = validator.Validate(new DeleteProductImageCommand(Guid.NewGuid(), Guid.Empty));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ImageId"));
    }

    // ---- SetPrimaryProductImageCommandValidator ----

    [Fact]
    public void SetPrimaryImage_ValidCommand_IsValid()
    {
        var validator = new SetPrimaryProductImageCommandValidator(Localizer);

        var result = validator.Validate(new SetPrimaryProductImageCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void SetPrimaryImage_EmptyProductId_IsInvalid()
    {
        var validator = new SetPrimaryProductImageCommandValidator(Localizer);

        var result = validator.Validate(new SetPrimaryProductImageCommand(Guid.Empty, Guid.NewGuid()));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("ProductId"));
    }

    // ---- ReorderProductImagesCommandValidator ----

    [Fact]
    public void ReorderImages_ValidCommand_IsValid()
    {
        var validator = new ReorderProductImagesCommandValidator(Localizer);

        var result = validator.Validate(new ReorderProductImagesCommand(Guid.NewGuid(), new[] { Guid.NewGuid(), Guid.NewGuid() }));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReorderImages_EmptyCollection_IsInvalid()
    {
        var validator = new ReorderProductImagesCommandValidator(Localizer);

        var result = validator.Validate(new ReorderProductImagesCommand(Guid.NewGuid(), Array.Empty<Guid>()));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("OrderedImageIds"));
    }

    [Fact]
    public void ReorderImages_NullCollection_IsInvalid()
    {
        var validator = new ReorderProductImagesCommandValidator(Localizer);

        var result = validator.Validate(new ReorderProductImagesCommand(Guid.NewGuid(), null!));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("OrderedImageIds"));
    }

    [Fact]
    public void ReorderImages_EmptyGuidInCollection_IsInvalid()
    {
        var validator = new ReorderProductImagesCommandValidator(Localizer);

        var result = validator.Validate(new ReorderProductImagesCommand(Guid.NewGuid(), new[] { Guid.Empty, Guid.NewGuid() }));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("OrderedImageIds"));
    }

    [Fact]
    public void ReorderImages_DuplicateIds_IsInvalid()
    {
        var validator = new ReorderProductImagesCommandValidator(Localizer);
        var id = Guid.NewGuid();

        var result = validator.Validate(new ReorderProductImagesCommand(Guid.NewGuid(), new[] { id, id }));

        Assert.False(result.IsValid);
        Assert.True(result.Errors.ContainsKey("OrderedImageIds"));
    }
}
