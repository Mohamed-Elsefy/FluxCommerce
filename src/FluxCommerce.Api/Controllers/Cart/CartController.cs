using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Cart.Application.Commands.AddAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.AddGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.ClearAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.ClearGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Commands.CreateGuestCart;
using FluxCommerce.Modules.Cart.Application.Commands.MergeCart;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveAuthenticatedCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.RemoveGuestCartItem;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateAuthenticatedCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Commands.UpdateGuestCartItemQuantity;
using FluxCommerce.Modules.Cart.Application.Models;
using FluxCommerce.Modules.Cart.Application.Queries.GetAuthenticatedCart;
using FluxCommerce.Modules.Cart.Application.Queries.GetGuestCart;
using FluxCommerce.Modules.Cart.Contracts.Requests;
using FluxCommerce.Modules.Cart.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Cart;

[ApiController]
[Route("cart")]
[Tags(EndpointTags.Cart)]
[Authorize]
public sealed class CartController : ApiControllerBase
{
    private readonly ICommandHandler<CreateGuestCartCommand, Result<CartState>> _createGuestCartHandler;
    private readonly IQueryHandler<GetGuestCartQuery, Result<CartState>> _getGuestCartHandler;
    private readonly ICommandHandler<AddGuestCartItemCommand, Result<CartState>> _addGuestCartItemHandler;
    private readonly ICommandHandler<UpdateGuestCartItemQuantityCommand, Result<CartState>> _updateGuestCartItemQuantityHandler;
    private readonly ICommandHandler<RemoveGuestCartItemCommand, Result<CartState>> _removeGuestCartItemHandler;
    private readonly ICommandHandler<ClearGuestCartCommand, Result<CartState>> _clearGuestCartHandler;

    private readonly ICommandHandler<CreateAuthenticatedCartCommand, Result<CartState>> _createAuthenticatedCartHandler;
    private readonly IQueryHandler<GetAuthenticatedCartQuery, Result<CartState>> _getAuthenticatedCartHandler;
    private readonly ICommandHandler<AddAuthenticatedCartItemCommand, Result<CartState>> _addAuthenticatedCartItemHandler;
    private readonly ICommandHandler<UpdateAuthenticatedCartItemQuantityCommand, Result<CartState>> _updateAuthenticatedCartItemQuantityHandler;
    private readonly ICommandHandler<RemoveAuthenticatedCartItemCommand, Result<CartState>> _removeAuthenticatedCartItemHandler;
    private readonly ICommandHandler<ClearAuthenticatedCartCommand, Result<CartState>> _clearAuthenticatedCartHandler;
    private readonly ICommandHandler<MergeCartCommand, Result<CartState>> _mergeCartHandler;

    public CartController(
        ICommandHandler<CreateGuestCartCommand, Result<CartState>> createGuestCartHandler,
        IQueryHandler<GetGuestCartQuery, Result<CartState>> getGuestCartHandler,
        ICommandHandler<AddGuestCartItemCommand, Result<CartState>> addGuestCartItemHandler,
        ICommandHandler<UpdateGuestCartItemQuantityCommand, Result<CartState>> updateGuestCartItemQuantityHandler,
        ICommandHandler<RemoveGuestCartItemCommand, Result<CartState>> removeGuestCartItemHandler,
        ICommandHandler<ClearGuestCartCommand, Result<CartState>> clearGuestCartHandler,
        ICommandHandler<CreateAuthenticatedCartCommand, Result<CartState>> createAuthenticatedCartHandler,
        IQueryHandler<GetAuthenticatedCartQuery, Result<CartState>> getAuthenticatedCartHandler,
        ICommandHandler<AddAuthenticatedCartItemCommand, Result<CartState>> addAuthenticatedCartItemHandler,
        ICommandHandler<UpdateAuthenticatedCartItemQuantityCommand, Result<CartState>> updateAuthenticatedCartItemQuantityHandler,
        ICommandHandler<RemoveAuthenticatedCartItemCommand, Result<CartState>> removeAuthenticatedCartItemHandler,
        ICommandHandler<ClearAuthenticatedCartCommand, Result<CartState>> clearAuthenticatedCartHandler,
        ICommandHandler<MergeCartCommand, Result<CartState>> mergeCartHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _createGuestCartHandler = createGuestCartHandler;
        _getGuestCartHandler = getGuestCartHandler;
        _addGuestCartItemHandler = addGuestCartItemHandler;
        _updateGuestCartItemQuantityHandler = updateGuestCartItemQuantityHandler;
        _removeGuestCartItemHandler = removeGuestCartItemHandler;
        _clearGuestCartHandler = clearGuestCartHandler;
        _createAuthenticatedCartHandler = createAuthenticatedCartHandler;
        _getAuthenticatedCartHandler = getAuthenticatedCartHandler;
        _addAuthenticatedCartItemHandler = addAuthenticatedCartItemHandler;
        _updateAuthenticatedCartItemQuantityHandler = updateAuthenticatedCartItemQuantityHandler;
        _removeAuthenticatedCartItemHandler = removeAuthenticatedCartItemHandler;
        _clearAuthenticatedCartHandler = clearAuthenticatedCartHandler;
        _mergeCartHandler = mergeCartHandler;
    }

    #region Guest Cart Endpoints

    /// <summary>Creates a new empty Guest Cart.</summary>
    [HttpPost("guest")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateGuestCart([FromBody] CreateGuestCartRequest request, CancellationToken cancellationToken)
    {
        var command = new CreateGuestCartCommand();
        var result = await _createGuestCartHandler.HandleAsync(command, cancellationToken);
        return Created(MapToResponse(result), location: $"/cart/guest/{result.Value?.CartId.Value}");
    }

    /// <summary>Retrieves a Guest Cart by its opaque identifier.</summary>
    [HttpGet("guest/{cartId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetGuestCart(Guid cartId, CancellationToken cancellationToken)
    {
        var query = new GetGuestCartQuery(cartId);
        var result = await _getGuestCartHandler.HandleAsync(query, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Adds an item to a Guest Cart.</summary>
    [HttpPost("guest/{cartId:guid}/items")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddGuestCartItem(Guid cartId, [FromBody] AddGuestCartItemRequest request, CancellationToken cancellationToken)
    {
        var command = new AddGuestCartItemCommand(
            cartId,
            request.VariantId,
            request.UnitPriceAmount,
            request.Currency,
            request.Quantity);
        var result = await _addGuestCartItemHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Updates an item quantity in a Guest Cart.</summary>
    [HttpPut("guest/{cartId:guid}/items/{variantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateGuestCartItemQuantity(Guid cartId, Guid variantId, [FromBody] UpdateGuestCartItemQuantityRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateGuestCartItemQuantityCommand(cartId, variantId, request.Quantity);
        var result = await _updateGuestCartItemQuantityHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Removes an item from a Guest Cart.</summary>
    [HttpDelete("guest/{cartId:guid}/items/{variantId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveGuestCartItem(Guid cartId, Guid variantId, CancellationToken cancellationToken)
    {
        var command = new RemoveGuestCartItemCommand(cartId, variantId);
        var result = await _removeGuestCartItemHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Clears all items from a Guest Cart.</summary>
    [HttpDelete("guest/{cartId:guid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ClearGuestCart(Guid cartId, CancellationToken cancellationToken)
    {
        var command = new ClearGuestCartCommand(cartId);
        var result = await _clearGuestCartHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    #endregion

    #region Authenticated Cart Endpoints

    /// <summary>Creates the authenticated user's active Cart (or returns the existing one).</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateAuthenticatedCart(CancellationToken cancellationToken)
    {
        var command = new CreateAuthenticatedCartCommand();
        var result = await _createAuthenticatedCartHandler.HandleAsync(command, cancellationToken);
        return Created(MapToResponse(result), location: $"/cart");
    }

    /// <summary>Retrieves the current authenticated user's active Cart.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuthenticatedCart(CancellationToken cancellationToken)
    {
        var query = new GetAuthenticatedCartQuery();
        var result = await _getAuthenticatedCartHandler.HandleAsync(query, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Adds an item to the authenticated user's Cart.</summary>
    [HttpPost("items")]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddAuthenticatedCartItem([FromBody] AddAuthenticatedCartItemRequest request, CancellationToken cancellationToken)
    {
        var command = new AddAuthenticatedCartItemCommand(
            request.VariantId,
            request.UnitPriceAmount,
            request.Currency,
            request.Quantity);
        var result = await _addAuthenticatedCartItemHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Updates an item quantity in the authenticated user's Cart.</summary>
    [HttpPut("items/{variantId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateAuthenticatedCartItemQuantity(Guid variantId, [FromBody] UpdateAuthenticatedCartItemQuantityRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateAuthenticatedCartItemQuantityCommand(variantId, request.Quantity);
        var result = await _updateAuthenticatedCartItemQuantityHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Removes an item from the authenticated user's Cart.</summary>
    [HttpDelete("items/{variantId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveAuthenticatedCartItem(Guid variantId, CancellationToken cancellationToken)
    {
        var command = new RemoveAuthenticatedCartItemCommand(variantId);
        var result = await _removeAuthenticatedCartItemHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Clears all items from the authenticated user's Cart.</summary>
    [HttpDelete]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClearAuthenticatedCart(CancellationToken cancellationToken)
    {
        var command = new ClearAuthenticatedCartCommand();
        var result = await _clearAuthenticatedCartHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    /// <summary>Merges a Guest Cart into the authenticated user's Cart.</summary>
    [HttpPost("merge")]
    [ProducesResponseType(typeof(ApiResponse<CartResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MergeCart([FromBody] MergeCartRequest request, CancellationToken cancellationToken)
    {
        var command = new MergeCartCommand(request.GuestCartId);
        var result = await _mergeCartHandler.HandleAsync(command, cancellationToken);
        return Success(MapToResponse(result));
    }

    #endregion

    private static Result<CartResponse> MapToResponse(Result<CartState> result)
    {
        if (result.IsFailure)
            return Result<CartResponse>.Failure(result.Error);

        var state = result.Value!;
        var items = state.Items.Select(i => new CartItemResponse(
            i.ProductId.Value,
            i.VariantId.Value,
            i.Sku.Value,
            i.ProductName,
            i.UnitPrice.Amount,
            i.UnitPrice.Currency,
            i.Quantity)).ToList();

        var response = new CartResponse(
            state.CartId.Value,
            state.UserId,
            state.Version,
            state.CreatedAtUtc,
            state.UpdatedAtUtc,
            items,
            state.UserId is null,
            state.UserId is not null);

        return Result<CartResponse>.Success(response);
    }
}
