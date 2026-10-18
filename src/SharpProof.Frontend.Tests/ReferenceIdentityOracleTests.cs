using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend.Host;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

public sealed class ReferenceIdentityOracleTests
{
    [TestCase(false, 0)]
    [TestCase(false, 1)]
    [TestCase(false, 2)]
    [TestCase(false, 3)]
    [TestCase(false, 4)]
    [TestCase(true, 0)]
    [TestCase(true, 1)]
    [TestCase(true, 2)]
    [TestCase(true, 3)]
    [TestCase(true, 4)]
    public void FrameworkReferenceIdentityAgreesWithClr(bool clause, int scenario)
    {
        var tree = CSharpSyntaxTree.ParseText("public static class Probe { public static bool Target(object left, object right) => object.ReferenceEquals(objB: right, objA: left); }");
        var compilation = CSharpCompilation.Create("ReferenceIdentityOracle", [tree], TestMetadataReferences.Platform,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.That(compilation.GetDiagnostics().Where(diagnostic => diagnostic is null ? throw new ArgumentNullException(nameof(diagnostic)) : diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var model = compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, model.GetDeclaredSymbol(syntax)!);
        object? left = scenario is 0 or 1 ? null : new EqualByValue();
        object? right = scenario is 0 or 2 ? null : scenario == 3 ? left : new EqualByValue();
        var inputs = new Dictionary<ScopedIrId<IrVariableTag>, IrValue>
        {
            [clause ? context.Parameters[0].Current : context.Parameters[0].Entry] = left == null ? factory.CreateNullValue(factory.ObjectType) : factory.CreateReferenceValue(factory.ObjectType, left),
            [clause ? context.Parameters[1].Current : context.Parameters[1].Entry] = right == null ? factory.CreateNullValue(factory.ObjectType) : factory.CreateReferenceValue(factory.ObjectType, right)
        };
        bool actual;
        if (clause)
        {
            var lowered = new RoslynTotalExpressionLowerer(context).LowerClause((IInvocationOperation)model.GetOperation(syntax.ExpressionBody!.Expression)!);
            Assert.That(lowered.Classification.IsExact, Is.True);
            var interpreter = new IrInterpreter(factory);
            Assert.That(interpreter.Evaluate(lowered.SafeCondition, inputs).Value!.Boolean, Is.True);
            var evaluated = interpreter.Evaluate(lowered.Value, inputs);
            Assert.That(evaluated.Status, Is.EqualTo(IrEvaluationStatus.Value));
            actual = evaluated.Value!.Boolean;
        }
        else
        {
            var lowered = new RoslynProgramLowerer(factory).LowerCandidate(
                Microsoft.CodeAnalysis.FlowAnalysis.ControlFlowGraph.Create(syntax, model)!, context, static _ => true, null,
                TestContext.CurrentContext.CancellationToken, opaqueCalls: true);
            Assert.That(lowered.IsExact, Is.True);
            var evaluated = new IrProgramInterpreter(factory).Execute(lowered.Program, inputs, 1000);
            Assert.That(evaluated.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(evaluated.ConsumedApproximation, Is.False);
            actual = evaluated.ReturnValue!.Boolean;
        }
        Assert.That(actual, Is.EqualTo(object.ReferenceEquals(left, right)));
    }

    private sealed class EqualByValue
    {
        public override bool Equals(object? obj) { return obj is EqualByValue; }
        public override int GetHashCode() { return 1; }
    }
}
