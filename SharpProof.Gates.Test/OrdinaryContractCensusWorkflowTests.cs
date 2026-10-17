using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryContractCensusWorkflowTests
{
    [Test]
    public void RepeatedUnchangedCompilerBodyReportsItsActualDuplicateSourceObligation()
    {
        var compilation = AnalyzerGateHost.CreateCompilation("""
            using SharpProof.Attributes;
            public static class Helper {
                internal static int Target(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            public static class Caller {
                public static int Root(int value) => Helper.Target(value);
            }
            """, "OrdinaryDuplicateContractEnrollment", includeExternalEffectsFixture: false);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken);
        Assert.That(batch.Gaps.All(static gap => gap.Reason == "UnsupportedOwnContracts"), Is.True);
        var body = batch.Callers.Single().Body;
        var original = CorpusGate.MatchNativeContractCalls(compilation, [body], TestContext.CurrentContext.CancellationToken);
        var repeated = CorpusGate.MatchNativeContractCalls(compilation, [body, body], TestContext.CurrentContext.CancellationToken);
        var obligation = body.CallPreconditions.Single();
        var marker = (SharpProof.Ir.IrAssignInstruction)body.Program.Blocks.SelectMany(static block => block.Instructions)
            .Single(instruction => instruction.Id == obligation.Instruction);
        var call = body.Program.Factory.GetOperationInfo(marker.Operation).SourceSpan!;
        var clause = body.Program.Factory.GetOperationInfo(obligation.ClauseSite).SourceSpan!;
        var sourceKey = new ContractCoverageKey(body.CallableId, obligation.CalleeIdentity, obligation.ClauseOrdinal,
            new(call.Document, call.Start, call.Length), new(clause.Document, clause.Start, clause.Length));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(original.Expected, Is.EqualTo(1));
            Assert.That(original.Matched, Is.EqualTo(1));
            Assert.That(original.Failures, Is.Empty);
            Assert.That(repeated.Expected, Is.EqualTo(1));
            Assert.That(repeated.Matched, Is.EqualTo(1));
            Assert.That(repeated.Failures, Is.EqualTo(new[] { "DuplicateNativeObligation:" + sourceKey }));
        }
    }

    [TestCase("Contract.Result<int>() > 0", "ResultOutsideEnsures")]
    [TestCase("Contract.Old(value) > 0", "OldOutsideEnsures")]
    public void ActualRequiresIntrinsicPlacementReportsTheSameCensusAndCoverageDiagnostic(
        string expression, string expectedFailure)
    {
        var compilation = AnalyzerGateHost.CreateCompilation($$"""
            using SharpProof.Attributes;
            public static class Helper {
                internal static int Target(int value) {
                    Contract.Requires({{expression}});
                    return value;
                }
            }
            public static class Caller {
                public static int Root(int value) => Helper.Target(value);
            }
            """, "OrdinaryContractCensus", includeExternalEffectsFixture: false);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);

        var census = CorpusGate.CensusContractCalls(compilation);
        var coverage = CorpusGate.MatchNativeContractCalls(compilation, [], TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(census.PublicOwners, Is.EqualTo(1));
            Assert.That(census.PotentialCalls, Is.EqualTo(1));
            Assert.That(census.BoundRequires, Is.Zero);
            Assert.That(census.Complete, Is.True);
            Assert.That(census.Unpublished, Is.True);
            Assert.That(census.ManifestUnchanged, Is.True);
            Assert.That(census.Failures, Is.EqualTo(new[] { expectedFailure }));
            Assert.That(coverage.Expected, Is.Zero);
            Assert.That(coverage.Matched, Is.Zero);
            Assert.That(coverage.Failures, Is.EqualTo(new[] { "RequiresBindingFailed:" + expectedFailure }));
        }
    }
}
