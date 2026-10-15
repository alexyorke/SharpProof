using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class NameOfTypeArgumentApiCoverageTests
{
    [Test]
    public void GenuineNonValueTypeArgumentKeepsBothExpressionApisConservative()
    {
        using var subject = TypedProgramSubject.Create("bool Target() => nameof(System.Int32) == \"Int32\";");
        Assert.That(subject.Invoke([]), Is.True);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var model = subject.Compilation.GetSemanticModel(tree);
        var syntax = tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>().Single();
        var operation = model.GetOperation(syntax)!;
        Assert.That(operation, Is.InstanceOf<INameOfOperation>());
        var nameOf = (INameOfOperation)operation;
        Assert.That(nameOf.ConstantValue.HasValue, Is.True);
        Assert.That(nameOf.ConstantValue.Value, Is.EqualTo("Int32"));
        var argument = nameOf.Argument;
        Assert.That(argument.Kind, Is.EqualTo(OperationKind.None));
        Assert.That(SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(argument.Syntax).Symbol,
            subject.Compilation.GetSpecialType(SpecialType.System_Int32)), Is.True);
        var expression = new RoslynOperationLowerer(subject.Factory).Lower(argument);
        Assert.That(expression.IsExact, Is.False);
        Assert.That(expression.Classification.Abstention, Is.EqualTo(FrontendAbstention.UnsupportedOperationKind));
        var clause = new RoslynTotalExpressionLowerer(subject.Context).LowerClause(argument);
        Assert.That(clause.Classification.IsExact, Is.False);
        Assert.That(clause.Classification.Abstention, Is.EqualTo(FrontendAbstention.InvalidOperation));
        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(lowering, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.Boolean, Is.True);
    }
}
