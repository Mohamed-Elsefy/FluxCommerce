using FluxCommerce.Modules.Inventory.Application.Expiration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxCommerce.Modules.Inventory.Infrastructure.Background;

/// <summary>
/// Background processor that periodically runs the reservation expiration use case. Each cycle
/// resolves a fresh scope so the DbContext lifetime mirrors a per-request scope. A failed cycle is
/// logged and simply retried on the next tick, and repeated cycles are safe because the underlying
/// use case is idempotent — already-terminal reservations are never expired again.
/// </summary>
public sealed class ReservationExpirationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<ReservationExpirationOptions> _options;
    private readonly ILogger<ReservationExpirationBackgroundService> _logger;

    public ReservationExpirationBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ReservationExpirationOptions> options,
        ILogger<ReservationExpirationBackgroundService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await ExecuteCycleAsync(stoppingToken).ConfigureAwait(false);

                if (expired > 0)
                    _logger.LogInformation("Expired {ExpiredCount} reservation(s).", expired);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Transient failures must not stop the processor; the next cycle retries.
                _logger.LogError(exception, "Reservation expiration cycle failed and will be retried.");
            }

            try
            {
                await Task.Delay(_options.Value.Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs a single expiration cycle inside a fresh scope and returns the number of expired
    /// reservations. Exposed for tests; the hosted-service loop calls it on each tick.
    /// </summary>
    internal async Task<int> ExecuteCycleAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();

        var processor = scope.ServiceProvider
            .GetRequiredService<IExpiredReservationProcessor>();

        return await processor
            .ProcessExpiredReservationsAsync(_options.Value.BatchSize, cancellationToken)
            .ConfigureAwait(false);
    }
}
