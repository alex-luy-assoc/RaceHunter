using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace RaceHunter.Infrastructure.Observability;

public static class TelemetryRegistration
{
    public static IServiceCollection AddRaceHunterTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var resource = ResourceBuilder.CreateDefault()
            .AddService(serviceName, serviceVersion: typeof(TelemetryRegistration).Assembly.GetName().Version?.ToString());
        services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing.SetResourceBuilder(resource)
                    .AddSource(RaceHunterTelemetry.SourceName)
                    .AddAspNetCoreInstrumentation(options => options.RecordException = true)
                    .AddHttpClientInstrumentation(options => options.RecordException = true);
                if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) tracing.AddOtlpExporter();
            })
            .WithMetrics(metrics =>
            {
                metrics.SetResourceBuilder(resource)
                    .AddMeter(RaceHunterTelemetry.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (!string.IsNullOrWhiteSpace(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])) metrics.AddOtlpExporter();
            });
        return services;
    }

    public static IApplicationBuilder UseRaceHunterRequestTelemetry(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            var supplied = context.Request.Headers["X-RaceHunter-Correlation-Id"].ToString();
            var correlationId = supplied.Length is > 0 and <= 160 && supplied.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or ':' or '.')
                ? supplied
                : Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
            context.Response.Headers["X-RaceHunter-Correlation-Id"] = correlationId;
            Activity.Current?.SetTag("racehunter.correlation.id", correlationId);
            using var scope = context.RequestServices.GetRequiredService<ILoggerFactory>()
                .CreateLogger("RaceHunter.Request")
                .BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId });
            await next(context);
        });
    }
}
