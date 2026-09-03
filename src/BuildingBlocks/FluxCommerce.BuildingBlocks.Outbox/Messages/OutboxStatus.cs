namespace FluxCommerce.BuildingBlocks.Outbox.Messages;

public enum OutboxStatus
{
    Pending = 0,

    InProgress = 1,

    Published = 2,

    Failed = 3
}
