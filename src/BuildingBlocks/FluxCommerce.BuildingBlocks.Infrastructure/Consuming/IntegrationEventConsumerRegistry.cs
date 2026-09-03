using FluxCommerce.BuildingBlocks.Messaging.Consuming;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Phase 7.5's singleton consumer registry. It implements the Messaging resolution contract
/// (<see cref="IIntegrationEventConsumerRegistry.Resolve"/>) and additionally materializes every
/// registered consumer descriptor (via DI's <see cref="IntegrationEventConsumerDescriptorRegistration"/>
/// collection) so the consumer host can bind the queue to each handled event type's routing key.
/// Descriptors are snapshotted at construction: <c>AddIntegrationEventConsumer&lt;,&gt;</c> registers the
/// descriptor registrations before the container is built, so the registry resolves them all.
/// </summary>
public sealed class IntegrationEventConsumerRegistry : IIntegrationEventConsumerRegistry
{
    private readonly Dictionary<string, IntegrationEventConsumerDescriptor> _descriptors;

    public IntegrationEventConsumerRegistry(
        IEnumerable<IntegrationEventConsumerDescriptorRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        _descriptors = registrations.ToDictionary(
            registration => registration.Descriptor.EventType,
            registration => registration.Descriptor);
    }

    public IntegrationEventConsumerDescriptor? Resolve(string eventType) =>
        _descriptors.TryGetValue(eventType, out var descriptor) ? descriptor : null;

    /// <summary>All registered descriptors; the consumer host binds one routing key per descriptor.</summary>
    public IReadOnlyList<IntegrationEventConsumerDescriptor> GetDescriptors() => _descriptors.Values.ToList();
}