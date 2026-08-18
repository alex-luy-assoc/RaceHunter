using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace RaceHunter.Infrastructure.Observability;

public static class RaceHunterTelemetry
{
    public const string SourceName = "RaceHunter";
    public const string MeterName = "RaceHunter";
    public static readonly ActivitySource Activities = new(SourceName);
    public static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> WorkMessages = Meter.CreateCounter<long>("racehunter.work.messages");
    public static readonly Counter<long> TargetRequests = Meter.CreateCounter<long>("racehunter.target.requests");
    public static readonly Counter<long> ManualTargetRequests = Meter.CreateCounter<long>("racehunter.target.manual.requests");
    public static readonly Counter<long> ModelCalls = Meter.CreateCounter<long>("racehunter.model.calls");
    public static readonly Counter<long> InvariantOutcomes = Meter.CreateCounter<long>("racehunter.invariant.outcomes");
    public static readonly Counter<long> Findings = Meter.CreateCounter<long>("racehunter.findings");
    public static readonly Counter<long> Replays = Meter.CreateCounter<long>("racehunter.replays");
    public static readonly Histogram<double> TargetLatency = Meter.CreateHistogram<double>("racehunter.target.duration", "ms");
    public static readonly Histogram<double> ManualTargetLatency = Meter.CreateHistogram<double>("racehunter.target.manual.duration", "ms");
    public static readonly Histogram<double> CancellationLatency = Meter.CreateHistogram<double>("racehunter.cancellation.duration", "ms");
    public static readonly Histogram<double> QueueDelay = Meter.CreateHistogram<double>("racehunter.work.queue_delay", "ms");

    public static Activity? StartCampaignActivity(
        Guid runId,
        Guid attemptId,
        int actorId,
        string stepId,
        string requestId,
        string modelInvocationId)
    {
        var activity = Activities.StartActivity("racehunter.campaign.step", ActivityKind.Internal);
        activity?.SetTag("racehunter.run.id", runId.ToString());
        activity?.SetTag("racehunter.attempt.id", attemptId.ToString());
        activity?.SetTag("racehunter.actor.id", actorId);
        activity?.SetTag("racehunter.step.id", stepId);
        activity?.SetTag("racehunter.request.id", requestId);
        activity?.SetTag("racehunter.model.invocation_id", modelInvocationId);
        return activity;
    }
}
