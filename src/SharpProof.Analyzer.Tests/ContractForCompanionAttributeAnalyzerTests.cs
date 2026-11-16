using System.Collections.Immutable;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

// A companion member may declare effect claims for its target, but an
// EffectContract or ResourceUpperBound on it would be ignored and is an error.
[TestFixture]
public sealed class ContractForCompanionAttributeAnalyzerTests
{
    private static readonly ImmutableArray<string> DiagnosticIds = ["SPCF0011"];

    [TestCase("[EffectContract(SharpProofEffect.None)]", "EffectContractAttribute", TestName = "EffectContractIsRejected")]
    [TestCase("[ResourceUpperBound(ResourceMetric.LoopBodyEntries, 1)]", "ResourceUpperBoundAttribute", TestName = "ResourceUpperBoundIsRejected")]
    public async Task UnsupportedAttributeOnACompanionMemberIsReported(string attribute, string name)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(name);
        var diagnostics = await AnalyzeAsync(Companion(attribute));
        Assert.That(diagnostics.Select(static diagnostic => diagnostic.Id + " " + diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)),
            Is.EqualTo(new[] { "SPCF0011 Companion method 'Map' declares " + name + ", which a ContractFor member cannot declare for its target" }));
    }

    [TestCase("[EnforcePure]")]
    [TestCase("[DoesNotThrow]")]
    [TestCase("[ZeroAllocations]")]
    public async Task EffectAttributesOnACompanionMemberAreAccepted(string attribute)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        Assert.That(await AnalyzeAsync(Companion(attribute)), Is.Empty);
    }

    private static string Companion(string attribute)
    {
        return """
            using SharpProof.Attributes;

            public static class Target
            {
                public static int Map(int value) => value;
            }

            [ContractFor(typeof(Target))]
            public static class TargetContracts
            {
            """ + attribute + """

                public static int Map(int value) => value;
            }
            """;
    }

    private static Task<ImmutableArray<Microsoft.CodeAnalysis.Diagnostic>> AnalyzeAsync(string source)
    {
        return AnalyzerTestHost.AnalyzeAsync(source, mode: "CONTRACTS", enabledIds: DiagnosticIds);
    }
}
