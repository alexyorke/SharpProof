using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ConstantConcatenationUtf16DomainCoverageTests
{
    [TestCase("\\uD800", 0xD800)]
    [TestCase("\\uDC00", 0xDC00)]
    public void ActualConstantConcatenationRetainsUnpairedCodeUnitAndAbstains(
        string escape, int codeUnit)
    {
        using var subject = TypedProgramSubject.Create($"string Target() => \"{escape}\" + \"\";");
        var native = subject.Invoke([]);
        Assert.That(native, Is.TypeOf<string>());
        var text = (string)native!;
        Assert.That(text, Has.Length.EqualTo(1));
        Assert.That((int)text[0], Is.EqualTo(codeUnit));
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = (IBinaryOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        Assert.That(operation.OperatorKind, Is.EqualTo(BinaryOperatorKind.Add));
        Assert.That(operation.OperatorMethod, Is.Null);
        Assert.That(operation.IsLifted, Is.False);
        Assert.That(operation.Type!.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(CSharpOperationSemantics.IsStringConcatenation(operation), Is.True);
        Assert.That(operation.ConstantValue.HasValue, Is.True);
        Assert.That(operation.ConstantValue.Value, Is.TypeOf<string>());
        Assert.That((string)operation.ConstantValue.Value!, Is.EqualTo(text));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.False);
        Assert.That(lowering.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);

        using var control = TypedProgramSubject.Create("""string Target() => "\uD83D\uDE00" + "";""");
        var expected = control.Invoke([]);
        Assert.That(expected, Is.EqualTo("\U0001F600"));
        Assert.That(expected, Is.Not.EqualTo(text));
        var exact = control.Lower();
        Assert.That(exact.IsExact, Is.True);
        var replay = control.Execute(exact, []);
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ReturnValue!.String, Is.EqualTo(expected));
    }
}
