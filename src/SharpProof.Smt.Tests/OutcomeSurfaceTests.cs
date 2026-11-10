using System.Collections.Immutable;
namespace SharpProof.Verify.Test;

[TestFixture]
public sealed class OutcomeSurfaceTests
{
    [Test]
    public void AbstentionReasonContainsOnlyCurrentProofReasons()
    {
        var expected = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        expected.Add(nameof(AbstentionReason.UnsupportedEncoding), 3);
        expected.Add(nameof(AbstentionReason.ResourceLimit), 4);
        expected.Add(nameof(AbstentionReason.Timeout), 5);
        expected.Add(nameof(AbstentionReason.BackendUnavailable), 6);
        expected.Add(nameof(AbstentionReason.InfrastructureFailure), 7);
        expected.Add(nameof(AbstentionReason.MalformedBackendResult), 8);
        expected.Add(nameof(AbstentionReason.CounterexampleReplayFailed), 9);
        expected.Add(nameof(AbstentionReason.PostconditionMayBeUndefined), 10);
        expected.Add(nameof(AbstentionReason.InternalConsistencyMayBeUndefined), 11);
        expected.Add(nameof(AbstentionReason.SolverIncomplete), 12);
        expected.Add(nameof(AbstentionReason.CounterexampleNotReplayable), 13);
        var actual = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.Ordinal);
        foreach (var value in Enum.GetValues<AbstentionReason>())
        {
            var key = value.ToString();
            if (actual.ContainsKey(key))
            { throw new ArgumentException(nameof(value)); }
            actual.Add(key, (int)value);
        }

        Assert.That(actual.ToImmutable(), Is.EqualTo(expected.ToImmutable()));
    }
}
