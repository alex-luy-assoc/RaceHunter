using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Xunit;

namespace RaceHunter.Architecture.Tests;

/// <summary>
/// Phase 1 strategy: exercise the provider-agnostic approval, binding, state, and
/// sanitized-evidence boundaries. Google provider behavior, secret values,
/// destruction, and production rollout are deliberately outside this suite.
/// </summary>
public sealed class StagingReleaseContractTests
{
    private static readonly string Root = FindRoot();
    private static readonly string ModulePath = Path.Combine(Root, "deploy", "scripts", "StagingRelease.psm1");

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
            $approval = New-StagingReleaseApproval -Stage 'Preflight' -Binding $binding
            [pscustomobject]@{
                preflight = Test-StagingReleaseApproval -Stage 'Preflight' -Binding $binding -Approval $approval
                foundation = Test-StagingReleaseApproval -Stage 'Foundation' -Binding $binding -Approval $approval
                deploy = Test-StagingReleaseApproval -Stage 'Deploy' -Binding $binding -Approval $approval
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("preflight").GetBoolean());
        Assert.False(json.RootElement.GetProperty("foundation").GetBoolean());
        Assert.False(json.RootElement.GetProperty("deploy").GetBoolean());
    }

    [Fact]
    public void Approval_record_requires_exact_properties_types_boolean_true_and_a_utc_timestamp()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $binding = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'
            $valid = New-StagingReleaseApproval -Stage 'Preflight' -Binding $binding
            function Copy-Approval { param($value) return ($value | ConvertTo-Json -Compress | ConvertFrom-Json -DateKind String) }
            $stringFalse = Copy-Approval $valid; $stringFalse.valid = 'false'
            $numericTruthy = Copy-Approval $valid; $numericTruthy.valid = 1
            $missing = Copy-Approval $valid; $missing.PSObject.Properties.Remove('bindingHash')
            $extra = Copy-Approval $valid; $extra | Add-Member -NotePropertyName 'unexpected' -NotePropertyValue 'value'
            $malformedTime = Copy-Approval $valid; $malformedTime.approvedAtUtc = 'not-a-time'
            $wrongType = Copy-Approval $valid; $wrongType.projectId = 42
            $rejected = 0
            foreach ($candidate in @($stringFalse, $numericTruthy, $missing, $extra, $malformedTime, $wrongType)) {
                if (-not (Test-StagingReleaseApproval -Stage 'Preflight' -Binding $binding -Approval $candidate)) { $rejected++ }
            }
            [pscustomobject]@{
                validAccepted = Test-StagingReleaseApproval -Stage 'Preflight' -Binding $binding -Approval $valid
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
            $approval = New-StagingReleaseApproval -Stage 'Preflight' -Binding $binding
            $differentCommit = New-StagingReleaseBinding -CommitSha '1123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests @{ api = 'registry/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            $differentProject = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'other-staging' -Region 'us-east1' -ImageDigests @{ api = 'registry/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            $differentRegion = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-central1' -ImageDigests @{ api = 'registry/api@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa' }
            [pscustomobject]@{
                exact = Test-StagingReleaseApproval -Stage 'Preflight' -Binding $binding -Approval $approval
                commit = Test-StagingReleaseApproval -Stage 'Preflight' -Binding $differentCommit -Approval $approval
                project = Test-StagingReleaseApproval -Stage 'Preflight' -Binding $differentProject -Approval $approval
                region = Test-StagingReleaseApproval -Stage 'Preflight' -Binding $differentRegion -Approval $approval
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
            $api = 'registry/api@sha256:' + ('a' * 64)
            $worker = 'registry/worker@sha256:' + ('b' * 64)
            $target = 'registry/target@sha256:' + ('c' * 64)
            $completeImages = @{ api = $api; worker = $worker; referenceTarget = $target }
            $cases = @(
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1'),
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests @{ api = $api; worker = $worker }),
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests $completeImages -SavedPlanPath '{{Escape(planPath)}}'),
                (New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests $completeImages -TerraformInputs @{ worker_max_instances = 1 })
            )
            $tamperedHash = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests $completeImages -TerraformInputs @{ worker_max_instances = 1 } -SavedPlanPath '{{Escape(planPath)}}'
            $tamperedHash.terraformInputHash = ''
            $cases += $tamperedHash
            $rejected = 0
            foreach ($binding in $cases) {
                try { New-StagingReleaseApproval -Stage 'Deploy' -Binding $binding -ErrorAction Stop | Out-Null } catch { $rejected++ }
            }
            $complete = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -ImageDigests $completeImages -TerraformInputs @{ worker_max_instances = 1 } -SavedPlanPath '{{Escape(planPath)}}'
            $approval = New-StagingReleaseApproval -Stage 'Deploy' -Binding $complete
            [pscustomobject]@{ rejected = $rejected; completeAccepted = Test-StagingReleaseApproval -Stage 'Deploy' -Binding $complete -Approval $approval } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(5, json.RootElement.GetProperty("rejected").GetInt32());
        Assert.True(json.RootElement.GetProperty("completeAccepted").GetBoolean());
    }

    [Fact]
    public void Billable_approvals_require_complete_protected_foundation_identity_and_ceilings()
    {
        var result = RunPowerShell($$"""
            Import-Module '{{Escape(ModulePath)}}' -Force
            $complete = [ordered]@{
                scopedApis = @('artifactregistry.googleapis.com', 'run.googleapis.com', 'sqladmin.googleapis.com')
                stateBucketName = 'racehunter-staging-tfstate'
                stateBucketPublicAccessPrevention = $true
                stateBucketVersioning = $true
                artifactRegistryRepository = 'racehunter'
                artifactRegistryLocation = 'us-east1'
                apiMaxInstances = 2
                workerMaxInstances = 1
                referenceTargetMaxInstances = 1
                monthlyBudgetAmount = 25
                budgetCurrency = 'USD'
            }
            function New-Binding { param($foundation) New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation }
            $empty = New-Binding @{}
            $missingApis = [ordered]@{} + $complete; $missingApis.scopedApis = @()
            $unprotectedBucket = [ordered]@{} + $complete; $unprotectedBucket.stateBucketPublicAccessPrevention = $false
            $wrongRegistryRegion = [ordered]@{} + $complete; $wrongRegistryRegion.artifactRegistryLocation = 'us-central1'
            $missingCeiling = [ordered]@{} + $complete; $missingCeiling.Remove('monthlyBudgetAmount')
            $incomplete = @($empty, (New-Binding $missingApis), (New-Binding $unprotectedBucket), (New-Binding $wrongRegistryRegion), (New-Binding $missingCeiling))
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
        Assert.Equal(10, json.RootElement.GetProperty("rejected").GetInt32());
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
            $images = @{ api = ('registry/api@sha256:' + ('a' * 64)); worker = ('registry/worker@sha256:' + ('b' * 64)); referenceTarget = ('registry/target@sha256:' + ('c' * 64)) }
            $foundation = [ordered]@{ scopedApis = @('artifactregistry.googleapis.com'); stateBucketName = 'racehunter-staging-tfstate'; stateBucketPublicAccessPrevention = $true; stateBucketVersioning = $true; artifactRegistryRepository = 'racehunter'; artifactRegistryLocation = 'us-east1'; apiMaxInstances = 2; workerMaxInstances = 1; referenceTargetMaxInstances = 1; monthlyBudgetAmount = 25; budgetCurrency = 'USD' }
            $old = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $images -TerraformInputs @{ worker_max_instances = 1 } -SavedPlanPath '{{Escape(planPath)}}'
            $state = Initialize-StagingReleaseState -Path '{{Escape(statePath)}}' -Binding $old
            foreach ($stage in @('Preflight', 'Foundation', 'Deploy')) {
                $approval = New-StagingReleaseApproval -Stage $stage -Binding $old
                Add-StagingReleaseApproval -Path '{{Escape(statePath)}}' -Approval $approval | Out-Null
            }
            Add-StagingReleaseEvidence -Path '{{Escape(statePath)}}' -Evidence @{ id = 'preflight-1'; classification = 'cloud-read-only' } | Out-Null
            $changed = New-StagingReleaseBinding -CommitSha '0123456789abcdef0123456789abcdef01234567' -ProjectId 'racehunter-staging' -Region 'us-east1' -FoundationInputs $foundation -ImageDigests $images -TerraformInputs @{ worker_max_instances = 2 } -SavedPlanPath '{{Escape(planPath)}}'
            Update-StagingReleaseBinding -Path '{{Escape(statePath)}}' -Binding $changed -ChangedAtStage 'Foundation' | Out-Null
            $resumed = Get-StagingReleaseState -Path '{{Escape(statePath)}}'
            $resumeRejected = $false
            try { Set-StagingReleaseStage -Path '{{Escape(statePath)}}' -Stage 'LocalQualified' -ErrorAction Stop | Out-Null } catch { $resumeRejected = $true }
            [pscustomobject]@{
                preflightValid = [bool]$resumed.approvals.Preflight.valid
                foundationValid = [bool]$resumed.approvals.Foundation.valid
                deployValid = [bool]$resumed.approvals.Deploy.valid
                evidenceCount = @($resumed.evidence).Count
                reason = $resumed.approvals.Deploy.invalidationReason
                resumeRejected = $resumeRejected
            } | ConvertTo-Json -Compress
            """);

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        Assert.True(json.RootElement.GetProperty("preflightValid").GetBoolean());
        Assert.False(json.RootElement.GetProperty("foundationValid").GetBoolean());
        Assert.False(json.RootElement.GetProperty("deployValid").GetBoolean());
        Assert.Equal(1, json.RootElement.GetProperty("evidenceCount").GetInt32());
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
