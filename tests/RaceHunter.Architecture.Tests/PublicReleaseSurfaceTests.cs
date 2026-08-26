using Xunit;
using System.Diagnostics;

namespace RaceHunter.Architecture.Tests;

public sealed class PublicReleaseSurfaceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Fact]
    public void Public_release_is_judge_ready_reproducible_and_security_auditable()
    {
        var license = File.ReadAllText(Path.Combine(Root, "LICENSE"));
        var readme = File.ReadAllText(Path.Combine(Root, "README.md"));
        var security = File.ReadAllText(Path.Combine(Root, "SECURITY.md"));
        var audit = File.ReadAllText(Path.Combine(Root, "scripts", "audit-public-release.ps1"));
        var workflow = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "ci.yml"));
        var dependabot = File.ReadAllText(Path.Combine(Root, ".github", "dependabot.yml"));
        var gitignore = File.ReadAllText(Path.Combine(Root, ".gitignore"));

        Assert.Contains("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("All Things Agentic", readme, StringComparison.Ordinal);
        Assert.Contains("Taskmaster", readme, StringComparison.Ordinal);
        Assert.Contains("Gemini 3.5 Flash", readme, StringComparison.Ordinal);
        Assert.Contains("Google.GenAI", readme, StringComparison.Ordinal);
        Assert.Contains("Google Cloud", readme, StringComparison.Ordinal);
        Assert.Contains("https://racehunter-api-vvkcj4sdma-ue.a.run.app", readme, StringComparison.Ordinal);
        Assert.Contains("docs/architecture/racehunter-google-cloud.png", readme, StringComparison.Ordinal);
        Assert.Contains("Quick start", readme, StringComparison.Ordinal);
        Assert.Contains("Security model", readme, StringComparison.Ordinal);
        Assert.Contains("Known submission gap", readme, StringComparison.Ordinal);
        Assert.Contains("security vulnerability", security, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("git rev-list --objects --all", audit, StringComparison.Ordinal);
        Assert.Contains("private_key", audit, StringComparison.Ordinal);
        Assert.Contains("github_fine_grained", audit, StringComparison.Ordinal);
        Assert.Contains("exit 0", audit, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Matches(@"uses: actions/checkout@[0-9a-f]{40} # v7", workflow);
        Assert.Matches(@"uses: actions/setup-dotnet@[0-9a-f]{40} # v6", workflow);
        Assert.Matches(@"uses: actions/setup-node@[0-9a-f]{40} # v7", workflow);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test", workflow, StringComparison.Ordinal);
        Assert.Contains("package-ecosystem: nuget", dependabot, StringComparison.Ordinal);
        Assert.Contains("package-ecosystem: npm", dependabot, StringComparison.Ordinal);
        Assert.Contains("interval: monthly", dependabot, StringComparison.Ordinal);
        Assert.Contains("routine-nuget-updates", dependabot, StringComparison.Ordinal);
        Assert.Contains("*.mp4", gitignore, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(Root, "docs", "architecture", "racehunter-google-cloud.png")));
    }

    [Fact]
    public void Public_release_audit_finds_deleted_history_secret_without_echoing_it()
    {
        var temporaryRoot = CreateRepository();
        try
        {
            var secret = "gh" + "p_" + new string('A', 36);
            File.WriteAllText(Path.Combine(temporaryRoot, "temporary.txt"), $"token={secret}");
            Run("git", temporaryRoot, "add", "temporary.txt");
            Run("git", temporaryRoot, "commit", "--quiet", "-m", "temporary credential");
            File.Delete(Path.Combine(temporaryRoot, "temporary.txt"));
            Run("git", temporaryRoot, "add", "-u");
            Run("git", temporaryRoot, "commit", "--quiet", "-m", "remove credential");

            var result = Audit(temporaryRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("credential-shape", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, result.Output, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRepository(temporaryRoot);
        }
    }

    [Fact]
    public void Public_release_audit_reads_staged_index_bytes_not_only_working_tree()
    {
        var temporaryRoot = CreateRepository();
        try
        {
            File.WriteAllText(Path.Combine(temporaryRoot, "candidate.txt"), "safe");
            Run("git", temporaryRoot, "add", "candidate.txt");
            Run("git", temporaryRoot, "commit", "--quiet", "-m", "safe baseline");

            var secret = "gh" + "p_" + new string('B', 36);
            File.WriteAllText(Path.Combine(temporaryRoot, "candidate.txt"), secret);
            Run("git", temporaryRoot, "add", "candidate.txt");
            File.WriteAllText(Path.Combine(temporaryRoot, "candidate.txt"), "safe working tree");

            var result = Audit(temporaryRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("index", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, result.Output, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRepository(temporaryRoot);
        }
    }

    [Fact]
    public void Public_release_audit_finds_deleted_sensitive_path_and_has_no_path_exemption()
    {
        var temporaryRoot = CreateRepository();
        try
        {
            Directory.CreateDirectory(Path.Combine(temporaryRoot, "scripts"));
            File.WriteAllText(Path.Combine(temporaryRoot, "credentials.json"), "{}");
            var secret = "AI" + "za" + new string('C', 35);
            File.WriteAllText(Path.Combine(temporaryRoot, "scripts", "scanner.ps1"), secret);
            Run("git", temporaryRoot, "add", ".");
            Run("git", temporaryRoot, "commit", "--quiet", "-m", "unsafe history");
            File.Delete(Path.Combine(temporaryRoot, "credentials.json"));
            Run("git", temporaryRoot, "add", "-u");
            Run("git", temporaryRoot, "commit", "--quiet", "-m", "delete sensitive path");

            var direct = Run(
                "git",
                temporaryRoot,
                "grep",
                "-I",
                "-l",
                "-P",
                "-e",
                "AI" + "za[0-9A-Za-z_-]{35}",
                "HEAD");
            Assert.Contains("scripts/scanner.ps1", direct.Output, StringComparison.Ordinal);

            var result = Audit(temporaryRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("history-path", result.Output, StringComparison.Ordinal);
            Assert.Contains("credentials.json", result.Output, StringComparison.Ordinal);
            Assert.Contains("scripts/scanner.ps1", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(secret, result.Output, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRepository(temporaryRoot);
        }
    }

    [Fact]
    public void Public_release_audit_scans_commit_and_annotated_tag_messages_without_echoing_them()
    {
        var temporaryRoot = CreateRepository();
        try
        {
            File.WriteAllText(Path.Combine(temporaryRoot, "safe.txt"), "safe");
            Run("git", temporaryRoot, "add", "safe.txt");
            var commitSecret = "gh" + "p_" + new string('D', 36);
            Run("git", temporaryRoot, "commit", "--quiet", "-m", $"message {commitSecret}");
            var tagSecret = "AI" + "za" + new string('E', 35);
            Run("git", temporaryRoot, "tag", "-a", "audit-tag", "-m", $"message {tagSecret}");

            var result = Audit(temporaryRoot);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("<commit-message>", result.Output, StringComparison.Ordinal);
            Assert.Contains("<annotated-tag-message>", result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(commitSecret, result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(tagSecret, result.Output, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRepository(temporaryRoot);
        }
    }

    [Fact]
    public void Public_release_audit_fails_closed_for_oversized_working_candidate()
    {
        var temporaryRoot = CreateRepository();
        try
        {
            File.WriteAllText(Path.Combine(temporaryRoot, "large.txt"), new string('x', 2048));

            var result = Audit(temporaryRoot, maximumBlobBytes: 1024);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("unscanned-candidate", result.Output, StringComparison.Ordinal);
            Assert.Contains("size-limit", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            DeleteRepository(temporaryRoot);
        }
    }

    [Fact]
    public void Public_release_audit_scans_tracked_dotfiles_portably()
    {
        var temporaryRoot = CreateRepository();
        try
        {
            File.WriteAllText(Path.Combine(temporaryRoot, ".dockerignore"), "**/bin\n**/obj\n");
            Run("git", temporaryRoot, "add", ".dockerignore");
            Run("git", temporaryRoot, "commit", "--quiet", "-m", "tracked dotfile");

            var result = Audit(temporaryRoot);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            DeleteRepository(temporaryRoot);
        }
    }

    private static string CreateRepository()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"racehunter-public-audit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        Run("git", temporaryRoot, "init", "--quiet");
        Run("git", temporaryRoot, "config", "user.name", "RaceHunter Test");
        Run("git", temporaryRoot, "config", "user.email", "racehunter-test@example.invalid");
        return temporaryRoot;
    }

    private static CommandResult Audit(string repositoryRoot, int? maximumBlobBytes = null)
    {
        var arguments = new List<string>
        {
            "-NoProfile",
            "-File",
            Path.Combine(Root, "scripts", "audit-public-release.ps1"),
            "-RepositoryRoot",
            repositoryRoot
        };
        if (maximumBlobBytes is not null)
        {
            arguments.Add("-MaximumBlobBytes");
            arguments.Add(maximumBlobBytes.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return Run("pwsh", Root, [.. arguments], allowFailure: true);
    }

    private static void DeleteRepository(string repositoryRoot)
    {
        foreach (var file in Directory.EnumerateFiles(repositoryRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
        Directory.Delete(repositoryRoot, recursive: true);
    }

    private static CommandResult Run(
        string fileName,
        string workingDirectory,
        params string[] arguments) => Run(fileName, workingDirectory, arguments, allowFailure: false);

    private static CommandResult Run(
        string fileName,
        string workingDirectory,
        string[] arguments,
        bool allowFailure)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (!allowFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed: {output}");
        }

        return new CommandResult(process.ExitCode, output);
    }

    private sealed record CommandResult(int ExitCode, string Output);
}
