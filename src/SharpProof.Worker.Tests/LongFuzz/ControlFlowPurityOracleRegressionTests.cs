using System.Collections.Immutable;
using System.Reflection;
using NUnit.Framework;

namespace SharpProof.Worker.Test.LongFuzz;

[TestFixture]
public sealed class ControlFlowPurityOracleRegressionTests
{
    [Test]
    public async Task ReachedLockIsObservedAndLockFreeControlRemainsPure()
    {
        const int seed = 3_250_105;
        var fixtureType = typeof(ControlFlowLongFuzzTests);
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
        var modes = (ImmutableArray<string>)fixtureType.GetField("s_modes", flags)!.GetValue(null)!;
        var body = fixtureType.GetMethod("Body", flags)!;
        var runCase = fixtureType.GetMethod("RunCase", flags)!;
        var session = new LongFuzzSession("control-flow-purity-regression", 0, 1);

        var random = new Random(seed);
        var mode = modes[random.Next(modes.Length)];
        Assert.That(mode, Is.EqualTo("pure-int"));
        var generated = body.Invoke(null, [seed, random])!;
        var generatedBody = (string)generated.GetType().GetProperty("Body")!.GetValue(generated)!;
        Assert.That(generatedBody,
            Is.EqualTo("lock (new object()) { if (x == 0) return 1; x = x == 0 ? 1 : x; }"));

        await (Task)runCase.Invoke(null, [session, seed, mode, generated])!;
        var counts = session.Counts;
        var lockRefutations = counts.GetValueOrDefault("outcome:Refuted");
        var lockUnwitnessed = counts.GetValueOrDefault("refuted-unwitnessed");

        var control = Activator.CreateInstance(generated.GetType(),
            "lock-free-control", "if (x == 0) return 1; x = x == 0 ? 1 : x;")!;
        await (Task)runCase.Invoke(null, [session, seed, mode, control])!;
        var controlCounts = session.Counts;

        Assert.That(lockRefutations, Is.EqualTo(1), "The reached lock must refute observable purity.");
        Assert.That(controlCounts.GetValueOrDefault("outcome:Proven"), Is.EqualTo(1),
            "The same body without a lock must remain pure.");
        Assert.That(lockUnwitnessed, Is.Zero,
            "The CLR oracle must observe the lock that refutes the purity claim.");
        Assert.That(session.Hits, Is.Zero);
    }
}
