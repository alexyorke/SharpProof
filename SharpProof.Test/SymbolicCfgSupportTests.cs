using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Test;

[TestFixture]
public sealed class SymbolicCfgSupportTests {
    [Test]
    public void ExactCfgState_IsRoutedWithoutApproximation() {
        var fixture = RoslynTestFixture.CreateCompilation(
            "static class C { static int M(int x) { int y = x + 1; return y; } }",
            nameof(ExactCfgState_IsRoutedWithoutApproximation));
        var site = fixture.Root.DescendantNodes().OfType<ReturnStatementSyntax>().Single();
        var lowered = SymbolicCfgProgramPointStateCollector.CollectState(
            site,
            fixture.SemanticModel,
            CancellationToken.None,
            null,
            false);
        var routed = SymbolicReachabilityService.CollectPathStateAt(
            site,
            fixture.SemanticModel,
            CancellationToken.None);

        Assert.Multiple(() => {
            Assert.That(lowered.IsExact, Is.True);
            Assert.That(routed.IsExact, Is.True);
            Assert.That(routed.NormalizedProofKey, Is.EqualTo(lowered.Value!.NormalizedProofKey));
        });
    }

    [Test]
    public void UnsupportedCfgState_ProducesUnknownProof() {
        var fixture = RoslynTestFixture.CreateCompilation(
            "static class C { static int Get() => 1; static void M() { for (int i = Get(); i < 3; i++) { } } }",
            nameof(UnsupportedCfgState_ProducesUnknownProof));
        var loop = fixture.Root.DescendantNodes().OfType<ForStatementSyntax>().Single();
        var lowered = SymbolicCfgProgramPointStateCollector.CollectForInitialEntryState(
            loop,
            fixture.SemanticModel,
            CancellationToken.None);
        var routed = SymbolicReachabilityService.CollectForInitialEntryState(
            loop,
            fixture.SemanticModel,
            CancellationToken.None);
        var proof = new SymbolicProofService(null).ClassifyReachability(routed);

        Assert.Multiple(() => {
            Assert.That(lowered.IsExact, Is.False);
            Assert.That(routed.IsExact, Is.False);
            Assert.That(proof.Status, Is.EqualTo(SymbolicProofStatus.Unknown));
        });
    }
}
