using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkScalarHelperDomainCoverageTests
{
    [Test]
    public void WholeApplyRejectsActualConditionalWhileSpecializedClauseRetainsBothValues()
    {
        using var subject = TypedProgramSubject.Create("int Target(bool flag, int first, int second) => flag ? first : second;");
        var operation = (IConditionalOperation)Expression(subject);
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        var children = operation.ChildOperations.Select(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (lowerer.LowerClause(child))).ToArray();
        Assert.That(children, Has.Length.EqualTo(3));
        Assert.That(children.All(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Classification.IsExact)), Is.True);
        Closed(subject.Factory, CSharpOperationSemantics.Apply(subject.Factory, operation, [.. children.Select(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Value))]), FrontendAbstention.UnsupportedOperationKind);
        var clause = lowerer.LowerClause(operation);
        Assert.That(clause.Classification.IsExact, Is.True);
        foreach (var flag in new[] { true, false })
        {
            object[] inputs = [flag, 17, 23];
            var expected = flag ? 17 : 23;
            Assert.That(subject.Invoke(inputs), Is.EqualTo(expected));
            var environment = subject.Context.Parameters.ToDictionary(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Current), binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (subject.Value(binding.Current, inputs[binding.Parameter.Ordinal])));
            Assert.That(new IrInterpreter(subject.Factory).Evaluate(clause.SafeCondition, environment).Value!.Boolean, Is.True);
            Assert.That(new IrInterpreter(subject.Factory).Evaluate(clause.Value, environment).Value!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
            Replay(subject, inputs, expected);
        }
    }

    [Test]
    public void WholeApplyRejectsRealPrimitiveLeftUserOperator()
    {
        using var subject = TypedProgramSubject.Create("int Target(int value) { Mark other = default; return value + other; } public readonly struct Mark { public static int operator +(int left, Mark right) => left; }");
        Assert.That(subject.Invoke([17]), Is.EqualTo(17));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = (IBinaryOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.OperatorMethod, Is.Not.Null);
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        var children = operation.ChildOperations.Select(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (lowerer.LowerClause(child))).ToArray();
        Assert.That(children, Has.Length.EqualTo(2));
        Assert.That(children.All(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Classification.IsExact)), Is.True);
        Closed(subject.Factory, CSharpOperationSemantics.Apply(subject.Factory, operation, [.. children.Select(child => child is null ? throw new System.ArgumentNullException(nameof(child)) : (child.Value))]), FrontendAbstention.UserDefinedOperator);
        var clause = lowerer.LowerClause(operation);
        Assert.That(clause.Classification.IsExact, Is.False);
        Assert.That(clause.Classification.Abstention, Is.EqualTo(FrontendAbstention.UserDefinedOperator));
        using var supported = TypedProgramSubject.Create("int Target(int value) => value + 0;");
        Assert.That(supported.Invoke([17]), Is.EqualTo(17));
        Replay(supported, [17], 17);
    }

    [Test]
    public void WholeCompoundRejectsActualLiftedShiftWithOwnedNullableParameters()
    {
        using var subject = TypedProgramSubject.Create("int? Target(int? value, int? count) { value <<= count; return value; }");
        Assert.That(subject.Invoke([1, 1]), Is.EqualTo(2));
        Assert.That(subject.Invoke([null!, 1]), Is.Null);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>().Single();
        var operation = (ICompoundAssignmentOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.OperatorKind, Is.EqualTo(BinaryOperatorKind.LeftShift));
        Assert.That(operation.IsLifted, Is.True);
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        var left = lowerer.LowerClause(operation.Target);
        var right = lowerer.LowerClause(operation.Value);
        Assert.That(left.Classification.IsExact, Is.True);
        Assert.That(right.Classification.IsExact, Is.True);
        Closed(subject.Factory, CSharpOperationSemantics.Compound(subject.Factory, operation, left.Value, right.Value, ((Microsoft.CodeAnalysis.CSharp.CSharpCompilation)subject.Compilation).Options.CheckOverflow), FrontendAbstention.UnsupportedMutation);
        using var supported = TypedProgramSubject.Create("int Target(int value, int count) { value <<= count; return value; }");
        Assert.That(supported.Invoke([1, 1]), Is.EqualTo(2));
        Replay(supported, [1, 1], 2);
    }

    [Test]
    public void WholeBitwiseRejectsActualAdditionIrOperatorAndAcceptsActualAndOperator()
    {
        using var subject = TypedProgramSubject.Create("int Target(int left, int right) => left + right;");
        Assert.That(subject.Invoke([7, 3]), Is.EqualTo(10));
        var clause = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(Expression(subject));
        Assert.That(clause.Classification.IsExact, Is.True);
        var addition = (IrBinaryTerm)clause.Value;
        Assert.That(addition.Operator, Is.EqualTo(IrBinaryOperator.Add));
        Closed(subject.Factory, CSharpOperationSemantics.Bitwise(subject.Factory, addition.Operator, addition.Left, addition.Right), FrontendAbstention.UnsupportedOperationKind);
        Replay(subject, [7, 3], 10);
        using var supported = TypedProgramSubject.Create("int Target(int left, int right) => left & right;");
        Assert.That(supported.Invoke([7, 3]), Is.EqualTo(3));
        var exact = new RoslynTotalExpressionLowerer(supported.Context).LowerClause(Expression(supported));
        Assert.That(exact.Classification.IsExact, Is.True);
        var binary = (IrBinaryTerm)exact.Value;
        var rule = CSharpOperationSemantics.Bitwise(supported.Factory, binary.Operator, binary.Left, binary.Right);
        Assert.That(rule.Classification.IsExact, Is.True);
        Assert.That(rule.Throws, Is.Empty);
        var environment = supported.Context.Parameters.ToDictionary(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Current), binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (supported.Value(binding.Current, binding.Parameter.Ordinal == 0 ? 7 : 3)));
        Assert.That(new IrInterpreter(supported.Factory).Evaluate(rule.Value, environment).Value!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(3)));
        Replay(supported, [7, 3], 3);
    }

    [Test]
    public void WholeBitwiseRejectsActualStringDomainWithNativeAndExactReplayControl()
    {
        using var subject = TypedProgramSubject.Create("string Target(string value) => value;");
        Assert.That(subject.Invoke(["word"]), Is.EqualTo("word"));
        var owned = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(Expression(subject));
        Assert.That(owned.Classification.IsExact, Is.True);
        Closed(subject.Factory, CSharpOperationSemantics.Bitwise(subject.Factory, IrBinaryOperator.BitwiseAnd, owned.Value, owned.Value), FrontendAbstention.UnsupportedOperationKind);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var environment = subject.Context.Parameters.ToDictionary(binding => binding is null ? throw new System.ArgumentNullException(nameof(binding)) : (binding.Entry), _ => subject.Factory.CreateStringValue("word"));
        var replay = new IrProgramInterpreter(subject.Factory).Execute(lowering.Program, environment);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.String, Is.EqualTo("word"));
        using var supported = TypedProgramSubject.Create("int Target(int left, int right) => left & right;");
        Assert.That(supported.Invoke([7, 3]), Is.EqualTo(3));
        Replay(supported, [7, 3], 3);
    }

    private static Microsoft.CodeAnalysis.IOperation Expression(TypedProgramSubject subject)
    {
        System.ArgumentNullException.ThrowIfNull(subject);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Target"));
        return subject.Compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
    }

    private static void Closed(IrFactory factory, TotalScalarRule rule, FrontendAbstention reason)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(reason));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(factory).Evaluate(rule.Value, new Dictionary<IrVarId, IrValue>()).Value!.Boolean, Is.False);
    }

    private static void Replay(TypedProgramSubject subject, object[] inputs, int expected)
    {
        System.ArgumentNullException.ThrowIfNull(inputs, nameof(inputs));
        System.ArgumentNullException.ThrowIfNull(subject);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, inputs);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
