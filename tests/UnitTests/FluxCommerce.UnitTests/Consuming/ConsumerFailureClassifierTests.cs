using FluxCommerce.BuildingBlocks.Infrastructure.Consuming;
using Microsoft.Extensions.Options;
using Xunit;

namespace FluxCommerce.UnitTests.Consuming;

/// <summary>
/// Phase 7.6: the failure classifier decides whether a processing failure is retryable (retried
/// in-process with backoff) or permanent (dead-lettered immediately). A failure is permanent when its
/// type — or any inner exception — is listed in the consumer's NonRetryableExceptionTypes; everything
/// else is retryable.
/// </summary>
public class ConsumerFailureClassifierTests
{
    [Fact]
    public void Classify_ExceptionMatchingConfiguredType_IsPermanent()
    {
        var classifier = CreateClassifier(typeof(InvalidOperationException));

        var classification = classifier.Classify(new InvalidOperationException("Permanent."));

        Assert.Equal(ConsumerFailureClassification.Permanent, classification);
    }

    [Fact]
    public void Classify_InnerExceptionMatchingConfiguredType_IsPermanent()
    {
        var classifier = CreateClassifier(typeof(ArgumentException));

        var classification = classifier.Classify(
            new InvalidOperationException("Outer.", new ArgumentException("Inner.")));

        Assert.Equal(ConsumerFailureClassification.Permanent, classification);
    }

    [Fact]
    public void Classify_UnlistedException_IsRetryable()
    {
        var classifier = CreateClassifier(typeof(InvalidOperationException));

        var classification = classifier.Classify(new ApplicationException("Transient."));

        Assert.Equal(ConsumerFailureClassification.Retryable, classification);
    }

    [Fact]
    public void Classify_NoConfiguredTypes_EverythingIsRetryable()
    {
        var classifier = CreateClassifier();

        Assert.Equal(
            ConsumerFailureClassification.Retryable,
            classifier.Classify(new InvalidOperationException("Anything.")));
    }

    [Fact]
    public void Classify_ConfiguredBaseType_DoesNotMatchDerivedException()
    {
        var classifier = CreateClassifier(typeof(Exception));

        Assert.Equal(
            ConsumerFailureClassification.Retryable,
            classifier.Classify(new InvalidOperationException("Derived.")));
    }

    private static IntegrationEventFailureClassifier CreateClassifier(params Type[] nonRetryableTypes)
    {
        var options = new RabbitMQConsumerOptions
        {
            Retry = new ConsumerRetryOptions
            {
                NonRetryableExceptionTypes = [.. nonRetryableTypes]
            }
        };

        return new IntegrationEventFailureClassifier(Options.Create(options));
    }
}
