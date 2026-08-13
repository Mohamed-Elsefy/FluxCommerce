using FluxCommerce.Modules.Inventory.Application.Expiration;
using FluxCommerce.Modules.Inventory.Infrastructure.Background;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Infrastructure;

public class ReservationExpirationBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteCycleAsync_ResolvesProcessorFromScope_AndPassesBatchSize()
    {
        var processor = new RecordingProcessor();
        using var provider = BuildProvider(processor, interval: TimeSpan.FromSeconds(30), batchSize: 7);

        var service = CreateService(provider);
        await service.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(7, processor.LastBatchSize);
        Assert.Equal(1, processor.CallCount);
    }

    [Fact]
    public async Task ExecuteCycleAsync_ReturnsProcessorResult()
    {
        var processor = new RecordingProcessor(() => 4);
        using var provider = BuildProvider(processor, interval: TimeSpan.FromSeconds(30), batchSize: 5);

        var service = CreateService(provider);
        var expired = await service.ExecuteCycleAsync(CancellationToken.None);

        Assert.Equal(4, expired);
    }

    [Fact]
    public async Task ExecuteAsync_RunsProcessorUntilStopped()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var processor = new RecordingProcessor(() =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            return 0;
        });

        using var provider = BuildProvider(processor, interval: TimeSpan.FromMilliseconds(5), batchSize: 5);
        var service = CreateService(provider);

        await service.StartAsync(CancellationToken.None);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(calls >= 1, $"Expected at least one cycle, ran {calls}.");
    }

    [Fact]
    public async Task ExecuteAsync_FailedCycle_IsRetriedOnNextTick()
    {
        var secondCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var processor = new RecordingProcessor(() =>
        {
            var current = Interlocked.Increment(ref calls);
            if (current == 1)
                throw new InvalidOperationException("Simulated transient failure.");
            secondCall.TrySetResult();
            return 1;
        });

        using var provider = BuildProvider(processor, interval: TimeSpan.FromMilliseconds(5), batchSize: 5);
        var service = CreateService(provider);

        await service.StartAsync(CancellationToken.None);

        await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.True(calls >= 2, $"Expected a retry after the failure, ran {calls}.");
    }

    private static ReservationExpirationBackgroundService CreateService(ServiceProvider provider) =>
        new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IOptions<ReservationExpirationOptions>>(),
            NullLogger<ReservationExpirationBackgroundService>.Instance);

    private static ServiceProvider BuildProvider(
        RecordingProcessor processor,
        TimeSpan interval,
        int batchSize)
    {
        var services = new ServiceCollection();

        services.AddScoped<IExpiredReservationProcessor>(_ => processor);
        services.AddOptions<ReservationExpirationOptions>()
            .Configure(options =>
            {
                options.Interval = interval;
                options.BatchSize = batchSize;
            });

        return services.BuildServiceProvider();
    }

    private sealed class RecordingProcessor : IExpiredReservationProcessor
    {
        private readonly Func<int>? _behavior;

        public RecordingProcessor(Func<int>? behavior = null)
        {
            _behavior = behavior;
        }

        public int CallCount { get; private set; }

        public int? LastBatchSize { get; private set; }

        public Task<int> ProcessExpiredReservationsAsync(int maxResults, CancellationToken cancellationToken)
        {
            CallCount++;
            LastBatchSize = maxResults;
            return Task.FromResult(_behavior?.Invoke() ?? 0);
        }
    }
}
