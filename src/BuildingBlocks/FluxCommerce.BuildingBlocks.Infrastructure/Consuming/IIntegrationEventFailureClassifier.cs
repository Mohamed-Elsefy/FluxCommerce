namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Classifies a processing failure as <see cref="ConsumerFailureClassification.Retryable"/> or
/// <see cref="ConsumerFailureClassification.Permanent"/> for the Phase 7.6 consumer retry policy.
/// </summary>
public interface IIntegrationEventFailureClassifier
{
    ConsumerFailureClassification Classify(Exception exception);
}
