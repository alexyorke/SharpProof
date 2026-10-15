using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class CompilerSourceDelegateBoundaryTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void SourceAndFrameworkDelegateCallsPreserveIncompleteCapture(bool sourceDelegate)
    {
        var declaration = sourceDelegate ? "public delegate int Step(int value);" : "";
        var delegateType = sourceDelegate ? "Step" : "System.Func<int, int>";
        var source = "using SharpProof.Attributes; " + declaration +
            " public static class Subject { [DoesNotThrow] public static int Root(int value) { " + delegateType +
            " step = Increment; return step(value); } private static int Increment(int value) => value + 1; }";
        var compilation = TestCompilation.Create("SourceDelegateBoundary", ("delegate.cs", source));
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        image.Position = 0;
        var runtime = new System.Runtime.Loader.AssemblyLoadContext("SourceDelegateBoundary", isCollectible: true);
        try
        {
            var root = runtime.LoadFromStream(image).GetType("Subject")!.GetMethod("Root")!;
            Assert.That(root.Invoke(null, [1]), Is.EqualTo(2));
            Assert.That(root.Invoke(null, [4]), Is.EqualTo(5));
        }
        finally { runtime.Unload(); }

        var tree = compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot();
        var rootDeclaration = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method.Identifier.ValueText == "Root");
        var model = compilation.GetSemanticModel(tree);
        var operation = (IMethodBodyOperation)model.GetOperation(rootDeclaration)!;
        var invocation = operation.Descendants().OfType<IInvocationOperation>().Single();
        var invoke = invocation.TargetMethod;
        Assert.That(invoke.MethodKind, Is.EqualTo(MethodKind.DelegateInvoke));
        Assert.That(invoke.ContainingType.TypeKind, Is.EqualTo(TypeKind.Delegate));
        Assert.That(invoke.Parameters.Single().Type.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(invoke.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
        Assert.That(SymbolEqualityComparer.Default.Equals(invoke.ContainingAssembly, compilation.Assembly), Is.EqualTo(sourceDelegate));
        if (sourceDelegate)
        {
            var delegateDeclaration = syntax.DescendantNodes().OfType<DelegateDeclarationSyntax>().Single();
            Assert.That(invoke.IsImplicitlyDeclared, Is.True);
            Assert.That(invoke.DeclaringSyntaxReferences.Single().GetSyntax(), Is.SameAs(delegateDeclaration));
            Assert.That(model.GetOperation(delegateDeclaration), Is.Null);
        }
        else
        {
            Assert.That(invoke.DeclaringSyntaxReferences, Is.Empty);
        }

        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0", WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(), WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        var reachable = artifact.ReachableSource!;
        Assert.That(reachable.CollectionComplete, Is.False);
        var rootBody = reachable.Bodies.Single(body => body.Start == rootDeclaration.SpanStart && body.Length == rootDeclaration.Span.Length);
        Assert.That(rootBody.CallsComplete, Is.False);
        if (sourceDelegate)
        {
            var delegateDeclaration = syntax.DescendantNodes().OfType<DelegateDeclarationSyntax>().Single();
            var delegateBody = reachable.Bodies.Single(body => body.Start == delegateDeclaration.SpanStart && body.Length == delegateDeclaration.Span.Length);
            Assert.That(delegateBody.CallsComplete, Is.False);
            Assert.That(rootBody.Callees, Does.Contain(delegateBody.BodyId));
        }
        else
        {
            Assert.That(reachable.Bodies, Has.Length.EqualTo(1));
            Assert.That(rootBody.Callees, Is.Empty);
        }
    }
}
