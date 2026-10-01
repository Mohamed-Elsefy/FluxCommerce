using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Contracts.Responses;

namespace FluxCommerce.Modules.Identity.Application.Commands.UpdateProfile;

/// <summary>
/// Updates the authenticated user's editable profile fields. Every field is optional:
/// <c>null</c> leaves the current value unchanged, while an empty value clears an
/// optional field (phone number, avatar URL).
/// </summary>
public sealed record UpdateProfileCommand(
    string? FirstName,
    string? LastName,
    string? PhoneNumber,
    string? AvatarUrl,
    string? PreferredLanguage,
    string? TimeZone)
    : ICommand<Result<UserResponse>>;
