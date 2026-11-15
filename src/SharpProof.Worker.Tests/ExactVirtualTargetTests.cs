using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A dispatched call whose target the program text fixes (a base call, a
// sealed override, a member of a sealed type, or a sealed receiver type) runs
// that body; any other dispatched call stays unknown.
[TestFixture]
[NonParallelizable]
internal sealed class ExactVirtualTargetTests
{
    private const string Types =
        "public class A { public virtual int F(int x) => x + 1; public virtual int P => 7; } " +
        "public class B : A { public override int F(int x) => x + 2; public override int P => 8; } " +
        "public sealed class C : B { } " +
        "public sealed class S : A { public override int F(int x) => x + 3; } " +
        "public class M : A { public sealed override int F(int x) => x + 4; } public class N : M { } " +
        "public class D : B { public int G(int x) => base.F(x); } " +
        "public interface I { int F(int x); } ";

    // C inherits B's override: the final override along the sealed type's bases.
    [TestCase("C c, int x", "c.F(x)", "x + 2", WorkerClaimOutcome.Proven)]
    [TestCase("C c, int x", "c.F(x)", "x + 1", WorkerClaimOutcome.Refuted)]
    [TestCase("S s, int x", "s.F(x)", "x + 3", WorkerClaimOutcome.Proven)]
    [TestCase("N n, int x", "n.F(x)", "x + 4", WorkerClaimOutcome.Proven)]
    [TestCase("D d, int x", "d.G(x)", "x + 2", WorkerClaimOutcome.Proven)]
    [TestCase("C c, int x", "c.P + x", "8 + x", WorkerClaimOutcome.Proven)]
    // An unsealed receiver, an interface and an upcast keep the call dispatched.
    [TestCase("B b, int x", "b.F(x)", "x + 2", WorkerClaimOutcome.Unknown)]
    [TestCase("A a, int x", "a.F(x)", "x + 1", WorkerClaimOutcome.Unknown)]
    [TestCase("I i, int x", "i.F(x)", "x + 1", WorkerClaimOutcome.Unknown)]
    [TestCase("C c, int x", "((A)c).F(x)", "x + 2", WorkerClaimOutcome.Unknown)]
    public async Task ExactTargetsRunTheirBody(string parameters, string call, string expected, WorkerClaimOutcome outcome)
    {
        var claim = await Verify(Types + "public static class T { public static int Target(" + parameters + ") { " +
            "Contract.Ensures(Contract.Result<int>() == " + expected + "); return " + call + "; } }");
        Assert.That(claim.Outcome, Is.EqualTo(outcome), claim.Reason.ToString());
    }

    // A null receiver still throws before the body runs.
    [Test]
    public async Task ANullSealedReceiverIsNotPure()
    {
        var claim = await Verify(Types + "public static class T { [DoesNotThrow] public static int Target(S s, int x) => s.F(x); }");
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> Verify(string types)
    {
        var source = "using SharpProof.Attributes; " + types;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: ImmutableArray.Create(Contract.ConditionalSymbol)),
            "/project/Subject.cs");
        var compilation = CSharpCompilation.Create("ExactTarget_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken);
        using var project = new ShadowTestProject(artifact);
        artifact = project.Artifact;
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        return response.ClaimResults.Single(result => artifact.Manifest.Claims.Any(item =>
            item is null ? throw new System.ArgumentNullException(nameof(item)) :
                (item.ClaimId == result.ClaimId && item.CallableId.Contains("T.Target", StringComparison.Ordinal) &&
                    item.Kind != WorkerClaimKind.CallPrecondition)));
    }
}
