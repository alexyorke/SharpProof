using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A recursive call into the verified method is assumed pure only by the
// purity check, by induction on recursion depth. Every other check, and a
// recursive helper that is not the verified method, still sees an unknown call.
[TestFixture]
[NonParallelizable]
internal sealed class RecursivePurityInductionTests
{
    [TestCase("[EnforcePure] public static int Target(int n) => n <= 1 ? 1 : n * Target(n - 1);")]
    [TestCase("[EnforcePure] public static int Target(int a, int b) => b == 0 ? a : Target(b, a % b);")]
    [TestCase("[EnforcePure] public static int Target(int n) => n <= 0 ? 0 : Helper(n - 1); " +
        "public static int Helper(int n) => n <= 0 ? 1 : Target(n - 1);")]
    [TestCase("[EnforcePure] public static int Target(int n) { if (n <= 0) { return 0; } var x = Target(n - 1); return x + Target(n - 2); }")]
    [TestCase("[EnforcePure] public static int Target(int n) { try { return n <= 0 ? 0 : Target(n - 1); } catch (System.Exception) { return -1; } }")]
    public async Task APureRecursiveMethodIsProvenPure(string members)
    {
        var claim = await Verify(members);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // An impure frame anywhere in the recursion is still found.
    [TestCase("[EnforcePure] public static int Target(int n) { if (n <= 0) { State = 1; return 0; } return Target(n - 1); }")]
    [TestCase("[EnforcePure] public static int Target(int n) { var r = n <= 0 ? 0 : Target(n - 1); State = r; return r; }")]
    public async Task AnImpureRecursiveMethodIsNotProven(string members)
    {
        var claim = await Verify(members);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // Not the inductive case: a precondition the nested call might violate,
    // a recursive helper, and checks other than purity.
    [TestCase("[EnforcePure] public static int Target(int n) { Contract.Requires(n >= 0); return n == 0 ? 1 : n * Target(n - 1); }")]
    [TestCase("[EnforcePure] public static int Target(int n) => Helper(n); public static int Helper(int n) => n <= 1 ? 1 : n * Helper(n - 1);")]
    [TestCase("[ZeroAllocations] public static int Target(int n) => n <= 1 ? 1 : n * Target(n - 1);")]
    [TestCase("[DoesNotThrow] public static int Target(int n) => n <= 1 ? 1 : n * Target(n - 1);")]
    public async Task OtherRecursionStaysUnknown(string members)
    {
        var claim = await Verify(members);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown), claim.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> Verify(string members)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; " + members + " }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: ImmutableArray.Create(Contract.ConditionalSymbol)),
            "/project/Subject.cs");
        var compilation = CSharpCompilation.Create("RecursivePurity_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
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
                (item.ClaimId == result.ClaimId && item.CallableId.Contains("Target", StringComparison.Ordinal) &&
                    item.EffectContractKind != WorkerEffectContractKind.Unspecified)));
    }
}
