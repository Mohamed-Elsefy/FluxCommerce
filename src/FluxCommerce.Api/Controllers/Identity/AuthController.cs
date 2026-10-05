using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.BuildingBlocks.Web.Controllers;
using FluxCommerce.BuildingBlocks.Web.Localization;
using FluxCommerce.BuildingBlocks.Web.Responses;
using FluxCommerce.Modules.Identity.Application.Commands.ChangePassword;
using FluxCommerce.Modules.Identity.Application.Commands.ForgotPassword;
using FluxCommerce.Modules.Identity.Application.Commands.GetCurrentUser;
using FluxCommerce.Modules.Identity.Application.Commands.Login;
using FluxCommerce.Modules.Identity.Application.Commands.Logout;
using FluxCommerce.Modules.Identity.Application.Commands.Refresh;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterAdmin;
using FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;
using FluxCommerce.Modules.Identity.Application.Commands.ResendVerification;
using FluxCommerce.Modules.Identity.Application.Commands.ResetPassword;
using FluxCommerce.Modules.Identity.Application.Commands.UpdateProfile;
using FluxCommerce.Modules.Identity.Application.Commands.VerifyEmail;
using FluxCommerce.Modules.Identity.Contracts.Requests;
using FluxCommerce.Modules.Identity.Contracts.Responses;

using FluxCommerce.Api.Extensions;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FluxCommerce.Api.Controllers.Identity;

[ApiController]
[Tags(EndpointTags.Identity)]
[Route("auth")]
[Authorize]
public sealed class AuthController : ApiControllerBase
{
    private readonly ICommandHandler<RegisterCustomerCommand, Result<UserResponse>> _registerCustomerHandler;
    private readonly ICommandHandler<RegisterAdminCommand, Result<UserResponse>> _registerAdminHandler;
    private readonly ICommandHandler<LoginCommand, Result<LoginResponse>> _loginHandler;
    private readonly ICommandHandler<RefreshCommand, Result<LoginResponse>> _refreshHandler;
    private readonly ICommandHandler<LogoutCommand, Result> _logoutHandler;
    private readonly ICommandHandler<VerifyEmailCommand, Result<UserResponse>> _verifyEmailHandler;
    private readonly ICommandHandler<ResendVerificationCommand, Result<ResendVerificationResponse>> _resendVerificationHandler;
    private readonly ICommandHandler<ForgotPasswordCommand, Result> _forgotPasswordHandler;
    private readonly ICommandHandler<ResetPasswordCommand, Result> _resetPasswordHandler;
    private readonly ICommandHandler<ChangePasswordCommand, Result> _changePasswordHandler;
    private readonly ICommandHandler<GetCurrentUserCommand, Result<UserResponse>> _getCurrentUserHandler;
    private readonly ICommandHandler<UpdateProfileCommand, Result<UserResponse>> _updateProfileHandler;

    public AuthController(
        ICommandHandler<RegisterCustomerCommand, Result<UserResponse>> registerCustomerHandler,
        ICommandHandler<RegisterAdminCommand, Result<UserResponse>> registerAdminHandler,
        ICommandHandler<LoginCommand, Result<LoginResponse>> loginHandler,
        ICommandHandler<RefreshCommand, Result<LoginResponse>> refreshHandler,
        ICommandHandler<LogoutCommand, Result> logoutHandler,
        ICommandHandler<VerifyEmailCommand, Result<UserResponse>> verifyEmailHandler,
        ICommandHandler<ResendVerificationCommand, Result<ResendVerificationResponse>> resendVerificationHandler,
        ICommandHandler<ForgotPasswordCommand, Result> forgotPasswordHandler,
        ICommandHandler<ResetPasswordCommand, Result> resetPasswordHandler,
        ICommandHandler<ChangePasswordCommand, Result> changePasswordHandler,
        ICommandHandler<GetCurrentUserCommand, Result<UserResponse>> getCurrentUserHandler,
        ICommandHandler<UpdateProfileCommand, Result<UserResponse>> updateProfileHandler,
        ILocalizer localizer,
        IErrorCodeLocalizer errorCodeLocalizer)
        : base(localizer, errorCodeLocalizer)
    {
        _registerCustomerHandler = registerCustomerHandler;
        _registerAdminHandler = registerAdminHandler;
        _loginHandler = loginHandler;
        _refreshHandler = refreshHandler;
        _logoutHandler = logoutHandler;
        _verifyEmailHandler = verifyEmailHandler;
        _resendVerificationHandler = resendVerificationHandler;
        _forgotPasswordHandler = forgotPasswordHandler;
        _resetPasswordHandler = resetPasswordHandler;
        _changePasswordHandler = changePasswordHandler;
        _getCurrentUserHandler = getCurrentUserHandler;
        _updateProfileHandler = updateProfileHandler;
    }

    /// <summary>Registers a new customer account.</summary>
    /// <remarks>
    /// Accepts the account's email, password, first name, and last name.
    /// The display name is derived from the first and last name; phone number,
    /// avatar URL, preferred language, and time zone are initialized with defaults
    /// and can be managed in a later phase.
    /// The account is created with a pending status and is activated once the
    /// verification email is confirmed.
    /// </remarks>
    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterCustomerRequest request, CancellationToken cancellationToken)
    {
        var command = new RegisterCustomerCommand(request.Email, request.Password, request.FirstName, request.LastName);
        return Success(await _registerCustomerHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Registers a new administrator account. Requires SuperAdmin role.</summary>
    [HttpPost("register-admin")]
    [Authorize(Policy = AuthenticationConfiguration.SuperAdminPolicy)]
    public async Task<IActionResult> RegisterAdmin([FromBody] RegisterAdminRequest request, CancellationToken cancellationToken)
    {
        var command = new RegisterAdminCommand(request.Email, request.Password, request.FirstName, request.LastName);
        return Created(await _registerAdminHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Authenticates a user and returns an access token with a refresh token.</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var command = new LoginCommand(request.Email, request.Password);
        return Success(await _loginHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Rotates an existing refresh token and returns a new token pair.</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var command = new RefreshCommand(request.RefreshToken);
        return Success(await _refreshHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Revokes the presented refresh token.</summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        var command = new LogoutCommand(request.RefreshToken);
        return Success(await _logoutHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Confirms an email address using the verification token.</summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var command = new VerifyEmailCommand(request.Token);
        return Success(await _verifyEmailHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Issues a new email verification token for a registered but unverified account.</summary>
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification([FromBody] ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var command = new ResendVerificationCommand(request.Email);
        return Success(await _resendVerificationHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Requests a password reset token for an existing account.</summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ForgotPasswordCommand(request.Email);
        return Success(await _forgotPasswordHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Sets a new password using a password reset token.</summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ResetPasswordCommand(request.Token, request.NewPassword);
        return Success(await _resetPasswordHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Changes the authenticated user's password using the current password.</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var command = new ChangePasswordCommand(request.CurrentPassword, request.NewPassword, request.ConfirmPassword);
        return Success(await _changePasswordHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Returns the profile of the authenticated user.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var command = new GetCurrentUserCommand();
        return Success(await _getCurrentUserHandler.HandleAsync(command, cancellationToken));
    }

    /// <summary>Partially updates the authenticated user's profile. Null fields are left unchanged.</summary>
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var command = new UpdateProfileCommand(
            request.FirstName,
            request.LastName,
            request.PhoneNumber,
            request.AvatarUrl,
            request.PreferredLanguage,
            request.TimeZone);
        return Success(await _updateProfileHandler.HandleAsync(command, cancellationToken));
    }
}
