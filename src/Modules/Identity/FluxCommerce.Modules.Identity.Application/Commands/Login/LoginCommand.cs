using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : ICommand<Result<LoginResponse>>;
