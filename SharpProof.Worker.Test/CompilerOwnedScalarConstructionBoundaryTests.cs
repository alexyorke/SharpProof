using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerOwnedScalarConstructionBoundaryTests
{
    [TestCase(3, true)]
    [TestCase(4097, false)]
    public async Task OwnedScalarBodyRetainsOnlyCompleteConstruction(int statementCount, bool expectedComplete)
    {
        var bodySource = "var result=value;" + string.Concat(Enumerable.Repeat("result+=1;", statementCount)) + "return result;";
        var source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes;" +
            "public static class Subject { [DoesNotThrow] public static int Root(int value) {" + bodySource + "} }";
        var compilation = TestCompilation.Create("OwnedScalarConstructionBoundary", ("scalar-boundary.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("OwnedScalarConstructionBoundary", isCollectible: true);
        try
        {
            Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!.Invoke(null, [1]),
                Is.EqualTo(statementCount + 1));
        }
        finally { runtime.Unload(); }
        var tree = compilation.SyntaxTrees.Single();
        var syntax = await tree.GetRootAsync();
        Assert.That(syntax.DescendantNodesAndSelf().Count(), Is.LessThan(65536));
        var declaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var model = compilation.GetSemanticModel(tree);
        var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
        Assert.That(method.IsStatic, Is.True);
        Assert.That(method.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(method.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
        Assert.That(model.GetOperation(declaration), Is.InstanceOf<IMethodBodyOperation>());
        var statements = declaration.Body!.Statements.OfType<ExpressionStatementSyntax>().ToArray();
        Assert.That(statements, Has.Length.EqualTo(statementCount));
        foreach (var statement in statements)
        {
            Assert.That(model.GetOperation(statement.Expression), Is.InstanceOf<ICompoundAssignmentOperation>());
            var addition = (ICompoundAssignmentOperation)model.GetOperation(statement.Expression)!;
            Assert.That(addition.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
            Assert.That(addition.OperatorMethod, Is.Null);
            Assert.That(addition.Value.ConstantValue.Value, Is.EqualTo(1));
        }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var claim = artifact.Manifest.Claims.Single();
        Assert.That(claim.EffectContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        var callable = artifact.Callables.Single();
        Assert.That(callable.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(callable.FailureReason, Is.EqualTo(WorkerClaimReason.None));
        var reachable = artifact.ReachableSource!;
        var root = reachable.Roots.Single();
        var body = reachable.Bodies.Single();
        Assert.That(root.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(root.BodyId, Is.EqualTo(body.BodyId));
        Assert.That(body.MethodIdentity, Is.EqualTo(claim.CallableId));
        Assert.That(body.Start, Is.EqualTo(declaration.SpanStart));
        Assert.That(body.Length, Is.EqualTo(declaration.Span.Length));
        Assert.That(body.CallsComplete, Is.EqualTo(expectedComplete));
        Assert.That(reachable.CollectionComplete, Is.EqualTo(expectedComplete));
        Assert.That(body.Graph != null, Is.EqualTo(expectedComplete));
        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var total = preparations.Single().Total;
        if (expectedComplete)
        {
            Assert.That(callable.Total, Is.Not.Null);
            Assert.That(total, Is.Not.Null);
            Assert.That(total!.IsBodyAbstraction, Is.False);
            var factory = total.Program.Factory;
            var entry = total.Parameters.Single().Entry;
            var execution = new IrProgramInterpreter(factory).Execute(total.Program,
                new Dictionary<IrVarId, IrValue> { [entry] = factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, 1) });
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(statementCount + 1)));
        }
        else
        {
            Assert.That(callable.Total, Is.Null);
            Assert.That(total, Is.Null);
        }
        using var project = new ShadowTestProject(artifact, cacheEnabled: false);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        Assert.That(result.ClaimId, Is.EqualTo(claim.ClaimId));
        Assert.That(result.Outcome, Is.EqualTo(expectedComplete ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo(expectedComplete ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
    }
}
