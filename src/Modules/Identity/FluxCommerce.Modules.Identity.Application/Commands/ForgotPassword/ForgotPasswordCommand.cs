using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;

namespace FluxCommerce.Modules.Identity.Application.Commands.ForgotPassword;

public sealed record ForgotPasswordCommand(string Email) : ICommand<Result>;
