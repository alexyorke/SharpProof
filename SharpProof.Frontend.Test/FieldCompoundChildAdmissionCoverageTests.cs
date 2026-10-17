using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class FieldCompoundChildAdmissionCoverageTests
{
    [TestCase(false, FrontendAbstention.ConversionMayChangeValue)]
    [TestCase(true, FrontendAbstention.UnsupportedMutation)]
    public void ActualFieldCompoundRetainsChildAdmissionAndTypedReplay(bool division, FrontendAbstention reason)
    {
        var members = division
            ? "int Target(int next) { var cell = new Cell(); cell.Value = 4; cell.Value /= next; return cell.Value; }"
            : "int Target(object next) { var cell = new Cell(); cell.Value = 1; cell.Value += (int)next; return cell.Value; }";
        const string cell = "public sealed class Cell { public int Value; }";
        using var rejected = TypedProgramSubject.Create(members, cell);
        var expected = division ? 2 : 3;
        Assert.That(rejected.Invoke([2]), Is.EqualTo(expected));
        var tree = rejected.Compilation.SyntaxTrees.Single();
        var assignment = tree.GetRoot().DescendantNodes().OfType<AssignmentExpressionSyntax>()
            .Single(node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.OperatorToken.ValueText is "+=" or "/="));
        var operation = rejected.Compilation.GetSemanticModel(tree).GetOperation(assignment)!;
        var builder = new IrProgramBuilder(rejected.Factory);
        var entry = builder.CreateBlock("entry");
        var exceptionalExit = builder.CreateBlock("exceptional-exit");
        builder.SetEntry(entry);
        var child = new RoslynTotalExpressionLowerer(rejected.Context, builder, exceptionalExit).LowerBodyValue(operation, entry);
        Assert.That(child.Classification.IsExact, Is.False);
        Assert.That(child.Classification.Abstention, Is.EqualTo(reason));
        var rejectedLowering = rejected.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(rejectedLowering.IsExact, Is.False);
        Assert.That(rejectedLowering.Classification.Abstention, Is.EqualTo(reason));
        Assert.That(rejectedLowering.ConstructionLimitExceeded, Is.False);

        var supportedMembers = division
            ? "int Target(int next) { var cell = new Cell(); cell.Value = 4; cell.Value -= next; return cell.Value; }"
            : "int Target(int next) { var cell = new Cell(); cell.Value = 1; cell.Value += next; return cell.Value; }";
        using var supported = TypedProgramSubject.Create(supportedMembers, cell);
        Assert.That(supported.Invoke([2]), Is.EqualTo(expected));
        var lowering = supported.LowerSourceCalls(false, opaqueCalls: true);
        Assert.That(lowering.IsExact, Is.True);
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        var execution = supported.Execute(lowering, [2]);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }
}
