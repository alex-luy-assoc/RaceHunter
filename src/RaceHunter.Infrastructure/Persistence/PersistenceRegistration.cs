using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Projects;

namespace RaceHunter.Infrastructure.Persistence;

public static class PersistenceRegistration
{
    public static IServiceCollection AddRaceHunterPersistence(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<RaceHunterDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<RaceHunterDbContext>());
        services.AddScoped<ProjectService>();
        return services;
    }

    public static async Task ApplyRaceHunterMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<RaceHunterDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
