using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A postcondition on an override that nothing can override again describes
// that body alone and is verified; on an overridable member it stays Unknown.
[TestFixture]
[NonParallelizable]
internal sealed class SealedOverridePostconditionTests
{
    private const string Base = "public abstract class B { public abstract int F(int x); } ";
    private const string Body = "public override int F(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); return x + 1; }";

    [TestCase("public sealed class Target : B { " + Body + " }", WorkerClaimOutcome.Proven)]
    [TestCase("public class Target : B { public sealed override int F(int x) { Contract.Ensures(Contract.Result<int>() == x + 1); return x + 1; } }", WorkerClaimOutcome.Proven)]
    [TestCase("public class Target : B { " + Body + " }", WorkerClaimOutcome.Unknown)]
    public async Task OnlyAnOverrideThatCannotBeOverriddenAgainIsVerified(string type, WorkerClaimOutcome expected)
    {
        var claim = await Verify(Base + type);
        Assert.That(claim.Outcome, Is.EqualTo(expected), claim.Reason.ToString());
    }

    [Test]
    public async Task AFalsePostconditionOnASealedOverrideIsRefuted()
    {
        var claim = await Verify(Base +
            "public sealed class Target : B { public override int F(int x) { Contract.Ensures(Contract.Result<int>() == x); return x + 1; } }");
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted), claim.Reason.ToString());
    }

    private static async Task<WorkerClaimResult> Verify(string types)
    {
        var source = "using SharpProof.Attributes; " + types;
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12, preprocessorSymbols: ImmutableArray.Create(Contract.ConditionalSymbol)),
            "/project/Subject.cs");
        var compilation = CSharpCompilation.Create("SealedOverride_" + Guid.NewGuid().ToString("N"), [tree],
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
                (item.ClaimId == result.ClaimId && item.CallableId.Contains("Target.F", StringComparison.Ordinal))));
    }
}
