using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;

namespace FluxCommerce.Modules.Payments.Infrastructure.Observability;

public static class PaymentsActivitySource
{
    public const string Name = TraceContext.ActivitySourceName;
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}
