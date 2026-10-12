using FluxCommerce.Api.Controllers.Payments;
using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.Modules.Payments.Application.Enums;
using FluxCommerce.Modules.Payments.Application.Interfaces;
using FluxCommerce.Modules.Payments.Application.Queries.GetMyPayments;
using FluxCommerce.Modules.Payments.Application.Queries.GetPayment;
using FluxCommerce.Modules.Payments.Application.Queries.GetPaymentInitialization;
using FluxCommerce.Modules.Payments.Contracts.Responses;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System.Text;
using Xunit;

namespace FluxCommerce.UnitTests.Payments;

public sealed class PaymentsControllerWebhookTests
{
    private static PaymentsController CreateController(WebhookEndpointResult outcome, DefaultHttpContext? ctx = null)
    {
        var localizerMock = new Mock<ILocalizer>();
        localizerMock.Setup(x => x[It.IsAny<string>()]).Returns((string k) => k);
        var errorLocalizerMock = new Mock<IErrorCodeLocalizer>();
        errorLocalizerMock.Setup(x => x.Localize(It.IsAny<string>(), It.IsAny<string>())).Returns((string c, string f) => f);

        var endpointMock = new Mock<IWebhookEndpointHandler>();
        endpointMock.Setup(x => x.HandleAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(outcome);

        var ctrl = new PaymentsController(
            localizerMock.Object,
            errorLocalizerMock.Object,
            endpointMock.Object,
            new Mock<IQueryHandler<GetPaymentQuery, Result<PaymentAdminResponse>>>().Object,
            new Mock<IQueryHandler<GetPaymentInitializationQuery, Result<PaymentInitializationResponse>>>().Object,
            new Mock<IQueryHandler<GetMyPaymentsQuery, Result<PagedResult<PaymentAdminResponse>>>>().Object);

        var http = ctx ?? new DefaultHttpContext();
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
    public async Task Webhook_Processed_Returns200Ok()
    {
        var payload = """{"id":"evt_1","type":"payment_intent.succeeded"}""";
        var ctx = HttpWithBody(payload, "t=1,v1=valid");
        var ctrl = CreateController(WebhookEndpointResult.Processed, ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Webhook_MissingSignature_Returns400()
    {
        var payload = """{"id":"evt_1"}""";
        var ctx = HttpWithBody(payload, sigHeader: null);
        var ctrl = CreateController(WebhookEndpointResult.MissingSignature, ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task Webhook_InvalidSignature_Returns400()
    {
        var payload = """{"id":"evt_1"}""";
        var ctx = HttpWithBody(payload, "t=1,v1=invalid");
        var ctrl = CreateController(WebhookEndpointResult.InvalidSignature, ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task Webhook_DuplicateEvent_Returns200Ok_Benign()
    {
        var payload = """{"id":"evt_dup","type":"payment_intent.succeeded"}""";
        var ctx = HttpWithBody(payload, "t=1,v1=valid");
        var ctrl = CreateController(WebhookEndpointResult.AlreadyProcessed, ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Webhook_EmptyPayload_Returns400()
    {
        var ctx = HttpWithBody(string.Empty, "t=1,v1=valid");
        var ctrl = CreateController(WebhookEndpointResult.EmptyPayload, ctx);

        var result = await ctrl.Webhook();

        Assert.IsType<BadRequestResult>(result);
    }

    [Fact]
    public async Task Webhook_AllowAnonymous_Present()
    {
        var method = typeof(PaymentsController).GetMethod(nameof(PaymentsController.Webhook))!;
        var attr = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute), false);
        Assert.NotEmpty(attr);
    }
}

