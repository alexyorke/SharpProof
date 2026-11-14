using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class LibraryBuildTests
{
    [Test]
    public void SummaryReportsSizeAgainstTheLimitAndTheVerdict()
    {
        var report = new LibraryBuildReport("nodatime", 1229, 1, false, 17.4, LibraryBuild.ManifestLimitBytes / 2,
            LibraryBuild.ManifestLimitBytes, "Complete",
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, int>("Proven", 62)]),
            ImmutableSortedDictionary<string, int>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableSortedDictionary.CreateRange(StringComparer.Ordinal, [new KeyValuePair<string, int>("error SP0051", 7)]),
            Crashed: false);

        var summary = LibraryBuild.Summarize(report);

        Assert.That(summary, Does.Contain("| Methods annotated | 1229 |"));
        Assert.That(summary, Does.Contain("(50% of the limit)"));
        Assert.That(summary, Does.Contain("| Claims | Proven 62 |"));
        Assert.That(summary, Does.Contain("| Unknown reasons | none |"));
        Assert.That(summary, Does.Contain("| Verdict | completed |"));
    }

    [Test]
    public void ACrashIsTheOnlyFailingVerdict()
    {
        var report = new LibraryBuildReport("nodatime", 1, 1, false, 0, null, LibraryBuild.ManifestLimitBytes, null,
            ImmutableSortedDictionary<string, int>.Empty, ImmutableSortedDictionary<string, int>.Empty,
            ImmutableSortedDictionary<string, int>.Empty, Crashed: true);

        Assert.That(LibraryBuild.Summarize(report), Does.Contain("| Compiler manifest | not written |"));
        Assert.That(LibraryBuild.Summarize(report), Does.Contain("| Verdict | the build crashed or timed out |"));
    }
}
