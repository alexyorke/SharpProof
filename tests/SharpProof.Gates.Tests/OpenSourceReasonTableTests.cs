using System.Collections.Immutable;
using System.Text;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OpenSourceReasonTableTests
{
    [Test]
    public void CheckedInTableMatchesTheManifestAndSnapshot()
    {
        var root = RepositoryLayout.FindRoot();
        var corpus = OpenSourceCorpusCatalog.GetCorpusDirectory(root);
        var expected = OpenSourceReasonTable.Render(
            OpenSourceCorpusCatalog.Load(root),
            CorpusSnapshotFormat.ReadObservations(Path.Combine(corpus, "expected.canonical.snapshot")));

        Assert.That(
            File.ReadAllText(Path.Combine(corpus, OpenSourceReasonTable.FileName)),
            Is.EqualTo(expected),
            "Run corpus-update to regenerate the table.");
    }

    [Test]
    public void EveryMethodIsCountedOnceUnderItsVerdictOrReason()
    {
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var first = document.Methods[0];
        var second = document.Methods[1];
        var advisory = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "SharpProof could not completely analyze selected method 'M': Advisory:Lowering:UnsupportedType"));
        ImmutableArray<CorpusObservation> observations =
        [
            new($"{first.Id}.baseline", CorpusVerdict.Proven, AnalyzerSemanticOutcome.Unknown, []),
            new($"{second.Id}.baseline", CorpusVerdict.Unknown, AnalyzerSemanticOutcome.Abstained,
                [$"SP0047@Warning@a.cs:1:1-1:2@{advisory}", "SP0002@Warning@a.cs:1:1-1:2@eA=="])
        ];

        var rows = OpenSourceReasonTable.Render(document, observations).Split('\n');
        var silent = document.Methods.Length - 2;

        Assert.That(rows, Has.Some.StartsWith("| Proven | 1 |"));
        Assert.That(rows, Has.Some.StartsWith("| SP0002 + SP0047 `Advisory:Lowering:UnsupportedType` | 1 |"));
        Assert.That(rows, Has.Some.StartsWith($"| Unknown without a diagnostic | {silent} |"));
        Assert.That(rows, Has.Some.StartsWith($"| **Total** | {document.Methods.Length} |"));
    }
}
