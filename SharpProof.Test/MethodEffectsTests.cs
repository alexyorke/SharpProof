using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Symbolic;

namespace SharpProof.Test;

[TestFixture]
public sealed class MethodEffectsTests {
    [TestCase("return new object();", SharpProofVerdict.Proven, SharpProofVerdict.Disproven)]
    [TestCase("throw null!;", SharpProofVerdict.Proven, SharpProofVerdict.Proven)]
    public void PurityIsDerivedIndependentlyFromAllocationAndThrows(
        string statement,
        SharpProofVerdict expectedPurity,
        SharpProofVerdict expectedAllocationFree) {
        using var session = SharpProofAnalysisSession.FromText($$"""
            #nullable enable
            class C {
                object M() { {{statement}} }
            }
            """);

        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: 3),
            SharpProofAnalysisFacet.Effects));

        Assert.Multiple(() => {
            Assert.That(result.Status, Is.EqualTo(SharpProofQueryStatus.Succeeded));
            Assert.That(result.Purity, Is.EqualTo(expectedPurity));
            Assert.That(result.AllocationFree, Is.EqualTo(expectedAllocationFree));
        });
    }

    [Test]
    public void VisibleStaticMutationDisprovesPurity() {
        using var session = SharpProofAnalysisSession.FromText("""
            class C {
                static int value;
                static void M() { value++; }
            }
            """);

        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: 3),
            SharpProofAnalysisFacet.Effects));

        Assert.That(result.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
        Assert.That(result.MethodEffects!.Effects.HasFlag(SharpProofEffect.WritesStaticState), Is.True);
    }

    [Test]
    public void UnresolvedDispatchRemainsUnknown() {
        using var session = SharpProofAnalysisSession.FromText("""
            interface I { int Read(); }
            class C {
                static int M(I value) => value.Read();
            }
            """);

        var result = session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: 3),
            SharpProofAnalysisFacet.Effects));

        Assert.That(result.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
        Assert.That(result.MethodEffects!.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
    }
}
