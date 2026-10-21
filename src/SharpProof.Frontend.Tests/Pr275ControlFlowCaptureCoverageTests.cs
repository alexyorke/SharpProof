using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class Pr275ControlFlowCaptureCoverageTests
{
    [TestCase("int", "bool condition, int left, int right", "condition ? left : right")]
    [TestCase("string", "string left, string right", "left ?? right")]
    [TestCase("int[]", "int[] left, int[] right", "left ?? right")]
    public void RealControlFlowCapturesReuseTheirVariableForEachReference(string returnType, string parameters, string expression)
    {
        System.ArgumentNullException.ThrowIfNull(expression);
        System.ArgumentNullException.ThrowIfNull(parameters);
        System.ArgumentNullException.ThrowIfNull(returnType);
        var tree = CSharpSyntaxTree.ParseText($"class Subject {{ static {returnType} Target({parameters}) => {expression}; }}");
        var compilation = CSharpCompilation.Create("Pr275Captures", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var graph = ControlFlowGraph.Create(method, compilation.GetSemanticModel(tree))!;
        var operations = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Operations
            .Concat(block.BranchValue == null ? [] : [block.BranchValue])))
            .SelectMany(DescendantsAndSelf).ToImmutableArray();
        var captures = operations.OfType<IFlowCaptureOperation>().ToImmutableArray();
        var references = operations.OfType<IFlowCaptureReferenceOperation>().ToImmutableArray();
        var lowerer = new RoslynOperationLowerer(new IrFactory());

        Assert.That(captures, Is.Not.Empty);
        Assert.That(references, Is.Not.Empty);
        foreach (var capture in captures)
        {
            Assert.That(lowerer.Lower(capture).IsExact, Is.True);
        }
        foreach (var reference in references)
        {
            var first = lowerer.Lower(reference);
            var second = lowerer.Lower(reference);
            Assert.That(first.IsExact, Is.True);
            Assert.That(first.Term, Is.TypeOf<IrVariableTerm>());
            Assert.That(((IrVariableTerm)second.Term).Variable, Is.EqualTo(((IrVariableTerm)first.Term).Variable));
            Assert.That(lowerer.GetReferencedVariable(reference), Is.EqualTo(((IrVariableTerm)first.Term).Variable));
        }
        foreach (var nullTest in operations.OfType<IIsNullOperation>())
        {
            var result = lowerer.Lower(nullTest);
            Assert.That(result.IsExact, Is.True);
            Assert.That(result.Term, Is.TypeOf<IrBinaryTerm>());
            Assert.That(((IrBinaryTerm)result.Term).Operator, Is.EqualTo(IrBinaryOperator.Equal));
        }
        Assert.That(lowerer.CreateCaptureBindings(), Has.Length.EqualTo(captures.Select(capture => capture is null ? throw new System.ArgumentNullException(nameof(capture)) : (capture.Id)).Distinct().Count()));
    }

    [Test]
    public void ReferencedVariablesDistinguishParametersLocalsConversionsAndConstants()
    {
        var tree = CSharpSyntaxTree.ParseText("class Subject { static long Target(int input) { int local = input; return (long)local; } }");
        var compilation = CSharpCompilation.Create("Pr275References", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var parameter = tree.GetRoot().DescendantNodes().OfType<IdentifierNameSyntax>().Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Identifier.Text == "input"));
        var conversion = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();
        var parameterOperation = model.GetOperation(parameter)!;
        var conversionOperation = model.GetOperation(conversion)!;
        var localOperation = ((IConversionOperation)conversionOperation).Operand;
        var lowerer = new RoslynOperationLowerer(new IrFactory());

        var parameterId = lowerer.GetReferencedVariable(parameterOperation);
        var localId = lowerer.GetReferencedVariable(localOperation);

        Assert.That(parameterId, Is.Not.Null);
        Assert.That(localId, Is.Not.Null);
        Assert.That(localId, Is.Not.EqualTo(parameterId));
        Assert.That(lowerer.GetReferencedVariable(conversionOperation), Is.EqualTo(localId));
        Assert.That(lowerer.GetReferencedVariable(conversionOperation, unwrapConversions: false), Is.Null);
        Assert.That(lowerer.CreateVariableIds(), Has.Length.EqualTo(2));
    }

    private static IEnumerable<IOperation> DescendantsAndSelf(IOperation operation)
    {
        System.ArgumentNullException.ThrowIfNull(operation);
        yield return operation;
        foreach (var child in operation.ChildOperations)
        {
            foreach (var descendant in DescendantsAndSelf(child))
            {
                yield return descendant;
            }
        }
    }
}
