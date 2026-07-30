using System.Diagnostics;
using System.Text.Json;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class ReleasePublicationScriptTests
{
    private static readonly string[] ExpectedPackageOrder = [
        PackagedProductFeed.AttributesPackageId,
        PackagedProductFeed.PortablePackageId,
        PackagedProductFeed.VerifierPackageId
    ];

    [Test]
    public async Task PublisherNeverSkipsExistingRemoteArtifacts()
    {
        var script = await File.ReadAllTextAsync(
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Publish-SharpProofRelease.ps1"));

        Assert.That(script, Does.Not.Contain("--skip-duplicate"));
        Assert.That(
            script,
            Does.Contain("Remote main package already exists"));
        Assert.That(
            script,
            Does.Contain("Remote symbol package already exists"));
    }

    [Test]
    public async Task OfflinePlanIsOrderedAndRejectsEveryExistingArtifact()
    {
        var feed = await PackagedProductFeed.GetAsync();
        using var workspace = PublicationWorkspace.Create();
        foreach (var package in feed.Packages.Concat(feed.SymbolPackages))
        {
            File.Copy(
                package.Path,
                Path.Combine(
                    workspace.PackageSource,
                    Path.GetFileName(package.Path)));
        }

        var evidence = await RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "New-SharpProofReleaseEvidence.ps1"),
            "-PackageSource",
            workspace.PackageSource);
        Assert.That(evidence.ExitCode, Is.Zero, evidence.Output);

        using (var absentPlan = await RunPlanAsync(workspace))
        {
            AssertPlan(
                absentPlan.RootElement,
                remoteState: "Absent",
                action: "Push");
        }

        var mainPackage = feed.Packages[0];
        var remoteMainPath = Path.Combine(
            workspace.RemoteSource,
            Path.GetFileName(mainPackage.Path));
        File.Copy(mainPackage.Path, remoteMainPath);
        var existingMain = await RunPublicationScriptAsync(
            workspace,
            Path.Combine(workspace.Root, "existing-main-plan.json"));
        Assert.That(
            existingMain.ExitCode,
            Is.Not.Zero,
            existingMain.Output);
        Assert.That(
            existingMain.Output,
            Does.Contain("Remote main package already exists"));
        File.Delete(remoteMainPath);

        var symbolPackage = feed.SymbolPackages[0];
        File.Copy(
            symbolPackage.Path,
            Path.Combine(
                workspace.RemoteSource,
                Path.GetFileName(symbolPackage.Path)));
        var existingSymbols = await RunPublicationScriptAsync(
            workspace,
            Path.Combine(workspace.Root, "existing-symbols-plan.json"));
        Assert.That(
            existingSymbols.ExitCode,
            Is.Not.Zero,
            existingSymbols.Output);
        Assert.That(
            existingSymbols.Output,
            Does.Contain("Remote symbol package already exists"));
    }

    private static void AssertPlan(
        JsonElement root,
        string remoteState,
        string action)
    {
        Assert.That(
            root.GetProperty("schemaVersion").GetInt32(),
            Is.EqualTo(1));
        Assert.That(
            root.GetProperty("planOnly").GetBoolean(),
            Is.True);
        var packages = root.GetProperty("packages")
            .EnumerateArray()
            .ToArray();
        Assert.That(
            packages.Select(package =>
                package.GetProperty("packageId").GetString()),
            Is.EqualTo(ExpectedPackageOrder));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("remoteState").GetString()),
            Is.All.EqualTo(remoteState));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("mainAction").GetString()),
            Is.All.EqualTo(action));
        Assert.That(
            packages.Select(package =>
                package.GetProperty("symbolsAction").GetString()),
            Is.All.EqualTo(action));
    }

    private static async Task<JsonDocument> RunPlanAsync(
        PublicationWorkspace workspace)
    {
        var planPath = Path.Combine(
            workspace.Root,
            "plan-" + Guid.NewGuid().ToString("N") + ".json");
        var result = await RunPublicationScriptAsync(workspace, planPath);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        return JsonDocument.Parse(
            await File.ReadAllBytesAsync(planPath));
    }

    private static Task<ProcessResult> RunPublicationScriptAsync(
        PublicationWorkspace workspace,
        string planPath)
    {
        return RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Publish-SharpProofRelease.ps1"),
            "-PackageSource",
            workspace.PackageSource,
            "-PlanOnly",
            "-RemotePackageDirectory",
            workspace.RemoteSource,
            "-PlanOutputPath",
            planPath,
            "-DotNetPath",
            "dotnet-must-not-run-in-plan-only");
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + Environment.NewLine +
            (await standardError));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            typeof(ReleasePublicationScriptTests).Assembly.Location);
        while (directory != null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "SharpProof.Release.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Repository root was not found.");
    }

    private sealed class PublicationWorkspace : IDisposable
    {
        private readonly string _expectedParent;

        private PublicationWorkspace(string root, string expectedParent)
        {
            Root = root;
            _expectedParent = expectedParent;
            PackageSource = Path.Combine(root, "packages");
            RemoteSource = Path.Combine(root, "remote");
            Directory.CreateDirectory(PackageSource);
            Directory.CreateDirectory(RemoteSource);
        }

        internal string Root
        {
            get;
        }
        internal string PackageSource
        {
            get;
        }
        internal string RemoteSource
        {
            get;
        }

        internal static PublicationWorkspace Create()
        {
            var parent = Path.GetFullPath(Path.Combine(
                Path.GetTempPath(),
                "SharpProof.ReleasePublication"));
            Directory.CreateDirectory(parent);
            var root = Path.Combine(parent, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new PublicationWorkspace(root, parent);
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var relative = Path.GetRelativePath(_expectedParent, resolved);
            if (Path.IsPathRooted(relative) ||
                relative == "." ||
                relative == ".." ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected publication directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
