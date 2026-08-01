using FluxCommerce.BuildingBlocks.Application.Abstractions;
using FluxCommerce.BuildingBlocks.Application.Errors;
using FluxCommerce.BuildingBlocks.Application.Results;
using FluxCommerce.Modules.Identity.Application.Interfaces.Repositories;
using FluxCommerce.Modules.Identity.Application.Interfaces.Security;
using FluxCommerce.Modules.Identity.Application.Mappings;
using FluxCommerce.Modules.Identity.Contracts.Responses;
using FluxCommerce.Modules.Identity.Domain.Aggregates;
using FluxCommerce.Modules.Identity.Domain.ValueObjects;

namespace FluxCommerce.Modules.Identity.Application.Commands.RegisterCustomer;

public sealed class RegisterCustomerCommandHandler : ICommandHandler<RegisterCustomerCommand, Result<UserResponse>>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public RegisterCustomerCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<UserResponse>> HandleAsync(
        RegisterCustomerCommand command,
        CancellationToken cancellationToken = default)
    {
        var validation = new RegisterCustomerCommandValidator().Validate(command);

        if (!validation.IsValid)
            return Result.Failure<UserResponse>(
                Error.Validation("Identity.ValidationFailed", "One or more fields are invalid."));

        Email email;
        try
        {
            email = new Email(command.Email);
        }
        catch (ArgumentException)
        {
            return Result.Failure<UserResponse>(
                Error.Validation("Identity.InvalidEmail", "The email address is invalid."));
        }

        if (await _userRepository.ExistsByEmailAsync(email, cancellationToken))
            return Result.Failure<UserResponse>(
                Error.Conflict("Identity.DuplicateEmail", "An account with this email address already exists."));

        var user = User.Register(
            Guid.NewGuid(),
            email,
            _passwordHasher.Hash(command.Password),
            _dateTimeProvider.UtcNow,
            [Roles.Customer]);

        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(UserResponseMapper.ToResponse(user));
    }
}
