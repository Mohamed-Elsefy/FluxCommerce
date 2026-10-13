using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Observability.Tracing;

namespace FluxCommerce.BuildingBlocks.Infrastructure.Tracing;

/// <summary>
/// Distributed-trace helpers shared by the producer and consumer pipelines. The
/// <see cref="ActivitySource"/> name (<see cref="ActivitySources.FluxCommerce"/>, the single source
/// of truth in <c>BuildingBlocks.Observability</c>) matches the source registered with the
/// OpenTelemetry exporter, so activities started here are exported without any configuration change.
/// W3C Trace Context is propagated across every async boundary: the producing activity's
/// <c>traceparent</c>/<c>tracestate</c> is staged into the outbox when the message is created,
/// restored by the outbox dispatcher as the parent of the publishing activity, injected into the
/// broker transport headers, and extracted by the consumer processor to seed the consuming
/// activity — so a single trace spans the whole producer → outbox → broker → consumer workflow
/// (e.g. Checkout → Order Saga → RabbitMQ → Inventory Consumer → RabbitMQ → Order Saga).
/// </summary>
public static class TraceContext
{
    /// <summary>
    /// The ActivitySource name registered with the OpenTelemetry exporter. Activities must be created from
    /// a source with exactly this name to be sampled and exported. Aliases
    /// <see cref="ActivitySources.FluxCommerce"/> so a rename can never silently drop spans.
    /// </summary>
    public const string ActivitySourceName = ActivitySources.FluxCommerce;

    /// <summary>W3C Trace Context transport header carrying the parent span id (the W3C <c>traceparent</c>).</summary>
    public const string TraceParentHeader = "traceparent";

    /// <summary>W3C Trace Context transport header carrying vendor trace state (the W3C <c>tracestate</c>).</summary>
    public const string TraceStateHeader = "tracestate";

    public static readonly ActivitySource Source = new(ActivitySourceName);

    /// <summary>
    /// Returns the activity's W3C <c>traceparent</c> value (its <see cref="Activity.Id"/>) when the activity
    /// uses the W3C id format, or <see langword="null"/> otherwise. <see langword="null"/> is returned for a
    /// <see langword="null"/> activity or one that cannot be propagated.
    /// </summary>
    public static string? GetTraceParent(Activity? activity) =>
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;

    /// <summary>Returns the activity's W3C <c>tracestate</c> value, or <see langword="null"/>.</summary>
    public static string? GetTraceState(Activity? activity) => activity?.TraceStateString;

    /// <summary>
    /// Parses a W3C <c>traceparent</c> (and optional <c>tracestate</c>) into an <see cref="ActivityContext"/>
    /// that can be used as the parent of a new activity. The parent is treated as remote (it was produced by
    /// another process). Returns <see langword="false"/> when the <c>traceparent</c> is absent or malformed.
    /// </summary>
    public static bool TryParse(string? traceParent, string? traceState, out ActivityContext context)
    {
        if (string.IsNullOrWhiteSpace(traceParent))
        {
            context = default;
            return false;
        }

        return ActivityContext.TryParse(traceParent, traceState, isRemote: true, out context);
    }

    /// <summary>
    /// Starts a new activity of the given kind from the <see cref="Source"/>, rooted at the remote
    /// W3C <c>traceparent</c>/<c>tracestate</c> when they parse (e.g. an outbox message restoring its
    /// producing context, or a consumer extracting the transport trace headers). Without a parseable
    /// parent the activity is rooted at the ambient <see cref="Activity.Current"/> when one is present, so
    /// e.g. saga activities stay on the trace of the consuming activity; with no ambient activity it starts
    /// a new trace root. Returns <see langword="null"/> when no listener is sampling this source (tracing
    /// is then skipped, never failing the caller).
    /// </summary>
    public static Activity? StartActivity(
        string name,
        ActivityKind kind,
        string? traceParent = null,
        string? traceState = null)
    {
        Activity? activity;

        if (TryParse(traceParent, traceState, out var context))
            activity = Source.CreateActivity(name, kind, context);
        else if (Activity.Current is { } parent)
            activity = Source.CreateActivity(name, kind, parent.Context);
        else
            activity = Source.CreateActivity(name, kind);

        activity?.Start();
        return activity;
    }
}