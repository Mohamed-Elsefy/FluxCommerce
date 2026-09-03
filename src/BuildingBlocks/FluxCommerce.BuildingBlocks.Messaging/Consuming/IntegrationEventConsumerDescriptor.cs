namespace FluxCommerce.BuildingBlocks.Messaging.Consuming;

/// <summary>
/// Describes how a consumer dispatches a wire event type: the stable type identifier
/// (<see cref="EventType"/>) that arrives on the transport, the CLR contract to deserialize the payload
/// into (<see cref="EventContractType"/>), the CLR handler type registered in the container that owns
/// the business reaction (<see cref="HandlerType"/>), and the CLR <c>IIdempotentProcessor</c>
/// implementation owned by the same module as the handler (<see cref="ProcessorType"/>). The consumer
/// registry resolves <see cref="EventContractType"/> for deserialization (switch-on-Type, mirroring the
/// Phase 6 publisher), <see cref="HandlerType"/> for DI resolution, and <see cref="ProcessorType"/> for
/// the idempotent-processing boundary. The processor type is bound to the handler's module so the
/// message processor never falls back to a container-wide <c>IIdempotentProcessor</c> (which, when
/// several modules register one, is last-registration-wins and would execute the consumer against the
/// wrong module's database). The descriptor never exposes a transport or persistence type.
/// </summary>
public sealed record IntegrationEventConsumerDescriptor
{
    public required string EventType { get; init; }

    public required Type EventContractType { get; init; }

    public required Type HandlerType { get; init; }

    public required Type ProcessorType { get; init; }
}
