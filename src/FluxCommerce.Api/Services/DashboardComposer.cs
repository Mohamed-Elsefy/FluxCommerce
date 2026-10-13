using FluxCommerce.Api.Mapping;
using FluxCommerce.Api.Models;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Catalog.Application.Queries.GetCatalogCounts;
using FluxCommerce.Modules.Catalog.Contracts.Responses;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUserCounts;
using FluxCommerce.Modules.Identity.Application.Queries.GetAdminUsers;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Inventory.Application.Queries.GetInventoryCounts;
using FluxCommerce.Modules.Inventory.Application.ReadModels;
using FluxCommerce.Modules.Inventory.Contracts.Responses;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrderCounts;
using FluxCommerce.Modules.Orders.Application.Queries.GetAdminOrders;
using FluxCommerce.Modules.Orders.Application.Queries.GetOrderDailyPoints;
using FluxCommerce.Modules.Orders.Application.ReadModels;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPaymentStats;
using FluxCommerce.Modules.Payments.Application.Queries.GetAdminPayments;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentDailyPoints;
using FluxCommerce.Modules.Payments.Application.ReadModels;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using FluxCommerce.Modules.Payments.Domain.Enums;

namespace FluxCommerce.Api.Services;

/// <summary>
/// Host-side dashboard composition (Phase 22 E1 semantics): every section is produced
/// by its owning module's read path; this composer only fans the independent reads out
/// concurrently and assembles the snapshot. Failure precedence matches the legacy
/// sequential order (daily points, orders, payments, inventory, users, catalog) so the
/// first failing entitled section surfaces its error deterministically.
/// </summary>
public sealed class DashboardComposer : IDashboardComposer
{
    private const int RecentOrdersLimit = 5;
    private const int RecentPaymentsLimit = 5;
    private const int RecentUsersLimit = 5;

    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> _orderCountsHandler;
    private readonly IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> _recentOrdersHandler;
    private readonly IQueryHandler<GetOrderDailyPointsQuery, Result<IReadOnlyList<OrderDailyPointReadModel>>> _orderPointsHandler;
    private readonly IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> _paymentStatsHandler;
    private readonly IQueryHandler<GetAdminPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> _recentPaymentsHandler;
    private readonly IQueryHandler<GetPaymentDailyPointsQuery, Result<IReadOnlyList<PaymentDailyPointReadModel>>> _paymentPointsHandler;
    private readonly IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> _inventoryCountsHandler;
    private readonly IQueryHandler<GetCatalogCountsQuery, Result<CatalogCountsResponse>> _catalogCountsHandler;
    private readonly IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> _userCountsHandler;
    private readonly IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> _recentUsersHandler;

    public DashboardComposer(
        IDateTimeProvider dateTimeProvider,
        IQueryHandler<GetAdminOrderCountsQuery, Result<AdminOrderCountsResponse>> orderCountsHandler,
        IQueryHandler<GetAdminOrdersQuery, Result<PagedResult<OrderSummaryResponse>>> recentOrdersHandler,
        IQueryHandler<GetOrderDailyPointsQuery, Result<IReadOnlyList<OrderDailyPointReadModel>>> orderPointsHandler,
        IQueryHandler<GetAdminPaymentStatsQuery, Result<PaymentAdminStatsResponse>> paymentStatsHandler,
        IQueryHandler<GetAdminPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>> recentPaymentsHandler,
        IQueryHandler<GetPaymentDailyPointsQuery, Result<IReadOnlyList<PaymentDailyPointReadModel>>> paymentPointsHandler,
        IQueryHandler<GetInventoryCountsQuery, Result<InventoryCountsReadModel>> inventoryCountsHandler,
        IQueryHandler<GetCatalogCountsQuery, Result<CatalogCountsResponse>> catalogCountsHandler,
        IQueryHandler<GetAdminUserCountsQuery, Result<UserAdminCountsResponse>> userCountsHandler,
        IQueryHandler<GetAdminUsersQuery, Result<PagedResult<UserAdminResponse>>> recentUsersHandler)
    {
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        _orderCountsHandler = orderCountsHandler ?? throw new ArgumentNullException(nameof(orderCountsHandler));
        _recentOrdersHandler = recentOrdersHandler ?? throw new ArgumentNullException(nameof(recentOrdersHandler));
        _orderPointsHandler = orderPointsHandler ?? throw new ArgumentNullException(nameof(orderPointsHandler));
        _paymentStatsHandler = paymentStatsHandler ?? throw new ArgumentNullException(nameof(paymentStatsHandler));
        _recentPaymentsHandler = recentPaymentsHandler ?? throw new ArgumentNullException(nameof(recentPaymentsHandler));
        _paymentPointsHandler = paymentPointsHandler ?? throw new ArgumentNullException(nameof(paymentPointsHandler));
        _inventoryCountsHandler = inventoryCountsHandler ?? throw new ArgumentNullException(nameof(inventoryCountsHandler));
        _catalogCountsHandler = catalogCountsHandler ?? throw new ArgumentNullException(nameof(catalogCountsHandler));
        _userCountsHandler = userCountsHandler ?? throw new ArgumentNullException(nameof(userCountsHandler));
        _recentUsersHandler = recentUsersHandler ?? throw new ArgumentNullException(nameof(recentUsersHandler));
    }

    public async Task<Result<AdminDashboardResponse>> ComposeAsync(
        DashboardSnapshotQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entitlements = query.Entitlements;

        var dailySeries = await BuildDailySeriesAsync(query.WindowDays, entitlements, cancellationToken).ConfigureAwait(false);
        if (dailySeries.IsFailure)
            return Result<AdminDashboardResponse>.Failure(dailySeries.Error);

        // Sections fan out per owning module: queries inside one module share its scoped
        // DbContext and must stay sequential (EF Core allows one in-flight operation per
        // context), while different modules own different contexts and run concurrently.
        var ordersTask = entitlements.Orders
            ? LoadOrdersSectionAsync(cancellationToken)
            : null;
        var paymentsTask = entitlements.Payments
            ? LoadPaymentsSectionAsync(entitlements.PaymentsList, cancellationToken)
            : null;
        var inventoryTask = entitlements.Inventory
            ? _inventoryCountsHandler.HandleAsync(new GetInventoryCountsQuery(query.LowStockThreshold), cancellationToken)
            : null;
        var usersTask = entitlements.Users
            ? LoadUsersSectionAsync(cancellationToken)
            : null;
        var catalogTask = entitlements.Catalog
            ? _catalogCountsHandler.HandleAsync(new GetCatalogCountsQuery(), cancellationToken)
            : null;

        await AwaitAllAsync(ordersTask, paymentsTask, inventoryTask, usersTask, catalogTask).ConfigureAwait(false);

        AdminOrderCountsResponse? orders = null;
        IReadOnlyList<OrderSummaryResponse> recentOrders = [];
        if (ordersTask is not null)
        {
            var section = ordersTask.Result;
            if (section.IsFailure)
                return Result<AdminDashboardResponse>.Failure(section.Error);

            orders = section.Value!.Counts;
            recentOrders = section.Value!.Recent;
        }

        PaymentAdminStatsResponse? payments = null;
        IReadOnlyList<PaymentAdminResponse> recentPayments = [];
        if (paymentsTask is not null)
        {
            var section = paymentsTask.Result;
            if (section.IsFailure)
                return Result<AdminDashboardResponse>.Failure(section.Error);

            payments = section.Value!.Stats;
            recentPayments = section.Value!.Recent;
        }

        InventoryCountsResponse? inventory = null;
        if (inventoryTask is not null)
        {
            if (inventoryTask.Result.IsFailure)
                return Result<AdminDashboardResponse>.Failure(inventoryTask.Result.Error);

            inventory = InventoryResponseMapper.ToCountsResponse(inventoryTask.Result.Value!);
        }

        UserAdminCountsResponse? users = null;
        IReadOnlyList<UserAdminResponse> recentUsers = [];
        if (usersTask is not null)
        {
            var section = usersTask.Result;
            if (section.IsFailure)
                return Result<AdminDashboardResponse>.Failure(section.Error);

            users = section.Value!.Counts;
            recentUsers = section.Value!.Recent;
        }

        CatalogCountsResponse? catalog = null;
        if (catalogTask is not null)
        {
            if (catalogTask.Result.IsFailure)
                return Result<AdminDashboardResponse>.Failure(catalogTask.Result.Error);

            catalog = catalogTask.Result.Value;
        }

        return Result.Success(new AdminDashboardResponse(
            orders, payments, inventory, catalog, users,
            recentOrders, recentPayments, recentUsers, dailySeries.Value!));
    }

    private sealed record OrdersSection(
        AdminOrderCountsResponse Counts,
        IReadOnlyList<OrderSummaryResponse> Recent);

    private sealed record PaymentsSection(
        PaymentAdminStatsResponse Stats,
        IReadOnlyList<PaymentAdminResponse> Recent);

    private sealed record UsersSection(
        UserAdminCountsResponse Counts,
        IReadOnlyList<UserAdminResponse> Recent);

    private async Task<Result<OrdersSection>> LoadOrdersSectionAsync(CancellationToken cancellationToken)
    {
        var counts = await _orderCountsHandler.HandleAsync(new GetAdminOrderCountsQuery(), cancellationToken).ConfigureAwait(false);
        if (counts.IsFailure)
            return Result.Failure<OrdersSection>(counts.Error);

        var recent = await _recentOrdersHandler.HandleAsync(
            new GetAdminOrdersQuery(1, RecentOrdersLimit, null, null, null, null, null),
            cancellationToken).ConfigureAwait(false);
        if (recent.IsFailure)
            return Result.Failure<OrdersSection>(recent.Error);

        return Result.Success(new OrdersSection(counts.Value!, recent.Value!.Items.ToList()));
    }

    private async Task<Result<PaymentsSection>> LoadPaymentsSectionAsync(bool includeRecent, CancellationToken cancellationToken)
    {
        var stats = await _paymentStatsHandler.HandleAsync(new GetAdminPaymentStatsQuery(), cancellationToken).ConfigureAwait(false);
        if (stats.IsFailure)
            return Result.Failure<PaymentsSection>(stats.Error);

        IReadOnlyList<PaymentAdminResponse> recent = [];
        if (includeRecent)
        {
            var recentResult = await _recentPaymentsHandler.HandleAsync(
                new GetAdminPaymentsQuery(1, RecentPaymentsLimit, null, null, null, null, null),
                cancellationToken).ConfigureAwait(false);
            if (recentResult.IsFailure)
                return Result.Failure<PaymentsSection>(recentResult.Error);

            recent = recentResult.Value!.Items.ToList();
        }

        return Result.Success(new PaymentsSection(stats.Value!, recent));
    }

    private async Task<Result<UsersSection>> LoadUsersSectionAsync(CancellationToken cancellationToken)
    {
        var counts = await _userCountsHandler.HandleAsync(new GetAdminUserCountsQuery(), cancellationToken).ConfigureAwait(false);
        if (counts.IsFailure)
            return Result.Failure<UsersSection>(counts.Error);

        var recent = await _recentUsersHandler.HandleAsync(
            new GetAdminUsersQuery(1, RecentUsersLimit, null, null, null),
            cancellationToken).ConfigureAwait(false);
        if (recent.IsFailure)
            return Result.Failure<UsersSection>(recent.Error);

        return Result.Success(new UsersSection(counts.Value!, recent.Value!.Items.ToList()));
    }

    private async Task<Result<IReadOnlyList<DailySeriesPointResponse>>> BuildDailySeriesAsync(
        int windowDays,
        DashboardEntitlements entitlements,
        CancellationToken cancellationToken)
    {
        var orderPointsTask = entitlements.Orders
            ? _orderPointsHandler.HandleAsync(new GetOrderDailyPointsQuery(windowDays), cancellationToken)
            : null;
        var paymentPointsTask = entitlements.Payments
            ? _paymentPointsHandler.HandleAsync(new GetPaymentDailyPointsQuery(windowDays), cancellationToken)
            : null;

        if (orderPointsTask is null && paymentPointsTask is null)
            return Result.Success<IReadOnlyList<DailySeriesPointResponse>>([]);

        await AwaitAllAsync(orderPointsTask, paymentPointsTask).ConfigureAwait(false);

        if (orderPointsTask?.Result.IsFailure == true)
            return Result.Failure<IReadOnlyList<DailySeriesPointResponse>>(orderPointsTask.Result.Error);
        if (paymentPointsTask?.Result.IsFailure == true)
            return Result.Failure<IReadOnlyList<DailySeriesPointResponse>>(paymentPointsTask.Result.Error);

        IReadOnlyList<DateTimeOffset> orderPoints = orderPointsTask is null
            ? []
            : orderPointsTask.Result.Value!.Select(point => point.CreatedAtUtc).ToList();

        IReadOnlyList<DailySeriesBuilder.PaymentRevenueInput> paymentPoints = paymentPointsTask is null
            ? []
            : paymentPointsTask.Result.Value!
                .Select(point => new DailySeriesBuilder.PaymentRevenueInput(
                    point.CreatedAtUtc,
                    point.Amount,
                    point.Currency,
                    point.Status == PaymentStatus.Succeeded))
                .ToList();

        return Result.Success(DailySeriesBuilder.Build(
            orderPoints,
            paymentPoints,
            windowDays,
            new DateTimeOffset(_dateTimeProvider.UtcNow, TimeSpan.Zero)));
    }

    private static Task AwaitAllAsync(params Task?[] tasks)
    {
        var pending = tasks.Where(static task => task is not null).Cast<Task>().ToArray();
        return pending.Length == 0 ? Task.CompletedTask : Task.WhenAll(pending);
    }
}
