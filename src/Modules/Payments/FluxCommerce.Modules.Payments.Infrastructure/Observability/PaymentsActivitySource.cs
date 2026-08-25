using System.Diagnostics;

namespace FluxCommerce.Modules.Payments.Infrastructure.Observability;

public static class PaymentsActivitySource
{
    public const string Name = "FluxCommerce";
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}
