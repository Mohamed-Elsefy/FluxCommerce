using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;

public sealed record RegisterCustomerCommand(string Email, string Password) : ICommand<Result<UserResponse>>;
