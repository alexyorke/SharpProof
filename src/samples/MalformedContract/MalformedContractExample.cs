using SharpProof.Attributes;

namespace SharpProof.Samples.MalformedContract;

public static class MalformedContractExample {
    public static int LatePrecondition(int value) {
        var copy = value;
        Contract.Requires(value > 0);
        return copy;
    }
}
