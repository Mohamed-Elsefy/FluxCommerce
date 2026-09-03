using FluxCommerce.Api.Controllers.Payments;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Models;
using FluxCommerce.Modules.Payments.Domain.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Text;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class PaymentsControllerWebhookTests
{
    private static PaymentsController CreateController(IStripeWebhookService svc, IWebhookEventProcessor? processorMock = null, DefaultHttpContext? ctx = null)
    {
        var localizerMock = new Mock<ILocalizer>();
        localizerMock.Setup(x => x[It.IsAny<string>()]).Returns((string k) => k);
        var errorLocalizerMock = new Mock<IErrorCodeLocalizer>();
        errorLocalizerMock.Setup(x => x.Localize(It.IsAny<string>(), It.IsAny<string>())).Returns((string c, string f) => f);

        IWebhookEventProcessor proc;
        if (processorMock != null) proc = processorMock;
        else
        {
            var m = new Mock<IWebhookEventProcessor>();
            m.Setup(x => x.TryProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(WebhookProcessResult.Processed);
            m.Setup(x => x.TryProcessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(WebhookProcessResult.Processed);
            proc = m.Object;
        }

        var repoMock = new Mock<FluxCommerce.Modules.Payments.Domain.Repositories.IPaymentRepository>();
        var userMock = new Mock<FluxCommerce.BuildingBlocks.Application.Abstractions.ICurrentUser>();
        userMock.Setup(x => x.UserId).Returns(Guid.NewGuid());

        var ctrl = new PaymentsController(
            localizerMock.Object,
            errorLocalizerMock.Object,
            NullLogger<PaymentsController>.Instance,
            svc,
            proc,
            repoMock.Object,
            userMock.Object);

        var http = ctx ?? new DefaultHttpContext();
        http.Request.Headers["Stripe-Signature"] = http.Request.Headers["Stripe-Signature"];
        ctrl.ControllerContext = new ControllerContext { HttpContext = http };
        return ctrl;
    }

    private static DefaultHttpContext HttpWithBody(string payload, string? sigHeader)
    {
        var ctx = new DefaultHttpContext();
        var bytes = Encoding.UTF8.GetBytes(payload);
        ctx.Request.Body = new MemoryStream(bytes);
        ctx.Request.ContentLength = bytes.Length;
        ctx.Request.ContentType = "application/json";
        if (sigHeader != null)
            ctx.Request.Headers["Stripe-Signature"] = sigHeader;
        return ctx;
    }

    [Fact]
    public async Task Webhook_ValidSignature_Returns200Ok()
    {
        var payload = """{"id":"evt_1","type":"payment_intent.succeeded"}""";
        var mock = new Mock<IStripeWebhookService>();
        mock.Setup(x => x.VerifyAndParse(payload, "t=1,v1=valid"))
            .Returns(new StripeWebhookVerificationResult(true, "evt_1", "payment_intent.succeeded", "pi_123", null));

        var ctx = HttpWithBody(payload, "t=1,v1=valid");
        var ctrl = CreateController(mock.Object, ctx: ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Webhook_MissingSignature_Returns400()
    {
        var payload = """{"id":"evt_1"}""";
        var mock = new Mock<IStripeWebhookService>();
        var ctx = HttpWithBody(payload, sigHeader: null);
        var ctrl = CreateController(mock.Object, ctx: ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<BadRequestResult>(result);
        mock.Verify(x => x.VerifyAndParse(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Returns400()
    {
        var payload = """{"id":"evt_1"}""";
        var mock = new Mock<IStripeWebhookService>();
        mock.Setup(x => x.VerifyAndParse(payload, "t=1,v1=invalid"))
            .Returns(new StripeWebhookVerificationResult(false, null, null, null, "Invalid signature"));

        var ctx = HttpWithBody(payload, "t=1,v1=invalid");
        var ctrl = CreateController(mock.Object, ctx: ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task Webhook_DuplicateEvent_Returns200Ok_Benign()
    {
        var payload = """{"id":"evt_dup","type":"payment_intent.succeeded"}""";
        var svcMock = new Mock<IStripeWebhookService>();
        svcMock.Setup(x => x.VerifyAndParse(payload, "t=1,v1=valid"))
            .Returns(new StripeWebhookVerificationResult(true, "evt_dup", "payment_intent.succeeded", "pi_dup", null));

        var procMock = new Mock<IWebhookEventProcessor>();
        procMock.Setup(x => x.TryProcessAsync("evt_dup", "payment_intent.succeeded", "pi_dup", It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookProcessResult.AlreadyProcessed);

        var ctx = HttpWithBody(payload, "t=1,v1=valid");
        var ctrl = CreateController(svcMock.Object, procMock.Object, ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<OkResult>(result);
        procMock.Verify(x => x.TryProcessAsync("evt_dup", "payment_intent.succeeded", "pi_dup", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_ValidSignature_CallsIdempotentProcessor()
    {
        var payload = """{"id":"evt_2","type":"payment_intent.succeeded"}""";
        var svcMock = new Mock<IStripeWebhookService>();
        svcMock.Setup(x => x.VerifyAndParse(payload, "t=1,v1=valid"))
            .Returns(new StripeWebhookVerificationResult(true, "evt_2", "payment_intent.succeeded", "pi_2", null));

        var procMock = new Mock<IWebhookEventProcessor>();
        procMock.Setup(x => x.TryProcessAsync("evt_2", "payment_intent.succeeded", "pi_2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(WebhookProcessResult.Processed);

        var ctx = HttpWithBody(payload, "t=1,v1=valid");
        var ctrl = CreateController(svcMock.Object, procMock.Object, ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<OkResult>(result);
        procMock.Verify(x => x.TryProcessAsync("evt_2", "payment_intent.succeeded", "pi_2", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Webhook_AllowAnonymous_Present()
    {
        var method = typeof(PaymentsController).GetMethod(nameof(PaymentsController.Webhook))!;
        var attr = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false);
        Assert.NotEmpty(attr);
    }
}

