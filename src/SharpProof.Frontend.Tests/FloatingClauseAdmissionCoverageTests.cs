using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FloatingClauseAdmissionCoverageTests
{
    [TestCase("float", false)]
    [TestCase("double", false)]
    [TestCase("int", true)]
    public void ActualParameterClauseKeepsFloatingClosureAndIntegerControl(string valueType, bool expectedExact)
    {
        System.ArgumentNullException.ThrowIfNull(valueType);
        using var subject = TypedProgramSubject.Create("bool Target(" + valueType + " left, " + valueType + " right) => left == right;");
        ImmutableArray<object> arguments = valueType == "float" ? [1f, 1f] : valueType == "double" ? [1d, 1d] : [1, 1];
        Assert.That(subject.Invoke(arguments), Is.True);
        var tree = subject.Compilation.SyntaxTrees.Single();
        var syntax = tree.GetRoot().DescendantNodes().OfType<BinaryExpressionSyntax>().Single();
        var operation = (IBinaryOperation)subject.Compilation.GetSemanticModel(tree).GetOperation(syntax)!;
        var lowerer = new RoslynTotalExpressionLowerer(subject.Context);
        Assert.That(lowerer.LowerClause(operation.LeftOperand).Classification.IsExact, Is.True);
        Assert.That(lowerer.LowerClause(operation.RightOperand).Classification.IsExact, Is.True);
        var clause = lowerer.LowerClause(operation);
        Assert.That(clause.Classification.IsExact, Is.EqualTo(expectedExact));
        Assert.That(clause.Classification.Abstention, Is.EqualTo(expectedExact
            ? FrontendAbstention.None : FrontendAbstention.UnsupportedType));
    }
}
