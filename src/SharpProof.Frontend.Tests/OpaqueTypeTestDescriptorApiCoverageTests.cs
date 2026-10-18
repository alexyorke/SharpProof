using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class OpaqueTypeTestDescriptorApiCoverageTests
{
    [TestCase("type")]
    [TestCase("parenthesized-pattern")]
    [TestCase("discard-declaration")]
    public void ActualGenericTypeTestShapesShareOperandAndTestedTypeAdmission(string route)
    {
        System.ArgumentNullException.ThrowIfNull(route);
        var expression = route switch
        {
            "type" => "value is object",
            "parenthesized-pattern" => "value is (object)",
            _ => "value is object _"
        };
        using var subject = TypedProgramSubject.Create(
            "bool Target() => Helper<object>(null!); private static bool Helper<T>(T value) => " + expression + ";");
        Assert.That(subject.Invoke([]), Is.False);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.Identifier.ValueText == "Helper"));
        var operation = subject.Compilation.GetSemanticModel(tree).GetOperation(syntax.ExpressionBody!.Expression)!;
        IOperation operand;
        ITypeSymbol testedType;
        if (route == "type")
        {
            Assert.That(operation, Is.InstanceOf<IIsTypeOperation>());
            var test = (IIsTypeOperation)operation;
            operand = test.ValueOperand;
            testedType = test.TypeOperand;
        }
        else
        {
            Assert.That(operation, Is.InstanceOf<IIsPatternOperation>());
            var test = (IIsPatternOperation)operation;
            operand = test.Value;
            if (route == "parenthesized-pattern")
            {
                Assert.That(test.Pattern, Is.InstanceOf<ITypePatternOperation>());
                testedType = ((ITypePatternOperation)test.Pattern).MatchedType;
            }
            else
            {
                Assert.That(test.Pattern, Is.InstanceOf<IDeclarationPatternOperation>());
                var pattern = (IDeclarationPatternOperation)test.Pattern;
                Assert.That(pattern.DeclaredSymbol, Is.Null);
                testedType = pattern.MatchedType!;
            }
        }
        Assert.That(operand, Is.InstanceOf<IParameterReferenceOperation>());
        Assert.That(operand.Type, Is.InstanceOf<ITypeParameterSymbol>());
        Assert.That(operand.Type!.IsReferenceType, Is.False);
        Assert.That(SymbolEqualityComparer.Default.Equals(testedType,
            subject.Compilation.GetSpecialType(SpecialType.System_Object)), Is.True);
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestOperand(operation), Is.SameAs(operand));
        Assert.That(CSharpOperationSemantics.OpaqueTypeTestMayAllocate(operation, subject.Compilation), Is.False);
        using var control = TypedProgramSubject.Create("bool Target(bool value) => value;");
        Assert.That(control.Invoke([false]), Is.False);
        var lowering = control.Lower();
        Assert.That(lowering.IsExact, Is.True);
        var replay = control.Execute(lowering, [false]);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.False);
    }
}
