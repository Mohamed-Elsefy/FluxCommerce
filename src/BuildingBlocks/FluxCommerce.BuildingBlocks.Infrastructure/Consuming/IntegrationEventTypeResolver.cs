using System.Reflection;
using FluxCommerce.BuildingBlocks.Messaging.Events;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Consuming;

/// <summary>
/// Resolves the stable wire <c>EventType</c> of an integration event contract from its declared
/// <c>public const string EventType</c> member (e.g. <c>OrderCreatedIntegrationEvent.EventType</c>).
/// The const is the single source of truth shared with the publisher's routing key and the Outbox
/// <c>Type</c> column; <c>AddIntegrationEventConsumer&lt;,&gt;</c> uses this to register the descriptor
/// without a redundant string argument.
/// </summary>
internal static class IntegrationEventTypeResolver
{
    private const BindingFlags EventTypeBindingFlags = BindingFlags.Public | BindingFlags.Static;

    public static string Get(Type eventContractType)
    {
        ArgumentNullException.ThrowIfNull(eventContractType);

        var field = eventContractType.GetField("EventType", EventTypeBindingFlags);
        var value = field?.GetValue(null) as string;

        if (field is null || field.FieldType != typeof(string) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"Integration event '{eventContractType.Name}' must declare a public const string 'EventType' identifying its stable wire type.",
                nameof(eventContractType));
        }

        return value!;
    }
}