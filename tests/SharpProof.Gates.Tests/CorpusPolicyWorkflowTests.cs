using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusPolicyWorkflowTests
{
    [TestCase("proven-to-unknown.json", "{\"schemaVersion\":2,\"allowances\":[]}",
        "Unsupported Proven->Unknown allowance schema.")]
    [TestCase("proven-to-unknown.json", "{\"schemaVersion\":1,\"allowances\":[{\"caseId\":\" \",\"reason\":\"reviewed\"}]}",
        "Every Proven->Unknown allowance needs a caseId and explanation.")]
    [TestCase("proven-to-unknown.json", "{\"schemaVersion\":1,\"allowances\":[{\"caseId\":\"E01.baseline\",\"reason\":\" \"}]}",
        "Every Proven->Unknown allowance needs a caseId and explanation.")]
    [TestCase("proven-to-unknown.json", "{\"schemaVersion\":1,\"allowances\":[{\"caseId\":\"E01.baseline\",\"reason\":\"first\"},{\"caseId\":\"E01.baseline\",\"reason\":\"second\"}]}",
        "Duplicate Proven->Unknown allowance: E01.baseline")]
    [TestCase("unknown-reason-ratchet.json", "{\"schemaVersion\":1}",
        "Unsupported corpus Unknown-reason ratchet schema.")]
    [TestCase("unknown-reason-ratchet.json", "{\"schemaVersion\":2,\"minimumSupportedCases\":-1,\"minimumSupportedOpenSourceMethods\":0,\"maximumTotalUnknown\":0,\"maximumByReason\":{}}",
        "Corpus support minima and Unknown maxima cannot be negative.")]
    [TestCase("unknown-reason-ratchet.json", "{\"schemaVersion\":2,\"minimumSupportedCases\":0,\"minimumSupportedOpenSourceMethods\":-1,\"maximumTotalUnknown\":0,\"maximumByReason\":{}}",
        "Corpus support minima and Unknown maxima cannot be negative.")]
    [TestCase("unknown-reason-ratchet.json", "{\"schemaVersion\":2,\"minimumSupportedCases\":0,\"minimumSupportedOpenSourceMethods\":0,\"maximumTotalUnknown\":-1,\"maximumByReason\":{}}",
        "Corpus support minima and Unknown maxima cannot be negative.")]
    [TestCase("unknown-reason-ratchet.json", "{\"schemaVersion\":2,\"minimumSupportedCases\":0,\"minimumSupportedOpenSourceMethods\":0,\"maximumTotalUnknown\":0,\"maximumByReason\":{\" \":0}}",
        "Corpus Unknown-reason maxima need a name and a non-negative count.")]
    [TestCase("unknown-reason-ratchet.json", "{\"schemaVersion\":2,\"minimumSupportedCases\":0,\"minimumSupportedOpenSourceMethods\":0,\"maximumTotalUnknown\":0,\"maximumByReason\":{\"Unsupported\":-1}}",
        "Corpus Unknown-reason maxima need a name and a non-negative count.")]
    [TestCase("unknown-reason-ratchet.json", "{\"schemaVersion\":2,\"minimumSupportedCases\":0,\"minimumSupportedOpenSourceMethods\":0,\"maximumTotalUnknown\":0,\"maximumByReason\":{\"Unsupported\":0,\"Unsupported\":1}}",
        "Duplicate corpus Unknown reason: Unsupported.")]
    public void GateRejectsInvalidPolicyBeforeRunningCases(
        string policyFile, string contents, string expectedMessage)
    {
        System.ArgumentNullException.ThrowIfNull(contents);
        System.ArgumentNullException.ThrowIfNull(expectedMessage);
        System.ArgumentNullException.ThrowIfNull(policyFile);
        using var temporary = new TempDirectory("SharpProof-corpus-policy-");
        var source = Path.Combine(RepositoryLayout.FindRoot(), "tests/SharpProof.Gates", "Corpus");
        var destination = Path.Combine(temporary.FullName, "tests/SharpProof.Gates", "Corpus");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        File.WriteAllText(Path.Combine(destination, policyFile), contents);

        var error = Assert.ThrowsAsync<InvalidDataException>(
            (Func<Task>)(async () => await CorpusGate.RunAsync(temporary.FullName, cancellationToken: TestContext.CurrentContext.CancellationToken)));

        Assert.That(error!.Message, Is.EqualTo(expectedMessage));
        Assert.That(File.ReadAllText(Path.Combine(destination, policyFile)), Is.EqualTo(contents));
    }

    [Test]
    public void GateRejectsRepeatedCanonicalSnapshotCaseBeforeRunningCases()
    {
        using var temporary = new TempDirectory("SharpProof-duplicate-snapshot-");
        var source = Path.Combine(RepositoryLayout.FindRoot(), "tests/SharpProof.Gates", "Corpus");
        var destination = OpenSourceCorpusCatalog.GetCorpusDirectory(temporary.FullName);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
        var snapshotPath = Path.Combine(destination, "expected.canonical.snapshot");
        var rows = CorpusSnapshotFormat.ReadDataLines(snapshotPath).ToBuilder();
        var repeatedRow = rows.Single(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.StartsWith("E01.baseline|", StringComparison.Ordinal)));
        rows.Add(repeatedRow);
        var contents = CorpusSnapshotFormat.Render(rows.ToImmutable().OrderBy(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row), StringComparer.Ordinal));
        File.WriteAllText(snapshotPath, contents);

        var error = Assert.ThrowsAsync<InvalidDataException>(
            (Func<Task>)(async () => await CorpusGate.RunAsync(temporary.FullName, cancellationToken: TestContext.CurrentContext.CancellationToken)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(error!.Message, Is.EqualTo("Duplicate corpus snapshot case: E01.baseline"));
            Assert.That(File.ReadAllText(snapshotPath), Is.EqualTo(contents));
            Assert.That(Directory.EnumerateFiles(destination, ".sharpproof-corpus-*", SearchOption.AllDirectories), Is.Empty);
        }
    }
}
