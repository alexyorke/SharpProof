using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CfgExpressionLoweringCoverageTests
{
    [TestCase("string Target(string text) => text ?? \"fallback\";")]
    [TestCase("long Target(bool flag, long left, long right) => flag ? left : right;")]
    public void ActualCfgCapturesAndNullTestsRemainExact(string members)
    {
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members);
        var factory = new IrFactory();
        var lowerer = new RoslynOperationLowerer(factory);
        var operations = subject.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue is { } branch ? ImmutableArray.Create<IOperation>(branch) : ImmutableArray<IOperation>.Empty))).ToImmutableArray();
        Assert.That(operations.OfType<IFlowCaptureOperation>(), Is.Not.Empty);
        foreach (var operation in operations)
        {
            var lowered = lowerer.Lower(operation);
            Assert.That(lowered.IsExact, Is.True, operation.Kind + ": " + operation.Syntax);
            Assert.That(lowered.Term, Is.Not.Null);
        }
        var captures = lowerer.CreateCaptureBindings();
        Assert.That(captures, Is.Not.Empty);
        Assert.That(captures, Is.Unique);
    }

    [Test]
    public void InvalidCompilerExpressionRetainsInvalidOperationAbstention()
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "public static class Subject { public static int Target() => Missing(); }");
        var compilation = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create("InvalidBinding", [tree],
            TestMetadataReferences.Platform,
            new Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var syntax = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>().Single();
        var operation = compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.Kind, Is.EqualTo(OperationKind.Invalid));
        var result = new RoslynOperationLowerer(new IrFactory()).Lower(operation);
        Assert.That(result.IsExact, Is.False);
        Assert.That(result.Classification.Abstention, Is.EqualTo(FrontendAbstention.InvalidOperation));
        Assert.That(compilation.GetDiagnostics().Single(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)).Id, Is.EqualTo("CS0103"));
        using var image = new System.IO.MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.False);
        var targetSyntax = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Single();
        var target = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(targetSyntax)!;
        var totalFactory = new IrFactory(IrExecutionSemantics.Total);
        var total = new RoslynTotalExpressionLowerer(new TotalLoweringContext(totalFactory, target)).LowerClause(operation);
        Assert.That(total.Classification.IsExact, Is.False);
        Assert.That(total.Classification.Abstention, Is.EqualTo(FrontendAbstention.InvalidOperation));
        Assert.That(new IrInterpreter(totalFactory).Evaluate(total.SafeCondition, global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty).Value!.Boolean, Is.False);
    }
}
