using Xunit;

namespace RaceHunter.Architecture.Tests;

public sealed class PhaseFiveDeploymentContractTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Terraform_keeps_only_api_public_and_private_services_identity_scoped()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Equal(1, Count(terraform, "member   = \"allUsers\""));
        Assert.Contains("google_cloud_run_v2_service_iam_member\" \"api_worker", terraform, StringComparison.Ordinal);
        Assert.Contains("serviceAccount:${google_service_account.api.email}", terraform, StringComparison.Ordinal);
        Assert.Contains("google_cloud_run_v2_service_iam_member\" \"worker_target", terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_wires_pubsub_and_api_id_token_audiences_to_private_worker()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Contains("audience              = google_cloud_run_v2_service.worker.uri", terraform, StringComparison.Ordinal);
        Assert.Contains("Worker__Audience", terraform, StringComparison.Ordinal);
        Assert.Contains("Worker__RequireAuthentication", terraform, StringComparison.Ordinal);
        Assert.Contains("ReferenceTarget__Audience", terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_enforces_instance_ceiling_and_optional_budget_alerts()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Contains("One worker owns the process-wide global and target limiters", terraform, StringComparison.Ordinal);
        Assert.Contains("max_instance_count = 1", terraform, StringComparison.Ordinal);
        Assert.Contains("max_instance_request_concurrency = 1", terraform, StringComparison.Ordinal);
        Assert.Contains("google_billing_budget\" \"staging", terraform, StringComparison.Ordinal);
        Assert.Contains("threshold_percent = 1.0", terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void Iam_private_services_accept_authenticated_run_app_calls_without_an_unconfigured_vpc_route()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Equal(3, Count(terraform, "ingress             = \"INGRESS_TRAFFIC_ALL\""));
        Assert.DoesNotContain("INGRESS_TRAFFIC_INTERNAL_ONLY", terraform, StringComparison.Ordinal);
        Assert.Equal(1, Count(terraform, "member   = \"allUsers\""));
    }

    [Fact]
    public void Staging_smoke_checks_private_service_denial_and_bounds_each_request()
    {
        var script = File.ReadAllText(Path.Combine(Root, "deploy", "scripts", "smoke.ps1"));

        Assert.Contains("WorkerUrl", script, StringComparison.Ordinal);
        Assert.Contains("ReferenceTargetUrl", script, StringComparison.Ordinal);
        Assert.Contains("Assert-UnauthenticatedDenied", script, StringComparison.Ordinal);
        Assert.Contains("-TimeoutSec", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_exports_application_otel_traces_and_metrics_through_google_collector_sidecars()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Equal(3, Count(terraform, "name  = \"OTEL_EXPORTER_OTLP_ENDPOINT\""));
        Assert.Equal(3, Count(terraform, "otelcol-google:0.156.0"));
        Assert.Contains("googlemanagedprometheus", terraform, StringComparison.Ordinal);
        Assert.Contains("roles/cloudtrace.agent", terraform, StringComparison.Ordinal);
        Assert.Contains("roles/monitoring.metricWriter", terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void Deployment_script_requires_explicit_billable_resource_approval_and_immutable_digests()
    {
        var script = File.ReadAllText(Path.Combine(Root, "deploy", "scripts", "deploy.ps1"));

        Assert.Contains("ApproveBillableResources", script, StringComparison.Ordinal);
        Assert.Contains("@sha256:", script, StringComparison.Ordinal);
        Assert.Contains("terraform apply", script, StringComparison.Ordinal);
    }

    private static int Count(string value, string fragment) => value.Split(fragment, StringSplitOptions.None).Length - 1;

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RaceHunter.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
