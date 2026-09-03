using FluxCommerce.Modules.Identity.Contracts.Requests;

namespace FluxCommerce.Modules.Identity.Contracts.Requests;

public sealed record RegisterCustomerRequest(string Email, string Password, string FirstName, string LastName);
