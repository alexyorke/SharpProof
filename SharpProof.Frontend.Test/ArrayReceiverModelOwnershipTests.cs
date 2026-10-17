using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;
[TestFixture]
public sealed class ArrayReceiverModelOwnershipTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ArraySpecializationRequiresAnUnambiguousSourceModel(bool shared)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) => Helper(values); " + "public static int Helper(System.Collections.Generic.IList<int> values) => values.Count; }");
        var compilation = CSharpCompilation.Create("ArrayReceiverOwner", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        if (shared)
        {
            compilation = compilation.AddReferences(compilation.WithAssemblyName("ArrayReceiverDependency").ToMetadataReference());
        }

        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = CompilationModelProvider.GetRootSemanticModel(compilation, tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var target = model.GetDeclaredSymbol(syntax)!;
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        var admitted = session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out var graph);
        Assert.That(admitted, Is.EqualTo(!shared));
        if (shared)
        {
            Assert.That(frame, Is.Null);
            Assert.That(graph, Is.Null);
        }
        else
        {
            Assert.That(frame, Is.Not.Null);
            Assert.That(graph, Is.Not.Null);
            Assert.That(new RoslynProgramLowerer(factory).LowerCandidate(graph!, frame!).IsExact, Is.True);
        }
    }

    [TestCase("System.Func<int> read = () => values.Count; return read();")]
    [TestCase("int Read() => values.Count; return Read();")]
    public void CapturedParameterReadersKeepOpaqueInterfaceBindings(string body)
    {
        global::System.ArgumentNullException.ThrowIfNull(body);
        var tree = CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int[] values) => Helper(values); " + "public static int Helper(System.Collections.Generic.IList<int> values) { " + body + " } }");
        var compilation = CSharpCompilation.Create("CapturedReceiver", [tree], TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new global::System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new global::System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        var invocation = (IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!;
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var session = new TotalSourceCallSession(compilation, static _ => true, null, default);
        Assert.That(session.TryPrepare(context, invocation.TargetMethod, null, invocation.Arguments, out var frame, out _), Is.True);
        Assert.That(factory.GetTypeInfo(factory.GetVariableInfo(frame!.Parameters[0].Entry).Type).Kind, Is.EqualTo(IrTypeKind.Reference));
    }
}
