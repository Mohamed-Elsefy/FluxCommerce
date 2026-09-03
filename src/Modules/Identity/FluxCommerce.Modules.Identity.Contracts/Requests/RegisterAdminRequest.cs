using FluxCommerce.Modules.Identity.Contracts.Requests;

namespace FluxCommerce.Modules.Identity.Contracts.Requests;

public sealed record RegisterAdminRequest(string Email, string Password, string FirstName, string LastName);
