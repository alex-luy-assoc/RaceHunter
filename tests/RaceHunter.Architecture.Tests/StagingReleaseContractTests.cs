using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RaceHunter.Architecture.Tests;

/// <summary>
/// Staging release strategy: exercise provider-agnostic approval, binding, local
/// qualification, state, and sanitized-evidence boundaries. Google provider
/// behavior, secret values, destruction, and production rollout are deliberately
/// outside this suite.
/// </summary>
public sealed class StagingReleaseContractTests
{
    private static readonly string Root = FindRoot();
    private static readonly string ModulePath = Path.Combine(Root, "deploy", "scripts", "StagingRelease.psm1");

    [Fact]
    public void Terraform_bootstrap_is_separate_from_the_remote_state_application_module()
    {
        var bootstrap = Path.Combine(Root, "deploy", "terraform", "bootstrap");
        var expectedFiles = new[] { "providers.tf", "variables.tf", "main.tf", "outputs.tf" };
        Assert.All(expectedFiles, file => Assert.True(File.Exists(Path.Combine(bootstrap, file)), $"Missing bootstrap/{file}."));

        var applicationProviders = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "providers.tf"));
        var applicationMain = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "main.tf"));
        var bootstrapMain = File.ReadAllText(Path.Combine(bootstrap, "main.tf"));

        Assert.Contains("backend \"gcs\"", applicationProviders, StringComparison.Ordinal);
        Assert.DoesNotContain("google_project_service", applicationMain, StringComparison.Ordinal);
        Assert.DoesNotContain("google_artifact_registry_repository", applicationMain, StringComparison.Ordinal);
        Assert.DoesNotContain("google_storage_bucket", applicationMain, StringComparison.Ordinal);
        Assert.DoesNotContain("google_cloud_run", bootstrapMain, StringComparison.Ordinal);
        Assert.DoesNotContain("google_sql", bootstrapMain, StringComparison.Ordinal);
        Assert.DoesNotContain("google_secret_manager", bootstrapMain, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_bootstrap_protects_remote_state_with_private_versioned_retained_storage()
    {
        var bootstrapMain = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "bootstrap", "main.tf"));

        Assert.Contains("resource \"google_storage_bucket\" \"terraform_state\"", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("uniform_bucket_level_access = true", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("public_access_prevention    = \"enforced\"", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("force_destroy               = false", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("versioning", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("enabled = true", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("retention_policy", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("retention_period = var.state_retention_days * 86400", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("prevent_destroy = true", bootstrapMain, StringComparison.Ordinal);
    }

    [Fact]
    public void Terraform_bootstrap_enables_declared_apis_and_an_immutable_artifact_repository()
    {
        var bootstrapMain = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "bootstrap", "main.tf"));

        Assert.Contains("serviceusage.googleapis.com", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("artifactregistry.googleapis.com", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("run.googleapis.com", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("sqladmin.googleapis.com", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("resource \"google_project_service\" \"required\"", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("disable_on_destroy = false", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("resource \"google_artifact_registry_repository\" \"images\"", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("immutable_tags = true", bootstrapMain, StringComparison.Ordinal);
    }

    [Fact]
    public void Release_plans_three_image_publications_and_binds_deploy_to_the_reviewed_plan_byte_for_byte()
    {
        using var temporary = new TemporaryDirectory();
        var planPath = Path.Combine(temporary.Path, "reviewed.tfplan");
        var tfvarsPath = Path.Combine(temporary.Path, "private.tfvars.json");
        File.WriteAllText(planPath, "reviewed-plan", Encoding.UTF8);
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $publication = New-StagingImagePublicationPlan -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -Repository 'racehunter'
            $requiredApis = @('aiplatform.googleapis.com', 'artifactregistry.googleapis.com', 'billingbudgets.googleapis.com', 'cloudresourcemanager.googleapis.com', 'cloudtrace.googleapis.com', 'iam.googleapis.com', 'iamcredentials.googleapis.com', 'logging.googleapis.com', 'monitoring.googleapis.com', 'pubsub.googleapis.com', 'run.googleapis.com', 'secretmanager.googleapis.com', 'serviceusage.googleapis.com', 'sqladmin.googleapis.com', 'storage.googleapis.com')
            $foundation = [ordered]@{ scopedApis = $requiredApis; billingAccountId = 'ABCDEF-123456-ABCDEF'; stateBucketName = 'racehunter-staging-tfstate'; stateBucketPublicAccessPrevention = $true; stateBucketUniformAccess = $true; stateBucketVersioning = $true; stateBucketRetentionDays = 30; artifactRegistryRepository = 'racehunter'; artifactRegistryLocation = 'us-east1'; artifactRegistryImmutableTags = $true; apiMaxInstances = 2; workerMaxInstances = 1; referenceTargetMaxInstances = 1; monthlyBudgetAmount = 25; budgetCurrency = 'USD'; deletionProtection = $true }
            $images = @{ api = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-api@sha256:' + ('a' * 64)); worker = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-worker@sha256:' + ('b' * 64)); referenceTarget = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-reference-target@sha256:' + ('c' * 64)) }
            $inputs = [ordered]@{ billing_account_id = 'ABCDEF-123456-ABCDEF'; monthly_budget_usd = 25; api_max_instance_count = 2; worker_max_instance_count = 1; reference_target_max_instance_count = 1; deletion_protection = $true; manual_target_secret_ids = @() }
            $planningBinding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $images -TerraformInputs $inputs
            $planning = New-StagingTerraformPlan -Binding $planningBinding -SavedPlanPath '{{Escape(planPath)}}' -TerraformVariablesPath '{{Escape(tfvarsPath)}}' -TerraformDirectory '{{Escape(Path.Combine(Root, "deploy", "terraform"))}}'
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $images -TerraformInputs $inputs -SavedPlanPath '{{Escape(planPath)}}'
            $deployment = New-StagingDeploymentPlan -Binding $binding -SavedPlanPath '{{Escape(planPath)}}' -TerraformDirectory '{{Escape(Path.Combine(Root, "deploy", "terraform"))}}'
            [pscustomobject]@{
                publicationStage = $publication.requiresApprovalStage
                imageCount = @($publication.images).Count
                allDigestBound = @($publication.images | Where-Object { $_.requiredDigestPrefix -notmatch '@sha256:$' }).Count -eq 0
                planStage = $planning.requiresApprovalStage
                planTarget = $planning.planArguments[-1]
                plannedApiImage = $planning.terraformVariables.api_image
                deployStage = $deployment.requiresApprovalStage
                bindingHash = $deployment.bindingHash
                savedPlanHash = $deployment.savedPlanHash
                actualPlanHash = (Get-FileHash -Algorithm SHA256 -LiteralPath '{{Escape(planPath)}}').Hash.ToLowerInvariant()
                regeneratesPlan = [bool]$deployment.regeneratesPlan
                applyTarget = $deployment.applyArguments[-1]
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("PublishImages", json.RootElement.GetProperty("publicationStage").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("imageCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("allDigestBound").GetBoolean());
        Assert.Equal("Plan", json.RootElement.GetProperty("planStage").GetString());
        Assert.Equal($"-out={Path.GetFullPath(planPath)}", json.RootElement.GetProperty("planTarget").GetString());
        Assert.EndsWith("@sha256:" + new string('a', 64), json.RootElement.GetProperty("plannedApiImage").GetString(), StringComparison.Ordinal);
        Assert.Equal("Deploy", json.RootElement.GetProperty("deployStage").GetString());
        Assert.Matches("^[a-f0-9]{64}$", json.RootElement.GetProperty("bindingHash").GetString());
        Assert.Equal(json.RootElement.GetProperty("actualPlanHash").GetString(), json.RootElement.GetProperty("savedPlanHash").GetString());
        Assert.False(json.RootElement.GetProperty("regeneratesPlan").GetBoolean());
        Assert.Equal(Path.GetFullPath(planPath), json.RootElement.GetProperty("applyTarget").GetString());
    }

    [Fact]
    public void Terraform_plan_materialization_rejects_unknown_missing_or_foundation_mismatched_inputs()
    {
        Assert.Contains("*.tfvars.json", File.ReadAllText(Path.Combine(Root, ".gitignore")), StringComparison.Ordinal);
        using var temporary = new TemporaryDirectory();
        var planPath = Path.Combine(temporary.Path, "reviewed.tfplan");
        var tfvarsPath = Path.Combine(temporary.Path, "private.tfvars.json");
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $requiredApis = @('aiplatform.googleapis.com', 'artifactregistry.googleapis.com', 'billingbudgets.googleapis.com', 'cloudresourcemanager.googleapis.com', 'cloudtrace.googleapis.com', 'iam.googleapis.com', 'iamcredentials.googleapis.com', 'logging.googleapis.com', 'monitoring.googleapis.com', 'pubsub.googleapis.com', 'run.googleapis.com', 'secretmanager.googleapis.com', 'serviceusage.googleapis.com', 'sqladmin.googleapis.com', 'storage.googleapis.com')
            $foundation = [ordered]@{ scopedApis = $requiredApis; billingAccountId = 'ABCDEF-123456-ABCDEF'; stateBucketName = 'racehunter-staging-tfstate'; stateBucketPublicAccessPrevention = $true; stateBucketUniformAccess = $true; stateBucketVersioning = $true; stateBucketRetentionDays = 30; artifactRegistryRepository = 'racehunter'; artifactRegistryLocation = 'us-east1'; artifactRegistryImmutableTags = $true; apiMaxInstances = 2; workerMaxInstances = 1; referenceTargetMaxInstances = 1; monthlyBudgetAmount = 25; budgetCurrency = 'USD'; deletionProtection = $true }
            $images = @{ api = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-api@sha256:' + ('a' * 64)); worker = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-worker@sha256:' + ('b' * 64)); referenceTarget = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-reference-target@sha256:' + ('c' * 64)) }
            function New-Inputs { return [ordered]@{ billing_account_id = 'ABCDEF-123456-ABCDEF'; monthly_budget_usd = 25; api_max_instance_count = 2; worker_max_instance_count = 1; reference_target_max_instance_count = 1; deletion_protection = $true; manual_target_secret_ids = @() } }
            function Invoke-Candidate { param($inputs, $path) $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $images -TerraformInputs $inputs; New-StagingTerraformPlan -Binding $binding -SavedPlanPath '{{Escape(planPath)}}' -TerraformVariablesPath $path -TerraformDirectory '{{Escape(Path.Combine(Root, "deploy", "terraform"))}}' }
            $unknown = New-Inputs; $unknown.unreviewed = 'value'
            $missing = New-Inputs; $missing.Remove('billing_account_id')
            $mismatch = New-Inputs; $mismatch.api_max_instance_count = 1
            $optionalBilling = New-Inputs; $optionalBilling.billing_account_id = $null
            $rejected = 0
            foreach ($candidate in @($unknown, $missing, $mismatch, $optionalBilling)) {
                try { Invoke-Candidate $candidate (Join-Path '{{Escape(temporary.Path)}}' "$rejected.tfvars.json") -ErrorAction Stop | Out-Null } catch { $rejected++ }
            }
            $valid = Invoke-Candidate (New-Inputs) '{{Escape(tfvarsPath)}}'
            $materialized = Get-Content -Raw -LiteralPath '{{Escape(tfvarsPath)}}' | ConvertFrom-Json -AsHashtable -Depth 100
            [pscustomobject]@{
                rejected = $rejected
                materialized = Test-Path -LiteralPath '{{Escape(tfvarsPath)}}'
                propertyCount = @($materialized.Keys).Count
                project = $materialized.project_id
                region = $materialized.region
                billing = $materialized.billing_account_id
                apiImage = $materialized.api_image
                deletionProtection = [bool]$materialized.deletion_protection
                semanticHash = $valid.terraformInputHash
                bindingHash = $valid.bindingHash
                fileHash = $valid.terraformVariablesFileHash
                actualFileHash = (Get-FileHash -Algorithm SHA256 -LiteralPath '{{Escape(tfvarsPath)}}').Hash.ToLowerInvariant()
                varFileArgument = @($valid.planArguments | Where-Object { $_ -like '-var-file=*' })[0]
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(4, json.RootElement.GetProperty("rejected").GetInt32());
        Assert.True(json.RootElement.GetProperty("materialized").GetBoolean());
        Assert.Equal(12, json.RootElement.GetProperty("propertyCount").GetInt32());
        Assert.Equal("racehunter-staging", json.RootElement.GetProperty("project").GetString());
        Assert.Equal("us-east1", json.RootElement.GetProperty("region").GetString());
        Assert.Equal("ABCDEF-123456-ABCDEF", json.RootElement.GetProperty("billing").GetString());
        Assert.EndsWith("@sha256:" + new string('a', 64), json.RootElement.GetProperty("apiImage").GetString(), StringComparison.Ordinal);
        Assert.True(json.RootElement.GetProperty("deletionProtection").GetBoolean());
        Assert.Matches("^[a-f0-9]{64}$", json.RootElement.GetProperty("semanticHash").GetString());
        Assert.Matches("^[a-f0-9]{64}$", json.RootElement.GetProperty("bindingHash").GetString());
        Assert.Equal(json.RootElement.GetProperty("semanticHash").GetString(), json.RootElement.GetProperty("fileHash").GetString());
        Assert.Equal(json.RootElement.GetProperty("actualFileHash").GetString(), json.RootElement.GetProperty("fileHash").GetString());
        Assert.Equal($"-var-file={Path.GetFullPath(tfvarsPath)}", json.RootElement.GetProperty("varFileArgument").GetString());
    }

    [Fact]
    public void Bootstrap_declares_a_two_step_local_to_gcs_migration_without_executing_it()
    {
        var bootstrap = Path.Combine(Root, "deploy", "terraform", "bootstrap");
        var provider = File.ReadAllText(Path.Combine(bootstrap, "providers.tf"));
        var applicationProvider = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "providers.tf"));
        var bootstrapMain = File.ReadAllText(Path.Combine(bootstrap, "main.tf"));
        var backendTemplate = Path.Combine(bootstrap, "backend.gcs.tf.example");
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            New-StagingBackendMigrationPlan -StateBucketName 'racehunter-staging-tfstate' -BootstrapDirectory '{{Escape(bootstrap)}}' -ApplicationDirectory '{{Escape(Path.Combine(Root, "deploy", "terraform"))}}' | ConvertTo-Json -Compress -Depth 20
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("local", json.RootElement.GetProperty("initialBackend").GetString());
        Assert.Equal("gcs", json.RootElement.GetProperty("remoteBackend").GetString());
        Assert.False(json.RootElement.GetProperty("executesMigration").GetBoolean());
        Assert.Equal(2, json.RootElement.GetProperty("steps").GetArrayLength());
        Assert.Contains("-migrate-state", json.RootElement.GetProperty("steps")[1].GetProperty("arguments").EnumerateArray().Select(value => value.GetString()));
        Assert.True(File.Exists(backendTemplate));
        Assert.Contains("backend \"gcs\"", File.ReadAllText(backendTemplate), StringComparison.Ordinal);
        Assert.Contains("deploy/terraform/bootstrap/backend.gcs.tf", File.ReadAllText(Path.Combine(Root, ".gitignore")), StringComparison.Ordinal);
        Assert.Contains("required_version = \"~> 1.14.0\"", provider, StringComparison.Ordinal);
        Assert.Contains("required_version = \"~> 1.14.0\"", applicationProvider, StringComparison.Ordinal);
        Assert.Contains("cloudresourcemanager.googleapis.com", bootstrapMain, StringComparison.Ordinal);
        Assert.Contains("iamcredentials.googleapis.com", bootstrapMain, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_migration_requires_explicit_local_backend_template_materialization_first()
    {
        var trackedBootstrap = Path.Combine(Root, "deploy", "terraform", "bootstrap");
        var isolatedBootstrap = Path.Combine(Path.GetTempPath(), $"racehunter-backend-contract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(isolatedBootstrap);
        try
        {
            File.Copy(
                Path.Combine(trackedBootstrap, "backend.gcs.tf.example"),
                Path.Combine(isolatedBootstrap, "backend.gcs.tf.example"));
            var generatedBackend = Path.Combine(isolatedBootstrap, "backend.gcs.tf");
            Assert.False(File.Exists(generatedBackend));
            var result = RunPowerShell($$"""
                Import-Module '{{Escape(ModulePath)}}' -Force
                $descriptor = New-StagingBackendMigrationPlan -StateBucketName 'racehunter-staging-tfstate' -BootstrapDirectory '{{Escape(isolatedBootstrap)}}' -ApplicationDirectory '{{Escape(Path.Combine(Root, "deploy", "terraform"))}}'
                [pscustomobject]@{
                    action = $descriptor.backendMaterialization.action
                    source = $descriptor.backendMaterialization.sourcePath
                    destination = $descriptor.backendMaterialization.destinationPath
                    executesAction = [bool]$descriptor.backendMaterialization.executesAction
                    requiredBefore = $descriptor.backendMaterialization.requiredBeforeStep
                    migrationRequiresDestination = [bool]$descriptor.steps[1].backendMaterializationRequired
                    migrationDestination = $descriptor.steps[1].requiredBackendPath
                    destinationExists = Test-Path -LiteralPath $descriptor.backendMaterialization.destinationPath
                } | ConvertTo-Json -Compress
                """);

            Assert.Equal(0, result.ExitCode);
            using var json = JsonDocument.Parse(result.Output);
            Assert.Equal("MaterializeBackendTemplate", json.RootElement.GetProperty("action").GetString());
            Assert.Equal(Path.Combine(isolatedBootstrap, "backend.gcs.tf.example"), json.RootElement.GetProperty("source").GetString());
            Assert.Equal(Path.GetFullPath(generatedBackend), json.RootElement.GetProperty("destination").GetString());
            Assert.False(json.RootElement.GetProperty("executesAction").GetBoolean());
            Assert.Equal("MigrateBootstrapAndConfigureApplicationState", json.RootElement.GetProperty("requiredBefore").GetString());
            Assert.True(json.RootElement.GetProperty("migrationRequiresDestination").GetBoolean());
            Assert.Equal(Path.GetFullPath(generatedBackend), json.RootElement.GetProperty("migrationDestination").GetString());
            Assert.False(json.RootElement.GetProperty("destinationExists").GetBoolean());
        }
        finally
        {
            Directory.Delete(isolatedBootstrap, recursive: true);
        }
    }

    [Fact]
    public void Foundation_and_plan_reject_fractional_monthly_budget_units()
    {
        var variables = File.ReadAllText(Path.Combine(Root, "deploy", "terraform", "variables.tf"));
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $foundation = [ordered]@{ scopedApis = @('aiplatform.googleapis.com', 'artifactregistry.googleapis.com', 'billingbudgets.googleapis.com', 'cloudresourcemanager.googleapis.com', 'cloudtrace.googleapis.com', 'iam.googleapis.com', 'iamcredentials.googleapis.com', 'logging.googleapis.com', 'monitoring.googleapis.com', 'pubsub.googleapis.com', 'run.googleapis.com', 'secretmanager.googleapis.com', 'serviceusage.googleapis.com', 'sqladmin.googleapis.com', 'storage.googleapis.com'); billingAccountId = 'ABCDEF-123456-ABCDEF'; stateBucketName = 'racehunter-staging-tfstate'; stateBucketPublicAccessPrevention = $true; stateBucketUniformAccess = $true; stateBucketVersioning = $true; stateBucketRetentionDays = 30; artifactRegistryRepository = 'racehunter'; artifactRegistryLocation = 'us-east1'; artifactRegistryImmutableTags = $true; apiMaxInstances = 2; workerMaxInstances = 1; referenceTargetMaxInstances = 1; monthlyBudgetAmount = 25.5; budgetCurrency = 'USD'; deletionProtection = $true }
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation
            $accepted = $true
            try { New-StagingReleaseApproval -Stage Foundation -Binding $binding -ErrorAction Stop | Out-Null } catch { $accepted = $false }
            [pscustomobject]@{ accepted = $accepted } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("accepted").GetBoolean());
        Assert.Contains("floor(var.monthly_budget_usd) == var.monthly_budget_usd", variables, StringComparison.Ordinal);
    }

    [Fact]
    public void External_stages_are_default_denied_and_phase_one_contains_no_cloud_execution()
    {
        var entryPointPath = Path.Combine(Root, "deploy", "scripts", "staging-release.ps1");
        var entryPoint = File.ReadAllText(entryPointPath);

        Assert.Contains("Assert-StagingReleaseApproval", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Preflight", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Foundation", entryPoint, StringComparison.Ordinal);
        Assert.Contains("PublishImages", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Deploy", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Validate", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Smoke", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Demo", entryPoint, StringComparison.Ordinal);
        Assert.Contains("ApiImageDigest", entryPoint, StringComparison.Ordinal);
        Assert.Contains("WorkerImageDigest", entryPoint, StringComparison.Ordinal);
        Assert.Contains("ReferenceTargetImageDigest", entryPoint, StringComparison.Ordinal);
        Assert.Contains("FoundationInputsPath", entryPoint, StringComparison.Ordinal);
        Assert.Contains("TerraformInputsPath", entryPoint, StringComparison.Ordinal);
        Assert.Contains("SavedPlanPath", entryPoint, StringComparison.Ordinal);
        Assert.DoesNotContain("gcloud ", entryPoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("terraform ", entryPoint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker ", entryPoint, StringComparison.OrdinalIgnoreCase);

        var result = RunPowerShell($$"""
            & '{{Escape(entryPointPath)}}' -Stage Preflight -ProjectId racehunter-staging -Region us-east1 -CommitSha 0123456789abcdef0123456789abcdef01234567
            """);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("default denied", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Approval_is_single_purpose_and_non_transitive()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'
            $approval = New-StagingReleaseApproval -Stage 'Validate' -Binding $binding
            [pscustomobject]@{
                validate = Test-StagingReleaseApproval -Stage 'Validate' -Binding $binding -Approval $approval
                foundation = Test-StagingReleaseApproval -Stage 'Foundation' -Binding $binding -Approval $approval
                deploy = Test-StagingReleaseApproval -Stage 'Deploy' -Binding $binding -Approval $approval
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("validate").GetBoolean());
        Assert.False(json.RootElement.GetProperty("foundation").GetBoolean());
        Assert.False(json.RootElement.GetProperty("deploy").GetBoolean());
    }

    [Fact]
    public void Release_completion_is_one_application_layer_gate_with_separate_resumable_smoke_and_demo_evidence()
    {
        var entryPoint = File.ReadAllText(Path.Combine(Root, "deploy", "scripts", "staging-release.ps1"));
        var coordinator = File.ReadAllText(Path.Combine(Root, "deploy", "scripts", "release-completion.ps1"));
        var smoke = File.ReadAllText(Path.Combine(Root, "deploy", "scripts", "smoke.ps1"));
        var browser = File.ReadAllText(Path.Combine(Root, "tests", "RaceHunter.AcceptanceTests", "staging-demo.spec.ts"));

        Assert.Contains("ReleaseCompletion", entryPoint, StringComparison.Ordinal);
        Assert.Contains("ApprovalRequestPath", coordinator, StringComparison.Ordinal);
        Assert.Contains("ApprovedRequestHash", coordinator, StringComparison.Ordinal);
        Assert.Contains("smoke-result.json", coordinator, StringComparison.Ordinal);
        Assert.Contains("demo-result.json", coordinator, StringComparison.Ordinal);
        Assert.Contains("SmokeComplete", coordinator, StringComparison.Ordinal);
        Assert.Contains("DemoComplete", coordinator, StringComparison.Ordinal);
        Assert.Contains("AmbiguousMutation", coordinator, StringComparison.Ordinal);
        Assert.Contains("ProgressPath", smoke, StringComparison.Ordinal);
        Assert.Contains("huntCreateStarted", smoke, StringComparison.Ordinal);
        Assert.Contains("deadlineAtUtc", smoke, StringComparison.Ordinal);
        Assert.Contains("Get-ResponseStatusCode", smoke, StringComparison.Ordinal);
        Assert.Contains("RequiredExistingHuntId", smoke, StringComparison.Ordinal);
        Assert.Contains("RequiredExistingPlanVersion", smoke, StringComparison.Ordinal);
        Assert.Contains("ResetExpiredDeadlineForExistingHunt", smoke, StringComparison.Ordinal);
        Assert.DoesNotContain("$_.Exception.Response.StatusCode.value__", smoke, StringComparison.Ordinal);
        Assert.Contains("RACEHUNTER_DEMO_PROGRESS_PATH", browser, StringComparison.Ordinal);
        Assert.Contains("demoAttemptStarted", browser, StringComparison.Ordinal);
        Assert.Contains("runCreateStarted", browser, StringComparison.Ordinal);
        Assert.Contains("RACEHUNTER_DEMO_DEADLINE_UTC", browser, StringComparison.Ordinal);
        Assert.Contains("Open verified finding", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("gcloud", coordinator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("terraform", coordinator, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docker", coordinator, StringComparison.OrdinalIgnoreCase);

        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'
            $approval = New-StagingReleaseApproval -Stage 'ReleaseCompletion' -Binding $binding
            $recoveryApproval = New-StagingReleaseApproval -Stage 'RecoveryCompletion' -Binding $binding
            [pscustomobject]@{
                releaseCompletion = Test-StagingReleaseApproval -Stage 'ReleaseCompletion' -Binding $binding -Approval $approval
                recoveryCompletion = Test-StagingReleaseApproval -Stage 'RecoveryCompletion' -Binding $binding -Approval $recoveryApproval
                releaseCannotRecover = Test-StagingReleaseApproval -Stage 'RecoveryCompletion' -Binding $binding -Approval $approval
                smoke = Test-StagingReleaseApproval -Stage 'Smoke' -Binding $binding -Approval $approval
                demo = Test-StagingReleaseApproval -Stage 'Demo' -Binding $binding -Approval $approval
                deploy = Test-StagingReleaseApproval -Stage 'Deploy' -Binding $binding -Approval $approval
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("releaseCompletion").GetBoolean());
        Assert.True(json.RootElement.GetProperty("recoveryCompletion").GetBoolean());
        Assert.False(json.RootElement.GetProperty("releaseCannotRecover").GetBoolean());
        Assert.False(json.RootElement.GetProperty("smoke").GetBoolean());
        Assert.False(json.RootElement.GetProperty("demo").GetBoolean());
        Assert.False(json.RootElement.GetProperty("deploy").GetBoolean());
    }

    [Fact]
    public void Approval_record_requires_exact_properties_types_boolean_true_and_a_utc_timestamp()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'
            $valid = New-StagingReleaseApproval -Stage 'Validate' -Binding $binding
            function Copy-Approval { param($value) return ($value | ConvertTo-Json -Compress | ConvertFrom-Json -DateKind String) }
            $stringFalse = Copy-Approval $valid; $stringFalse.valid = 'false'
            $numericTruthy = Copy-Approval $valid; $numericTruthy.valid = 1
            $missing = Copy-Approval $valid; $missing.PSObject.Properties.Remove('bindingHash')
            $extra = Copy-Approval $valid; $extra | Add-Member -NotePropertyName 'unexpected' -NotePropertyValue 'value'
            $malformedTime = Copy-Approval $valid; $malformedTime.approvedAtUtc = 'not-a-time'
            $wrongType = Copy-Approval $valid; $wrongType.projectId = 42
            $rejected = 0
            foreach ($candidate in @($stringFalse, $numericTruthy, $missing, $extra, $malformedTime, $wrongType)) {
                if (-not (Test-StagingReleaseApproval -Stage 'Validate' -Binding $binding -Approval $candidate)) { $rejected++ }
            }
            [pscustomobject]@{
                validAccepted = Test-StagingReleaseApproval -Stage 'Validate' -Binding $binding -Approval $valid
                rejected = $rejected
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("validAccepted").GetBoolean());
        Assert.Equal(6, json.RootElement.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public void Approval_is_bound_to_exact_commit_project_region_and_release_identity()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests @{ api = 'registry/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            $approval = New-StagingReleaseApproval -Stage 'Validate' -Binding $binding
            $differentCommit = New-StagingReleaseBinding -CommitSha '1123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests @{ api = 'registry/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            $differentProject = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'other-staging' -Region 'us-east1' -ImageDigests @{ api = 'registry/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            $differentRegion = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-central1' -ImageDigests @{ api = 'registry/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            [pscustomobject]@{
                exact = Test-StagingReleaseApproval -Stage 'Validate' -Binding $binding -Approval $approval
                commit = Test-StagingReleaseApproval -Stage 'Validate' -Binding $differentCommit -Approval $approval
                project = Test-StagingReleaseApproval -Stage 'Validate' -Binding $differentProject -Approval $approval
                region = Test-StagingReleaseApproval -Stage 'Validate' -Binding $differentRegion -Approval $approval
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("exact").GetBoolean());
        Assert.False(json.RootElement.GetProperty("commit").GetBoolean());
        Assert.False(json.RootElement.GetProperty("project").GetBoolean());
        Assert.False(json.RootElement.GetProperty("region").GetBoolean());
    }

    [Fact]
    public void Manifest_allows_only_ordered_transitions_and_resumes_from_durable_state()
    {
        using var temporary = new TemporaryDirectory();
        var statePath = Path.Combine(temporary.Path, "release-state.json");
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'
            $state = Initialize-StagingReleaseState -Path '{{Escape(statePath)}}' -Binding $binding
            $state = Set-StagingReleaseStage -Path '{{Escape(statePath)}}' -Stage 'LocalQualified'
            $resumed = Get-StagingReleaseState -Path '{{Escape(statePath)}}'
            $skipRejected = $false
            try { Set-StagingReleaseStage -Path '{{Escape(statePath)}}' -Stage 'PlanReviewed' -ErrorAction Stop | Out-Null } catch { $skipRejected = $true }
            [pscustomobject]@{
                currentStage = $resumed.currentStage
                transitionCount = @($resumed.transitions).Count
                skipRejected = $skipRejected
                durable = Test-Path -LiteralPath '{{Escape(statePath)}}'
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal("LocalQualified", json.RootElement.GetProperty("currentStage").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("transitionCount").GetInt32());
        Assert.True(json.RootElement.GetProperty("skipRejected").GetBoolean());
        Assert.True(json.RootElement.GetProperty("durable").GetBoolean());
    }

    [Fact]
    public void Binding_hash_is_canonical_and_detects_terraform_input_or_saved_plan_tampering()
    {
        using var temporary = new TemporaryDirectory();
        var planPath = Path.Combine(temporary.Path, "release.tfplan");
        File.WriteAllText(planPath, "reviewed-plan-v1", Encoding.UTF8);

        var first = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $one = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -TerraformInputs @{ worker_max_instances = 1; api_min_instances = 0 } -SavedPlanPath '{{Escape(planPath)}}'
            $two = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -TerraformInputs @{ api_min_instances = 0; worker_max_instances = 1 } -SavedPlanPath '{{Escape(planPath)}}'
            $changedInput = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -TerraformInputs @{ api_min_instances = 0; worker_max_instances = 2 } -SavedPlanPath '{{Escape(planPath)}}'
            [pscustomobject]@{ one = $one.bindingHash; two = $two.bindingHash; changedInput = $changedInput.bindingHash; plan = $one.savedPlanHash } | ConvertTo-Json -Compress
            """);
        Assert.Equal(0, first.ExitCode);

        File.WriteAllText(planPath, "tampered-plan-v2", Encoding.UTF8);
        var second = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -TerraformInputs @{ worker_max_instances = 1; api_min_instances = 0 } -SavedPlanPath '{{Escape(planPath)}}' | ConvertTo-Json -Compress
            """);
        Assert.Equal(0, second.ExitCode);

        using var original = JsonDocument.Parse(first.Output);
        using var tampered = JsonDocument.Parse(second.Output);
        Assert.Equal(original.RootElement.GetProperty("one").GetString(), original.RootElement.GetProperty("two").GetString());
        Assert.NotEqual(original.RootElement.GetProperty("one").GetString(), original.RootElement.GetProperty("changedInput").GetString());
        Assert.NotEqual(original.RootElement.GetProperty("plan").GetString(), tampered.RootElement.GetProperty("savedPlanHash").GetString());
        Assert.NotEqual(original.RootElement.GetProperty("one").GetString(), tampered.RootElement.GetProperty("bindingHash").GetString());
    }

    [Fact]
    public void Deployment_approval_requires_three_images_nonempty_inputs_and_a_verified_saved_plan()
    {
        using var temporary = new TemporaryDirectory();
        var planPath = Path.Combine(temporary.Path, "reviewed.tfplan");
        File.WriteAllText(planPath, "reviewed-plan", Encoding.UTF8);
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $requiredApis = @('aiplatform.googleapis.com', 'artifactregistry.googleapis.com', 'billingbudgets.googleapis.com', 'cloudresourcemanager.googleapis.com', 'cloudtrace.googleapis.com', 'iam.googleapis.com', 'iamcredentials.googleapis.com', 'logging.googleapis.com', 'monitoring.googleapis.com', 'pubsub.googleapis.com', 'run.googleapis.com', 'secretmanager.googleapis.com', 'serviceusage.googleapis.com', 'sqladmin.googleapis.com', 'storage.googleapis.com')
            $foundation = [ordered]@{ scopedApis = $requiredApis; billingAccountId = 'ABCDEF-123456-ABCDEF'; stateBucketName = 'racehunter-staging-tfstate'; stateBucketPublicAccessPrevention = $true; stateBucketUniformAccess = $true; stateBucketVersioning = $true; stateBucketRetentionDays = 30; artifactRegistryRepository = 'racehunter'; artifactRegistryLocation = 'us-east1'; artifactRegistryImmutableTags = $true; apiMaxInstances = 2; workerMaxInstances = 1; referenceTargetMaxInstances = 1; monthlyBudgetAmount = 25; budgetCurrency = 'USD'; deletionProtection = $true }
            $inputs = [ordered]@{ billing_account_id = 'ABCDEF-123456-ABCDEF'; monthly_budget_usd = 25; api_max_instance_count = 2; worker_max_instance_count = 1; reference_target_max_instance_count = 1; deletion_protection = $true; manual_target_secret_ids = @() }
            $api = 'us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-api@sha256:' + ('a' * 64)
            $worker = 'us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-worker@sha256:' + ('b' * 64)
            $target = 'us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-reference-target@sha256:' + ('c' * 64)
            $completeImages = @{ api = $api; worker = $worker; referenceTarget = $target }
            $wrongRepositoryImages = @{ api = ('us-east1-docker.pkg.dev/racehunter-staging/other/racehunter-api@sha256:' + ('a' * 64)); worker = ('us-east1-docker.pkg.dev/racehunter-staging/other/racehunter-worker@sha256:' + ('b' * 64)); referenceTarget = ('us-east1-docker.pkg.dev/racehunter-staging/other/racehunter-reference-target@sha256:' + ('c' * 64)) }
            $cases = @(
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation),
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests @{ api = $api; worker = $worker }),
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $completeImages -SavedPlanPath '{{Escape(planPath)}}'),
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $completeImages -TerraformInputs $inputs),
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $wrongRepositoryImages -TerraformInputs $inputs -SavedPlanPath '{{Escape(planPath)}}')
            )
            $tamperedHash = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $completeImages -TerraformInputs $inputs -SavedPlanPath '{{Escape(planPath)}}'
            $tamperedHash.terraformInputHash = ''
            $cases += $tamperedHash
            $rejected = 0
            foreach ($binding in $cases) {
                try { New-StagingReleaseApproval -Stage 'Deploy' -Binding $binding -ErrorAction Stop | Out-Null } catch { $rejected++ }
            }
            $complete = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $completeImages -TerraformInputs $inputs -SavedPlanPath '{{Escape(planPath)}}'
            $approval = New-StagingReleaseApproval -Stage 'Deploy' -Binding $complete
            [pscustomobject]@{ rejected = $rejected; completeAccepted = Test-StagingReleaseApproval -Stage 'Deploy' -Binding $complete -Approval $approval } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(6, json.RootElement.GetProperty("rejected").GetInt32());
        Assert.True(json.RootElement.GetProperty("completeAccepted").GetBoolean());
    }

    [Fact]
    public void Billable_approvals_require_complete_protected_foundation_identity_and_ceilings()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $complete = [ordered]@{
                scopedApis = @('aiplatform.googleapis.com', 'artifactregistry.googleapis.com', 'billingbudgets.googleapis.com', 'cloudresourcemanager.googleapis.com', 'cloudtrace.googleapis.com', 'iam.googleapis.com', 'iamcredentials.googleapis.com', 'logging.googleapis.com', 'monitoring.googleapis.com', 'pubsub.googleapis.com', 'run.googleapis.com', 'secretmanager.googleapis.com', 'serviceusage.googleapis.com', 'sqladmin.googleapis.com', 'storage.googleapis.com')
                billingAccountId = 'ABCDEF-123456-ABCDEF'
                stateBucketName = 'racehunter-staging-tfstate'
                stateBucketPublicAccessPrevention = $true
                stateBucketUniformAccess = $true
                stateBucketVersioning = $true
                stateBucketRetentionDays = 30
                artifactRegistryRepository = 'racehunter'
                artifactRegistryLocation = 'us-east1'
                artifactRegistryImmutableTags = $true
                apiMaxInstances = 2
                workerMaxInstances = 1
                referenceTargetMaxInstances = 1
                monthlyBudgetAmount = 25
                budgetCurrency = 'USD'
                deletionProtection = $true
            }
            function New-Binding { param($foundation) New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation }
            $empty = New-Binding @{}
            $missingApis = [ordered]@{} + $complete; $missingApis.scopedApis = @()
            $unprotectedBucket = [ordered]@{} + $complete; $unprotectedBucket.stateBucketPublicAccessPrevention = $false
            $nonUniformBucket = [ordered]@{} + $complete; $nonUniformBucket.stateBucketUniformAccess = $false
            $missingRetention = [ordered]@{} + $complete; $missingRetention.Remove('stateBucketRetentionDays')
            $mutableRegistry = [ordered]@{} + $complete; $mutableRegistry.artifactRegistryImmutableTags = $false
            $wrongRegistryRegion = [ordered]@{} + $complete; $wrongRegistryRegion.artifactRegistryLocation = 'us-central1'
            $missingCeiling = [ordered]@{} + $complete; $missingCeiling.Remove('monthlyBudgetAmount')
            $incomplete = @($empty, (New-Binding $missingApis), (New-Binding $unprotectedBucket), (New-Binding $nonUniformBucket), (New-Binding $missingRetention), (New-Binding $mutableRegistry), (New-Binding $wrongRegistryRegion), (New-Binding $missingCeiling))
            $rejected = 0
            foreach ($binding in $incomplete) {
                foreach ($stage in @('Foundation', 'PublishImages')) {
                    try { New-StagingReleaseApproval -Stage $stage -Binding $binding -ErrorAction Stop | Out-Null } catch { $rejected++ }
                }
            }
            $completeBinding = New-Binding $complete
            $foundationApproval = New-StagingReleaseApproval -Stage 'Foundation' -Binding $completeBinding
            $publishApproval = New-StagingReleaseApproval -Stage 'PublishImages' -Binding $completeBinding
            [pscustomobject]@{
                rejected = $rejected
                foundationAccepted = Test-StagingReleaseApproval -Stage 'Foundation' -Binding $completeBinding -Approval $foundationApproval
                publishAccepted = Test-StagingReleaseApproval -Stage 'PublishImages' -Binding $completeBinding -Approval $publishApproval
                foundationHash = $completeBinding.foundationInputHash
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(16, json.RootElement.GetProperty("rejected").GetInt32());
        Assert.True(json.RootElement.GetProperty("foundationAccepted").GetBoolean());
        Assert.True(json.RootElement.GetProperty("publishAccepted").GetBoolean());
        Assert.Matches("^[a-f0-9]{64}$", json.RootElement.GetProperty("foundationHash").GetString());
    }

    [Fact]
    public void Binding_drift_invalidates_current_and_downstream_approvals_but_preserves_prior_evidence()
    {
        using var temporary = new TemporaryDirectory();
        var statePath = Path.Combine(temporary.Path, "release-state.json");
        var planPath = Path.Combine(temporary.Path, "reviewed.tfplan");
        File.WriteAllText(planPath, "reviewed-plan", Encoding.UTF8);
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $images = @{ api = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-api@sha256:' + ('a' * 64)); worker = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-worker@sha256:' + ('b' * 64)); referenceTarget = ('us-east1-docker.pkg.dev/racehunter-staging/racehunter/racehunter-reference-target@sha256:' + ('c' * 64)) }
            $foundation = [ordered]@{ scopedApis = @('aiplatform.googleapis.com', 'artifactregistry.googleapis.com', 'billingbudgets.googleapis.com', 'cloudresourcemanager.googleapis.com', 'cloudtrace.googleapis.com', 'iam.googleapis.com', 'iamcredentials.googleapis.com', 'logging.googleapis.com', 'monitoring.googleapis.com', 'pubsub.googleapis.com', 'run.googleapis.com', 'secretmanager.googleapis.com', 'serviceusage.googleapis.com', 'sqladmin.googleapis.com', 'storage.googleapis.com'); billingAccountId = 'ABCDEF-123456-ABCDEF'; stateBucketName = 'racehunter-staging-tfstate'; stateBucketPublicAccessPrevention = $true; stateBucketUniformAccess = $true; stateBucketVersioning = $true; stateBucketRetentionDays = 30; artifactRegistryRepository = 'racehunter'; artifactRegistryLocation = 'us-east1'; artifactRegistryImmutableTags = $true; apiMaxInstances = 2; workerMaxInstances = 1; referenceTargetMaxInstances = 1; monthlyBudgetAmount = 25; budgetCurrency = 'USD'; deletionProtection = $true }
            $inputs = [ordered]@{ billing_account_id = 'ABCDEF-123456-ABCDEF'; monthly_budget_usd = 25; api_max_instance_count = 2; worker_max_instance_count = 1; reference_target_max_instance_count = 1; deletion_protection = $true; manual_target_secret_ids = @() }
            $old = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $images -TerraformInputs $inputs -SavedPlanPath '{{Escape(planPath)}}'
            $runner = {
                param($gate)
                $output = if ($gate.name -eq 'release-candidate-commit') { $old.commitSha } else { '' }
                $exitCode = if ($gate.name -eq 'repository-secret-scan') { 1 } else { 0 }
                return [pscustomobject]@{ exitCode = $exitCode; standardOutput = $output }
            }
            $qualification = Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $old -CommandRunner $runner -ObservedAtUtc '2026-08-19T18:30:00Z'
            $state = Save-StagingLocalQualification -Path '{{Escape(statePath)}}' -Binding $old -Qualification $qualification
            $qualificationEvidenceCount = @($state.evidence).Count
            foreach ($stage in @('Preflight', 'Foundation', 'Deploy')) {
                $approval = if ($stage -eq 'Preflight') {
                    New-StagingReleaseApproval -Stage $stage -Binding $old -PreflightRequest $state.preflightRequest
                } else {
                    New-StagingReleaseApproval -Stage $stage -Binding $old
                }
                Add-StagingReleaseApproval -Path '{{Escape(statePath)}}' -Approval $approval | Out-Null
            }
            Add-StagingReleaseEvidence -Path '{{Escape(statePath)}}' -Evidence @{ id = 'preflight-1'; classification = 'cloud-read-only' } | Out-Null
            $changedInputs = [ordered]@{} + $inputs; $changedInputs.manual_target_secret_ids = @('approved-target-auth')
            $changed = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $images -TerraformInputs $changedInputs -SavedPlanPath '{{Escape(planPath)}}'
            Update-StagingReleaseBinding -Path '{{Escape(statePath)}}' -Binding $changed -ChangedAtStage 'Foundation' | Out-Null
            $resumed = Get-StagingReleaseState -Path '{{Escape(statePath)}}'
            $resumeRejected = $false
            try { Set-StagingReleaseStage -Path '{{Escape(statePath)}}' -Stage 'LocalQualified' -ErrorAction Stop | Out-Null } catch { $resumeRejected = $true }
            [pscustomobject]@{
                preflightValid = [bool]$resumed.approvals.Preflight.valid
                foundationValid = [bool]$resumed.approvals.Foundation.valid
                deployValid = [bool]$resumed.approvals.Deploy.valid
                qualificationEvidenceCount = $qualificationEvidenceCount
                evidenceCount = @($resumed.evidence).Count
                reason = $resumed.approvals.Deploy.invalidationReason
                resumeRejected = $resumeRejected
            } | ConvertTo-Json -Compress
            """);

        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("preflightValid").GetBoolean());
        Assert.False(json.RootElement.GetProperty("foundationValid").GetBoolean());
        Assert.False(json.RootElement.GetProperty("deployValid").GetBoolean());
        Assert.Equal(json.RootElement.GetProperty("qualificationEvidenceCount").GetInt32() + 1, json.RootElement.GetProperty("evidenceCount").GetInt32());
        Assert.Contains("binding drift", json.RootElement.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(json.RootElement.GetProperty("resumeRejected").GetBoolean());
    }

    [Fact]
    public void Ambiguous_failure_blocks_resume_until_verified_read_only_reconciliation()
    {
        using var temporary = new TemporaryDirectory();
        var statePath = Path.Combine(temporary.Path, "release-state.json");
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'
            Initialize-StagingReleaseState -Path '{{Escape(statePath)}}' -Binding $binding | Out-Null
            Set-StagingReleaseFailure -Path '{{Escape(statePath)}}' -Stage 'Preflight' -Reason 'ambiguous provider response' -AmbiguousMutation | Out-Null
            $forwardRejected = $false
            try { Set-StagingReleaseStage -Path '{{Escape(statePath)}}' -Stage 'LocalQualified' -ErrorAction Stop | Out-Null } catch { $forwardRejected = $true }
            $unverifiedRejected = $false
            try { Complete-StagingReleaseReconciliation -Path '{{Escape(statePath)}}' -ErrorAction Stop | Out-Null } catch { $unverifiedRejected = $true }
            Complete-StagingReleaseReconciliation -Path '{{Escape(statePath)}}' -VerifiedReadOnlyInspection | Out-Null
            Set-StagingReleaseStage -Path '{{Escape(statePath)}}' -Stage 'LocalQualified' | Out-Null
            $resumed = Get-StagingReleaseState -Path '{{Escape(statePath)}}'
            [pscustomobject]@{
                forwardRejected = $forwardRejected
                unverifiedRejected = $unverifiedRejected
                currentStage = $resumed.currentStage
                requiresInspection = [bool]$resumed.recovery.requiresReadOnlyInspection
                ambiguous = [bool]$resumed.recovery.ambiguousMutation
                failureCount = @($resumed.failures).Count
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("forwardRejected").GetBoolean());
        Assert.True(json.RootElement.GetProperty("unverifiedRejected").GetBoolean());
        Assert.Equal("LocalQualified", json.RootElement.GetProperty("currentStage").GetString());
        Assert.False(json.RootElement.GetProperty("requiresInspection").GetBoolean());
        Assert.False(json.RootElement.GetProperty("ambiguous").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("failureCount").GetInt32());
    }

    [Fact]
    public void Raw_release_state_is_gitignored_and_schema_permits_only_sanitized_evidence()
    {
        var gitignore = File.ReadAllText(Path.Combine(Root, ".gitignore"));
        Assert.Contains("memory-bank/.local/", gitignore, StringComparison.Ordinal);

        var schemaPath = Path.Combine(Root, "deploy", "scripts", "staging-evidence.schema.json");
        using var schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        var root = schema.RootElement;
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root.GetProperty("$schema").GetString());
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        var required = root.GetProperty("required").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Contains("schemaVersion", required);
        Assert.Contains("records", required);
        var classifications = root.GetProperty("$defs").GetProperty("evidenceRecord").GetProperty("properties").GetProperty("classification").GetProperty("enum").EnumerateArray().Select(value => value.GetString()).ToArray();
        Assert.Equal(new[] { "local", "local-emulated", "cloud-read-only", "deployed-staging", "live-gemini", "timed-staging-demo" }, classifications);
        var serialized = root.GetRawText();
        Assert.DoesNotContain("rawPayload", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secretValue", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evidence_promotion_redacts_safe_identifiers_and_rejects_secret_bearing_material()
    {
        using var temporary = new TemporaryDirectory();
        var outputPath = Path.Combine(temporary.Path, "staging-evidence.json");
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $unsafeRejected = $false
            try {
                Protect-StagingEvidence -Evidence @{ schemaVersion = '1.0'; classification = 'cloud-read-only'; observedAtUtc = '2026-08-19T12:00:00Z'; environment = 'staging'; method = 'read-only preflight'; expected = @{ summary = 'identity inspected' }; observed = @{ summary = 'Authorization: Bearer abc.def.ghi'; status = 'passed' }; commitSha = '0123456789abcdef0123456789abcdef01234567'; imageDigests = @{}; identifiers = @{ project = 'racehunter-staging' }; artifactReference = 'memory-bank/.local/staging-release/preflight.json' } -ErrorAction Stop | Out-Null
            } catch { $unsafeRejected = $true }
            $safe = Protect-StagingEvidence -Evidence @{ schemaVersion = '1.0'; classification = 'cloud-read-only'; observedAtUtc = '2026-08-19T12:00:00Z'; environment = 'staging'; method = 'read-only preflight'; expected = @{ summary = 'identity inspected' }; observed = @{ summary = 'principal [REDACTED]; project available'; status = 'passed' }; commitSha = '0123456789abcdef0123456789abcdef01234567'; imageDigests = @{}; identifiers = @{ project = 'racehunter-staging'; revision = 'racehunter-api-00001-abc' }; artifactReference = 'memory-bank/.local/staging-release/preflight.json' }
            Publish-StagingEvidence -Path '{{Escape(outputPath)}}' -Evidence $safe
            [pscustomobject]@{
                unsafeRejected = $unsafeRejected
                promoted = Test-Path -LiteralPath '{{Escape(outputPath)}}'
                content = Get-Content -Raw -LiteralPath '{{Escape(outputPath)}}'
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("unsafeRejected").GetBoolean());
        Assert.True(json.RootElement.GetProperty("promoted").GetBoolean());
        var content = json.RootElement.GetProperty("content").GetString();
        Assert.Contains("cloud-read-only", content, StringComparison.Ordinal);
        Assert.Contains("racehunter-api-00001-abc", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Bearer", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evidence_schema_rejects_unknown_invalid_or_empty_records_before_atomic_promotion()
    {
        using var temporary = new TemporaryDirectory();
        var outputPath = Path.Combine(temporary.Path, "staging-evidence.json");
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            function New-Record {
                return [ordered]@{ schemaVersion = '1.0'; classification = 'cloud-read-only'; observedAtUtc = '2026-08-19T12:00:00Z'; environment = 'staging'; method = 'read-only preflight'; expected = @{ summary = 'identity inspected' }; observed = @{ summary = 'project available'; status = 'passed' }; commitSha = '0123456789abcdef0123456789abcdef01234567'; imageDigests = @{}; identifiers = @{ project = 'racehunter-staging' }; artifactReference = 'memory-bank/.local/staging-release/preflight.json' }
            }
            $unknown = New-Record; $unknown.rawPayload = @{ status = 'ok' }
            $timestamp = New-Record; $timestamp.observedAtUtc = '2026-99-99T12:00:00Z'
            $digest = New-Record; $digest.imageDigests = @{ api = 'registry/api:latest' }
            $identifier = New-Record; $identifier.identifiers = @{ project = 42 }
            $empty = New-Record; $empty.method = ''
            $rejected = 0
            $unexpectedPromotion = 0
            foreach ($record in @($unknown, $timestamp, $digest, $identifier, $empty)) {
                if (Test-Path -LiteralPath '{{Escape(outputPath)}}') { Remove-Item -LiteralPath '{{Escape(outputPath)}}' -Force }
                try { Publish-StagingEvidence -Path '{{Escape(outputPath)}}' -Evidence $record -ErrorAction Stop } catch { $rejected++ }
                if (Test-Path -LiteralPath '{{Escape(outputPath)}}') { $unexpectedPromotion++ }
            }
            [pscustomobject]@{ rejected = $rejected; unexpectedPromotion = $unexpectedPromotion } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(5, json.RootElement.GetProperty("rejected").GetInt32());
        Assert.Equal(0, json.RootElement.GetProperty("unexpectedPromotion").GetInt32());
    }

    [Fact]
    public void Evidence_scanner_blocks_raw_json_tokens_api_keys_signed_urls_connections_and_cookies()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $unsafeSummaries = @(
                '{"status":"ok"}',
                '{"access_token":"obviously-fake"}',
                'eyJhbGciOiJub25lIn0.eyJzdWIiOiJmYWtlIn0.fake-signature',
                ('AIza' + ('A' * 35)),
                'https://storage.invalid/object?X-Goog-Signature=obviously-fake',
                'Host=db.invalid;Username=fake;Password=obviously-fake',
                'Cookie: session=obviously-fake'
            )
            $rejected = 0
            foreach ($summary in $unsafeSummaries) {
                $record = @{ schemaVersion = '1.0'; classification = 'cloud-read-only'; observedAtUtc = '2026-08-19T12:00:00Z'; environment = 'staging'; method = 'read-only preflight'; expected = @{ summary = 'identity inspected' }; observed = @{ summary = $summary; status = 'passed' }; commitSha = '0123456789abcdef0123456789abcdef01234567'; imageDigests = @{}; identifiers = @{ project = 'racehunter-staging' }; artifactReference = 'memory-bank/.local/staging-release/preflight.json' }
                try { Protect-StagingEvidence -Evidence $record -ErrorAction Stop | Out-Null } catch { $rejected++ }
            }
            [pscustomobject]@{ rejected = $rejected; total = $unsafeSummaries.Count } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(json.RootElement.GetProperty("total").GetInt32(), json.RootElement.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public void Local_qualification_orchestrates_every_repository_gate_in_a_deterministic_order()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $commit = '0123456789abcdef0123456789abcdef01234567'
            $binding = New-StagingReleaseBinding -CommitSha $commit -ProjectId 'racehunter-staging' -Region 'us-east1'
            $invocations = [Collections.Generic.List[string]]::new()
            $runner = {
                param($gate)
                $invocations.Add([string]$gate.name)
                $output = if ($gate.name -eq 'release-candidate-commit') { $commit } else { '' }
                $exitCode = if ($gate.name -eq 'repository-secret-scan') { 1 } else { 0 }
                return [pscustomobject]@{ exitCode = $exitCode; standardOutput = $output }
            }
            $qualification = Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $binding -CommandRunner $runner -ObservedAtUtc '2026-08-19T18:30:00Z'
            [pscustomobject]@{
                names = @($invocations)
                passed = [bool]$qualification.passed
                resultCount = @($qualification.gates).Count
                requestStage = $qualification.preflightRequest.stage
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var names = json.RootElement.GetProperty("names").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(new[]
        {
            "clean-checkout", "release-candidate-commit", "dotnet-restore", "dotnet-tests", "web-install", "web-tests", "web-lint", "web-build",
            "acceptance-install", "fresh-volume-real-playwright", "api-image-build", "worker-image-build", "reference-target-image-build", "compose-config",
            "nuget-dependency-audit", "web-dependency-audit", "acceptance-dependency-audit", "repository-secret-scan", "terraform-format",
            "terraform-bootstrap-init", "terraform-bootstrap-validate", "terraform-application-init", "terraform-application-validate"
        }, names);
        Assert.True(json.RootElement.GetProperty("passed").GetBoolean());
        Assert.Equal(names.Length, json.RootElement.GetProperty("resultCount").GetInt32());
        Assert.Equal("Preflight", json.RootElement.GetProperty("requestStage").GetString());
    }

    [Fact]
    public void Local_qualification_records_only_local_evidence_and_resumes_without_duplicate_promotion()
    {
        using var temporary = new TemporaryDirectory();
        var statePath = Path.Combine(temporary.Path, "release-state.json");
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $commit = '0123456789abcdef0123456789abcdef01234567'
            $binding = New-StagingReleaseBinding -CommitSha $commit -ProjectId 'racehunter-staging' -Region 'us-east1'
            $runner = {
                param($gate)
                $output = if ($gate.name -eq 'release-candidate-commit') { $commit } else { '' }
                $exitCode = if ($gate.name -eq 'repository-secret-scan') { 1 } else { 0 }
                return [pscustomobject]@{ exitCode = $exitCode; standardOutput = $output }
            }
            $qualification = Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $binding -CommandRunner $runner -ObservedAtUtc '2026-08-19T18:30:00Z'
            Save-StagingLocalQualification -Path '{{Escape(statePath)}}' -Binding $binding -Qualification $qualification | Out-Null
            $resumed = Save-StagingLocalQualification -Path '{{Escape(statePath)}}' -Binding $binding -Qualification $qualification
            [pscustomobject]@{
                classifications = @($resumed.evidence | ForEach-Object { $_.classification } | Sort-Object -Unique)
                evidenceCount = @($resumed.evidence).Count
                gateCount = @($qualification.gates).Count
                currentStage = $resumed.currentStage
                transitionCount = @($resumed.transitions).Count
                requestHash = $resumed.preflightRequest.qualificationHash
                qualificationHash = $resumed.localQualification.qualificationHash
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var classifications = json.RootElement.GetProperty("classifications").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.Equal(new[] { "local", "local-emulated" }, classifications);
        Assert.Equal(json.RootElement.GetProperty("gateCount").GetInt32(), json.RootElement.GetProperty("evidenceCount").GetInt32());
        Assert.Equal("LocalQualified", json.RootElement.GetProperty("currentStage").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("transitionCount").GetInt32());
        Assert.Equal(json.RootElement.GetProperty("qualificationHash").GetString(), json.RootElement.GetProperty("requestHash").GetString());
    }

    [Fact]
    public void Local_qualification_rejects_a_dirty_checkout_or_commit_mismatch_before_expensive_gates()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $commit = '0123456789abcdef0123456789abcdef01234567'
            $binding = New-StagingReleaseBinding -CommitSha $commit -ProjectId 'racehunter-staging' -Region 'us-east1'
            $dirtyCalls = [Collections.Generic.List[string]]::new()
            $dirtyRunner = {
                param($gate)
                $dirtyCalls.Add([string]$gate.name)
                return [pscustomobject]@{ exitCode = 0; standardOutput = ' M tracked-file.txt' }
            }
            $dirtyRejected = $false
            try { Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $binding -CommandRunner $dirtyRunner -ErrorAction Stop | Out-Null } catch { $dirtyRejected = $true }

            $driftCalls = [Collections.Generic.List[string]]::new()
            $driftRunner = {
                param($gate)
                $driftCalls.Add([string]$gate.name)
                $output = if ($gate.name -eq 'release-candidate-commit') { '1123456789abcdef0123456789abcdef01234567' } else { '' }
                return [pscustomobject]@{ exitCode = 0; standardOutput = $output }
            }
            $driftRejected = $false
            try { Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $binding -CommandRunner $driftRunner -ErrorAction Stop | Out-Null } catch { $driftRejected = $true }
            [pscustomobject]@{
                dirtyRejected = $dirtyRejected
                dirtyCalls = @($dirtyCalls)
                driftRejected = $driftRejected
                driftCalls = @($driftCalls)
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("dirtyRejected").GetBoolean());
        Assert.Equal(new[] { "clean-checkout" }, json.RootElement.GetProperty("dirtyCalls").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.True(json.RootElement.GetProperty("driftRejected").GetBoolean());
        Assert.Equal(new[] { "clean-checkout", "release-candidate-commit" }, json.RootElement.GetProperty("driftCalls").EnumerateArray().Select(item => item.GetString()).ToArray());
    }

    [Fact]
    public void Qualification_is_credential_free_and_preflight_request_is_exact_default_deny_and_drift_sensitive()
    {
        var entryPoint = File.ReadAllText(Path.Combine(Root, "deploy", "scripts", "staging-release.ps1"));
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $commit = '0123456789abcdef0123456789abcdef01234567'
            $binding = New-StagingReleaseBinding -CommitSha $commit -ProjectId 'racehunter-staging' -Region 'us-east1'
            $unsafeCommandObserved = $false
            $runner = {
                param($gate)
                $rendered = "$($gate.filePath) $($gate.argumentList -join ' ')"
                if ($rendered -match '(?i)gcloud|auth\s|access-token|google_application_credentials') { $unsafeCommandObserved = $true }
                $output = if ($gate.name -eq 'release-candidate-commit') { $commit } else { '' }
                $exitCode = if ($gate.name -eq 'repository-secret-scan') { 1 } else { 0 }
                return [pscustomobject]@{ exitCode = $exitCode; standardOutput = $output }
            }
            $qualification = Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $binding -CommandRunner $runner -ObservedAtUtc '2026-08-19T18:30:00Z'
            $request = $qualification.preflightRequest
            $drifted = New-StagingReleaseBinding -CommitSha '1123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'
            [pscustomobject]@{
                unsafeCommandObserved = $unsafeCommandObserved
                exact = Test-StagingPreflightRequest -Request $request -Binding $binding -Qualification $qualification
                drifted = Test-StagingPreflightRequest -Request $request -Binding $drifted -Qualification $qualification
                stage = $request.stage
                commitSha = $request.commitSha
                projectId = $request.projectId
                region = $request.region
                approvalRequired = [bool]$request.approvalRequired
                authorizesMutation = [bool]$request.authorizesMutation
                allowedChecks = @($request.allowedChecks)
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.False(json.RootElement.GetProperty("unsafeCommandObserved").GetBoolean());
        Assert.True(json.RootElement.GetProperty("exact").GetBoolean());
        Assert.False(json.RootElement.GetProperty("drifted").GetBoolean());
        Assert.Equal("Preflight", json.RootElement.GetProperty("stage").GetString());
        Assert.Equal("0123456789abcdef0123456789abcdef01234567", json.RootElement.GetProperty("commitSha").GetString());
        Assert.Equal("racehunter-staging", json.RootElement.GetProperty("projectId").GetString());
        Assert.Equal("us-east1", json.RootElement.GetProperty("region").GetString());
        Assert.True(json.RootElement.GetProperty("approvalRequired").GetBoolean());
        Assert.False(json.RootElement.GetProperty("authorizesMutation").GetBoolean());
        Assert.Equal(new[] { "active-principal", "project", "billing-link", "quotas", "permissions", "region-availability", "existing-resources" },
            json.RootElement.GetProperty("allowedChecks").EnumerateArray().Select(item => item.GetString()).ToArray());
        Assert.Contains("QualifyLocal", entryPoint, StringComparison.Ordinal);
        Assert.Contains("Assert-StagingPreflightRequest", entryPoint, StringComparison.Ordinal);
    }

    [Fact]
    public void Preflight_approval_rejects_prequalification_expired_future_and_mismatched_requests()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $commit = '0123456789abcdef0123456789abcdef01234567'
            $binding = New-StagingReleaseBinding -CommitSha $commit -ProjectId 'racehunter-staging' -Region 'us-east1'
            $runner = {
                param($gate)
                $output = if ($gate.name -eq 'release-candidate-commit') { $commit } else { '' }
                $exitCode = if ($gate.name -eq 'repository-secret-scan') { 1 } else { 0 }
                return [pscustomobject]@{ exitCode = $exitCode; standardOutput = $output }
            }
            $qualification = Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $binding -CommandRunner $runner -ObservedAtUtc '2026-08-19T18:30:00Z'
            $request = $qualification.preflightRequest
            $valid = New-StagingReleaseApproval -Stage Preflight -Binding $binding -PreflightRequest $request -ApprovedAtUtc '2026-08-19T18:31:00Z'
            function Copy-Value { param($value) return ($value | ConvertTo-Json -Compress -Depth 100 | ConvertFrom-Json -Depth 100 -DateKind String) }
            $preQualification = Copy-Value $valid; $preQualification.approvedAtUtc = '2026-08-19T18:29:59Z'
            $expired = Copy-Value $valid
            $future = Copy-Value $valid; $future.approvedAtUtc = '2026-08-19T18:38:01Z'
            $mismatchedRequest = Copy-Value $request; $mismatchedRequest.allowedChecks = @('active-principal')
            [pscustomobject]@{
                valid = Test-StagingReleaseApproval -Stage Preflight -Binding $binding -Approval $valid -PreflightRequest $request -Qualification $qualification -CurrentTimeUtc '2026-08-19T18:35:00Z'
                preQualification = Test-StagingReleaseApproval -Stage Preflight -Binding $binding -Approval $preQualification -PreflightRequest $request -Qualification $qualification -CurrentTimeUtc '2026-08-19T18:35:00Z'
                expired = Test-StagingReleaseApproval -Stage Preflight -Binding $binding -Approval $expired -PreflightRequest $request -Qualification $qualification -CurrentTimeUtc '2026-08-19T18:47:01Z'
                future = Test-StagingReleaseApproval -Stage Preflight -Binding $binding -Approval $future -PreflightRequest $request -Qualification $qualification -CurrentTimeUtc '2026-08-19T18:35:00Z'
                mismatchedRequest = Test-StagingReleaseApproval -Stage Preflight -Binding $binding -Approval $valid -PreflightRequest $mismatchedRequest -Qualification $qualification -CurrentTimeUtc '2026-08-19T18:35:00Z'
                qualificationHash = $valid.qualificationHash
                requestHash = $valid.preflightRequestHash
            } | ConvertTo-Json -Compress
            """);

        Assert.True(result.ExitCode == 0, result.Error);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("valid").GetBoolean());
        Assert.False(json.RootElement.GetProperty("preQualification").GetBoolean());
        Assert.False(json.RootElement.GetProperty("expired").GetBoolean());
        Assert.False(json.RootElement.GetProperty("future").GetBoolean());
        Assert.False(json.RootElement.GetProperty("mismatchedRequest").GetBoolean());
        Assert.Matches("^[a-f0-9]{64}$", json.RootElement.GetProperty("qualificationHash").GetString()!);
        Assert.Matches("^[a-f0-9]{64}$", json.RootElement.GetProperty("requestHash").GetString()!);
    }

    [Fact]
    public void Preflight_approval_creation_rejects_tampered_request_content_with_a_stale_hash()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $commit = '0123456789abcdef0123456789abcdef01234567'
            $binding = New-StagingReleaseBinding -CommitSha $commit -ProjectId 'racehunter-staging' -Region 'us-east1'
            $runner = {
                param($gate)
                $output = if ($gate.name -eq 'release-candidate-commit') { $commit } else { '' }
                $exitCode = if ($gate.name -eq 'repository-secret-scan') { 1 } else { 0 }
                return [pscustomobject]@{ exitCode = $exitCode; standardOutput = $output }
            }
            $qualification = Invoke-StagingLocalQualification -RepositoryRoot '{{Escape(Root)}}' -Binding $binding -CommandRunner $runner -ObservedAtUtc '2026-08-19T18:30:00Z'
            $request = $qualification.preflightRequest
            $tampered = $request | ConvertTo-Json -Compress -Depth 100 | ConvertFrom-Json -Depth 100 -DateKind String
            $tampered.allowedChecks = @('active-principal')
            $tamperedRejected = $false
            try { New-StagingReleaseApproval -Stage Preflight -Binding $binding -PreflightRequest $tampered -ApprovedAtUtc '2026-08-19T18:31:00Z' -ErrorAction Stop | Out-Null }
            catch { $tamperedRejected = $true }
            $validAccepted = $true
            try { New-StagingReleaseApproval -Stage Preflight -Binding $binding -PreflightRequest $request -ApprovedAtUtc '2026-08-19T18:31:00Z' -ErrorAction Stop | Out-Null }
            catch { $validAccepted = $false }
            [pscustomobject]@{ tamperedRejected = $tamperedRejected; validAccepted = $validAccepted } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("tamperedRejected").GetBoolean());
        Assert.True(json.RootElement.GetProperty("validAccepted").GetBoolean());
    }

    [Fact]
    public void Real_local_process_runner_hides_google_credentials_and_uses_isolated_discovery_roots()
    {
        using var temporary = new TemporaryDirectory();
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $credentialNames = @(
                'GOOGLE_APPLICATION_CREDENTIALS', 'GOOGLE_API_KEY', 'GOOGLE_AUTH_TOKEN', 'GOOGLE_OAUTH_ACCESS_TOKEN',
                'GOOGLE_GHA_CREDS_PATH', 'CLOUDSDK_AUTH_ACCESS_TOKEN', 'CLOUDSDK_AUTH_CREDENTIAL_FILE_OVERRIDE', 'CLOUDSDK_CORE_ACCOUNT'
            )
            foreach ($name in $credentialNames) { [Environment]::SetEnvironmentVariable($name, 'sentinel-not-a-credential') }
            $env:CLOUDSDK_CONFIG = 'sentinel-cloud-sdk-config'
            $env:HOME = 'sentinel-home'
            $env:USERPROFILE = 'sentinel-user-profile'
            $sentinel = @'
            $credentialNames = @('GOOGLE_APPLICATION_CREDENTIALS', 'GOOGLE_API_KEY', 'GOOGLE_AUTH_TOKEN', 'GOOGLE_OAUTH_ACCESS_TOKEN', 'GOOGLE_GHA_CREDS_PATH', 'CLOUDSDK_AUTH_ACCESS_TOKEN', 'CLOUDSDK_AUTH_CREDENTIAL_FILE_OVERRIDE', 'CLOUDSDK_CORE_ACCOUNT')
            [pscustomobject]@{
                leakedNames = @($credentialNames | Where-Object { -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) })
                cloudSdkIsolated = -not [string]::IsNullOrWhiteSpace($env:CLOUDSDK_CONFIG) -and $env:CLOUDSDK_CONFIG -ne 'sentinel-cloud-sdk-config'
                homeIsolated = $env:HOME -ne 'sentinel-home' -and $env:USERPROFILE -ne 'sentinel-user-profile'
            } | ConvertTo-Json -Compress
            '@
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($sentinel))
            $gate = [pscustomobject]@{
                name = 'credential-environment-sentinel'
                filePath = 'pwsh'
                argumentList = @('-NoLogo', '-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)
                workingDirectory = '{{Escape(temporary.Path)}}'
            }
            $outcome = Invoke-StagingLocalQualificationCommand -Gate $gate
            [pscustomobject]@{ exitCode = $outcome.exitCode; child = ($outcome.standardOutput | ConvertFrom-Json) } | ConvertTo-Json -Compress -Depth 10
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(0, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.Empty(json.RootElement.GetProperty("child").GetProperty("leakedNames").EnumerateArray());
        Assert.True(json.RootElement.GetProperty("child").GetProperty("cloudSdkIsolated").GetBoolean());
        Assert.True(json.RootElement.GetProperty("child").GetProperty("homeIsolated").GetBoolean());
    }

    [Fact]
    public void Real_local_process_runner_resolves_windows_command_shims_before_launch()
    {
        using var temporary = new TemporaryDirectory();
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $command = if ($IsWindows) { 'npm.cmd' } else { 'npm' }
            $gate = [pscustomobject]@{
                name = 'npm-runtime-sentinel'
                filePath = $command
                argumentList = @('--version')
                workingDirectory = '{{Escape(temporary.Path)}}'
            }
            $outcome = Invoke-StagingLocalQualificationCommand -Gate $gate
            [pscustomobject]@{
                exitCode = $outcome.exitCode
                versionFormat = [string]$outcome.standardOutput -match '^\s*\d+\.\d+\.\d+\s*$'
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(0, json.RootElement.GetProperty("exitCode").GetInt32());
        Assert.True(json.RootElement.GetProperty("versionFormat").GetBoolean());
    }

    private static PowerShellResult RunPowerShell(string command)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'\n" + command));
        var startInfo = new ProcessStartInfo("pwsh", $"-NoLogo -NoProfile -NonInteractive -EncodedCommand {encoded}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new PowerShellResult(process.ExitCode, output.Trim(), error.Trim());
    }

    private static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "RaceHunter.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed record PowerShellResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"racehunter-staging-release-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
