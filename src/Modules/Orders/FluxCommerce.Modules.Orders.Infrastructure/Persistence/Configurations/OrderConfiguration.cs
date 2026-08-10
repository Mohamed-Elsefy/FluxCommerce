using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FluxCommerce.Modules.Orders.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");

        builder.HasKey(order => order.Id);

        builder.Property(order => order.Id)
            .HasColumnName("Id")
            .HasConversion(orderId => orderId.Value, value => new OrderId(value));

        builder.Property(order => order.CustomerId)
            .HasColumnName("CustomerId")
            .HasConversion(customerId => customerId.Value, value => new CustomerId(value));

        builder.Property(order => order.Status)
            .HasColumnName("Status");

        builder.Property(order => order.Version)
            .HasColumnName("Version");

        builder.Property(order => order.CreatedAtUtc)
            .HasColumnName("CreatedAtUtc");

        builder.Property(order => order.UpdatedAtUtc)
            .HasColumnName("UpdatedAtUtc");

        builder.OwnsOne(order => order.OrderNumber, orderNumber =>
        {
            orderNumber.Property(number => number.Value)
                .HasColumnName("OrderNumber")
                .HasMaxLength(OrderNumber.MaxLength);

            orderNumber.HasIndex(number => number.Value)
                .IsUnique();
        });

        builder.OwnsOne(order => order.ShippingAddress, address => ConfigureAddress(address, "Shipping"));

        builder.OwnsOne(order => order.BillingAddress, address => ConfigureAddress(address, "Billing"));

        builder.OwnsOne(order => order.ShippingCost, money => ConfigureMoney(money, "ShippingCost"));

        builder.OwnsOne(order => order.Discount, money => ConfigureMoney(money, "Discount"));

        builder.OwnsMany(order => order.LineItems, lineItem =>
        {
            lineItem.ToTable("OrderItems");

            lineItem.WithOwner().HasForeignKey("OrderId");

            lineItem.HasKey("OrderId", nameof(OrderLineItem.Id));

            lineItem.Property(item => item.Id)
                .HasColumnName("VariantId")
                .HasConversion(variantId => variantId.Value, value => new OrderVariantId(value));

            lineItem.Property(item => item.ProductId)
                .HasColumnName("ProductId")
                .HasConversion(productId => productId.Value, value => new OrderProductId(value));

            lineItem.Property(item => item.Sku)
                .HasColumnName("Sku")
                .HasMaxLength(OrderSku.MaxLength)
                .HasConversion(sku => sku.Value, value => new OrderSku(value));

            lineItem.Property(item => item.ProductName)
                .HasColumnName("ProductName")
                .HasMaxLength(OrderLineItem.ProductNameMaxLength);

            lineItem.Property(item => item.Quantity)
                .HasColumnName("Quantity");

            lineItem.OwnsOne(item => item.UnitPrice, money => ConfigureMoney(money, "UnitPrice"));
        });

        builder.Navigation(order => order.LineItems)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(order => order.CustomerId)
            .HasDatabaseName("IX_Orders_CustomerId");

        builder.HasIndex(order => order.CreatedAtUtc)
            .HasDatabaseName("IX_Orders_CreatedAtUtc");
    }

    private static void ConfigureAddress(
        OwnedNavigationBuilder<Order, OrderAddress> address,
        string prefix)
    {
        address.Property(a => a.RecipientName)
            .HasColumnName($"{prefix}RecipientName")
            .HasMaxLength(OrderAddress.RecipientNameMaxLength);

        address.Property(a => a.Line1)
            .HasColumnName($"{prefix}Line1")
            .HasMaxLength(OrderAddress.Line1MaxLength);

        address.Property(a => a.Line2)
            .HasColumnName($"{prefix}Line2")
            .HasMaxLength(OrderAddress.Line2MaxLength);

        address.Property(a => a.City)
            .HasColumnName($"{prefix}City")
            .HasMaxLength(OrderAddress.CityMaxLength);

        address.Property(a => a.Region)
            .HasColumnName($"{prefix}Region")
            .HasMaxLength(OrderAddress.RegionMaxLength);

        address.Property(a => a.PostalCode)
            .HasColumnName($"{prefix}PostalCode")
            .HasMaxLength(OrderAddress.PostalCodeMaxLength);

        address.Property(a => a.CountryCode)
            .HasColumnName($"{prefix}CountryCode")
            .HasMaxLength(OrderAddress.CountryCodeLength);
    }

    private static void ConfigureMoney(
        OwnedNavigationBuilder<Order, OrderMoney> money,
        string prefix)
    {
        money.Property(m => m.Amount)
            .HasColumnName($"{prefix}Amount")
            .HasPrecision(18, 6);

        money.Property(m => m.Currency)
            .HasColumnName($"{prefix}Currency")
            .HasMaxLength(3);
    }

    private static void ConfigureMoney(
        OwnedNavigationBuilder<OrderLineItem, OrderMoney> money,
        string prefix)
    {
        money.Property(m => m.Amount)
            .HasColumnName($"{prefix}Amount")
            .HasPrecision(18, 6);

        money.Property(m => m.Currency)
            .HasColumnName($"{prefix}Currency")
            .HasMaxLength(3);
    }
}
