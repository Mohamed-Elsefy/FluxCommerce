using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;

public sealed record RegisterAdminCommand(
    string Email,
    string Password,
    string FirstName,
    string LastName) : ICommand<Result<UserResponse>>;
