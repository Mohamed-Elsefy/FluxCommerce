using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.ReadModels;
using FluxCommerce.Modules.Payments.Application.Validation;

namespace FluxCommerce.Modules.Payments.Application.Queries.GetPaymentDailyPoints;

/// <summary>
/// Serves the Payments daily-series projection from a lightweight filtered select. The trailing
/// window is anchored on the shared clock so the host buckets the same days it queried.
/// </summary>
public sealed class GetPaymentDailyPointsQueryHandler
    : IQueryHandler<GetPaymentDailyPointsQuery, Result<IReadOnlyList<PaymentDailyPointReadModel>>>
{
    private readonly IPaymentAdminStore _adminStore;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetPaymentDailyPointsQueryHandler(IPaymentAdminStore adminStore, IDateTimeProvider dateTimeProvider)
    {
        _adminStore = adminStore ?? throw new ArgumentNullException(nameof(adminStore));
        _dateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
    }

    public async Task<Result<IReadOnlyList<PaymentDailyPointReadModel>>> HandleAsync(
        GetPaymentDailyPointsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Days < 1)
            return Result.Failure<IReadOnlyList<PaymentDailyPointReadModel>>(PaymentAdminErrors.ValidationFailed);

        var from = new DateTimeOffset(_dateTimeProvider.UtcNow, TimeSpan.Zero).AddDays(-query.Days);

        var points = await _adminStore
            .GetDailyPointsAsync(from, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(points);
    }
}
