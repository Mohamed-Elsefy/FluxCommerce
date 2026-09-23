using FluxCommerce.BuildingBlocks.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Entities;
using FluxCommerce.Modules.Orders.Domain.Enums;
using FluxCommerce.Modules.Orders.Domain.Errors;
using FluxCommerce.Modules.Orders.Domain.Events;
using FluxCommerce.Modules.Orders.Domain.Results;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Domain.Aggregates;

public sealed class Order : AggregateRoot<OrderId>
{
    public const long InitialVersion = 1;

    public CustomerId CustomerId { get; }

    public OrderNumber OrderNumber { get; }

    public OrderStatus Status { get; private set; }

    public OrderAddress ShippingAddress { get; }

    public OrderAddress BillingAddress { get; }

    public OrderMoney ShippingCost { get; private set; }

    public OrderMoney Discount { get; private set; }

    public long Version => _version;

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc => _updatedAtUtc;

    public OrderMoney Subtotal => ComputeSubtotal();

    public OrderMoney GrandTotal => Subtotal.Add(ShippingCost).Subtract(Discount);

    private long _version;

    private DateTimeOffset _updatedAtUtc;

    private readonly List<OrderLineItem> _lineItems = [];

    public IReadOnlyCollection<OrderLineItem> LineItems => _lineItems;

    private Order(
        OrderId id,
        CustomerId customerId,
        OrderNumber orderNumber,
        OrderAddress shippingAddress,
        OrderAddress billingAddress,
        OrderStatus status,
        long version,
        DateTimeOffset createdAtUtc)
        : base(id)
    {
        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        CustomerId = customerId ?? throw new ArgumentNullException(nameof(customerId));
        OrderNumber = orderNumber ?? throw new ArgumentNullException(nameof(orderNumber));
        ShippingAddress = shippingAddress ?? throw new ArgumentNullException(nameof(shippingAddress));
        BillingAddress = billingAddress ?? throw new ArgumentNullException(nameof(billingAddress));
        Status = status;
        _version = version;
        CreatedAtUtc = createdAtUtc;
        _updatedAtUtc = createdAtUtc;
        ShippingCost = OrderMoney.Zero("USD");
        Discount = OrderMoney.Zero("USD");
    }

    #pragma warning disable CS8618
    private Order() { }
    #pragma warning restore CS8618

    public static Order Place(
        OrderId id,
        CustomerId customerId,
        OrderNumber orderNumber,
        OrderAddress shippingAddress,
        OrderAddress billingAddress,
        IReadOnlyCollection<OrderLineItem> lineItems,
        DateTimeOffset utcNow)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));

        if (lineItems is null)
            throw new ArgumentNullException(nameof(lineItems));

        if (lineItems.Count == 0)
            throw new ArgumentException("An order must contain at least one line item.", nameof(lineItems));

        var order = new Order(
            id,
            customerId,
            orderNumber,
            shippingAddress,
            billingAddress,
            OrderStatus.Pending,
            InitialVersion,
            utcNow);

        var currency = DetermineCurrency(lineItems);

        foreach (var item in lineItems)
        {
            if (item is null)
                throw new ArgumentException("An order cannot contain a null line item.", nameof(lineItems));

            if (order._lineItems.Any(existing => existing.VariantId == item.VariantId))
                throw new ArgumentException("An order cannot contain duplicate variants.", nameof(lineItems));

            if (!string.Equals(item.UnitPrice.Currency, currency, StringComparison.Ordinal))
                throw new ArgumentException("All line items must share the same currency.", nameof(lineItems));

            order._lineItems.Add(item);
        }

        order.ShippingCost = OrderMoney.Zero(currency);
        order.Discount = OrderMoney.Zero(currency);

        order.AddDomainEvent(new OrderCreated(order.Id, order.CustomerId, order.OrderNumber, utcNow));

        return order;
    }

    public static Order Hydrate(
        OrderId id,
        CustomerId customerId,
        OrderNumber orderNumber,
        OrderAddress shippingAddress,
        OrderAddress billingAddress,
        OrderStatus status,
        long version,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        IReadOnlyCollection<OrderLineItem> lineItems,
        OrderMoney? shippingCost = null,
        OrderMoney? discount = null)
    {
        if (id is null)
            throw new ArgumentNullException(nameof(id));

        if (version < InitialVersion)
            throw new ArgumentException(
                $"An order version must be at least {InitialVersion}.",
                nameof(version));

        if (createdAtUtc == default)
            throw new ArgumentException("A valid creation timestamp is required.", nameof(createdAtUtc));

        if (updatedAtUtc == default)
            throw new ArgumentException("A valid update timestamp is required.", nameof(updatedAtUtc));

        var order = new Order(
            id,
            customerId,
            orderNumber,
            shippingAddress,
            billingAddress,
            status,
            version,
            createdAtUtc);

        order._updatedAtUtc = updatedAtUtc;

        string? lineCurrency = null;
        if (lineItems is not null && lineItems.Count > 0)
        {
            lineCurrency = DetermineCurrency(lineItems);

            foreach (var item in lineItems)
            {
                if (item is null)
                    throw new ArgumentException("A hydrated order cannot contain a null line item.", nameof(lineItems));

                if (order._lineItems.Any(existing => existing.VariantId == item.VariantId))
                    throw new ArgumentException("A hydrated order cannot contain duplicate variants.", nameof(lineItems));

                if (!string.Equals(item.UnitPrice.Currency, lineCurrency, StringComparison.Ordinal))
                    throw new ArgumentException("All line items must share the same currency.", nameof(lineItems));

                order._lineItems.Add(item);
            }
        }

        var resolvedCurrency = lineCurrency
            ?? shippingCost?.Currency
            ?? discount?.Currency
            ?? "USD";

        order.ShippingCost = shippingCost ?? OrderMoney.Zero(resolvedCurrency);
        order.Discount = discount ?? OrderMoney.Zero(resolvedCurrency);

        if (lineCurrency is not null)
        {
            if (!string.Equals(order.ShippingCost.Currency, lineCurrency, StringComparison.Ordinal)
                || !string.Equals(order.Discount.Currency, lineCurrency, StringComparison.Ordinal))
                throw new ArgumentException("Shipping cost and discount must share the line items currency.", nameof(shippingCost));
        }

        return order;
    }

    public DomainResult AddLineItem(
        OrderProductId productId,
        OrderVariantId variantId,
        OrderSku sku,
        string productName,
        OrderMoney unitPrice,
        int quantity,
        DateTimeOffset utcNow)
    {
        if (quantity < 1)
            return DomainResult.Failure(OrderErrors.InvalidQuantity);

        if (_lineItems.Any(item => item.VariantId == variantId))
            return DomainResult.Failure(OrderErrors.DuplicateVariant);

        if (Status != OrderStatus.Pending)
            return DomainResult.Failure(OrderErrors.InvalidStateTransition);

        if (!string.Equals(unitPrice.Currency, ShippingCost.Currency, StringComparison.Ordinal))
            return DomainResult.Failure(OrderErrors.CurrencyMismatch);

        _lineItems.Add(new OrderLineItem(productId, variantId, sku, productName, unitPrice, quantity));

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult UpdateLineItemQuantity(OrderVariantId variantId, int quantity, DateTimeOffset utcNow)
    {
        if (variantId is null)
            throw new ArgumentNullException(nameof(variantId));

        if (quantity < 1)
            return DomainResult.Failure(OrderErrors.InvalidQuantity);

        if (Status != OrderStatus.Pending)
            return DomainResult.Failure(OrderErrors.InvalidStateTransition);

        var item = _lineItems.FirstOrDefault(lineItem => lineItem.VariantId == variantId);

        if (item is null)
            return DomainResult.Failure(OrderErrors.LineItemNotFound);

        if (item.Quantity == quantity)
            return DomainResult.Success();

        item.ChangeQuantity(quantity);

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult RemoveLineItem(OrderVariantId variantId, DateTimeOffset utcNow)
    {
        if (variantId is null)
            throw new ArgumentNullException(nameof(variantId));

        if (Status != OrderStatus.Pending)
            return DomainResult.Failure(OrderErrors.InvalidStateTransition);

        var item = _lineItems.FirstOrDefault(lineItem => lineItem.VariantId == variantId);

        if (item is null)
            return DomainResult.Failure(OrderErrors.LineItemNotFound);

        _lineItems.Remove(item);

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult SetShippingCost(OrderMoney shippingCost, DateTimeOffset utcNow)
    {
        if (shippingCost is null)
            throw new ArgumentNullException(nameof(shippingCost));

        if (Status != OrderStatus.Pending)
            return DomainResult.Failure(OrderErrors.InvalidStateTransition);

        if (!string.Equals(shippingCost.Currency, ShippingCost.Currency, StringComparison.Ordinal))
            return DomainResult.Failure(OrderErrors.CurrencyMismatch);

        if (shippingCost == ShippingCost)
            return DomainResult.Success();

        ShippingCost = shippingCost;

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult SetDiscount(OrderMoney discount, DateTimeOffset utcNow)
    {
        if (discount is null)
            throw new ArgumentNullException(nameof(discount));

        if (Status != OrderStatus.Pending)
            return DomainResult.Failure(OrderErrors.InvalidStateTransition);

        if (!string.Equals(discount.Currency, Discount.Currency, StringComparison.Ordinal))
            return DomainResult.Failure(OrderErrors.CurrencyMismatch);

        if (discount == Discount)
            return DomainResult.Success();

        Discount = discount;

        Touch(utcNow);

        return DomainResult.Success();
    }

    public DomainResult Cancel(DateTimeOffset utcNow)
    {
        if (Status != OrderStatus.Pending)
            return DomainResult.Failure(OrderErrors.InvalidStateTransition);

        Status = OrderStatus.Cancelled;

        Touch(utcNow);

        AddDomainEvent(new OrderCancelled(Id, CustomerId, OrderNumber, utcNow));

        return DomainResult.Success();
    }

    /// <summary>
    /// Phase 10.8: compensating cancel after post-payment refund. Allows
    /// Pending → Cancelled (same as Cancel) and Confirmed → Cancelled for refund.
    /// Idempotent on already Cancelled.
    /// </summary>
    public DomainResult CancelForRefund(DateTimeOffset utcNow)
    {
        if (Status == OrderStatus.Cancelled)
            return DomainResult.Success();

        if (Status != OrderStatus.Pending && Status != OrderStatus.Confirmed)
            return DomainResult.Failure(OrderErrors.InvalidStateTransition);

        Status = OrderStatus.Cancelled;

        Touch(utcNow);

        AddDomainEvent(new OrderCancelled(Id, CustomerId, OrderNumber, utcNow));

        return DomainResult.Success();
    }

    /// <summary>
    /// Phase 10.4: confirms the order after successful payment.
    /// Valid transition: Pending → Confirmed. Duplicate on Confirmed is idempotent;
    /// any other state is rejected.
    /// </summary>
    public DomainResult Confirm(DateTimeOffset utcNow)
    {
        if (utcNow == default)
            throw new ArgumentException("A valid timestamp is required.", nameof(utcNow));

        return Status switch
        {
            OrderStatus.Pending => ConfirmTransition(utcNow),
            OrderStatus.Confirmed => DomainResult.Success(),
            _ => DomainResult.Failure(OrderErrors.InvalidStateTransition)
        };
    }

    private DomainResult ConfirmTransition(DateTimeOffset utcNow)
    {
        Status = OrderStatus.Confirmed;
        Touch(utcNow);
        AddDomainEvent(new OrderConfirmed(Id, CustomerId, OrderNumber, utcNow));
        return DomainResult.Success();
    }

    private OrderMoney ComputeSubtotal()
    {
        var currency = _lineItems.Count > 0
            ? _lineItems[0].UnitPrice.Currency
            : ShippingCost.Currency;

        var total = _lineItems.Aggregate(
            0m,
            (sum, item) => sum + item.LineTotal.Amount);

        return new OrderMoney(total, currency);
    }

    private static string DetermineCurrency(IReadOnlyCollection<OrderLineItem> lineItems)
    {
        var currency = lineItems.First().UnitPrice.Currency;

        if (lineItems.Any(item => !string.Equals(item.UnitPrice.Currency, currency, StringComparison.Ordinal)))
            throw new ArgumentException("All line items must share the same currency.");

        return currency;
    }

    private void Touch(DateTimeOffset utcNow)
    {
        _version++;
        _updatedAtUtc = utcNow;
    }
}
