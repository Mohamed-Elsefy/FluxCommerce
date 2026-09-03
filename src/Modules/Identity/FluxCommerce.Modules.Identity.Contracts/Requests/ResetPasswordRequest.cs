namespace FluxCommerce.Modules.Identity.Contracts.Requests;

public sealed record ResetPasswordRequest(string Token, string NewPassword);
