using System.Text.RegularExpressions;
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
        Assert.Contains("max_instance_count = var.worker_max_instance_count", terraform, StringComparison.Ordinal);
        Assert.Contains("max_instance_request_concurrency = 1", terraform, StringComparison.Ordinal);
        Assert.Contains("google_billing_budget\" \"staging", terraform, StringComparison.Ordinal);
        Assert.Contains("threshold_percent = 1.0", terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_enforces_hard_application_cost_ceilings_and_deletion_protection()
    {
        var variables = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "variables.tf"));
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Contains("variable \"api_max_instance_count\"", variables, StringComparison.Ordinal);
        Assert.Contains("var.api_max_instance_count <= 2", variables, StringComparison.Ordinal);
        Assert.Contains("variable \"reference_target_max_instance_count\"", variables, StringComparison.Ordinal);
        Assert.Contains("var.reference_target_max_instance_count <= 2", variables, StringComparison.Ordinal);
        Assert.Contains("var.monthly_budget_usd <= 100", variables, StringComparison.Ordinal);
        Assert.Contains("disk_autoresize_limit = 20", terraform, StringComparison.Ordinal);
        Assert.Equal(5, Count(terraform, "deletion_protection = var.deletion_protection"));
        Assert.Contains("max_instance_count = var.api_max_instance_count", terraform, StringComparison.Ordinal);
        Assert.Contains("max_instance_count = var.reference_target_max_instance_count", terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_requires_a_bound_billing_account_and_never_silently_disables_the_budget()
    {
        var variables = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "variables.tf"));
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        var billingVariableStart = variables.IndexOf("variable \"billing_account_id\"", StringComparison.Ordinal);
        var billingVariableEnd = variables.IndexOf("variable \"monthly_budget_usd\"", billingVariableStart, StringComparison.Ordinal);
        var billingVariable = variables[billingVariableStart..billingVariableEnd];
        Assert.DoesNotContain("default", billingVariable, StringComparison.Ordinal);
        Assert.DoesNotContain("nullable", billingVariable, StringComparison.Ordinal);
        Assert.DoesNotContain("\n  count ", terraform[terraform.IndexOf("resource \"google_billing_budget\" \"staging\"", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("billing_account = var.billing_account_id", terraform, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_routes_user_access_token_quota_through_the_explicit_staging_project()
    {
        var providers = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "providers.tf"));

        Assert.Matches(@"billing_project\s*=\s*var\.project_id", providers);
        Assert.Matches(@"user_project_override\s*=\s*true", providers);
    }

    [Fact]
    public void Terraform_pins_cost_bounded_cloud_sql_to_the_enterprise_edition()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Equal(2, Regex.Matches(terraform, "edition\\s*=\\s*\"ENTERPRISE\"").Count);
        Assert.Equal(2, Count(terraform, "tier                  = \"db-f1-micro\""));
    }

    [Fact]
    public void Terraform_orders_cloud_run_after_every_secret_version_each_service_consumes()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));
        var api = CloudRunService(terraform, "api");
        var worker = CloudRunService(terraform, "worker");
        var referenceTarget = CloudRunService(terraform, "reference_target");

        Assert.Contains("google_secret_manager_secret_version.racehunter_database", api, StringComparison.Ordinal);
        Assert.Contains("google_secret_manager_secret_version.otel_collector_config", api, StringComparison.Ordinal);

        Assert.Contains("google_secret_manager_secret_version.racehunter_database", worker, StringComparison.Ordinal);
        Assert.Contains("google_secret_manager_secret_version.demo_control", worker, StringComparison.Ordinal);
        Assert.Contains("google_secret_manager_secret_version.otel_collector_config", worker, StringComparison.Ordinal);

        Assert.Contains("google_secret_manager_secret_version.target_database", referenceTarget, StringComparison.Ordinal);
        Assert.Contains("google_secret_manager_secret_version.demo_control", referenceTarget, StringComparison.Ordinal);
        Assert.Contains("google_secret_manager_secret_version.otel_collector_config", referenceTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_pins_generated_secret_references_to_the_exact_created_versions()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));
        var api = CloudRunService(terraform, "api");
        var worker = CloudRunService(terraform, "worker");
        var referenceTarget = CloudRunService(terraform, "reference_target");

        Assert.Contains("version = google_secret_manager_secret_version.racehunter_database.version", api, StringComparison.Ordinal);
        Assert.Contains("version = google_secret_manager_secret_version.otel_collector_config.version", api, StringComparison.Ordinal);

        Assert.Contains("version = google_secret_manager_secret_version.racehunter_database.version", worker, StringComparison.Ordinal);
        Assert.Contains("version = google_secret_manager_secret_version.demo_control.version", worker, StringComparison.Ordinal);
        Assert.Contains("version = google_secret_manager_secret_version.otel_collector_config.version", worker, StringComparison.Ordinal);

        Assert.Contains("version = google_secret_manager_secret_version.target_database.version", referenceTarget, StringComparison.Ordinal);
        Assert.Contains("version = google_secret_manager_secret_version.demo_control.version", referenceTarget, StringComparison.Ordinal);
        Assert.Contains("version = google_secret_manager_secret_version.otel_collector_config.version", referenceTarget, StringComparison.Ordinal);

        Assert.DoesNotContain("version = \"latest\"", api, StringComparison.Ordinal);
        Assert.DoesNotContain("version = \"latest\"", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("version = \"latest\"", referenceTarget, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_isolates_primary_and_target_database_credentials_by_instance()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));

        Assert.Contains("resource \"random_password\" \"primary_database\"", terraform, StringComparison.Ordinal);
        Assert.Contains("resource \"random_password\" \"target_database\"", terraform, StringComparison.Ordinal);
        Assert.Contains("resource \"google_sql_database_instance\" \"target\"", terraform, StringComparison.Ordinal);
        Assert.Contains("resource \"google_sql_user\" \"reference_target\"", terraform, StringComparison.Ordinal);
        Assert.Contains("instance = google_sql_database_instance.target.name", terraform, StringComparison.Ordinal);
        Assert.Contains("password = random_password.target_database.result", terraform, StringComparison.Ordinal);
        Assert.Contains("Username=${google_sql_user.reference_target.name};Password=${random_password.target_database.result}", terraform, StringComparison.Ordinal);
        var targetSecretStart = terraform.IndexOf("resource \"google_secret_manager_secret_version\" \"target_database\"", StringComparison.Ordinal);
        var targetSecretEnd = terraform.IndexOf("resource \"google_secret_manager_secret\" \"demo_control\"", targetSecretStart, StringComparison.Ordinal);
        var targetSecret = terraform[targetSecretStart..targetSecretEnd];
        Assert.DoesNotContain("google_sql_database_instance.main", targetSecret, StringComparison.Ordinal);
        Assert.DoesNotContain("google_sql_user.racehunter", targetSecret, StringComparison.Ordinal);
        Assert.DoesNotContain("random_password.primary_database", targetSecret, StringComparison.Ordinal);
        Assert.Equal(5, Count(terraform, "deletion_protection = var.deletion_protection"));
    }

    [Fact]
    public void Terraform_uses_keyless_resource_scoped_iam_and_exact_destination_audiences()
    {
        var terraform = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));
        var outputs = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "outputs.tf"));

        Assert.DoesNotContain("google_service_account_key", terraform, StringComparison.Ordinal);
        Assert.Equal(4, Count(terraform, "resource \"google_service_account\""));
        Assert.Equal(4, Count(terraform, "role     = \"roles/run.invoker\""));
        Assert.Equal(1, Count(terraform, "member   = \"allUsers\""));
        Assert.Equal(1, Count(terraform, "role    = \"roles/aiplatform.user\""));
        Assert.Contains("secret_id = google_secret_manager_secret.racehunter_database.id", terraform, StringComparison.Ordinal);
        Assert.Contains("secret_id = google_secret_manager_secret.target_database.id", terraform, StringComparison.Ordinal);
        Assert.Contains("service_account_email = google_service_account.pubsub_push.email", terraform, StringComparison.Ordinal);
        Assert.Equal(2, Count(terraform, "value = google_cloud_run_v2_service.worker.uri"));
        Assert.Equal(2, Count(terraform, "value = google_cloud_run_v2_service.reference_target.uri"));
        Assert.Equal(1, Count(terraform, "audience              = google_cloud_run_v2_service.worker.uri"));
        Assert.Contains("output \"service_account_emails\"", outputs, StringComparison.Ordinal);
        Assert.Contains("pubsub_push = google_service_account.pubsub_push.email", outputs, StringComparison.Ordinal);
        Assert.Contains("output \"worker_audience\"", outputs, StringComparison.Ordinal);
        Assert.Contains("output \"reference_target_audience\"", outputs, StringComparison.Ordinal);
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
        Assert.Contains("'/api/capabilities'", script, StringComparison.Ordinal);
        Assert.Contains("'/internal/replays'", script, StringComparison.Ordinal);
        Assert.Contains("'/api/inventory'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("'/healthz'", script, StringComparison.Ordinal);
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

    private static string CloudRunService(string terraform, string name)
    {
        var marker = $"resource \"google_cloud_run_v2_service\" \"{name}\"";
        var start = terraform.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing Cloud Run service {name}.");
        var nextService = terraform.IndexOf("resource \"google_cloud_run_v2_service\"", start + marker.Length, StringComparison.Ordinal);
        var nextIam = terraform.IndexOf("resource \"google_cloud_run_v2_service_iam_member\"", start + marker.Length, StringComparison.Ordinal);
        var end = new[] { nextService, nextIam }.Where(index => index >= 0).DefaultIfEmpty(terraform.Length).Min();
        return terraform[start..end];
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RaceHunter.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
