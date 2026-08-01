using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.ResendVerification;

public sealed record ResendVerificationCommand(string Email) : ICommand<Result<ResendVerificationResponse>>;
