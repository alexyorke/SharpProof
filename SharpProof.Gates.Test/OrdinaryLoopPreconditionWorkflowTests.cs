using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Gates.Corpus;
using SharpProof.Worker;
using SharpProof.Worker.Protocol;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class OrdinaryLoopPreconditionWorkflowTests
{
    private const string Source = """
        using SharpProof.Attributes;
        public static class LoopCaller {
            [DoesNotThrow]
            public static int Marker() => 1;
            public static int Root(int n) {
                var i = 0;
                while (i < n) { i++; }
                return Helper.Target(i);
            }
        }
        internal static class Helper {
            internal static int Target(int value) {
                Contract.Requires(value >= 0);
                return value;
            }
        }
        """;

    [Test]
    public async Task ActualLoopCutCannotBecomeARefutationWithoutAReplayableCallWitness()
    {
        var compilation = AnalyzerGateHost.CreateCompilation(Source,
            "OrdinaryLoopPrecondition", includeExternalEffectsFixture: false);
        Assert.That(compilation.GetDiagnostics().Where(static item => item.Severity == DiagnosticSeverity.Error), Is.Empty);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        image.Position = 0;
        var context = new AssemblyLoadContext("OrdinaryLoopPrecondition", isCollectible: true);
        try
        {
            var assembly = context.LoadFromStream(image);
            var method = assembly.GetType("LoopCaller", throwOnError: true)!.GetMethod("Root")!;
            var invoke = method.CreateDelegate<Func<int, int>>();
            Assert.That(invoke(-3), Is.EqualTo(0));
            Assert.That(invoke(0), Is.EqualTo(0));
            Assert.That(invoke(1), Is.EqualTo(1));
            Assert.That(invoke(5), Is.EqualTo(5));
        }
        finally
        {
            context.Unload();
        }

        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), null,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
        Assert.That(batch.Gaps.All(static gap => gap.Reason == "UnsupportedOwnContracts"), Is.True);
        var body = batch.Callers.Single().Body;
        var rootSymbol = (IMethodSymbol)compilation.GetTypeByMetadataName("LoopCaller")!.GetMembers("Root").Single();
        Assert.That(body.CallableId, Is.EqualTo(SemanticClaimIdentity.CreateCallableId(rootSymbol)));
        Assert.That(body.CallPreconditions, Has.Length.EqualTo(1));
        var census = CorpusGate.MatchNativeContractCalls(compilation, [body]);
        Assert.That(census.Expected, Is.EqualTo(1));
        Assert.That(census.Matched, Is.EqualTo(1));
        Assert.That(census.Failures, Is.Empty);
        var encoded = CompilerTotalCallableArtifactCodec.Encode(body);
        Assert.That(encoded, Is.Not.Null);
        var detached = CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId, encoded!, CancellationToken.None);
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.EnrollShadow(detached), out var plan,
            out var reason, CancellationToken.None), Is.True, reason.ToString());
        Assert.That(plan!.LoopSearch, Is.Not.Null);
        using var solver = new PassiveCallableSolver(plan);
        var actual = await solver.VerifyCallPreconditionAsync(0, CancellationToken.None);
        Assert.That(actual.Outcome, Is.Null);
        Assert.That(actual.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(actual.CallPreconditionWitness, Is.Null);

        var verdicts = await NativeCorpusVerifier.VerifyAsync(compilation, CancellationToken.None);
        Assert.That(verdicts.Single(static item => item.Method.Name == "Marker").Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(verdicts.Single(static item => item.Method.Name == "Root").Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(verdicts.Count(static item => item.Method.Name == "Root"), Is.EqualTo(1));
    }
}
