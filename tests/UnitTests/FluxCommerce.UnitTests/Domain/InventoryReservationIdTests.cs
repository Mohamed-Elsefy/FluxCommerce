using FluxCommerce.Modules.Inventory.Domain.ValueObjects;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class InventoryReservationIdTests
{
    [Fact]
    public void Constructor_WithValidGuid_Succeeds()
    {
        var value = Guid.NewGuid();

        var reservationId = new InventoryReservationId(value);

        Assert.Equal(value, reservationId.Value);
    }

    [Fact]
    public void Constructor_WithEmptyGuid_Throws()
    {
        Assert.Throws<ArgumentException>(() => new InventoryReservationId(Guid.Empty));
    }

    [Fact]
    public void Create_ReturnsNonEmptyIdentifier()
    {
        var reservationId = InventoryReservationId.Create();

        Assert.NotEqual(Guid.Empty, reservationId.Value);
    }

    [Fact]
    public void Create_ReturnsUniqueIdentifiers()
    {
        var first = InventoryReservationId.Create();
        var second = InventoryReservationId.Create();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equality_SameValue_AreEqual()
    {
        var value = Guid.NewGuid();

        var first = new InventoryReservationId(value);
        var second = new InventoryReservationId(value);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DifferentValue_AreNotEqual()
    {
        Assert.NotEqual(InventoryReservationId.Create(), InventoryReservationId.Create());
    }
}
