namespace FluxCommerce.BuildingBlocks.Messaging.Consuming;

/// <summary>
/// Describes how a consumer dispatches a wire event type: the stable type identifier
/// (<see cref="EventType"/>) that arrives on the transport, the CLR contract to deserialize the payload
/// into (<see cref="EventContractType"/>), and the CLR handler type registered in the container that
/// owns the business reaction (<see cref="HandlerType"/>). The consumer registry resolves
/// <see cref="EventContractType"/> for deserialization (switch-on-Type, mirroring the Phase 6 publisher)
/// and <see cref="HandlerType"/> for DI resolution. The descriptor never exposes a transport or
/// persistence type.
/// </summary>
public sealed record IntegrationEventConsumerDescriptor
{
    public required string EventType { get; init; }

    public required Type EventContractType { get; init; }

    public required Type HandlerType { get; init; }
}
