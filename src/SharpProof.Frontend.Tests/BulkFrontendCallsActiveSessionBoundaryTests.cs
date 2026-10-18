using System.Collections.Immutable;
using System.Numerics;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class BulkFrontendCallsActiveSessionBoundaryTests
{
    private const string Members =
        "int Target(bool allow) { try { Callee(); return 7; } " +
        "catch (System.Exception) when (allow) { return 9; } } " +
        "static void Callee() { }";

    [Test]
    public void FullOwningSessionRejectsTheFilteredCalleeWithoutAdmittingIt()
    {
        var parkedMethods = string.Join(" ", Enumerable.Range(0, 255)
            .Select(index => "public static void Park" + index + "() { }"));
        using var subject = TypedProgramSubject.Create(Members,
            "public static class Parking { " + parkedMethods + " }");

        Assert.That(subject.Invoke([false]), Is.EqualTo(7));
        Assert.That(subject.Invoke([true]), Is.EqualTo(7));

        var parked = subject.Compilation.GetTypeByMetadataName("Parking")!
            .GetMembers().OfType<IMethodSymbol>()
            .Where(method => method is null ? throw new System.ArgumentNullException(nameof(method)) : (method.MethodKind == MethodKind.Ordinary))
            .ToArray();
        var callee = subject.Compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Callee").OfType<IMethodSymbol>().Single();
        Assert.That(parked, Has.Length.EqualTo(255));

        var prepared = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var session = new TotalSourceCallSession(subject.Compilation,
            frame =>
            {
                System.ArgumentNullException.ThrowIfNull(frame);
                prepared.Add(frame.Target);
                return true;
            }, null, default);
        var entered = ImmutableArray.CreateBuilder<IMethodSymbol>();
        try
        {
            foreach (var method in parked)
            {
                var accepted = session.Enter(method);
                if (accepted)
                { entered.Add(method); }
                Assert.That(accepted, Is.True);
            }

            var lowering = new RoslynTotalProgramLowerer(
                subject.Context, default, session).Lower(subject.Graph);

            Assert.That(lowering.IsExact, Is.False);
            Assert.That(lowering.Classification.Abstention,
                Is.EqualTo(FrontendAbstention.UnsupportedControlFlow));
            Assert.That(lowering.ConstructionLimitExceeded, Is.False);
            Assert.That(prepared, Has.Count.EqualTo(1));
            Assert.That(SymbolEqualityComparer.Default.Equals(
                prepared.Single(), callee), Is.True);
            Assert.That(session.IsActive(callee), Is.False);
            Assert.That(session.IsActive(subject.Context.Target), Is.False);
            Assert.That(parked.All(session.IsActive), Is.True);
        }
        finally
        {
            foreach (var method in entered.AsEnumerable().Reverse())
            { session.Leave(method); }
        }

        Assert.That(parked.Any(session.IsActive), Is.False);
        Assert.That(session.IsActive(subject.Context.Target), Is.False);
        Assert.That(session.IsActive(callee), Is.False);
        Assert.That(session.ConstructionLimitExceeded, Is.False);

        using var control = TypedProgramSubject.Create(Members);
        Assert.That(control.Invoke([false]), Is.EqualTo(7));
        Assert.That(control.Invoke([true]), Is.EqualTo(7));
        var exact = control.LowerSourceCalls();
        Assert.That(exact.IsExact, Is.True);
        Assert.That(exact.ConstructionLimitExceeded, Is.False);
        foreach (var allow in new[] { false, true })
        {
            var replay = control.Execute(exact, [allow]);
            Assert.That(replay.Status,
                Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(replay.ReturnValue!.IntegerNumericValue,
                Is.EqualTo(new BigInteger(7)));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(replay.ApproximationVariables, Is.Empty);
        }
    }
}
