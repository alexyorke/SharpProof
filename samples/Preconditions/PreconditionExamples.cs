using SharpProof.Attributes;

namespace SharpProof.Samples.Preconditions;

public static class PreconditionExamples {
    public static int Positive(int value) {
        Contract.Requires(value > 0);
        return value;
    }

    public static int KnownGoodCall() => Positive(1);

    public static string RequiredText([NotNull] string value) => value;

    public static int RequiredPositive([Positive] int value) => value;

    public static int RequiredRange([InRange(0, 10)] int value) => value;

    public static int ClosedContractCalls() =>
        RequiredText("known").Length +
        RequiredPositive(1) +
        RequiredRange(5);
}

public sealed class PositiveCount {
    public PositiveCount(int count) {
        Contract.Requires(count > 0);
        Count = count;
    }

    public int Count { get; }

    public static PositiveCount KnownGoodConstruction() => new(1);
}
