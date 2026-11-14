using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// SharpProofTrustExceptionMessages: a framework exception constructor's
// message lookup is assumed to have no effects. The assumption is declared on
// every effect-claimed callable and marked used only when a proof needs it.
[TestFixture]
[NonParallelizable]
internal sealed class ExceptionMessageTrustTests
{
    private const string Guarded =
        "if (x < 0) { throw new System.ArgumentOutOfRangeException(nameof(x)); } return x;";

    // The runtime's resource lookup enters a lock, which replays as a real
    // synchronization effect.
    [Test]
    public async Task WithoutTrustAMessageLookupRefutesPurity()
    {
        var claim = await Verify("[EnforcePure]", Guarded, trust: false);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(claim.Assumptions, Is.Empty);
        }
    }

    [Test]
    public async Task TrustProvesPurityAndRecordsTheUsedAssumption()
    {
        var claim = await Verify("[EnforcePure]", Guarded, trust: true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
            Assert.That(claim.Assumptions.Select(static assumption => (assumption.Kind, assumption.Used,
                    assumption.Id.StartsWith(ExceptionMessageTrust.AssumptionPrefix, StringComparison.Ordinal))),
                Is.EqualTo(new[] { (WorkerAssumptionKind.TrustedBoundary, true, true) }));
        }
    }

    [Test]
    public async Task AProofThatDoesNotNeedTrustLeavesItUnused()
    {
        var claim = await Verify("[EnforcePure]", "return x + 1;", trust: true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
            Assert.That(claim.Assumptions.Single().Used, Is.False);
        }
    }

    // Trust covers only the message lookup: a real write still refutes.
    [Test]
    public async Task TrustDoesNotHideARealEffect()
    {
        var claim = await Verify("[EnforcePure]",
            "if (x < 0) { throw new System.ArgumentOutOfRangeException(nameof(x)); } State = x; return x;", trust: true);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    // Trust covers only the message lookup, not the throw that follows it.
    [Test]
    public async Task TrustDoesNotProveThatAThrowingMethodDoesNotThrow()
    {
        var claim = await Verify("[DoesNotThrow]", Guarded, trust: true);
        Assert.That(claim.Outcome, Is.Not.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    [Test]
    public async Task TrustProvesThatAnUnreachableThrowDoesNotThrow()
    {
        var claim = await Verify("[DoesNotThrow]",
            "if (x < 0 && x > 0) { throw new System.ArgumentOutOfRangeException(nameof(x)); } return x;", trust: true);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> Verify(string attribute, string body, bool trust)
    {
        var source = "using SharpProof.Attributes; public static class C { public static int State; " +
            attribute + " public static int Target(int x) { " + body + " } }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: ImmutableArray.Create(Contract.ConditionalSymbol)),
            "/project/Subject.cs");
        var compilation = CSharpCompilation.Create("ExceptionMessageTrust_" + Guid.NewGuid().ToString("N"), [tree],
            TestMetadataReferences.WithSharpProof,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable));
        Assert.That(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error), Is.Empty);
        var discovery = new ClaimManifestBuilder(compilation) { TrustExceptionMessages = trust }.Build();
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
            item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.ClaimId == result.ClaimId && item.CallableId.Contains("Target", StringComparison.Ordinal))));
    }
}
