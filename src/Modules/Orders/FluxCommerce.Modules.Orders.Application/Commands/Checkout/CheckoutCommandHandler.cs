using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Cart.Contracts.Abstractions;
using FluxCommerce.Modules.Catalog.Contracts.Abstractions;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Models;
using FluxCommerce.Modules.Orders.Application.Enums;
using FluxCommerce.Modules.Orders.Application.Services;
using FluxCommerce.Modules.Orders.Domain.Aggregates;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;
using System.Diagnostics;

namespace FluxCommerce.Modules.Orders.Application.Commands.Checkout;

/// <summary>
/// Coordinates the Phase 4.7+ Checkout flow: it authenticates the caller, reads the Cart as a
/// checkout snapshot, batch-revalidates the variants against the current Catalog state, assembles the
/// immutable Order snapshot and persists it atomically together with its (Customer, Idempotency-Key)
/// record. Reusing an Idempotency-Key for the same request returns the previously placed Order; a
/// different request carrying the same key is rejected with a conflict. Only after the Order and its
/// idempotency record are durably committed does Phase 4.8 clear the purchased Cart as a best-effort
/// step whose failure never rolls back or duplicates the Order.
/// </summary>
public sealed class CheckoutCommandHandler : ICommandHandler<CheckoutCommand, Result<Order>>
{
    public const int IdempotencyKeyMaxLength = 128;

    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ICartCheckoutSnapshotProvider _cartSnapshotProvider;
    private readonly ICatalogVariantCheckoutLookup _catalogLookup;
    private readonly ICheckoutOrderSnapshotBuilder _snapshotBuilder;
    private readonly ICheckoutStore _checkoutStore;
    private readonly ICartCheckoutConsumption _cartConsumption;

    public CheckoutCommandHandler(
        ICurrentUser currentUser,
        IDateTimeProvider dateTimeProvider,
        ICartCheckoutSnapshotProvider cartSnapshotProvider,
        ICatalogVariantCheckoutLookup catalogLookup,
        ICheckoutOrderSnapshotBuilder snapshotBuilder,
        ICheckoutStore checkoutStore,
        ICartCheckoutConsumption cartConsumption)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _cartSnapshotProvider = cartSnapshotProvider ?? throw new ArgumentNullException(nameof(cartSnapshotProvider));
        _catalogLookup = catalogLookup ?? throw new ArgumentNullException(nameof(catalogLookup));
        _snapshotBuilder = snapshotBuilder ?? throw new ArgumentNullException(nameof(snapshotBuilder));
        _checkoutStore = checkoutStore ?? throw new ArgumentNullException(nameof(checkoutStore));
        _cartConsumption = cartConsumption ?? throw new ArgumentNullException(nameof(cartConsumption));
    }

    public async Task<Result<Order>> HandleAsync(
        CheckoutCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userId = _currentUser.UserId;
        if (!_currentUser.IsAuthenticated || userId is null)
            return Result.Failure<Order>(CheckoutErrors.AuthenticationRequired);

        var idempotencyKey = command.IdempotencyKey?.Trim();
        if (string.IsNullOrEmpty(idempotencyKey) || idempotencyKey.Length > IdempotencyKeyMaxLength)
            return Result.Failure<Order>(CheckoutErrors.InvalidIdempotencyKey);

        var fingerprintV1 = CheckoutRequestFingerprint.Compute(
            command.CartId,
            command.ShippingAddress,
            command.BillingAddress);

        var existing = await _checkoutStore
            .FindIdempotencyRecordAsync(userId.Value, idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            return await ResolveExistingAsync(
                existing,
                fingerprintV1,
                ct => ComputeCurrentV2Async(command, ct),
                cancellationToken).ConfigureAwait(false);
        }

        var cart = await _cartSnapshotProvider
            .GetCheckoutSnapshotAsync(command.CartId, cancellationToken)
            .ConfigureAwait(false);

        if (cart is null)
            return Result.Failure<Order>(CheckoutErrors.CartNotFound);

        if (cart.CustomerId is null || cart.CustomerId.Value != userId.Value)
            return Result.Failure<Order>(CheckoutErrors.CartAccessDenied);

        var variantIds = cart.Items.Select(item => item.VariantId).ToList();

        var revalidation = await _catalogLookup
            .GetCheckoutInfoAsync(variantIds, cancellationToken)
            .ConfigureAwait(false);

        OrderAddress shippingAddress;
        OrderAddress billingAddress;
        try
        {
            shippingAddress = MapAddress(command.ShippingAddress);
            billingAddress = MapAddress(command.BillingAddress);
        }
        catch (ArgumentException)
        {
            return Result.Failure<Order>(CheckoutErrors.InvalidAddress);
        }

        var utcNow = new DateTimeOffset(_dateTimeProvider.UtcNow);

        var build = _snapshotBuilder.Build(
            cart,
            revalidation,
            new CustomerId(userId.Value),
            OrderNumber.Create(),
            shippingAddress,
            billingAddress,
            utcNow);

        if (build.IsFailure)
            return Result.Failure<Order>(build.Error);

        var order = build.Value;

        // v2 records cover cart lines; the cart is guaranteed present on this path.
        var fingerprintV2 = CheckoutRequestFingerprint.ComputeV2(
            command.CartId,
            command.ShippingAddress,
            command.BillingAddress,
            cart.Items.Select(item => new CheckoutRequestFingerprint.CartLine(item.VariantId, item.Quantity)));

        var persist = await _checkoutStore.PersistAsync(
            order,
            new CheckoutIdempotencyRecord(
                Guid.NewGuid(),
                userId.Value,
                idempotencyKey,
                order.Id.Value,
                fingerprintV2,
                utcNow),
            cancellationToken).ConfigureAwait(false);

        if (persist.Status == CheckoutPersistStatus.Created)
        {
            await ConsumeCartBestEffortAsync(command.CartId, userId.Value, cancellationToken).ConfigureAwait(false);

            return Result.Success(order);
        }

        return await ResolveExistingAsync(
            persist.Existing!,
            fingerprintV1,
            _ => Task.FromResult<string?>(fingerprintV2),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Phase 4.8: clears the customer's Cart only AFTER the Order and its idempotency record have
    /// been durably committed. The outcome is deliberately ignored: a failed Cart clear must never
    /// roll back the Order or produce a retry duplicate, and the authorization guarantees the commit
    /// is unreachable until the Cart was validated as owned by the caller. Replays that resolve an
    /// existing idempotency record skip this step so a late retry can never clear items added to the
    /// Cart after the original Checkout.
    /// </summary>
    private async Task ConsumeCartBestEffortAsync(
        Guid cartId,
        Guid customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var outcome = await _cartConsumption
                .ConsumeAsync(cartId, customerId, cancellationToken)
                .ConfigureAwait(false);

            // The transaction boundary stays Orders-only: a non-consumed cart must never roll back
            // or duplicate a committed Checkout. The outcome is recorded on the ambient activity
            // (Application never logs directly) so a stuck cart stays visible in tracing.
            if (outcome.Status != CartCheckoutConsumptionStatus.Consumed)
            {
                Activity.Current?.AddEvent(new ActivityEvent(
                    "checkout.cart_consumption_not_consumed",
                    tags: new ActivityTagsCollection
                    {
                        { "cart.id", cartId.ToString() },
                        { "consumption.status", outcome.Status.ToString() }
                    }));
            }
        }
        catch (Exception ex)
        {
            Activity.Current?.AddEvent(new ActivityEvent(
                "checkout.cart_consumption_failed",
                tags: new ActivityTagsCollection
                {
                    { "cart.id", cartId.ToString() },
                    { "exception.type", ex.GetType().FullName },
                    { "exception.message", ex.Message }
                }));
        }
    }

    private async Task<Result<Order>> ResolveExistingAsync(
        CheckoutIdempotencyRecord existing,
        string fingerprintV1,
        Func<CancellationToken, Task<string?>> computeV2Async,
        CancellationToken cancellationToken)
    {
        // v2 rows are compared with the current v2 hash, which needs the cart.
        // The cart is loaded lazily so legacy v1 replays keep their no-cart fast path.
        // When the cart is already consumed (post-commit replay), Matches falls back
        // to the v1 half embedded in the stored v2 fingerprint.
        string? fingerprintV2 = null;
        if (CheckoutRequestFingerprint.IsV2(existing.RequestFingerprint))
            fingerprintV2 = await computeV2Async(cancellationToken).ConfigureAwait(false);

        if (!CheckoutRequestFingerprint.Matches(existing.RequestFingerprint, fingerprintV1, fingerprintV2))
            return Result.Failure<Order>(CheckoutErrors.IdempotencyConflict);

        var order = await _checkoutStore
            .GetOrderAsync(new OrderId(existing.OrderId), cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
            return Result.Failure<Order>(OrderQueryErrors.NotFound);

        return Result.Success(order);
    }

    private async Task<string?> ComputeCurrentV2Async(
        CheckoutCommand command,
        CancellationToken cancellationToken)
    {
        var cart = await _cartSnapshotProvider
            .GetCheckoutSnapshotAsync(command.CartId, cancellationToken)
            .ConfigureAwait(false);

        if (cart is null)
            return null;

        return CheckoutRequestFingerprint.ComputeV2(
            command.CartId,
            command.ShippingAddress,
            command.BillingAddress,
            cart.Items.Select(item => new CheckoutRequestFingerprint.CartLine(item.VariantId, item.Quantity)));
    }

    private static OrderAddress MapAddress(CheckoutAddressData address) =>
        new(
            address.RecipientName,
            address.Line1,
            address.Line2,
            address.City,
            address.Region,
            address.PostalCode,
            address.CountryCode);
}
