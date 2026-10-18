using SharpProof.Attributes;

namespace SharpProof.Samples.Diagnostics;

public static class DiagnosticExamples {
    public static int Positive(int value) {
        Contract.Requires(value > 0);
        return value;
    }

    public static int RefutedPrecondition() => Positive(0);

    [ZeroAllocations]
    public static object Allocates() => new();

    [ZeroAllocations]
    public static int Unsupported() {
        Func<int> value = () => 1;
        return value();
    }

    public static PositiveBox RefutedConstructor() => new(0);
}

public sealed class PositiveBox {
    public PositiveBox(int value) {
        Contract.Requires(value > 0);
        Value = value;
    }

    public int Value { get; }
}
