namespace FluxCommerce.BuildingBlocks.Resilience.Contexts;

public interface IExecutionContext
{
    Guid? CorrelationId { get; }

    Guid? CausationId { get; }

    Guid? UserId { get; }
}
