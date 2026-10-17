using System.Collections.Immutable;
using System.Text;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OpenSourceCorpusImporterTests
{
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
        await GitAsync(upstream.FullName, "add", ".");
        await GitAsync(upstream.FullName, "commit", "--quiet", "-m", "fixture");
        var commit = await GitAsync(upstream.FullName, "rev-parse", "HEAD");

        // An uncommitted edit must not contaminate the imported snapshot.
        var changedPath = Path.Combine(upstream.FullName, "Algorithms", "F00.cs");
        await File.AppendAllTextAsync(changedPath, "public class Uncommitted { public int Leak() => 1; }\n");
        var (files, candidates) = await OpenSourceCorpusImporter.DiscoverSourcesAsync(
            upstream.FullName, commit, TestContext.CurrentContext.CancellationToken);
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
        }
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
        await GitAsync(upstream.FullName, "add", ".");
        await GitAsync(upstream.FullName, "commit", "--quiet", "-m", "fixture");
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
        await GitAsync(upstream.FullName, "add", ".");
        await GitAsync(upstream.FullName, "commit", "--quiet", "-m", "fixture");
        await GitAsync(upstream.FullName, "remote", "add", "origin",
            "https://example.com/untrusted-repository.git");

        var error = Assert.ThrowsAsync<InvalidDataException>((Func<Task>)(async () =>
            await OpenSourceCorpusImporter.PrepareAsync(
                upstream.FullName, upstream.FullName, TestContext.CurrentContext.CancellationToken)));
        Assert.That(error!.Message, Does.Contain("unexpected origin"));
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
        foreach (var repository in new[] { upstream.FullName, approved.FullName })
        {
            await GitAsync(repository, "add", ".");
            await GitAsync(repository, "commit", "--quiet", "-m", "fixture");
        }
        await GitAsync(upstream.FullName, "remote", "add", "origin",
            "https://github.com/aalhour/C-Sharp-Algorithms");
        var untrustedCommit = await GitAsync(upstream.FullName, "rev-parse", "HEAD");

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
                        upstream.FullName, upstream.FullName,
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

    private static async Task InitializeRepositoryAsync(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "Algorithms"));
        Directory.CreateDirectory(Path.Combine(path, "DataStructures"));
        await GitAsync(path, "init", "--quiet");
        await GitAsync(path, "config", "user.name", "SharpProof Test");
        await GitAsync(path, "config", "user.email", "test@example.invalid");
    }

    private static Task<string> GitAsync(string path, params string[] arguments)
    {
        return OpenSourceCorpusImporter.ReadGitAsync(
            path, arguments, TestContext.CurrentContext.CancellationToken);
    }
}
