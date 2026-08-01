using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Identity.Application.Commands.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand<Result>;
