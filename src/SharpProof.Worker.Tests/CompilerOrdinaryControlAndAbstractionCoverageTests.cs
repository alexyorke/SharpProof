using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Compiler.Semantics;
using SharpProof.CompilerArtifact;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerOrdinaryControlAndAbstractionCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task CompilerValidEmptySuppressionReasonCannotSuppressItsOwnClaim(bool emptyReason)
    {
        var source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; public static class Subject { " +
            (emptyReason ? "[SharpProofSuppress(\"\")]" : string.Empty) +
            "public static int Root() { Contract.Ensures(Contract.Result<int>()==1); return 1; } }";
        var compilation = TestCompilation.Create("ControlReasonBoundary", ("control-reason.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        EmitAndInvoke(compilation, false);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = (await tree.GetRootAsync()).DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(model.GetOperation(declaration), Is.InstanceOf<IMethodBodyOperation>());
        if (emptyReason)
        {
            var attribute = method.GetAttributes().Single();
            Assert.That(attribute.AttributeClass!.Name, Is.EqualTo("SharpProofSuppressAttribute"));
            Assert.That(attribute.ConstructorArguments.Single().Value, Is.EqualTo(string.Empty));
            Assert.That(attribute.ApplicationSyntaxReference, Is.Not.Null);
        }
        var diagnostics = new List<Diagnostic>();
        var session = new AnalyzerSession(compilation, CompilerConfiguration.AdvisoryAll, TestContext.CurrentContext.CancellationToken);
        Assert.That(SharpProofControlAttributePolicy.ValidateAndShouldSuppress(method, session,
            diagnostics.Add, TestContext.CurrentContext.CancellationToken), Is.False);
        Assert.That(diagnostics, Has.Count.EqualTo(emptyReason ? 1 : 0));
        if (emptyReason)
        {
            Assert.That(diagnostics.Single().GetMessage(System.Globalization.CultureInfo.InvariantCulture), Does.Contain("expected a non-empty reason"));
            Assert.That(diagnostics.Single().Location.IsInSource, Is.True);
        }
        var artifact = CreateArtifact(compilation);
        var claim = artifact.Manifest.Claims.Single();
        var callable = artifact.Callables.Single();
        Assert.That(callable.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(callable.Total, Is.Not.Null);
        Assert.That(callable.Total!.IsBodyAbstraction, Is.False);
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().ClaimId, Is.EqualTo(claim.ClaimId));
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    private static CompilerManifestArtifact CreateArtifact(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
    }

    private static void EmitAndInvoke(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation, bool instance)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("ControlAndAbstractionBoundary", isCollectible: true);
        try
        {
            var type = runtime.LoadFromStream(image).GetType("Subject")!;
            var receiver = instance ? Activator.CreateInstance(type) : null;
            if (instance)
            {
                type.GetField("Value")!.SetValue(receiver, 3);
            }
            Assert.That(type.GetMethod("Root")!.Invoke(receiver, null), Is.EqualTo(instance ? 3 : 1));
        }
        finally { runtime.Unload(); }
    }
}
