using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

// A callee expansion that runs out of work gives its work back to the
// caller, but only up to a fixed allowance, so one lowering stays bounded.
public sealed class SourceCallBudgetRecoveryTests
{
    private static CSharpCompilation Compilation()
    {
        return CSharpCompilation.Create("BudgetRecovery", [CSharpSyntaxTree.ParseText("public static class C { public static void R() { } public static void A() { } }")],
            TestMetadataReferences.Platform, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static TotalSourceCallSession Session(CSharpCompilation? compilation = null)
    { return new TotalSourceCallSession(compilation ?? Compilation(), static _ => true, null, default); }

    // A frame below the root leaves the last budget of the allowance to the
    // root's own calls.
    [Test]
    public void FramesBelowTheRootKeepABudgetInReserve()
    {
        var compilation = Compilation();
        var type = compilation.GetTypeByMetadataName("C")!;
        var session = Session(compilation);
        Assert.That(session.Enter(type.GetMembers("R").OfType<IMethodSymbol>().Single()), Is.True);
        // The root's call of A, which A's own calls run inside.
        var rootMark = session.Mark();
        Assert.That(session.Enter(type.GetMembers("A").OfType<IMethodSymbol>().Single()), Is.True);
        var nested = 0;
        while (true)
        {
            var mark = session.Mark();
            Assert.That(session.Spend(RoslynTotalProgramLowerer.MaximumRegionSteps), Is.False);
            if (!session.TryRecover(mark))
            { break; }
            nested++;
        }
        Assert.That(nested, Is.LessThan(TotalSourceCallSession.MaximumRecoveredSteps / RoslynTotalProgramLowerer.MaximumRegionSteps));
        session.Leave(type.GetMembers("A").OfType<IMethodSymbol>().Single());
        Assert.That(session.TryRecover(rootMark), Is.True);
        Assert.That(session.ConstructionLimitExceeded, Is.False);
    }

    [Test]
    public void ExhaustedWorkIsGivenBackOnlyWithinTheAllowance()
    {
        var session = Session();
        Assert.That(session.Spend(10), Is.True);
        var recoveries = 0;
        while (true)
        {
            var mark = session.Mark();
            Assert.That(session.Spend(RoslynTotalProgramLowerer.MaximumRegionSteps), Is.False);
            Assert.That(session.ConstructionLimitExceeded, Is.True);
            if (!session.TryRecover(mark))
            { break; }
            recoveries++;
            Assert.That(session.ConstructionLimitExceeded, Is.False);
        }
        // Each pass gives back the whole remaining budget, so the allowance
        // covers a bounded number of them; then the budget stays exhausted.
        Assert.That(recoveries, Is.EqualTo(TotalSourceCallSession.MaximumRecoveredSteps / (RoslynTotalProgramLowerer.MaximumRegionSteps - 10)));
        Assert.That(session.ConstructionLimitExceeded, Is.True);
        Assert.That(session.Spend(1), Is.False);
    }

    [Test]
    public void NestedMarksGiveBackTheirOwnWork()
    {
        var session = Session();
        var outer = session.Mark();
        Assert.That(session.Spend(100), Is.True);
        var inner = session.Mark();
        Assert.That(session.Spend(100), Is.True);
        Assert.That(session.TryRecover(inner), Is.True);
        Assert.That(session.TryRecover(outer), Is.True);
        Assert.That(session.Spend(RoslynTotalProgramLowerer.MaximumRegionSteps), Is.True);
        Assert.That(session.Spend(1), Is.False);
    }
}
