using Microsoft.Extensions.Options;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Default Phase 7.6 failure classifier: an exception whose type — or any of its inner exceptions —
/// is listed in <see cref="ConsumerRetryOptions.NonRetryableExceptionTypes"/> is classified
/// <see cref="ConsumerFailureClassification.Permanent"/> (dead-lettered immediately); everything else
/// is <see cref="ConsumerFailureClassification.Retryable"/>.
/// </summary>
public sealed class IntegrationEventFailureClassifier : IIntegrationEventFailureClassifier
{
    private readonly ConsumerRetryOptions _retryOptions;

    public IntegrationEventFailureClassifier(IOptions<RabbitMQConsumerOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _retryOptions = options.Value.Retry;
    }

    public ConsumerFailureClassification Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (_retryOptions.NonRetryableExceptionTypes.Contains(current.GetType()))
                return ConsumerFailureClassification.Permanent;
        }

        return ConsumerFailureClassification.Retryable;
    }
}
