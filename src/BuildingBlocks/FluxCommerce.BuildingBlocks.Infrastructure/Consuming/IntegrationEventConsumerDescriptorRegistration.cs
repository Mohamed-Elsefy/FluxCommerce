using FluxCommerce.BuildingBlocks.Messaging.Consuming;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// DI data registration produced by <c>AddIntegrationEventConsumer&lt;,&gt;</c> for each consumer
/// mapping. The singleton <see cref="IntegrationEventConsumerRegistry"/> resolves the full collection of
/// these registrations at construction time (they are all registered as container-owned instances before
/// the container is built), so every declared handler contributes its descriptor without a custom
/// configuration step.
/// </summary>
public sealed class IntegrationEventConsumerDescriptorRegistration
{
    public IntegrationEventConsumerDescriptorRegistration(IntegrationEventConsumerDescriptor descriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    public IntegrationEventConsumerDescriptor Descriptor { get; }
}