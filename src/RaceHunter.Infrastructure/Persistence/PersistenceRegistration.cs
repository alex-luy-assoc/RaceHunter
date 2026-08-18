using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Findings;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Application.Projects;
using RaceHunter.Application.Replays;
using RaceHunter.Application.Runs;

namespace RaceHunter.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddRaceHunterPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPooledDbContextFactory<RaceHunterDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<RaceHunterDbContext>>().CreateDbContext());
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IManualTargetStore, ManualTargetStore>();
        services.AddScoped<ISecurityAuditStore, SecurityAuditStore>();
        services.AddScoped<HuntWorkflowStore>();
        services.AddScoped<IHuntStore>(provider => provider.GetRequiredService<HuntWorkflowStore>());
        services.AddScoped<IHuntWorkflowStore>(provider => provider.GetRequiredService<HuntWorkflowStore>());
        services.AddScoped<IOutboxStore>(provider => provider.GetRequiredService<HuntWorkflowStore>());
        services.AddScoped<IAgentIterationStore>(provider => provider.GetRequiredService<HuntWorkflowStore>());
        services.AddScoped<FindingStore>();
        services.AddScoped<IFindingStore>(provider => provider.GetRequiredService<FindingStore>());
        services.AddScoped<IReplayStore>(provider => provider.GetRequiredService<FindingStore>());
        services.AddScoped<IAgentIterationReader>(provider => provider.GetRequiredService<FindingStore>());
        services.AddScoped<IFindingProbeCheckpointStore, FindingProbeCheckpointStore>();
        services.AddScoped<IWorkInbox, WorkInboxStore>();
        services.AddScoped<IWorkSubjectStore, WorkSubjectStore>();
        services.AddScoped<IAgentDecisionCheckpointStore, AgentDecisionCheckpointStore>();
        services.AddScoped<RunStore>();
        services.AddScoped<IRunStore>(provider => provider.GetRequiredService<RunStore>());
        services.AddScoped<ITraceStore>(provider => provider.GetRequiredService<RunStore>());
        services.AddScoped<IRunAttemptStore>(provider => provider.GetRequiredService<RunStore>());
        services.AddSingleton<IRunCancellationProbe, RunCancellationProbe>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<RaceHunterDbContext>());
        services.AddScoped<ProjectService>();
        services.AddScoped<GetRun>();
        services.AddScoped<GetCloudExecutionEvidence>();
        services.AddScoped<CancelRun>();
        services.AddScoped<CreateHunt>();
        services.AddScoped<GeneratePlan>();
        services.AddScoped<ApproveAndRun>();
        services.AddScoped<GetFinding>();
        services.AddScoped<VerifyFix>();
        return services;
    }

    public static async Task ApplyRaceHunterMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RaceHunterDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
