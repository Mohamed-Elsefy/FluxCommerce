using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using FluxCommerce.Modules.Payments.Infrastructure.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace FluxCommerce.Modules.Payments.Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly PaymentsDbContext _dbContext;
    public PaymentRepository(PaymentsDbContext dbContext) => _dbContext = dbContext;

    public Task<Payment?> GetByOrderIdAsync(Guid orderId, CancellationToken cancellationToken = default)
        => _dbContext.Set<Payment>().FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    public Task<Payment?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken = default)
        => _dbContext.Set<Payment>().FirstOrDefaultAsync(p => p.Id == paymentId, cancellationToken);

    public Task<Payment?> GetByProviderIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken = default)
        => _dbContext.Set<Payment>().FirstOrDefaultAsync(p => p.ProviderPaymentIntentId == providerPaymentIntentId, cancellationToken);

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        await _dbContext.Set<Payment>().AddAsync(payment, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _dbContext.SaveChangesAsync(cancellationToken);
}
