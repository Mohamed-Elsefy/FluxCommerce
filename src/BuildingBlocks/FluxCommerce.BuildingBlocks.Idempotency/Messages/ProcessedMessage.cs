namespace FluxCommerce.BuildingBlocks.Idempotency.Messages;

public sealed record ProcessedMessage
{
    public string Key { get; init; } = string.Empty;

    public string? Handler { get; init; }

    public DateTime ProcessedAtUtc { get; init; }

    public static ProcessedMessage Create(Guid messageId, string? handler = null, DateTime? processedAtUtc = null) => new()
    {
        Key = messageId.ToString(),
        Handler = handler,
        ProcessedAtUtc = processedAtUtc ?? DateTime.UtcNow
    };

    public static ProcessedMessage Create(string key, string? handler = null, DateTime? processedAtUtc = null) => new()
    {
        Key = key,
        Handler = handler,
        ProcessedAtUtc = processedAtUtc ?? DateTime.UtcNow
    };
}
