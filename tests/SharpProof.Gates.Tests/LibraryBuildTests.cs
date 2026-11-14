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

    [TestCase("error SP0051: Refuted effect:EnforcePure claim for A.B.", false)]
    [TestCase("error SP0049: The compiler manifest is 40000000 bytes, which exceeds the 33554432 byte limit", false)]
    [TestCase("error SP0049: Unexpected exception while collecting the compiler manifest.", true)]
    [TestCase("error CS0103: The name 'x' does not exist in the current context", true)]
    [TestCase("", true)]
    public void OnlyRefutationsAndTheSizeLimitAreExpectedStops(string log, bool crashed)
    {
        System.ArgumentNullException.ThrowIfNull(log);
        Assert.That(LibraryBuild.IsCrash(exitCode: 1, timedOut: false, log), Is.EqualTo(crashed));
    }

    [Test]
    public void ASuccessfulBuildIsNotACrash()
    {
        Assert.That(LibraryBuild.IsCrash(exitCode: 0, timedOut: false, "warning SP0047: x"), Is.False);
        Assert.That(LibraryBuild.IsCrash(exitCode: 0, timedOut: true, ""), Is.True);
    }
}
