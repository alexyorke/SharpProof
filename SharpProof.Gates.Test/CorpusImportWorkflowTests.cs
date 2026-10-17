using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[NonParallelizable]
[Platform("Linux")]
public sealed class CorpusImportWorkflowTests
{
    private const string ApprovedRepository = "https://github.com/aalhour/C-Sharp-Algorithms";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    public async Task UnrequestedImportDoesNotReadUpstream(string? source)
    {
        var previous = Environment.GetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable, source);
            Assert.That(await OpenSourceCorpusImporter.PrepareIfRequestedAsync(
                "unused-repository", TestContext.CurrentContext.CancellationToken), Is.Null);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable, previous);
        }
    }

    [Test]
    public async Task ImportRejectsAnUnsupportedUpstreamGitObjectFormat()
    {
        using var upstream = new TempDirectory("SharpProof-import-git-format-");
        await GitAsync(upstream.FullName, "init", "--quiet", "--object-format=sha256");
        await GitAsync(upstream.FullName, "config", "user.name", "SharpProof Test");
        await GitAsync(upstream.FullName, "config", "user.email", "test@example.invalid");
        await File.WriteAllTextAsync(Path.Combine(upstream.FullName, "ordinary.cs"),
            "public static class C { public static int Value() => 1; }\n");
        await GitAsync(upstream.FullName, "add", ".");
        await GitAsync(upstream.FullName, "commit", "--quiet", "-m", "ordinary source fixture");
        var commit = await GitAsync(upstream.FullName, "rev-parse", "HEAD");
        Assert.That(commit, Has.Length.EqualTo(64));
        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await OpenSourceCorpusImporter.PrepareAsync(upstream.FullName, upstream.FullName, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Is.EqualTo($"Upstream HEAD is not a full Git commit: {commit}"));
        Assert.That(await GitAsync(upstream.FullName, "status", "--porcelain"), Is.Empty);
    }

    [Test]
    public async Task SourceDiscoveryRejectsAnIncompleteUpstreamDirectoryLayout()
    {
        using var upstream = new TempDirectory("SharpProof-import-directory-layout-");
        await GitAsync(upstream.FullName, "init", "--quiet");
        await GitAsync(upstream.FullName, "config", "user.name", "SharpProof Test");
        await GitAsync(upstream.FullName, "config", "user.email", "test@example.invalid");
        var algorithms = Path.Combine(upstream.FullName, "Algorithms");
        Directory.CreateDirectory(algorithms);
        await File.WriteAllTextAsync(Path.Combine(algorithms, "ordinary.cs"),
            "public static class C { public static int Value() => 1; }\n");
        await GitAsync(upstream.FullName, "add", ".");
        await GitAsync(upstream.FullName, "commit", "--quiet", "-m", "ordinary source fixture");
        var commit = await GitAsync(upstream.FullName, "rev-parse", "HEAD");
        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await OpenSourceCorpusImporter.DiscoverSourcesAsync(upstream.FullName, commit, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Is.EqualTo(
            $"Expected upstream source directory is missing: {Path.Combine(upstream.FullName, "DataStructures")}"));
        Assert.That(await GitAsync(upstream.FullName, "status", "--porcelain"), Is.Empty);
    }

    [TestCase("reviewed")]
    [TestCase("unreviewed")]
    [TestCase("empty-manifest")]
    public async Task ImportUsesCommittedOrdinarySourceAndReviewedClassifications(string review)
    {
        using var upstream = new TempDirectory("SharpProof-import-source-");
        using var target = new TempDirectory("SharpProof-import-target-");
        using var configuration = new TempDirectory("SharpProof-import-config-");
        await GitAsync(upstream.FullName, "init", "--quiet");
        await GitAsync(upstream.FullName, "config", "user.name", "SharpProof Test");
        await GitAsync(upstream.FullName, "config", "user.email", "test@example.invalid");
        var pinned = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var pinnedSource = pinned.Sources.Single();
        var license = await File.ReadAllTextAsync(Path.Combine(
            RepositoryLayout.FindRoot(), "SharpProof.Gates", "Corpus", pinnedSource.LicenseFile));
        await File.WriteAllTextAsync(Path.Combine(upstream.FullName, "LICENSE"), license);
        for (var file = 0; file < 25; file++)
        {
            var directory = file < 13 ? "Algorithms" : "DataStructures";
            var path = Path.Combine(upstream.FullName, directory, $"F{file:D2}.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var source = new StringBuilder($"public static class F{file:D2} {{\n");
            for (var method = 0; method < 8; method++)
            {
                source.AppendLine(CultureInfo.InvariantCulture,
                    $"public static int M{method:D2}() => {file * 8 + method};");
            }
            source.AppendLine("}");
            await File.WriteAllTextAsync(path, source.ToString());
        }
        await GitAsync(upstream.FullName, "add", ".");
        await GitAsync(upstream.FullName, "commit", "--quiet", "-m", "ordinary source fixture");
        await GitAsync(upstream.FullName, "remote", "add", "origin", ApprovedRepository);
        var commit = await GitAsync(upstream.FullName, "rev-parse", "HEAD");
        var (files, candidates) = await OpenSourceCorpusImporter.DiscoverSourcesAsync(
            upstream.FullName, commit, TestContext.CurrentContext.CancellationToken);
        var selected = OpenSourceCorpusImporter.SelectDiverseCandidates(candidates, 200);
        var corpusDirectory = OpenSourceCorpusCatalog.GetCorpusDirectory(target.FullName);
        Directory.CreateDirectory(corpusDirectory);
        var manifestPath = Path.Combine(corpusDirectory, "oss-methods.json");
        if (review == "reviewed")
        {
            var document = new OpenSourceCorpusDocument(2,
                [pinnedSource with { Commit = commit }], files,
                selected.Select((candidate, index) => candidate.ToMethod(
                    $"OSS{index + 1:D4}", CorpusVerdict.Proven, CorpusSupport.Supported)).ToImmutableArray());
            await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(document, JsonOptions));
        }
        else if (review == "empty-manifest")
        {
            await File.WriteAllTextAsync(manifestPath, "null");
        }
        var existingManifest = File.Exists(manifestPath) ? await File.ReadAllTextAsync(manifestPath) : null;
        var configPath = Path.Combine(configuration.FullName, "gitconfig");
        var localRepository = new Uri(Path.GetFullPath(upstream.FullName) + Path.DirectorySeparatorChar).AbsoluteUri;
        await File.WriteAllTextAsync(configPath,
            $"[url \"{localRepository}\"]\n\tinsteadOf = {ApprovedRepository}\n");
        var previousConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        var previousSource = Environment.GetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            Environment.SetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable, upstream.FullName);
            if (review != "reviewed")
            {
                var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                    await OpenSourceCorpusImporter.PrepareIfRequestedAsync(target.FullName, TestContext.CurrentContext.CancellationToken)));
                Assert.That(error!.Message, review == "empty-manifest"
                    ? Is.EqualTo("The existing OSS corpus manifest is empty.")
                    : Does.Contain("methods without a reviewed support classification"));
                Assert.That(File.Exists(manifestPath), Is.EqualTo(existingManifest is not null));
                if (existingManifest is not null)
                {
                    Assert.That(await File.ReadAllTextAsync(manifestPath), Is.EqualTo(existingManifest));
                }
                if (review == "empty-manifest")
                {
                    const string missingMethodsManifest = "{\"schemaVersion\":2}";
                    await File.WriteAllTextAsync(manifestPath, missingMethodsManifest);
                    var missingMethodsError = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
                        await OpenSourceCorpusImporter.PrepareIfRequestedAsync(target.FullName, TestContext.CurrentContext.CancellationToken)));
                    Assert.That(missingMethodsError!.Message,
                        Is.EqualTo("The existing OSS corpus manifest has no methods array."));
                    Assert.That(await File.ReadAllTextAsync(manifestPath), Is.EqualTo(missingMethodsManifest));
                    Assert.That(await GitAsync(upstream.FullName, "status", "--porcelain"), Is.Empty);
                }
                return;
            }
            var plan = await OpenSourceCorpusImporter.PrepareIfRequestedAsync(target.FullName, TestContext.CurrentContext.CancellationToken);
            Assert.That(plan, Is.Not.Null);
            Assert.That(await File.ReadAllTextAsync(manifestPath), Is.EqualTo(existingManifest));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(plan!.Document.Methods, Has.Length.EqualTo(200));
                Assert.That(plan.Document.Files, Has.Length.EqualTo(25));
                Assert.That(plan.Document.Methods.Select(static method => method.Support),
                    Is.All.EqualTo(CorpusSupport.Supported));
                Assert.That(plan.Document.Methods.Select(static method => method.ExpectedVerdict),
                    Is.All.EqualTo(CorpusVerdict.Proven));
                Assert.That(plan.Document.Sources.Single().Commit, Is.EqualTo(commit));
                Assert.That(plan.Updates, Has.Length.EqualTo(2));
            }
            await CorpusFileTransaction.WriteAllAsync(corpusDirectory, plan!.Updates, TestContext.CurrentContext.CancellationToken);
            var imported = OpenSourceCorpusCatalog.Load(target.FullName);
            Assert.That(imported.Methods, Is.EqualTo(plan.Document.Methods));
            Assert.That(imported.Files, Is.EqualTo(plan.Document.Files));
            Assert.That(await File.ReadAllTextAsync(Path.Combine(corpusDirectory, pinnedSource.LicenseFile)), Is.EqualTo(license));
            Assert.That(await GitAsync(upstream.FullName, "status", "--porcelain"), Is.Empty);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousConfig);
            Environment.SetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable, previousSource);
        }
    }

    private static Task<string> GitAsync(string path, params string[] arguments)
    {
        return OpenSourceCorpusImporter.ReadGitAsync(path, arguments, TestContext.CurrentContext.CancellationToken);
    }
}
