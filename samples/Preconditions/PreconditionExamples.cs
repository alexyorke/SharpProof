using SharpProof.Attributes;

namespace SharpProof.Samples.Preconditions;

public static class PreconditionExamples {
    public static int Positive(int value) {
        Contract.Requires(value > 0);
        return value;
    }

    public static int KnownGoodCall() => Positive(1);
}
