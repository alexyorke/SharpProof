using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerSourceCollectionBoundaryCoverageTests
{
    [TestCase(0, true)]
    [TestCase(4089, false)]
    [TestCase(4090, false)]
    public void ActualTupleDeconstructionUsesTheRemainingOperationBudget(int emptyStatements, bool expectedComplete)
    {
        var source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; public static class Subject { " +
            "public static int Root((int,int) pair) { Contract.Ensures(Contract.Result<int>()==1); int a,b; (a,b)=pair;" +
            new string(';', emptyStatements) + "return 1; } }";
        var compilation = TestCompilation.Create("DeconstructionCollectionBoundary", ("deconstruction.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        EmitAndInvoke(compilation, [(3, 4)]);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot();
        var model = compilation.GetSemanticModel(tree);
        var declaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var assignment = syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        Assert.That(model.GetOperation(assignment), Is.InstanceOf<IDeconstructionAssignmentOperation>());
        var information = model.GetDeconstructionInfo(assignment);
        Assert.That(information.Method, Is.Null);
        Assert.That(information.Nested, Has.Length.EqualTo(2));
        Assert.That(information.Nested.All(item => item.Method == null && item.Nested.IsEmpty), Is.True);
        var operation = model.GetOperation(declaration);
        Assert.That(operation, Is.InstanceOf<IMethodBodyOperation>());
        var pending = new Stack<IOperation>();
        pending.Push(operation!);
        var rank = 0;
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            rank++;
            if (current is IDeconstructionAssignmentOperation)
            { break; }
            foreach (var child in current.ChildOperations)
            { pending.Push(child); }
        }
        Assert.That(rank, Is.EqualTo(emptyStatements + 6));
        var artifact = CreateArtifact(compilation);
        var claim = artifact.Manifest.Claims.Single();
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
        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
    }

    [TestCase(4, true)]
    [TestCase(4096, false)]
    public void RealBoundedCallTreeRetainsAllAdmittedBodies(int helperCount, bool expectedComplete)
    {
        var methods = string.Concat(Enumerable.Range(0, helperCount).Select(index =>
        {
            var left = index * 2 + 1;
            var right = left + 1;
            return $"private static void H{index}() {{ " +
                (left < helperCount ? $"H{left}();" : string.Empty) +
                (right < helperCount ? $"H{right}();" : string.Empty) + "}\n";
        }));
        var source = "#undef SHARPPROOF_CONTRACTS\nusing SharpProof.Attributes; public static class Subject { " +
            "public static int Root() { Contract.Ensures(Contract.Result<int>()==1); H0(); return 1; }\n" + methods + "}";
        var compilation = TestCompilation.Create("ReachableBodyBoundary", ("call-tree.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        EmitAndInvoke(compilation, []);
        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot();
        Assert.That(syntax.DescendantNodesAndSelf().Count(), Is.LessThan(65536));
        var declarations = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        Assert.That(declarations, Has.Length.EqualTo(helperCount + 1));
        var model = compilation.GetSemanticModel(tree);
        foreach (var declaration in declarations)
        {
            var method = (IMethodSymbol)model.GetDeclaredSymbol(declaration)!;
            Assert.That(method.IsStatic, Is.True);
            Assert.That(method.DeclaringSyntaxReferences, Has.Length.EqualTo(1));
            Assert.That(model.GetOperation(declaration), Is.InstanceOf<IMethodBodyOperation>());
        }
        var rootDeclaration = declarations.Single(declaration => declaration.Identifier.ValueText == "Root");
        var artifact = CreateArtifact(compilation);
        var claim = artifact.Manifest.Claims.Single();
        var reachable = artifact.ReachableSource!;
        var root = reachable.Roots.Single();
        var body = reachable.Bodies.Single(candidate => candidate.BodyId == root.BodyId);
        Assert.That(root.CallableId, Is.EqualTo(claim.CallableId));
        Assert.That(body.MethodIdentity, Is.EqualTo(claim.CallableId));
        Assert.That(body.Start, Is.EqualTo(rootDeclaration.SpanStart));
        Assert.That(body.Length, Is.EqualTo(rootDeclaration.Span.Length));
        Assert.That(body.CallsComplete, Is.True);
        Assert.That(body.Graph, Is.Not.Null);
        Assert.That(body.IsCallSkeleton, Is.True);
        Assert.That(reachable.Bodies, Has.Length.EqualTo(expectedComplete ? helperCount + 1 : 4096));
        Assert.That(reachable.CollectionComplete, Is.EqualTo(expectedComplete));
        Assert.That(reachable.Bodies.All(candidate => candidate.CallsComplete), Is.EqualTo(expectedComplete));
        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
    }

    private static CompilerManifestArtifact CreateArtifact(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation)
    {
        return CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
    }

    private static void EmitAndInvoke(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation, object?[] arguments)
    {
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        Assert.That(image.Length, Is.GreaterThan(0));
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("SourceCollectionBoundary", isCollectible: true);
        try
        {
            Assert.That(runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!.Invoke(null, arguments), Is.EqualTo(1));
        }
        finally { runtime.Unload(); }
    }
}
