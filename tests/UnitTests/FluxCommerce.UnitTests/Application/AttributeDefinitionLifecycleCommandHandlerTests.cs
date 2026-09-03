using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.Modules.Catalog.Application.Commands.ActivateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.DeactivateAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.RestoreAttributeDefinition;
using FluxCommerce.Modules.Catalog.Application.Commands.SoftDeleteAttributeDefinition;
using FluxCommerce.Modules.Catalog.Domain.Enums;
using FluxCommerce.UnitTests.TestInfrastructure;
using Xunit;

namespace FluxCommerce.UnitTests.Application;

public class AttributeDefinitionLifecycleCommandHandlerTests
{
    private readonly FakeAttributeDefinitionRepository _attributeDefinitions = new();
    private readonly FakeCatalogUnitOfWork _unitOfWork = new();
    private readonly FakeDateTimeProvider _clock = new();

    [Fact]
    public async Task Activate_ActivatesInactiveAttributeDefinition()
    {
        var attributeDefinition = TestAttributeDefinitions.Create();
        attributeDefinition.Deactivate();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new ActivateAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateAttributeDefinitionCommand(attributeDefinition.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(AttributeStatus.Active, attributeDefinition.Status);
        Assert.Equal(1, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Activate_NotFound_ReturnsNotFound()
    {
        var handler = new ActivateAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(0, _unitOfWork.SaveChangesCount);
    }

    [Fact]
    public async Task Activate_Deleted_ReturnsConflict()
    {
        var attributeDefinition = TestAttributeDefinitions.CreateDeleted();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new ActivateAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new ActivateAttributeDefinitionCommand(attributeDefinition.Id));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.DeletedAttributeDefinitionCannotBeModified", result.Error.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
    }

    [Fact]
    public async Task Deactivate_DeactivatesActiveAttributeDefinition()
    {
        var attributeDefinition = TestAttributeDefinitions.Create();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new DeactivateAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeactivateAttributeDefinitionCommand(attributeDefinition.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(AttributeStatus.Inactive, attributeDefinition.Status);
    }

    [Fact]
    public async Task Deactivate_NotFound_ReturnsNotFound()
    {
        var handler = new DeactivateAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new DeactivateAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
    }

    [Fact]
    public async Task SoftDelete_MarksDeleted_WithTimestamp()
    {
        var attributeDefinition = TestAttributeDefinitions.Create();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new SoftDeleteAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new SoftDeleteAttributeDefinitionCommand(attributeDefinition.Id));

        Assert.True(result.IsSuccess);
        Assert.True(attributeDefinition.IsDeleted);
        Assert.Equal(_clock.UtcNow, attributeDefinition.DeletedAtUtc);
    }

    [Fact]
    public async Task SoftDelete_NotFound_ReturnsNotFound()
    {
        var handler = new SoftDeleteAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, _clock, new FakeLocalizer());

        var result = await handler.HandleAsync(new SoftDeleteAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
    }

    [Fact]
    public async Task Restore_RestoresDeletedAttributeDefinition()
    {
        var attributeDefinition = TestAttributeDefinitions.CreateDeleted();
        await _attributeDefinitions.AddAsync(attributeDefinition);
        var handler = new RestoreAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RestoreAttributeDefinitionCommand(attributeDefinition.Id));

        Assert.True(result.IsSuccess);
        Assert.False(attributeDefinition.IsDeleted);
        Assert.Null(attributeDefinition.DeletedAtUtc);
    }

    [Fact]
    public async Task Restore_NotFound_ReturnsNotFound()
    {
        var handler = new RestoreAttributeDefinitionCommandHandler(_attributeDefinitions, _unitOfWork, new FakeLocalizer());

        var result = await handler.HandleAsync(new RestoreAttributeDefinitionCommand(Guid.NewGuid()));

        Assert.True(result.IsFailure);
        Assert.Equal("Catalog.AttributeDefinitionNotFound", result.Error.Code);
    }
}
