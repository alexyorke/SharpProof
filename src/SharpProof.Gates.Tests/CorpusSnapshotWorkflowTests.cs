using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[NonParallelizable]
public sealed class CorpusSnapshotWorkflowTests
{
    [Test]
    public void DefaultDataLinesRejectAtTheRenderingBoundary()
    {
        var exception = Assert.Throws<ArgumentException>((Action)(() =>
            CorpusSnapshotFormat.Render(default(ImmutableArray<string>))));
        Assert.That(exception!.ParamName, Is.EqualTo("lines"));
    }

    [Test]
    public async Task GateCountsAndRejectsAnUnreviewedSilentUnknown()
    {
        using var temporary = new TempDirectory("SharpProof-silent-unknown-");
        var corpusDirectory = CopyCorpus(temporary.FullName);
        var manifestPath = Path.Combine(corpusDirectory, "oss-methods.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var method = manifest["methods"]!.AsArray().Single(static item =>
            item is null ? throw new ArgumentNullException(nameof(item)) : item["id"]!.GetValue<string>() == "OSS0139")!;
        var file = manifest["files"]!.AsArray().Single(item =>
            item is null ? throw new ArgumentNullException(nameof(item)) : item["path"]!.GetValue<string>() == method["path"]!.GetValue<string>())!;
        var source = file["content"]!.GetValue<string>();
        var syntax = await CSharpSyntaxTree.ParseText(source).GetRootAsync(TestContext.CurrentContext.CancellationToken);
        var declaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(static item =>
            item is null ? throw new ArgumentNullException(nameof(item)) : item.Identifier.ValueText == "UniversalHash" && item.ParameterList.Parameters[0].Type!.ToString() == "int");
        // In this private fixture, an explicit message removes the resource
        // advisory while unsupported static initialization still prevents a
        // native purity proof. The reviewed corpus itself remains untouched.
        var replacement = declaration.ToFullString().Replace("new ArgumentOutOfRangeException(",
            "new ArgumentOutOfRangeException(\"whichHashFunction\", ", StringComparison.Ordinal);
        Assert.That(replacement, Is.Not.EqualTo(declaration.ToFullString()));
        var changed = source.Replace(declaration.ToFullString(), replacement, StringComparison.Ordinal);
        var changedSyntax = await CSharpSyntaxTree.ParseText(changed).GetRootAsync(TestContext.CurrentContext.CancellationToken);
        var changedDeclaration = changedSyntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(item =>
            item is null ? throw new ArgumentNullException(nameof(item)) : item.SpanStart == declaration.SpanStart);
        file["content"] = changed;
        file["contentSha256"] = OpenSourceCorpusCatalog.ComputeNormalizedSha256(changed);
        method["declarationSha256"] = OpenSourceCorpusCatalog.ComputeNormalizedSha256(OpenSourceCorpusCatalog.GetDeclaration(changedDeclaration));
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());

        var result = await CorpusGate.RunAsync(temporary.FullName, TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Passed, Is.False);
            Assert.That(result.SilentUnknownCount, Is.EqualTo(1));
            Assert.That(result.TotalUnknownCount, Is.EqualTo(218));
            Assert.That(result.SupportedUnknownCount, Is.Zero);
            Assert.That(result.UnknownReasons.Single(static item => item is null ? throw new ArgumentNullException(nameof(item)) : item.Reason == "silent-unclassified").Count, Is.EqualTo(1));
            Assert.That(result.Failures, Does.Contain("Corpus produced a new unreviewed Unknown reason 'silent-unclassified' (1 cases)."));
            Assert.That(result.Failures, Has.Some.StartsWith("Snapshot mismatch for OSS0139.baseline:"));
        }
    }

    [Test]
    public async Task SnapshotUpdateReproducesTheReviewedCanonicalSnapshot()
    {
        using var temporary = new TempDirectory("SharpProof-snapshot-update-");
        var corpusDirectory = CopyCorpus(temporary.FullName);
        var snapshotPath = Path.Combine(corpusDirectory, "expected.canonical.snapshot");
        var before = await File.ReadAllBytesAsync(snapshotPath);
        var previousSource = Environment.GetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable, null);
            await CorpusGate.WriteActualSnapshotAsync(temporary.FullName, cancellationToken: TestContext.CurrentContext.CancellationToken);
        }
        finally
        {
            Environment.SetEnvironmentVariable(OpenSourceCorpusImporter.SourceEnvironmentVariable, previousSource);
        }
        Assert.That(await File.ReadAllBytesAsync(snapshotPath), Is.EqualTo(before));
        Assert.That(CorpusSnapshotFormat.ReadDataLines(snapshotPath), Has.Length.EqualTo(462));
        Assert.That(Directory.EnumerateFiles(corpusDirectory, ".sharpproof-corpus-*", SearchOption.AllDirectories), Is.Empty);
    }

    [Test]
    public async Task GateReportsSnapshotDriftAndUnusedAllowancesThroughItsNormalWorkflow()
    {
        using var temporary = new TempDirectory("SharpProof-snapshot-drift-");
        var corpusDirectory = CopyCorpus(temporary.FullName);
        var snapshotPath = Path.Combine(corpusDirectory, "expected.canonical.snapshot");
        var rows = CorpusSnapshotFormat.ReadDataLines(snapshotPath).ToBuilder();
        var changedRow = rows.Single(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.StartsWith("E01.baseline|", StringComparison.Ordinal)));
        var fields = ImmutableArray.CreateBuilder<string>();
        fields.AddRange(changedRow.Split('|'));
        Assert.That(fields[1], Is.EqualTo("Proven"));
        fields[1] = "Unknown";
        rows[rows.IndexOf(changedRow)] = string.Join('|', fields.ToImmutable());
        var allowedRow = rows.Single(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.StartsWith("OSS0003.baseline|", StringComparison.Ordinal)));
        var allowedFields = ImmutableArray.CreateBuilder<string>();
        allowedFields.AddRange(allowedRow.Split('|'));
        Assert.That(allowedFields[1], Is.EqualTo("Unknown"));
        allowedFields[1] = "Proven";
        rows[rows.IndexOf(allowedRow)] = string.Join('|', allowedFields.ToImmutable());
        var removedRow = rows.Single(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.StartsWith("E02.baseline|", StringComparison.Ordinal)));
        rows.Remove(removedRow);
        rows.Add("obsolete-case-a|Proven|Proven|");
        rows.Add("obsolete-case-b|Proven|Proven|");
        var changedSnapshot = CorpusSnapshotFormat.Render(rows.ToImmutable().OrderBy(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row), StringComparer.Ordinal));
        await File.WriteAllTextAsync(snapshotPath, changedSnapshot);
        var allowancePath = Path.Combine(corpusDirectory, "proven-to-unknown.json");
        const string allowance = """
            {"schemaVersion":1,"allowances":[{"caseId":"obsolete-allowance","reason":"reviewed historical case"},{"caseId":"OSS0003.baseline","reason":"temporary policy fixture for actual unsupported Bellman-Ford source"}]}
            """;
        await File.WriteAllTextAsync(allowancePath, allowance);

        var manifestPath = Path.Combine(corpusDirectory, "oss-methods.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        var misclassified = manifest["methods"]!.AsArray()
            .Single(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method!["id"]!.GetValue<string>() == "OSS0003"))!;
        Assert.That(misclassified["support"]!.GetValue<string>(), Is.EqualTo("IntentionallyUnsupported"));
        Assert.That(misclassified["expectedVerdict"]!.GetValue<string>(), Is.EqualTo("Unknown"));
        misclassified["support"] = "Supported";
        var changedManifest = manifest.ToJsonString();
        await File.WriteAllTextAsync(manifestPath, changedManifest);

        CorpusGateResult result;
        try
        {
            result = await CorpusGate.RunAsync(temporary.FullName, cancellationToken: TestContext.CurrentContext.CancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Data["SharpProof.Corpus.WorkerFailure"] is string diagnostics)
        {
            await TestContext.Progress.WriteLineAsync("corpus-worker-failure: " + diagnostics);
            throw;
        }

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Passed, Is.False);
            Assert.That(result.Failures, Has.Some.StartsWith("Snapshot has 463 cases but generator produced 462."));
            Assert.That(result.Failures, Has.Some.StartsWith("Snapshot verdict for E01.baseline is Unknown"));
            Assert.That(result.Failures, Has.Some.StartsWith("Snapshot mismatch for E01.baseline:"));
            Assert.That(result.Failures, Does.Contain("Missing snapshot entry: E02.baseline."));
            Assert.That(result.Failures, Does.Contain("Stale snapshot entry: obsolete-case-a."));
            Assert.That(result.Failures, Does.Contain("Stale snapshot entry: obsolete-case-b."));
            Assert.That(result.Failures, Does.Contain("Stale or unnecessary Proven->Unknown allowance: obsolete-allowance."));
            Assert.That(await File.ReadAllTextAsync(snapshotPath), Is.EqualTo(changedSnapshot));
            Assert.That(await File.ReadAllTextAsync(allowancePath), Is.EqualTo(allowance));
            Assert.That(result.AllowedDegradations, Does.Contain(
                "OSS0003.baseline: temporary policy fixture for actual unsupported Bellman-Ford source"));
            Assert.That(result.Failures, Does.Not.Contain(
                "Stale or unnecessary Proven->Unknown allowance: OSS0003.baseline."));
            Assert.That(result.SupportedUnknownCount, Is.EqualTo(1));
            Assert.That(result.Failures, Does.Contain(
                "1 supported corpus cases produced Unknown; supported cases " +
                "must have an accountable Proven or Refuted result."));
            Assert.That(await File.ReadAllTextAsync(manifestPath), Is.EqualTo(changedManifest));
        }
    }

    private static string CopyCorpus(string targetRoot)
    {
        System.ArgumentNullException.ThrowIfNull(targetRoot);
        var source = Path.Combine(RepositoryLayout.FindRoot(), "src/SharpProof.Gates", "Corpus");
        var destination = OpenSourceCorpusCatalog.GetCorpusDirectory(targetRoot);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        return destination;
    }
}
