using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Infrastructure.Webhook;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class StripeWebhookEndpointHandlerTests
{
    private static StripeWebhookEndpointHandler CreateHandler(
        IStripeWebhookService svc,
        IWebhookEventProcessor proc) =>
        new(svc, proc, NullLogger<StripeWebhookEndpointHandler>.Instance);

    [Fact]
    public async Task MissingSignature_ReturnsMissingSignature_WithoutVerifying()
    {
        var svcMock = new Mock<IStripeWebhookService>();
        var procMock = new Mock<IWebhookEventProcessor>();
        var handler = CreateHandler(svcMock.Object, procMock.Object);

        var result = await handler.HandleAsync("""{"id":"evt_1"}""", signatureHeader: null);

        Assert.Equal(WebhookEndpointResult.MissingSignature, result);
        svcMock.Verify(x => x.VerifyAndParse(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EmptyPayload_ReturnsEmptyPayload()
    {
        var svcMock = new Mock<IStripeWebhookService>();
        var procMock = new Mock<IWebhookEventProcessor>();
        var handler = CreateHandler(svcMock.Object, procMock.Object);

        var result = await handler.HandleAsync(string.Empty, "t=1,v1=x");

        Assert.Equal(WebhookEndpointResult.EmptyPayload, result);
        svcMock.Verify(x => x.VerifyAndParse(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InvalidSignature_ReturnsInvalidSignature()
    {
        var payload = """{"id":"evt_1"}""";
        var svcMock = new Mock<IStripeWebhookService>();
        svcMock.Setup(x => x.VerifyAndParse(payload, "t=1,v1=bad"))
            .Returns(new StripeWebhookVerificationResult(false, null, null, null, "Invalid signature"));
        var procMock = new Mock<IWebhookEventProcessor>();
        var handler = CreateHandler(svcMock.Object, procMock.Object);

        var result = await handler.HandleAsync(payload, "t=1,v1=bad");

        Assert.Equal(WebhookEndpointResult.InvalidSignature, result);
        procMock.Verify(x => x.TryProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ValidNewEvent_ReturnsProcessed()
    {
        var payload = """{"id":"evt_2","type":"payment_intent.succeeded"}""";
        var svcMock = new Mock<IStripeWebhookService>();
        svcMock.Setup(x => x.VerifyAndParse(payload, "t=1,v1=valid"))
            .Returns(new StripeWebhookVerificationResult(true, "evt_2", "payment_intent.succeeded", "pi_2", null));
        var procMock = new Mock<IWebhookEventProcessor>();
        procMock.Setup(x => x.TryProcessAsync("evt_2", "payment_intent.succeeded", "pi_2", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookProcessResult.Processed);
        var handler = CreateHandler(svcMock.Object, procMock.Object);

        var result = await handler.HandleAsync(payload, "t=1,v1=valid");

        Assert.Equal(WebhookEndpointResult.Processed, result);
    }

    [Fact]
    public async Task DuplicateEvent_ReturnsAlreadyProcessed()
    {
        var payload = """{"id":"evt_dup","type":"payment_intent.succeeded"}""";
        var svcMock = new Mock<IStripeWebhookService>();
        svcMock.Setup(x => x.VerifyAndParse(payload, "t=1,v1=valid"))
            .Returns(new StripeWebhookVerificationResult(true, "evt_dup", "payment_intent.succeeded", "pi_dup", null));
        var procMock = new Mock<IWebhookEventProcessor>();
        procMock.Setup(x => x.TryProcessAsync("evt_dup", "payment_intent.succeeded", "pi_dup", null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookProcessResult.AlreadyProcessed);
        var handler = CreateHandler(svcMock.Object, procMock.Object);

        var result = await handler.HandleAsync(payload, "t=1,v1=valid");

        Assert.Equal(WebhookEndpointResult.AlreadyProcessed, result);
    }
}
