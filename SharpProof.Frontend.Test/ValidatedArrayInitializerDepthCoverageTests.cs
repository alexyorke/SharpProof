using System.Numerics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ValidatedArrayInitializerDepthCoverageTests
{
    private static readonly int[] Inputs = [5, -17];

    [TestCase(253, true)]
    [TestCase(254, false)]
    public void AdmittedLocalInitializerRetainsTheExistingExpressionDepthBoundary(
        int conversionCount, bool exact)
    {
        var casts = string.Concat(Enumerable.Repeat("(int[])", conversionCount));
        using var subject = TypedProgramSubject.Create(
            $"int Target(int value) {{ int local = value; return ({casts}new int[] {{ local }})[0]; }}");
        foreach (var input in Inputs)
        { Assert.That(subject.Invoke([input]), Is.EqualTo(input)); }

        var operation = subject.Graph.Blocks.Single(block =>
            block.FallThroughSuccessor?.Semantics == ControlFlowBranchSemantics.Return).BranchValue;
        Assert.That(operation, Is.InstanceOf<IArrayElementReferenceOperation>());
        var access = (IArrayElementReferenceOperation)operation!;
        IOperation receiver = access.ArrayReference;
        var actualConversions = 0;
        while (receiver is IConversionOperation conversion)
        {
            Assert.That(conversion.Conversion.IsIdentity, Is.True);
            Assert.That(conversion.OperatorMethod, Is.Null);
            Assert.That(SymbolEqualityComparer.Default.Equals(conversion.Type, conversion.Operand.Type), Is.True);
            receiver = conversion.Operand;
            actualConversions++;
        }
        Assert.That(actualConversions, Is.EqualTo(conversionCount));
        Assert.That(receiver, Is.InstanceOf<IArrayCreationOperation>());
        var creation = (IArrayCreationOperation)receiver;
        Assert.That(creation.Initializer!.ElementValues.Single(), Is.InstanceOf<ILocalReferenceOperation>());
        Assert.That(CSharpOperationSemantics.IsSupportedLocalArrayInitializer(creation, subject.Context.Target), Is.True);

        var lowering = subject.Lower();
        Assert.That(lowering.IsExact, Is.EqualTo(exact));
        Assert.That(lowering.Classification.Abstention,
            Is.EqualTo(exact ? FrontendAbstention.None : FrontendAbstention.ExpressionDepthLimit));
        Assert.That(lowering.ConstructionLimitExceeded, Is.False);
        if (exact)
        {
            foreach (var input in Inputs)
            {
                var replay = subject.Execute(lowering, [input]);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new BigInteger(input)));
            }
        }
    }
}
