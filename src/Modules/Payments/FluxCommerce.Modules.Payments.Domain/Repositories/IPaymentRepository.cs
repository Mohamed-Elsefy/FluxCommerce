using FluxCommerce.Modules.Payments.Domain.Aggregates;

namespace FluxCommerce.Modules.Payments.Domain.Repositories;

public interface IPaymentRepository
{
    Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<Payment?> GetByProviderIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken = default);
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
