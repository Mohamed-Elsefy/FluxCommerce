namespace FluxCommerce.Modules.Identity.Contracts.Requests;

/// <summary>
/// Grants one action-level permission to a user. Requires SuperAdmin role.
/// </summary>
public sealed record GrantPermissionRequest(string Permission);
