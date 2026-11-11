using System.Collections.Immutable;
using System.Text;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OpenSourceCorpusImporterTests
{
    [Test]
    public void DefaultGitArgumentsRejectBeforeStartingAProcess()
    {
        using var temporary = new TempDirectory("SharpProof-importer-default-arguments-");
        var unavailableGit = Path.Combine(temporary.FullName, "missing-git");
        var exception = Assert.ThrowsAsync<ArgumentNullException>((Func<Task>)(() =>
            OpenSourceCorpusImporter.ReadGitAsync(temporary.FullName, default,
                TestContext.CurrentContext.CancellationToken, unavailableGit)));
        Assert.That(exception!.ParamName, Is.EqualTo("arguments"));
        Assert.That(Directory.EnumerateFileSystemEntries(temporary.FullName), Is.Empty);
    }

    [Test]
    public async Task SourceDiscoveryUsesCommittedBlobsAndSelectsAcrossFiles()
    {
        using var upstream = new TempDirectory("SharpProof-importer-");
        await InitializeRepositoryAsync(upstream.FullName);
        for (var fileIndex = 0; fileIndex < 26; fileIndex++)
        {
            var directory = fileIndex < 13 ? "Algorithms" : "DataStructures";
            var path = Path.Combine(upstream.FullName, directory, $"F{fileIndex:D2}.cs");
            var source = new StringBuilder($"public class F{fileIndex:D2} {{\n");
            for (var methodIndex = 0; methodIndex < 8; methodIndex++)
            {
                // The last file repeats one declaration from the first file.
                var value = fileIndex == 25 && methodIndex == 0
                    ? 0
                    : fileIndex * 8 + methodIndex;
                source.AppendLine(System.Globalization.CultureInfo.InvariantCulture,
                    $"public int M{methodIndex:D2}() => {value};");
            }
            if (fileIndex == 0)
            {
                source.AppendLine("public abstract int Abstract();");
                source.AppendLine("public extern int External();");
                source.AppendLine("public unsafe int Unsafe() { return 1; }");
                source.AppendLine("public int Conditional() {\n#if DEBUG\nreturn 1;\n#else\nreturn 2;\n#endif\n}");
            }
            source.AppendLine("}");
            await File.WriteAllTextAsync(path, source.ToString());
        }
        await GitAsync(upstream.FullName, ["add", "."]);
        await GitAsync(upstream.FullName, ["commit", "--quiet", "-m", "fixture"]);
        var commit = await GitAsync(upstream.FullName, ["rev-parse", "HEAD"]);

        // An uncommitted edit must not contaminate the imported snapshot.
        var changedPath = Path.Combine(upstream.FullName, "Algorithms", "F00.cs");
        await File.AppendAllTextAsync(changedPath, "public class Uncommitted { public int Leak() => 1; }\n");
        var (files, candidates) = await OpenSourceCorpusImporter.DiscoverSourcesAsync(
            upstream.FullName, TestSource(commit, "https://github.com/aalhour/C-Sharp-Algorithms", "license\n"),
            TestContext.CurrentContext.CancellationToken);
        var selected = OpenSourceCorpusImporter.SelectDiverseCandidates(
            candidates, OpenSourceCorpusCatalog.MinimumMethodCount);
        var firstMethod = selected[0].ToMethod(
            "OSS0001", CorpusVerdict.Proven, CorpusSupport.Supported);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(files, Has.Length.EqualTo(26));
            Assert.That(candidates, Has.Length.EqualTo(207));
            Assert.That(candidates.Select(static candidate => candidate.DeclarationSha256)
                .Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(207));
            Assert.That(candidates.Select(static candidate => candidate.MethodName)
                    .Intersect(["Abstract", "External", "Unsafe", "Conditional", "Leak"]),
                Is.Empty);
            Assert.That(selected, Has.Length.EqualTo(200));
            Assert.That(selected.Select(static candidate => candidate.Path)
                .Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(26));
            Assert.That(selected[0].Path, Is.EqualTo("Algorithms/F00.cs"));
            Assert.That(selected[25].Path, Is.EqualTo("DataStructures/F25.cs"));
            Assert.That(selected[26].Path, Is.EqualTo("Algorithms/F00.cs"));
            Assert.That(files[0].Content, Does.Not.Contain("Uncommitted"));
            Assert.That(firstMethod.Path, Is.EqualTo("Algorithms/F00.cs"));
            Assert.That(firstMethod.DeclarationSha256,
                Is.EqualTo(selected[0].DeclarationSha256));
            Assert.That(firstMethod.ExpectedVerdict, Is.EqualTo(CorpusVerdict.Proven));
            Assert.That(firstMethod.Support, Is.EqualTo(CorpusSupport.Supported));
            Assert.That(firstMethod.SourceId, Is.EqualTo("aalhour-c-sharp-algorithms"));
        }
    }

    [Test]
    public void MergeKeepsOtherSourcesAndStableIdsAndAppendsNewMethods()
    {
        static OpenSourceCorpusMethod Method(string id, string source, string hash, CorpusSupport support)
        {
            return new(id, source, $"{hash}.cs", 1, 1, hash, "M", "effects", CorpusVerdict.Proven, support);
        }
        static OpenSourceCorpusSource Source(string id, string commit)
        {
            return new(id, "https://example.invalid/" + id, commit, "MIT", $"third-party/{id}/LICENSE", new string('0', 64));
        }
        var existing = new OpenSourceCorpusDocument(OpenSourceCorpusCatalog.SchemaVersion,
            [Source("other", new string('1', 40)), Source("imported", new string('2', 40))],
            [new("other", "a1.cs", "h", "c"), new("imported", "old.cs", "h", "c")],
            [Method("OSS0001", "other", "a1", CorpusSupport.Supported),
             Method("OSS0002", "imported", "kept", CorpusSupport.Supported),
             Method("OSS0003", "imported", "removed", CorpusSupport.IntentionallyUnsupported),
             Method("OSS0004", "other", "a2", CorpusSupport.IntentionallyUnsupported)]);
        var candidates = Enumerable.Range(0, 30)
            .Select(static index => new OpenSourceCorpusImporter.ImportCandidate($"F{index:D2}.cs", 3, 4, $"new{index:D2}", "N") { SourceId = "imported" })
            .Prepend(new OpenSourceCorpusImporter.ImportCandidate("Kept.cs", 7, 9, "kept", "M") { SourceId = "imported" })
            .Prepend(new OpenSourceCorpusImporter.ImportCandidate("Collides.cs", 1, 1, "a1", "M") { SourceId = "imported" })
            .ToImmutableArray();
        var source = Source("imported", new string('3', 40));
        var files = ImmutableArray.Create(new OpenSourceCorpusFile("imported", "new.cs", "h", "c"));

        var merged = OpenSourceCorpusImporter.Merge(existing, source, files, candidates, 26);
        var again = OpenSourceCorpusImporter.Merge(merged, source, files, candidates, 26);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(merged.Sources.Select(static item => item.Commit), Is.EqualTo(ImmutableArray.Create(new string('1', 40), new string('3', 40))));
            Assert.That(merged.Files.Select(static file => file.Path), Is.EqualTo(ImmutableArray.Create("a1.cs", "new.cs")));
            Assert.That(merged.Methods, Has.Length.EqualTo(28));
            Assert.That(merged.Methods.Select(static method => method.Id),
                Is.EqualTo(Enumerable.Range(1, 28).Select(static index => $"OSS{index:D4}")));
            Assert.That(merged.Methods[0], Is.EqualTo(existing.Methods[0]));
            Assert.That(merged.Methods[1] with { Path = "kept.cs", StartLine = 1, EndLine = 1 }, Is.EqualTo(existing.Methods[1]));
            Assert.That(merged.Methods[1].Path, Is.EqualTo("Kept.cs"));
            Assert.That(merged.Methods[2], Is.EqualTo(existing.Methods[3] with { Id = "OSS0003" }));
            Assert.That(merged.Methods.Skip(3).Select(static method => method.Support), Is.All.EqualTo(CorpusSupport.Unspecified));
            Assert.That(merged.Methods.Select(static method => method.DeclarationSha256), Has.None.EqualTo("removed"));
            Assert.That(merged.Methods.Count(static method => method.DeclarationSha256 == "a1"), Is.EqualTo(1));
            Assert.That(again, Is.EqualTo(merged).Using<OpenSourceCorpusDocument>((left, right) =>
                left.Methods.SequenceEqual(right.Methods) && left.Files.SequenceEqual(right.Files) && left.Sources.SequenceEqual(right.Sources)));
        }
    }

    [TestCase("license", "unsupported license")]
    [TestCase("quota", "at most 500 are allowed")]
    [TestCase("id", "must be unique lowercase")]
    [TestCase("commit", "pin a full Git commit")]
    [TestCase("path", "invalid license, notice, root, or exclude paths")]
    [TestCase("compilation", "invalid compilation options")]
    public void SourceConfigurationRejectsUnreviewedEntries(string field, string message)
    {
        System.ArgumentNullException.ThrowIfNull(message);
        var source = TestSource(new string('a', 40), "https://github.com/aalhour/C-Sharp-Algorithms", "license\n");
        source = field switch
        {
            "license" => source with { LicenseSpdx = "GPL-3.0-only" },
            "quota" => source with { MethodQuota = 501 },
            "id" => source with { Id = "Upper Case" },
            "commit" => source with { Commit = "main" },
            "path" => source with { Roots = ["../outside"] },
            "compilation" => source with { Compilation = source.Compilation with { PreprocessorSymbols = ["NOT A SYMBOL"] } },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        var error = Assert.Throws<InvalidDataException>((Action)(() => OpenSourceCorpusImporter.ValidateSources(
            new OpenSourceCorpusSourcesDocument(OpenSourceCorpusImporter.SourcesSchemaVersion, [source]))));
        Assert.That(error!.Message, Does.Contain(message));
    }

    [Test]
    public void CheckedInSourceConfigurationIsValidAndApacheIsAllowed()
    {
        var configuration = OpenSourceCorpusImporter.LoadSources(RepositoryLayout.FindRoot());
        Assert.That(configuration.Sources.Select(static source => source.LicenseSpdx), Has.Some.EqualTo("Apache-2.0"));
        Assert.That(configuration.Sources.Sum(static source => source.MethodQuota),
            Is.LessThanOrEqualTo(OpenSourceCorpusCatalog.MaximumMethodCount));
    }

    [Test]
    public void SelectionRejectsInsufficientMethodsAndSourceFiles()
    {
        var fewMethods = ImmutableArray.Create(
            new OpenSourceCorpusImporter.ImportCandidate("Algorithms/A.cs", 1, 1,
                "hash-1", "A"));
        var insufficientMethods = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusImporter.SelectDiverseCandidates(fewMethods, 2)));
        Assert.That(insufficientMethods!.Message, Does.Contain("Only 1 distinct"));

        var oneFile = Enumerable.Range(0, OpenSourceCorpusCatalog.MinimumMethodCount)
            .Select(index => new OpenSourceCorpusImporter.ImportCandidate(
                "Algorithms/A.cs", index + 1, index + 1,
                $"hash-{index}", $"M{index}"))
            .ToImmutableArray();
        var insufficientFiles = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusImporter.SelectDiverseCandidates(
                oneFile, OpenSourceCorpusCatalog.MinimumMethodCount)));
        Assert.That(insufficientFiles!.Message, Does.Contain("spans 1 files"));
    }

    [Test]
    public async Task PrepareRejectsDirtyCheckoutBeforeOriginVerification()
    {
        using var upstream = new TempDirectory("SharpProof-importer-");
        await InitializeRepositoryAsync(upstream.FullName);
        await File.WriteAllTextAsync(Path.Combine(upstream.FullName, "fixture.txt"), "committed\n");
        await GitAsync(upstream.FullName, ["add", "."]);
        await GitAsync(upstream.FullName, ["commit", "--quiet", "-m", "fixture"]);
        await File.AppendAllTextAsync(Path.Combine(upstream.FullName, "fixture.txt"), "dirty\n");

        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await OpenSourceCorpusImporter.PrepareAsync(
                upstream.FullName, upstream.FullName, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Does.Contain("clean upstream checkout"));
    }

    [Test]
    public async Task PrepareRejectsUnexpectedOriginBeforeFetching()
    {
        using var upstream = new TempDirectory("SharpProof-importer-");
        await InitializeRepositoryAsync(upstream.FullName);
        await File.WriteAllTextAsync(Path.Combine(upstream.FullName, "fixture.txt"), "committed\n");
        await GitAsync(upstream.FullName, ["add", "."]);
        await GitAsync(upstream.FullName, ["commit", "--quiet", "-m", "fixture"]);
        await GitAsync(upstream.FullName, ["remote", "add", "origin",
            "https://example.com/untrusted-repository.git"]);
        using var repository = new TempDirectory("SharpProof-importer-repository-");
        await WriteSourcesAsync(repository.FullName, TestSource(
            await GitAsync(upstream.FullName, ["rev-parse", "HEAD"]), "https://github.com/aalhour/C-Sharp-Algorithms", "license\n"));

        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await OpenSourceCorpusImporter.PrepareAsync(
                repository.FullName, upstream.FullName, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Does.Contain("unexpected origin"));
    }

    [Test]
    public async Task PrepareRejectsHeadThatIsNotThePinnedCommit()
    {
        using var upstream = new TempDirectory("SharpProof-importer-");
        await InitializeRepositoryAsync(upstream.FullName);
        await File.WriteAllTextAsync(Path.Combine(upstream.FullName, "fixture.txt"), "committed\n");
        await GitAsync(upstream.FullName, ["add", "."]);
        await GitAsync(upstream.FullName, ["commit", "--quiet", "-m", "fixture"]);
        await GitAsync(upstream.FullName, ["remote", "add", "origin",
            "https://github.com/aalhour/C-Sharp-Algorithms.git"]);
        using var repository = new TempDirectory("SharpProof-importer-repository-");
        await WriteSourcesAsync(repository.FullName, TestSource(
            new string('a', 40), "https://github.com/aalhour/C-Sharp-Algorithms", "license\n"));

        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await OpenSourceCorpusImporter.PrepareAsync(
                repository.FullName, upstream.FullName, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Does.Contain("is not the commit pinned for aalhour-c-sharp-algorithms"));
    }

    [Test]
    [NonParallelizable]
    [Platform("Linux")]
    public async Task PrepareRejectsCommitMissingFromApprovedOrigin()
    {
        using var upstream = new TempDirectory("SharpProof-importer-untrusted-");
        using var approved = new TempDirectory("SharpProof-importer-approved-");
        await InitializeRepositoryAsync(upstream.FullName);
        await InitializeRepositoryAsync(approved.FullName);
        await File.WriteAllTextAsync(
            Path.Combine(upstream.FullName, "fixture.txt"), "untrusted\n");
        await File.WriteAllTextAsync(
            Path.Combine(approved.FullName, "fixture.txt"), "trusted\n");
        foreach (var repository in ImmutableArray.Create(upstream.FullName, approved.FullName))
        {
            await GitAsync(repository, ["add", "."]);
            await GitAsync(repository, ["commit", "--quiet", "-m", "fixture"]);
        }
        await GitAsync(upstream.FullName, ["remote", "add", "origin",
            "https://github.com/aalhour/C-Sharp-Algorithms"]);
        var untrustedCommit = await GitAsync(upstream.FullName, ["rev-parse", "HEAD"]);
        using var configuredRepository = new TempDirectory("SharpProof-importer-repository-");
        await WriteSourcesAsync(configuredRepository.FullName, TestSource(
            untrustedCommit, "https://github.com/aalhour/C-Sharp-Algorithms", "license\n"));

        // Git rewrites this fetch only inside the test process and its children.
        // The importer still sees the approved origin URL and verifies membership.
        var configPath = Path.Combine(approved.FullName, "local-gitconfig");
        var approvedUrl = new Uri(
            Path.GetFullPath(approved.FullName) + Path.DirectorySeparatorChar)
            .AbsoluteUri;
        await File.WriteAllTextAsync(configPath,
            $"[url \"{approvedUrl}\"]\n\tinsteadOf = https://github.com/aalhour/C-Sharp-Algorithms\n");
        var previousConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", configPath);
            var error = Assert.ThrowsAsync<InvalidDataException>(
                (Func<Task>)(async () =>
                    await OpenSourceCorpusImporter.PrepareAsync(
                        configuredRepository.FullName, upstream.FullName,
                        TestContext.CurrentContext.CancellationToken)));
            Assert.That(error!.Message, Does.Contain(untrustedCommit));
            Assert.That(error.Message,
                Does.Contain("not a commit from the approved repository"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousConfig);
        }
    }

    internal static OpenSourceCorpusSourceConfig TestSource(string commit, string repository, string licenseText, int quota = 200)
    {
        System.ArgumentNullException.ThrowIfNull(commit);
        System.ArgumentNullException.ThrowIfNull(repository);
        System.ArgumentNullException.ThrowIfNull(licenseText);
        return new("aalhour-c-sharp-algorithms", repository, commit, "MIT",
            new OpenSourceCorpusUpstreamFile("LICENSE", OpenSourceCorpusCatalog.ComputeNormalizedSha256(licenseText)),
            [], ["Algorithms", "DataStructures"], [], quota, OpenSourceCorpusCompilationOptions.Default);
    }

    internal static async Task WriteSourcesAsync(string repositoryRoot, OpenSourceCorpusSourceConfig source)
    {
        System.ArgumentNullException.ThrowIfNull(repositoryRoot);
        System.ArgumentNullException.ThrowIfNull(source);
        var path = OpenSourceCorpusCatalog.GetSourcesConfigPath(repositoryRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, System.Text.Json.JsonSerializer.Serialize(
            new OpenSourceCorpusSourcesDocument(OpenSourceCorpusImporter.SourcesSchemaVersion, [source])));
    }

    private static async Task InitializeRepositoryAsync(string path)
    {
        System.ArgumentNullException.ThrowIfNull(path);
        Directory.CreateDirectory(Path.Combine(path, "Algorithms"));
        Directory.CreateDirectory(Path.Combine(path, "DataStructures"));
        await GitAsync(path, ["init", "--quiet"]);
        await GitAsync(path, ["config", "user.name", "SharpProof Test"]);
        await GitAsync(path, ["config", "user.email", "test@example.invalid"]);
    }

    private static Task<string> GitAsync(string path, ImmutableArray<string> arguments)
    {
        if (arguments.IsDefault)
        { throw new System.ArgumentNullException(nameof(arguments)); }
        System.ArgumentNullException.ThrowIfNull(path);
        return OpenSourceCorpusImporter.ReadGitAsync(
            path, arguments, TestContext.CurrentContext.CancellationToken);
    }
}
