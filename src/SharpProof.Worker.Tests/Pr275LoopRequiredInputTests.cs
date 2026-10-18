using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275LoopRequiredInputTests
{
    [Test]
    public void InvariantGenerationRequiresInitializedLoops()
    {
        var candidate = CreateActualCandidate();
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            LoopInvariantCandidates.Generate(candidate, default, default)));
        Assert.That(error!.ParamName, Is.EqualTo("loops"));
    }

    [Test]
    public void LoopEncodingRequiresInitializedInvariants()
    {
        var candidate = CreateActualCandidate();
        var error = Assert.Throws<ArgumentException>((Action)(() =>
            PassiveLoopCutter.TryEncodeWithInvariants(candidate, default, default)));
        Assert.That(error!.ParamName, Is.EqualTo("invariants"));
    }

    [Test]
    public void ActualStraightLineSourceNeedsNoLoopEncoding()
    {
        var candidate = CreateActualCandidate();
        var loops = PassiveLoopCutter.Loops(candidate, default);
        Assert.That(loops.IsDefault, Is.False);
        Assert.That(loops, Is.Empty);
        Assert.That(LoopInvariantCandidates.Generate(candidate, loops, default), Is.Empty);
        Assert.That(PassiveLoopCutter.TryEncodeWithInvariants(candidate,
            ImmutableArray<PassiveLoopCutter.Invariant>.Empty, default), Is.Null);
    }

    [Test]
    public void AnActualSourceLoopAcceptsInitializedEmptyInvariants()
    {
        var candidate = CreateActualCandidate("""
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow]
                internal static int Target(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() == 0);
                    var remaining = value;
                    while (remaining > 0) { remaining--; }
                    return remaining;
                }
            }
            """);
        var loops = PassiveLoopCutter.Loops(candidate, default);
        Assert.That(loops, Is.Not.Empty);
        Assert.That(LoopInvariantCandidates.Generate(candidate, loops, default).IsDefault, Is.False);
        Assert.That(PassiveLoopCutter.TryEncodeWithInvariants(candidate,
            ImmutableArray<PassiveLoopCutter.Invariant>.Empty, default), Is.Not.Null);
    }

    private static PassiveCallableCandidate CreateActualCandidate(string? source = null)
    {
        var compilation = TestCompilation.Create("Pr275LoopRequiredInput", source ?? """
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow]
                internal static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(preparation.IsSuccess, Is.True);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        return candidate!;
    }
}
