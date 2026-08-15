using FluxCommerce.BuildingBlocks.Outbox.Messages;
using FluxCommerce.IntegrationTests.TestInfrastructure;
using FluxCommerce.Modules.Inventory.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FluxCommerce.IntegrationTests;

/// <summary>
/// Phase 6.2: verifies the Inventory OutboxMessages table on the real SQL Server engine — the
/// migration is applied by the fixture and the OutboxMessage entity round-trips every field,
/// including the int status representation and the (max) payload column.
/// </summary>
[Collection(SqlServerInventoryTestCollection.Name)]
public class InventoryOutboxPersistenceTests
{
    private readonly SqlServerInventoryTestFixture _fixture;

    public InventoryOutboxPersistenceTests(SqlServerInventoryTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OutboxMessage_RoundTrips_OnSqlServer()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var message = CreateMessage();

        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        try
        {
            var loaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);

            Assert.Equal(message.EventId, loaded.EventId);
            Assert.Equal(message.Type, loaded.Type);
            Assert.Equal(message.Payload, loaded.Payload);
            Assert.Equal(message.CorrelationId, loaded.CorrelationId);
            Assert.Equal(message.CausationId, loaded.CausationId);
            Assert.Equal(message.OccurredAtUtc, loaded.OccurredAtUtc);
            Assert.Equal(message.CreatedAtUtc, loaded.CreatedAtUtc);
            Assert.Equal(message.ProcessedAtUtc, loaded.ProcessedAtUtc);
            Assert.Equal(OutboxStatus.Pending, loaded.Status);
            Assert.Equal(message.RetryCount, loaded.RetryCount);
            Assert.Equal(message.Error, loaded.Error);
        }
        finally
        {
            await context.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task OutboxMessage_Status_IsPersistedAsInt_OnSqlServer()
    {
        using var scope = _fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var message = CreateMessage(status: OutboxStatus.Failed);
        context.OutboxMessages.Add(message);
        await context.SaveChangesAsync();

        try
        {
            var storedValue = await context.Database
                .SqlQueryRaw<int>("SELECT Status AS Value FROM OutboxMessages WHERE Id = {0}", message.Id)
                .SingleAsync();

            Assert.Equal((int)OutboxStatus.Failed, storedValue);

            var loaded = await context.OutboxMessages.SingleAsync(m => m.Id == message.Id);
            Assert.Equal(OutboxStatus.Failed, loaded.Status);
        }
        finally
        {
            await context.OutboxMessages
                .Where(m => m.Id == message.Id)
                .ExecuteDeleteAsync();
        }
    }

    private static OutboxMessage CreateMessage(OutboxStatus status = OutboxStatus.Pending) =>
        new()
        {
            Id = Guid.NewGuid(),
            EventId = Guid.NewGuid(),
            Type = "FluxCommerce.IntegrationTests.TestDomainEvent",
            Payload = "{\"sku\":\"SKU-1\",\"quantity\":3,\"status\":\"pending\"}",
            CorrelationId = Guid.NewGuid(),
            CausationId = Guid.NewGuid(),
            OccurredAtUtc = new DateTime(2026, 2, 10, 8, 15, 0),
            CreatedAtUtc = new DateTime(2026, 2, 10, 8, 15, 1),
            ProcessedAtUtc = null,
            Status = status,
            RetryCount = 0,
            Error = null
        };
}