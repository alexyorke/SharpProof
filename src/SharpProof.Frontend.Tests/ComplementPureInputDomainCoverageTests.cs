using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ComplementPureInputDomainCoverageTests
{
    [TestCase("bool Target(bool value) => value;", true)]
    [TestCase("byte Target(byte value) => value;", (byte)17)]
    public void PureComplementRejectsActualBooleanOrUnpromotedByte(string members, object input)
    {
        System.ArgumentNullException.ThrowIfNull(input);
        System.ArgumentNullException.ThrowIfNull(members);
        using var subject = TypedProgramSubject.Create(members);
        Assert.That(subject.Invoke([input]), Is.EqualTo(input));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(method.ExpressionBody!.Expression)!;
        var owned = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(operation);
        Assert.That(owned.Classification.IsExact, Is.True);
        var rule = CSharpOperationSemantics.Complement(subject.Factory, owned.Value, owned.Value.Type);
        Assert.That(rule.Classification.IsExact, Is.False);
        Assert.That(rule.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(rule.Throws, Is.Empty);
        Assert.That(new IrInterpreter(subject.Factory).Evaluate(rule.Value, new Dictionary<IrVarId, IrValue>()).Value!.Boolean, Is.False);
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, [input]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(input is bool boolean ? replay.ReturnValue!.Boolean == boolean
            : replay.ReturnValue!.IntegerNumericValue == new System.Numerics.BigInteger((byte)input), Is.True);
    }
}
