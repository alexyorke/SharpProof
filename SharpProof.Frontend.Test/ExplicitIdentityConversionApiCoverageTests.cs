using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ExplicitIdentityConversionApiCoverageTests
{
    [TestCase("int Target(int value) => (int)value;", 17)]
    [TestCase("bool Target(bool value) => (bool)value;", true)]
    public void ActualIdentityCastRetainsExactOperandAndNativeReplay(string source, object input)
    {
        using var subject = TypedProgramSubject.Create(source);
        Assert.That(subject.Invoke([input]), Is.EqualTo(input));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<CastExpressionSyntax>().Single();
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax);
        Assert.That(operation, Is.AssignableTo<IConversionOperation>());
        var conversion = (IConversionOperation)operation!;
        Assert.That(conversion.Conversion.IsIdentity, Is.True);
        Assert.That(conversion.OperatorMethod, Is.Null);
        Assert.That(conversion.IsTryCast, Is.False);
        Assert.That(Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
            conversion.Type, conversion.Operand.Type), Is.True);
        var lowerer = new RoslynOperationLowerer(subject.Factory);
        var operand = lowerer.Lower(conversion.Operand);
        Assert.That(operand.Classification.IsExact, Is.True);
        var lowered = lowerer.Lower(conversion);
        Assert.That(lowered.Classification.IsExact, Is.True);
        Assert.That(lowered.Term, Is.SameAs(operand.Term));
        var program = subject.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(program, [input]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        if (input is bool boolean)
        {
            Assert.That(replay.ReturnValue!.Boolean, Is.EqualTo(boolean));
        }
        else
        {
            Assert.That(replay.ReturnValue!.IntegerNumericValue,
                Is.EqualTo(new System.Numerics.BigInteger((int)input)));
        }
    }
}
