using FluxCommerce.BuildingBlocks.Localization.Abstractions;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Validation;
using FluxCommerce.Modules.Identity.Domain.Aggregates;

namespace FluxCommerce.Modules.Identity.Application.Commands.AssignRole;

public sealed class AssignRoleCommandValidator
{
    private readonly ILocalizer _localizer;
    private readonly IRoleRepository _roleRepository;

    public AssignRoleCommandValidator(ILocalizer localizer, IRoleRepository roleRepository)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _roleRepository = roleRepository ?? throw new ArgumentNullException(nameof(roleRepository));
    }

    public async Task<ValidationResult> ValidateAsync(AssignRoleCommand command, CancellationToken cancellationToken = default)
    {
        var errors = new Dictionary<string, string[]>();

        if (command.UserId == Guid.Empty)
        {
            errors["UserId"] = [ _localizer["Validation.Identity.UserIdRequired"] ];
        }

        if (string.IsNullOrWhiteSpace(command.Role))
        {
            errors["Role"] = [ _localizer["Validation.Identity.RoleRequired"] ];
        }
        else
        {
            var trimmed = command.Role.Trim();

            if (trimmed.Length > Role.NameMaxLength)
            {
                errors["Role"] = [ _localizer.Get("Validation.Identity.RoleTooLong", Role.NameMaxLength) ];
            }
            else
            {
                // Validate against persisted Roles lookup + whitelist constants
                var exists = await _roleRepository.ExistsAsync(trimmed, cancellationToken);
                if (!exists)
                {
                    errors["Role"] = [ _localizer["Validation.Identity.RoleInvalid"] ];
                }
            }
        }

        return errors.Count == 0
            ? ValidationResult.Success()
            : ValidationResult.Failure(errors);
    }
}
