namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class OutcomeSurfaceTests
{
    [Test]
    public void AbstentionReasonContainsOnlyCurrentProofReasons()
    {
        var expected = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [nameof(AbstentionReason.UnsupportedEncoding)] = 3,
            [nameof(AbstentionReason.ResourceLimit)] = 4,
            [nameof(AbstentionReason.Timeout)] = 5,
            [nameof(AbstentionReason.BackendUnavailable)] = 6,
            [nameof(AbstentionReason.InfrastructureFailure)] = 7,
            [nameof(AbstentionReason.MalformedBackendResult)] = 8,
            [nameof(AbstentionReason.CounterexampleReplayFailed)] = 9,
            [nameof(AbstentionReason.PostconditionMayBeUndefined)] = 10,
            [nameof(AbstentionReason.InternalConsistencyMayBeUndefined)] = 11
        };
        var actual = Enum.GetValues<AbstentionReason>()
            .ToDictionary(
                static value => value.ToString(),
                static value => (int)value,
                StringComparer.Ordinal);

        Assert.That(actual, Is.EqualTo(expected));
    }
}
