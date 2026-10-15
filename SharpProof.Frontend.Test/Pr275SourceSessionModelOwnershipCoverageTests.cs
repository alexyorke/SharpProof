using System.Numerics;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class Pr275SourceSessionModelOwnershipCoverageTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ActualSharedSourceTreeKeepsCalleeModelOwnershipClosed(bool sharedWithSourceReference)
    {
        const string source = "public static class Subject { public static int Target(int value) => Helper(value); public static int Helper(int value) => value + 17; }";
        var tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.CSharp12), "owned-session.cs");
        var compilation = CSharpCompilation.Create("OwnedSessionRoot", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        if (sharedWithSourceReference)
        {
            var dependency = compilation.WithAssemblyName("OwnedSessionDependency");
            compilation = compilation.AddReferences(dependency.ToMetadataReference());
        }
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = CompilationModelProvider.GetRootSemanticModel(compilation, tree);
        var targetSyntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Target");
        var target = (IMethodSymbol)model.GetDeclaredSymbol(targetSyntax)!;
        var invocation = (IInvocationOperation)model.GetOperation(targetSyntax.ExpressionBody!.Expression)!;
        Assert.That(invocation.TargetMethod.Name, Is.EqualTo("Helper"));
        Assert.That(SymbolEqualityComparer.Default.Equals(invocation.TargetMethod.ContainingAssembly, compilation.Assembly), Is.True);
        Assert.That(compilation.ContainsSyntaxTree(invocation.TargetMethod.DeclaringSyntaxReferences.Single().SyntaxTree), Is.True);
        Assert.That(invocation.Instance, Is.Null);
        Assert.That(invocation.Arguments, Has.Length.EqualTo(1));
        if (sharedWithSourceReference)
        {
            var exception = Assert.Throws<ArgumentException>(() => CompilationModelProvider.GetSemanticModel(compilation, tree));
            Assert.That(exception!.ParamName, Is.EqualTo("tree"));
        }
        else
        {
            Assert.That(CompilationModelProvider.GetSemanticModel(compilation, tree).Compilation, Is.SameAs(compilation));
        }
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join(Environment.NewLine, emitted.Diagnostics));
        var runtime = new AssemblyLoadContext("OwnedSessionRoot", isCollectible: true);
        try
        {
            image.Position = 0;
            var assembly = runtime.LoadFromStream(image);
            Assert.That(assembly.GetType("Subject")!.GetMethod("Target")!.Invoke(null, [3]), Is.EqualTo(20));
        }
        finally
        {
            runtime.Unload();
        }
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var caller = new TotalLoweringContext(factory, target);
        var prepared = 0;
        var session = new TotalSourceCallSession(compilation, _ => { prepared++; return true; }, null, default);
        var admitted = session.TryPrepare(caller, invocation.TargetMethod, invocation.Instance, invocation.Arguments,
            out var frame, out var graph);
        Assert.That(prepared, Is.EqualTo(1));
        Assert.That(admitted, Is.EqualTo(!sharedWithSourceReference));
        Assert.That(session.ConstructionLimitExceeded, Is.False);
        if (sharedWithSourceReference)
        {
            Assert.That(frame, Is.Null);
            Assert.That(graph, Is.Null);
        }
        else
        {
            Assert.That(frame, Is.Not.Null);
            Assert.That(graph, Is.Not.Null);
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(graph!, frame!);
            Assert.That(lowered.IsExact, Is.True);
            Assert.That(lowered.ConstructionLimitExceeded, Is.False);
            var inputs = frame!.Parameters.ToDictionary(parameter => parameter.Entry,
                parameter => factory.CreateIntegerValue(factory.GetVariableInfo(parameter.Entry).Type, 3));
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(20)));
        }
    }
}
