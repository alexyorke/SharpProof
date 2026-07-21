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
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(expectedPurity));
            Assert.That(result.MethodEffects.AllocationFree, Is.EqualTo(expectedAllocationFree));
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

        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Disproven));
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

        Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
        Assert.That(result.MethodEffects!.Effects.HasFlag(SharpProofEffect.DispatchUncertainty), Is.True);
    }

    [TestCase("throw new E();", "E", MethodExceptionSource.ExplicitThrow)]
    [TestCase("return 10 / 0;", "System.DivideByZeroException", MethodExceptionSource.RuntimeHazard)]
    public void EscapingExceptionsAreCanonicalStructuredFacts(
        string body,
        string exceptionType,
        MethodExceptionSource source) {
        var result = Analyze($$"""
            sealed class E : System.Exception { }
            class C {
                static int M() { {{body}} }
            }
            """, 3);

        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Proven),
                string.Join(" | ", result.UnknownReasons.Select(static reason => reason.Message)));
            Assert.That(result.MethodEffects.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Disproven));
            Assert.That(result.MethodEffects!.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
                fact.ExceptionType == exceptionType && fact.Source == source &&
                fact.Escape == SharpProofVerdict.Proven));
        });
    }

    [Test]
    public void CaughtExceptionDoesNotEscape() {
        var result = Analyze("""
            sealed class E : System.Exception { }
            class C {
                static int M() {
                    try { throw new E(); }
                    catch (E) { return 1; }
                }
            }
            """, 3);

        Assert.That(result.MethodEffects!.DoesNotThrow, Is.EqualTo(SharpProofVerdict.Proven),
            string.Join(" | ", result.MethodEffects!.ExceptionFacts.Select(static fact =>
                fact.ExceptionType + ":" + fact.Escape + ":" + fact.Reason)));
        Assert.That(result.MethodEffects!.ExceptionFacts,
            Has.Some.Property(nameof(MethodExceptionFact.Escape)).EqualTo(SharpProofVerdict.Disproven));
    }

    [Test]
    public void SourceCalleeExceptionIsTransitive() {
        var result = Analyze("""
            class C {
                static int Throw() => throw new System.FormatException();
                static int M() => Throw();
            }
            """, 3);

        Assert.That(result.MethodEffects!.ExceptionFacts, Has.Some.Matches<MethodExceptionFact>(fact =>
            fact.Source == MethodExceptionSource.Callee && fact.IsTransitive &&
            fact.ExceptionType == "System.FormatException"));
    }

    [Test]
    public void RecursionProducesUnknownEvidence() {
        var result = Analyze("""
            class C {
                static int M(int value) => value == 0 ? 0 : M(value - 1);
            }
            """);

        Assert.Multiple(() => {
            Assert.That(result.MethodEffects!.Purity, Is.EqualTo(SharpProofVerdict.Unknown));
            Assert.That(result.UnknownReasons, Has.Some.Property(nameof(SharpProofUnknownReason.Message))
                .EqualTo("recursive_call"));
        });
    }

    [Test]
    public void LockAndNativeCallsProduceStructuralCapabilities() {
        var lockResult = Analyze("""
            class C {
                static void M(object gate) { lock (gate) { } }
            }
            """);
        var nativeResult = Analyze("""
            using System.Runtime.InteropServices;
            class C {
                [DllImport("native")] static extern int Call();
                static int M() => Call();
            }
            """, 4);

        Assert.Multiple(() => {
            Assert.That(lockResult.MethodEffects!.Effects.HasFlag(SharpProofEffect.Synchronizes), Is.True);
            Assert.That(lockResult.MethodEffects.Capabilities.HasFlag(SharpProofCapability.Synchronization), Is.True);
            Assert.That(nativeResult.MethodEffects!.Effects.HasFlag(SharpProofEffect.UsesNativeCode), Is.True);
            Assert.That(nativeResult.MethodEffects.Capabilities.HasFlag(SharpProofCapability.NativeInterop), Is.True);
        });
    }

    private static SharpProofAnalysisResult Analyze(string source, int line = 2) {
        using var session = SharpProofAnalysisSession.FromText(source);
        return session.Analyze(new SharpProofAnalysisRequest(
            new SharpProofTarget(SharpProofTargetKind.Line, Line: line),
            SharpProofAnalysisFacet.Effects));
    }
}
