using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;
using FluxCommerce.Modules.Identity.Application.Commands.ForgotPassword;
using FluxCommerce.Modules.Identity.Application.Commands.GetCurrentUser;
using FluxCommerce.Modules.Identity.Application.Commands.Login;
using FluxCommerce.Modules.Identity.Application.Commands.Logout;
using FluxCommerce.Modules.Identity.Application.Commands.Refresh;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;
using FluxCommerce.Modules.Identity.Application.Commands.ResendVerification;
using FluxCommerce.Modules.Identity.Application.Commands.ResetPassword;
using FluxCommerce.Modules.Identity.Application.Commands.VerifyEmail;
using FluxCommerce.Modules.Identity.Contracts.Requests;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers;

[ApiController]
[Route("auth")]
[Authorize]
public sealed class AuthController : ApiControllerBase
{
    private readonly ICommandHandler<RegisterCustomerCommand, Result<UserResponse>> _registerCustomerHandler;
    private readonly ICommandHandler<LoginCommand, Result<LoginResponse>> _loginHandler;
    private readonly ICommandHandler<RefreshCommand, Result<LoginResponse>> _refreshHandler;
    private readonly ICommandHandler<LogoutCommand, Result> _logoutHandler;
    private readonly ICommandHandler<VerifyEmailCommand, Result<UserResponse>> _verifyEmailHandler;
    private readonly ICommandHandler<ResendVerificationCommand, Result<ResendVerificationResponse>> _resendVerificationHandler;
    private readonly ICommandHandler<ForgotPasswordCommand, Result> _forgotPasswordHandler;
    private readonly ICommandHandler<ResetPasswordCommand, Result> _resetPasswordHandler;
    private readonly ICommandHandler<ChangePasswordCommand, Result> _changePasswordHandler;
    private readonly ICommandHandler<GetCurrentUserCommand, Result<UserResponse>> _getCurrentUserHandler;

    public AuthController(
        ICommandHandler<RegisterCustomerCommand, Result<UserResponse>> registerCustomerHandler,
        ICommandHandler<LoginCommand, Result<LoginResponse>> loginHandler,
        ICommandHandler<RefreshCommand, Result<LoginResponse>> refreshHandler,
        ICommandHandler<LogoutCommand, Result> logoutHandler,
        ICommandHandler<VerifyEmailCommand, Result<UserResponse>> verifyEmailHandler,
        ICommandHandler<ResendVerificationCommand, Result<ResendVerificationResponse>> resendVerificationHandler,
        ICommandHandler<ForgotPasswordCommand, Result> forgotPasswordHandler,
        ICommandHandler<ResetPasswordCommand, Result> resetPasswordHandler,
        ICommandHandler<ChangePasswordCommand, Result> changePasswordHandler,
        ICommandHandler<GetCurrentUserCommand, Result<UserResponse>> getCurrentUserHandler)
    {
        _registerCustomerHandler = registerCustomerHandler;
        _loginHandler = loginHandler;
        _refreshHandler = refreshHandler;
        _logoutHandler = logoutHandler;
        _verifyEmailHandler = verifyEmailHandler;
        _resendVerificationHandler = resendVerificationHandler;
        _forgotPasswordHandler = forgotPasswordHandler;
        _resetPasswordHandler = resetPasswordHandler;
        _changePasswordHandler = changePasswordHandler;
        _getCurrentUserHandler = getCurrentUserHandler;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterCustomerRequest request, CancellationToken cancellationToken)
    {
        var command = new RegisterCustomerCommand(request.Email, request.Password);
        return Success(await _registerCustomerHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email, request.Password);
        return Success(await _loginHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var command = new RefreshCommand(request.RefreshToken);
        return Success(await _refreshHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        var command = new LogoutCommand(request.RefreshToken);
        return Success(await _logoutHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var command = new VerifyEmailCommand(request.Token);
        return Success(await _verifyEmailHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var command = new ResendVerificationCommand(request.Email);
        return Success(await _resendVerificationHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ForgotPasswordCommand(request.Email);
        return Success(await _forgotPasswordHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(request.Token, request.NewPassword);
        return Success(await _resetPasswordHandler.HandleAsync(command, cancellationToken));
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ChangePasswordCommand(request.CurrentPassword, request.NewPassword, request.ConfirmPassword);
        return Success(await _changePasswordHandler.HandleAsync(command, cancellationToken));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var command = new GetCurrentUserCommand();
        return Success(await _getCurrentUserHandler.HandleAsync(command, cancellationToken));
    }
}
