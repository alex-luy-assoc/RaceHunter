using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Projects;
using RaceHunter.Application.Runs;

namespace RaceHunter.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddRaceHunterPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddPooledDbContextFactory<RaceHunterDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped(provider => provider.GetRequiredService<IDbContextFactory<RaceHunterDbContext>>().CreateDbContext());
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<RunStore>();
        services.AddScoped<IRunStore>(provider => provider.GetRequiredService<RunStore>());
        services.AddScoped<ITraceStore>(provider => provider.GetRequiredService<RunStore>());
        services.AddScoped<IRunAttemptStore>(provider => provider.GetRequiredService<RunStore>());
        services.AddSingleton<IRunCancellationProbe, RunCancellationProbe>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<RaceHunterDbContext>());
        services.AddScoped<ProjectService>();
        services.AddScoped<GetRun>();
        services.AddScoped<CancelRun>();
        return services;
    }

    public static async Task ApplyRaceHunterMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RaceHunterDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
