namespace FluxCommerce.BuildingBlocks.Idempotency.Messages;

public readonly record struct IdempotencyResult
{
    public bool IsDuplicate { get; }

    public DateTime? ProcessedAtUtc { get; }

    private IdempotencyResult(bool isDuplicate, DateTime? processedAtUtc)
    {
        IsDuplicate = isDuplicate;
        ProcessedAtUtc = processedAtUtc;
    }

    public static IdempotencyResult NotProcessed() => new(false, null);

    public static IdempotencyResult AlreadyProcessed(DateTime processedAtUtc) => new(true, processedAtUtc);
}
