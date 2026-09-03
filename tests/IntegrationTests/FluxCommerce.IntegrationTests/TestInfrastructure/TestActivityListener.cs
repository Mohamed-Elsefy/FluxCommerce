using System.Diagnostics;
using FluxCommerce.BuildingBlocks.Infrastructure.Tracing;

namespace FluxCommerce.IntegrationTests.TestInfrastructure;

/// <summary>
/// Phase 8.9 test helper: an <see cref="ActivityListener"/> that samples every activity from the
/// <see cref="TraceContext.Source"/> ("FluxCommerce") and captures the stopped activities so integration
/// tests can assert on the distributed trace produced by the Order/Inventory saga workflow. Reads take an
/// immutable snapshot under a lock because activities stop on background consumer threads.
/// </summary>
public sealed class TestActivityListener : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];
    private readonly object _gate = new();

    public TestActivityListener()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TraceContext.ActivitySourceName,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (_gate)
                {
                    _activities.Add(activity);
                }
            }
        };

        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<Activity> Activities
    {
        get
        {
            lock (_gate)
            {
                return _activities.ToList();
            }
        }
    }

    public void Dispose() => _listener.Dispose();
}