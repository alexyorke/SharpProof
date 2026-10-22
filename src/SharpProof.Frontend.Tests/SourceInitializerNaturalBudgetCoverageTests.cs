using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class SourceInitializerNaturalBudgetCoverageTests
{
    [TestCase("public sealed class Box { public int First=2, Second=3; }")]
    [TestCase("public sealed class Box { public int First=2; public int Second=3; }")]
    public void ConstructorAndObjectInitializersCompleteAtomicallyAcrossWorkAllowances(string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        using var subject = TypedProgramSubject.Create(
            "int Target() { var item=new Box { Second=7 }; return item.First+item.Second; }", declaration);
        Assert.That(subject.Invoke([]), Is.EqualTo(9));
        var type = subject.Compilation.GetTypeByMetadataName("Box")!;
        var constructor = type.InstanceConstructors.Single();
        int? firstExact = null;
        var limitSeen = false;
        for (var available = 0; available <= 256; available++)
        {
            var exact = CheckAllowance(available);
            if (exact)
            { firstExact ??= available; }
            else
            {
                Assert.That(firstExact, Is.Null, "A larger allowance must not invalidate an exact constructor import.");
                limitSeen = true;
            }
        }
        Assert.That(limitSeen, Is.True);
        var exactAllowance = firstExact ?? throw new AssertionException("No exact constructor import within the tested allowance range.");
        Assert.That(exactAllowance, Is.LessThan(256));
        TestContext.WriteLine("First exact constructor import: " + exactAllowance);

        bool CheckAllowance(int available)
        {
            if (available is < 0 or > 256)
            { throw new ArgumentOutOfRangeException(nameof(available)); }
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var context = new TotalLoweringContext(factory, subject.Context.Target);
            var session = new TotalSourceCallSession(subject.Compilation, static _ => true, null,
                TestContext.CurrentContext.CancellationToken);
            Assert.That(session.Spend(RoslynTotalProgramLowerer.MaximumRegionSteps - available), Is.True);
            var lowered = new RoslynTotalProgramLowerer(context, TestContext.CurrentContext.CancellationToken, session).Lower(subject.Graph);
            Assert.That(session.IsActive(subject.Context.Target), Is.False);
            Assert.That(session.IsActive(constructor), Is.False);
            Assert.That(lowered.ConstructionLimitExceeded, Is.EqualTo(session.ConstructionLimitExceeded));
            if (lowered.ConstructionLimitExceeded)
            {
                Assert.That(lowered.IsExact, Is.False);
                return false;
            }
            Assert.That(lowered.IsExact, Is.True, lowered.Classification.Abstention.ToString());
            var replay = new IrProgramInterpreter(factory).Execute(lowered.Program,
                ImmutableDictionary<IrVarId, IrValue>.Empty, 1000,
                new IrProgramReplayOptions(static _ => null), TestContext.CurrentContext.CancellationToken);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(9)));
            return true;
        }
    }

    [Test]
    public void ActualStaticFieldDeclarationsConsumeTheDefaultSessionBudget()
    {
        const int fieldCount = RoslynTotalProgramLowerer.MaximumRegionSteps / 2;
        var fields = string.Join("\n", Enumerable.Range(0, fieldCount)
            .Select(static ordinal => $"public static int Field{ordinal};"));
        var cells = "public sealed class LargeCell { " + fields + " } " +
            "public sealed class SmallCell { public static int Value; }";
        using var subject = TypedProgramSubject.Create("int Target() => 17;", cells);
        Assert.That(subject.Invoke([]), Is.EqualTo(17));
        var large = subject.Compilation.GetTypeByMetadataName("LargeCell")!;
        var small = subject.Compilation.GetTypeByMetadataName("SmallCell")!;
        Assert.That(large.GetMembers().OfType<IFieldSymbol>().Count(), Is.EqualTo(fieldCount));
        var visitedFields = large.GetMembers().Take(fieldCount).ToImmutableArray();
        Assert.That(visitedFields, Has.Length.EqualTo(fieldCount));
        Assert.That(visitedFields.All(static member => member is null ? throw new System.ArgumentNullException(nameof(member)) : (member is IFieldSymbol { IsStatic: true, HasConstantValue: false, DeclaringSyntaxReferences.Length: 1 })), Is.True);
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
