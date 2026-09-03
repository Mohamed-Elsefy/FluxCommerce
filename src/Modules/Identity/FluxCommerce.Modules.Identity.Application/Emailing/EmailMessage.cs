namespace FluxCommerce.Modules.Identity.Application.Emailing;

public sealed record EmailMessage(
    string ToEmail,
    string ToName,
    string Subject,
    string HtmlBody,
    string TextBody);
