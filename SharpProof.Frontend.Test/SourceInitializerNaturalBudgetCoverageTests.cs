using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceInitializerNaturalBudgetCoverageTests
{
    [Test]
    public void ActualStaticFieldDeclarationsConsumeTheDefaultSessionBudget()
    {
        var fieldCount = RoslynTotalProgramLowerer.MaximumRegionSteps / 2;
        var fields = string.Join("\n", Enumerable.Range(0, fieldCount)
            .Select(static ordinal => $"public static int Field{ordinal};"));
        var cells = "public sealed class LargeCell { " + fields + " } " +
            "public sealed class SmallCell { public static int Value; }";
        using var subject = TypedProgramSubject.Create("int Target() => 17;", cells);
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var large = subject.Compilation.GetTypeByMetadataName("LargeCell")!;
        var small = subject.Compilation.GetTypeByMetadataName("SmallCell")!;
        Assert.That(large.GetMembers().OfType<IFieldSymbol>().Count(), Is.EqualTo(fieldCount));
        var visitedFields = large.GetMembers().Take(fieldCount).ToArray();
        Assert.That(visitedFields, Has.Length.EqualTo(fieldCount));
        Assert.That(visitedFields.All(static member => member is IFieldSymbol { IsStatic: true, HasConstantValue: false, DeclaringSyntaxReferences.Length: 1 }), Is.True);
        Assert.That(large.StaticConstructors, Is.Empty);

        // Enter is the normal root-session operation; declarations spend the default budget.
        // Neither a supplied budget nor a manual Spend call is used.
        var exhausted = new TotalSourceCallSession(subject.Compilation, null, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(exhausted.Enter(subject.Context.Target), Is.True);
        try
        {
            Assert.That(exhausted.HasNoTypeInitialization(large), Is.False);
            Assert.That(exhausted.ConstructionLimitExceeded, Is.True);
            Assert.That(exhausted.IsActive(subject.Context.Target), Is.True);
        }
        finally
        {
            exhausted.Leave(subject.Context.Target);
        }
        Assert.That(exhausted.IsActive(subject.Context.Target), Is.False);

        var supported = new TotalSourceCallSession(subject.Compilation, null, null, TestContext.CurrentContext.CancellationToken);
        Assert.That(supported.Enter(subject.Context.Target), Is.True);
        try
        {
            Assert.That(supported.HasNoTypeInitialization(small), Is.True);
            Assert.That(supported.HasNoTypeInitialization(small), Is.True);
            Assert.That(supported.ConstructionLimitExceeded, Is.False);
        }
        finally
        {
            supported.Leave(subject.Context.Target);
        }
        Assert.That(supported.IsActive(subject.Context.Target), Is.False);
        var program = subject.Lower();
        Assert.That(program.IsExact, Is.True);
        Assert.That(program.ConstructionLimitExceeded, Is.False);
        var replay = subject.Execute(program, []);
        Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(replay.ConsumedApproximation, Is.False);
        Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(17)));
    }
}
