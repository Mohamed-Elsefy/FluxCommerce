namespace FluxCommerce.Modules.Orders.Domain.Errors;

public static class OrderSagaErrors
{
    public static readonly DomainError InvalidStateTransition = DomainError.Create(
        "OrderSaga.InvalidStateTransition",
        "The saga cannot transition to the requested state from its current state.");
}