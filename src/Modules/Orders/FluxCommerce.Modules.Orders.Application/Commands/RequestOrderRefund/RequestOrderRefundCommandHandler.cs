using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Orders.Application.Enums;
using FluxCommerce.Modules.Orders.Application.Errors;
using FluxCommerce.Modules.Orders.Application.Interfaces;
using FluxCommerce.Modules.Orders.Application.Mapping;
using FluxCommerce.Modules.Orders.Contracts.Responses;
using FluxCommerce.Modules.Orders.Domain.ValueObjects;

namespace FluxCommerce.Modules.Orders.Application.Commands.RequestOrderRefund;

/// <summary>
/// Validates the refund reason and delegates to the saga-backed refund service. A fresh
/// request and an in-flight duplicate both succeed (returning the current Order details);
/// a missing Order is not-found and an ineligible state is a conflict.
/// </summary>
public sealed class RequestOrderRefundCommandHandler
    : ICommandHandler<RequestOrderRefundCommand, Result<OrderDetailsResponse>>
{
    public const int MaxReasonLength = 500;

    private readonly IOrderRefundService _refundService;

    public RequestOrderRefundCommandHandler(IOrderRefundService refundService)
    {
        _refundService = refundService ?? throw new ArgumentNullException(nameof(refundService));
    }

    public async Task<Result<OrderDetailsResponse>> HandleAsync(
        RequestOrderRefundCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length > MaxReasonLength)
            return Result.Failure<OrderDetailsResponse>(OrderQueryErrors.ValidationFailed);

        var result = await _refundService
            .RequestRefundAsync(new OrderId(command.OrderId), command.Reason, cancellationToken)
            .ConfigureAwait(false);

        return result.Outcome switch
        {
            RefundRequestOutcome.Applied => Result.Success(OrderResponseMapper.ToDetails(result.Order!)),
            RefundRequestOutcome.Duplicate => Result.Success(OrderResponseMapper.ToDetails(result.Order!)),
            RefundRequestOutcome.OrderNotFound => Result.Failure<OrderDetailsResponse>(OrderQueryErrors.NotFound),
            _ => Result.Failure<OrderDetailsResponse>(OrderQueryErrors.RefundConflict),
        };
    }
}
