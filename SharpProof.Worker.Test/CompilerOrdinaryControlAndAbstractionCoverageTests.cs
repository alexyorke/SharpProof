using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;
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
        var session = new AnalyzerSession(compilation, AnalyzerConfiguration.AdvisoryAll, CancellationToken.None);
        Assert.That(SharpProofControlAttributePolicy.ValidateAndShouldSuppress(method, session,
            diagnostics.Add, CancellationToken.None), Is.False);
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
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().ClaimId, Is.EqualTo(claim.ClaimId));
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void RealReceiverClauseCannotBeDroppedByUnsupportedBodyAbstraction(bool unsupportedBody, bool mentionsReceiver)
    {
        var clause = mentionsReceiver ? "Contract.Result<int>() == this.Value" : "Contract.Result<int>() == Contract.Result<int>()";
        var source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; public sealed class Subject { public int Value; " +
            $"public int Root() {{ Contract.Ensures({clause}); " +
            (unsupportedBody ? "decimal ignored=0m;" : string.Empty) + "return this.Value; } }";
        var compilation = TestCompilation.Create("ReceiverAbstractionBoundary", ("receiver.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        EmitAndInvoke(compilation, true);
        var tree = compilation.SyntaxTrees.Single();
        var model = compilation.GetSemanticModel(tree);
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(method.IsStatic, Is.False);
        Assert.That(method.ContainingType.IsSealed, Is.True);
        Assert.That(model.GetOperation(declaration), Is.InstanceOf<IMethodBodyOperation>());
        var returnField = declaration.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        var field = (IFieldReferenceOperation)model.GetOperation(returnField)!;
        Assert.That(field.Instance, Is.InstanceOf<IInstanceReferenceOperation>());
        Assert.That(field.Field.Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(declaration.DescendantNodes().OfType<VariableDeclarationSyntax>().Any(node => node.Type.ToString() == "decimal"), Is.EqualTo(unsupportedBody));
        var artifact = CreateArtifact(compilation);
        var claim = artifact.Manifest.Claims.Single();
        var callable = artifact.Callables.Single();
        Assert.That(callable.CallableId, Is.EqualTo(claim.CallableId));
        if (unsupportedBody && mentionsReceiver)
        {
            Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(callable.Total, Is.Null);
        }
        else
        {
            Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(callable.Total, Is.Not.Null);
            Assert.That(callable.Total!.IsBodyAbstraction, Is.EqualTo(unsupportedBody));
        }
    }

    private static CompilerManifestArtifact CreateArtifact(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
    }

    private static void EmitAndInvoke(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation, bool instance)
    {
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
