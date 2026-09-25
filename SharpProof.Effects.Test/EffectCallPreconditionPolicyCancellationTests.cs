namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class EffectCallPreconditionPolicyCancellationTests
{
    [Test]
    public void CancelledPolicyDoesNotPoisonAnotherPolicyForSameCompilation()
    {
        var compilation = EffectTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            public interface IThing {
                void Get();
            }

            [ContractFor(typeof(IThing))]
            public static class IThingContracts {
            }

            public static class Other {
                public static void Plain() {
                }
            }
            """);
        var thingMethod = EffectTestHost.RequireType(
                compilation,
                "IThing")
            .GetMembers("Get")
            .OfType<IMethodSymbol>()
            .Single();
        var plainMethod = EffectTestHost.RequireType(
                compilation,
                "Other")
            .GetMembers("Plain")
            .OfType<IMethodSymbol>()
            .Single();
        using var cancellation = new CancellationTokenSource();
        var cancelledPolicy = new ConservativeEffectCallPreconditionPolicy(
            compilation,
            cancellationToken: cancellation.Token);

        cancellation.Cancel();
        Assert.That(
            (Action)(() => cancelledPolicy.HasPotentialPreconditions(
                thingMethod)),
            Throws.InstanceOf<OperationCanceledException>());

        var livePolicy = new ConservativeEffectCallPreconditionPolicy(
            compilation,
            cancellationToken: CancellationToken.None);
        Assert.That(
            livePolicy.HasPotentialPreconditions(thingMethod),
            Is.True);
        Assert.That(
            livePolicy.HasPotentialPreconditions(plainMethod),
            Is.False);
    }
}
