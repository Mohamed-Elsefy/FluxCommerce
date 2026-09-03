using FluxCommerce.Modules.Payments.Domain.Aggregates;
using FluxCommerce.Modules.Payments.Domain.Enums;
using Xunit;

namespace FluxCommerce.UnitTests.Domain;

public class PaymentRefundTests
{
    [Fact]
    public void Refund_FromSucceeded_ToRefunded_IsAllowed()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference("pi_test_123", "Stripe");
        payment.Succeed("pi_test_123");
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);

        payment.Refund("re_test_123");
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal("re_test_123", payment.ProviderRefundId);
    }

    [Fact]
    public void Refund_OnAlreadyRefunded_IsIdempotent_ThrowsAlreadyRefunded()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", Guid.NewGuid());
        payment.UpdateProviderReference("pi_test", "Stripe");
        payment.Succeed("pi_test");
        payment.Refund("re_1");
        var ex = Assert.Throws<InvalidOperationException>(() => payment.Refund("re_2"));
        Assert.Contains("already been refunded", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal("re_1", payment.ProviderRefundId);
    }

    [Fact]
    public void Refund_FromInvalidState_Throws()
    {
        var payment = new Payment(Guid.NewGuid(), Guid.NewGuid(), 10m, "USD", Guid.NewGuid());
        var ex = Assert.Throws<InvalidOperationException>(() => payment.Refund());
        Assert.Contains("cannot transition", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }
}
