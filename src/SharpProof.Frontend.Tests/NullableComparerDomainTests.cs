using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class NullableComparerDomainTests
{
    [TestCase(false, 0)]
    [TestCase(true, 0)]
    [TestCase(false, 255)]
    public void PureComparisonClausesRetainReceiverAndDepthSafety(bool isNull, int depth)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static int Target(System.Collections.Generic.Comparer<int> comparer, int x, int y) => comparer.Compare(x, y); }");
        var compilation = CSharpCompilation.Create("NullableComparerClause", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        context.SpecializeNullableDefaultComparerParameter(context.Parameters[0].Parameter);
        context.TrackDefaultComparers(Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!);
        var lowered = new RoslynTotalExpressionLowerer(context).LowerClause((IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!, TotalParameterState.Current, depth);
        if (depth != 0)
        {
            Assert.That(lowered.Classification.IsExact, Is.False);
            return;
        }
        Assert.That(lowered.Classification.IsExact, Is.True);
        var type = factory.GetVariableInfo(context.Parameters[0].Current).Type;
        var inputs = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
        {
            [context.Parameters[0].Current] = isNull ? factory.CreateNullValue(type) : factory.CreateReferenceValue(type, Comparer<int>.Default),
            [context.Parameters[1].Current] = factory.CreateIntegerValue(1),
            [context.Parameters[2].Current] = factory.CreateIntegerValue(2)
        };
        var interpreter = new IrInterpreter(factory);
        var safe = interpreter.Evaluate(lowered.SafeCondition, inputs);
        Assert.That(safe.Status, Is.EqualTo(IrEvaluationStatus.Value));
        Assert.That(safe.Value!.Boolean, Is.EqualTo(!isNull));
        if (!isNull)
        { Assert.That(interpreter.Evaluate(lowered.Value, inputs).Value!.Integer, Is.EqualTo(Comparer<int>.Default.Compare(1, 2))); }
    }
    [TestCase(false)]
    [TestCase(true)]
    public void ReceiverDefinitionsMustIncludeUnknownParameterEntryValues(bool comparer)
    {
        var source = comparer
            ? "public static class Probe { public static int Target(System.Collections.Generic.Comparer<int> value) { var before = value; value = System.Collections.Generic.Comparer<int>.Default; return before.Compare(1, 2); } }"
            : "public static class Probe { public static int Target(System.Collections.Generic.IList<int> value) { var before = value; value = new int[1]; return before.Count; } }";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("ReceiverEntryOwnership", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var context = new TotalLoweringContext(new IrFactory(IrExecutionSemantics.Total), model.GetDeclaredSymbol(syntax)!);
        var graph = Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!;
        context.TrackArrayReceivers(graph);
        context.TrackDefaultComparers(graph);
        if (comparer)
        {
            var invocation = (IInvocationOperation)model.GetOperation(syntax.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!)!;
            Assert.That(context.IsDefaultIntegerComparison(invocation), Is.False);
        }
        else
        {
            var property = (IPropertyReferenceOperation)model.GetOperation(syntax.Body!.Statements.OfType<ReturnStatementSyntax>().Single().Expression!)!;
            Assert.That(context.ConcreteArray(property.Instance!), Is.Null);
        }
    }
}
