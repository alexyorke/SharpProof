using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ControlFlowPurityWriteControlTests
{
    [Test]
    public async Task StaticWriteRemainsObserved()
    {
        var fixtureType = typeof(ControlFlowLongFuzzTests);
        var bodyType = fixtureType.GetNestedType("GeneratedBody", BindingFlags.NonPublic)!;
        var runCase = fixtureType.GetMethod("RunCase", BindingFlags.NonPublic | BindingFlags.Static)!;
        var session = new LongFuzzSession("control-flow-purity-write", 0, 1);
        var body = Activator.CreateInstance(bodyType, "state-write", "State = x;")!;

        await (Task)runCase.Invoke(null, [session, 3_250_105, "pure-int", body])!;

        var counts = session.Counts;
        Assert.That(counts.GetValueOrDefault("outcome:Refuted"), Is.EqualTo(1));
        Assert.That(counts.GetValueOrDefault("refuted-unwitnessed"), Is.Zero);
        Assert.That(session.Hits, Is.Zero);
    }
}
