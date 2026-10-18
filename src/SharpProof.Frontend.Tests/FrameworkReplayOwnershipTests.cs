using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class FrameworkReplayOwnershipTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void OnlyTheFrameworkDefaultGetterReceivesReplayAuthority(bool sourceDefined)
    {
        var source = "public static class Subject { public static object Target() => System.Collections.Generic.Comparer<int>.Default; }" +
            (sourceDefined ? "namespace System.Collections.Generic { public class Comparer<T> { public static Comparer<T> Default => null; } }" : "");
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("FrameworkReplayOwnership", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        var lowered = new RoslynProgramLowerer(factory).LowerCandidate(
            Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!, context, static _ => true, null,
            TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
        Assert.That(lowered.IsExact, Is.True);
        var approved = lowered.Program.Blocks.SelectMany(block => block is null ? throw new ArgumentNullException(nameof(block)) : block.Instructions)
            .OfType<IrHavocInstruction>().Count(havoc => havoc is null ? throw new ArgumentNullException(nameof(havoc)) :
                factory.GetOperationInfo(havoc.Operation).Description is { } description && factory.GetString(description) == "framework-default-comparer:System_Int32");
        Assert.That(approved, sourceDefined ? Is.Zero : Is.EqualTo(2));
    }
}
