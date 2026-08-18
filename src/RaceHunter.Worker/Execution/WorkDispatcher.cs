using RaceHunter.Application.Agents;
using RaceHunter.Application.Messaging;
using RaceHunter.Contracts;

namespace RaceHunter.Worker.Execution;

internal enum WorkDispatchOutcome
{
    Acknowledged,
    Retry
}

internal sealed class WorkDispatcher(
    IWorkInbox inbox,
    IWorkPublisher publisher,
    IPlanWorkHandler planHandler,
    ICampaignWorkHandler campaignRunner,
    IWorkSubjectStore subjects,
    IConfiguration configuration,
    IServiceScopeFactory scopeFactory)
{
    private readonly string owner = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    private readonly TimeSpan leaseDuration = TimeSpan.FromSeconds(configuration.GetValue("Work:LeaseSeconds", 30));
    private readonly TimeSpan heartbeatInterval = TimeSpan.FromMilliseconds(configuration.GetValue(
        "Work:HeartbeatIntervalMilliseconds",
        (int)Math.Max(1000, TimeSpan.FromSeconds(configuration.GetValue("Work:LeaseSeconds", 30)).TotalMilliseconds / 3)));

    public async Task<WorkDispatchOutcome> DispatchAsync(WorkMessage message, string messageId, CancellationToken cancellationToken)
    {
        var acquired = await inbox.TryAcquireAsync(message.WorkId, messageId, owner, DateTime.UtcNow, leaseDuration, cancellationToken);
        if (acquired.Outcome == WorkAcquireOutcome.Duplicate) return WorkDispatchOutcome.Acknowledged;
        if (acquired.Outcome == WorkAcquireOutcome.DeadLettered)
        {
            var kind = Enum.TryParse<WorkKind>(message.Kind, out var parsed) ? parsed : WorkKind.Unknown;
            var failure = acquired.Failure ?? new WorkFailure(WorkFailureCategory.Poison, false, true, "work was dead-lettered");
            await subjects.MarkDeadLetteredAsync(kind, message.SubjectId, failure, DateTime.UtcNow, cancellationToken);
            await publisher.PublishDeadLetterAsync(ToDispatch(message), failure, cancellationToken);
            return WorkDispatchOutcome.Acknowledged;
        }
        if (acquired.Outcome is WorkAcquireOutcome.Busy or WorkAcquireOutcome.RetryLater) return WorkDispatchOutcome.Retry;

        using var processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var leaseLost = 0;
        var heartbeat = HeartbeatAsync(message.WorkId, () =>
        {
            Interlocked.Exchange(ref leaseLost, 1);
            processing.Cancel();
        }, processing.Token);
        try
        {
            switch (message.Kind)
            {
                case "PlanRequested":
                    await planHandler.ExecuteAsync(message.SubjectId, processing.Token);
                    await inbox.SaveCheckpointAsync(message.WorkId, owner, new WorkCheckpoint("plan-finished", 0, "{}", DateTime.UtcNow), processing.Token);
                    break;
                case "RunRequested":
                    await campaignRunner.ExecuteAsync(
                        message.SubjectId,
                        message.WorkId,
                        owner,
                        acquired.Checkpoint,
                        processing.Token);
                    break;
                default:
                    throw new InvalidDataException("The work kind is not supported.");
            }
            await inbox.CompleteAsync(message.WorkId, owner, DateTime.UtcNow, CancellationToken.None);
            return WorkDispatchOutcome.Acknowledged;
        }
        catch (OperationCanceledException) when (Volatile.Read(ref leaseLost) == 1)
        {
            return WorkDispatchOutcome.Retry;
        }
        catch (WorkLeaseLostException)
        {
            return WorkDispatchOutcome.Retry;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failure = Classify(exception, message.Kind);
            var kind = Enum.TryParse<WorkKind>(message.Kind, out var parsed) ? parsed : WorkKind.Unknown;
            var maxRetries = await subjects.GetMaxRetriesAsync(kind, message.SubjectId, CancellationToken.None);
            var outcome = await inbox.RecordFailureAsync(message.WorkId, owner, failure, maxRetries, DateTime.UtcNow, CancellationToken.None);
            if (outcome == WorkFailureOutcome.DeadLettered)
            {
                await subjects.MarkDeadLetteredAsync(kind, message.SubjectId, failure, DateTime.UtcNow, CancellationToken.None);
                await publisher.PublishDeadLetterAsync(ToDispatch(message), failure, CancellationToken.None);
                return WorkDispatchOutcome.Acknowledged;
            }
            return WorkDispatchOutcome.Retry;
        }
        finally
        {
            processing.Cancel();
            await heartbeat;
        }
    }

    private async Task HeartbeatAsync(Guid workId, Action leaseLost, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(heartbeatInterval, cancellationToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var heartbeatInbox = scope.ServiceProvider.GetRequiredService<IWorkInbox>();
                if (!await heartbeatInbox.HeartbeatAsync(workId, owner, DateTime.UtcNow, leaseDuration, cancellationToken))
                {
                    leaseLost();
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            leaseLost();
        }
    }

    private static WorkFailure Classify(Exception exception, string kind) => exception switch
    {
        ModelOutputException model => new WorkFailure(WorkFailureCategory.Model, model.Outcome == ModelOutcome.TransientFailure, true, model.Outcome.ToString()),
        HttpRequestException => new WorkFailure(WorkFailureCategory.Target, true, false, "target request failed"),
        InvalidDataException => new WorkFailure(WorkFailureCategory.Poison, false, true, "unsupported work contract"),
        OperationCanceledException => new WorkFailure(WorkFailureCategory.Cancellation, false, true, "work cancelled"),
        InvalidOperationException => new WorkFailure(WorkFailureCategory.Orchestration, false, kind == "PlanRequested", "worker orchestration failed"),
        _ => new WorkFailure(WorkFailureCategory.Persistence, true, true, "worker persistence failed")
    };

    private static WorkDispatch ToDispatch(WorkMessage message) => new(
        message.Version,
        message.WorkId,
        Enum.TryParse<WorkKind>(message.Kind, out var kind) ? kind : WorkKind.Unknown,
        message.SubjectId,
        message.CorrelationId,
        message.CreatedAtUtc);
}
