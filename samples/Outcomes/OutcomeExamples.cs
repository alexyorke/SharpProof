using SharpProof.Attributes;

namespace SharpProof.Samples.Outcomes;

public static class OutcomeExamples {
    public static long Proven(long value) {
        Contract.Ensures(Contract.Result<long>() == value);
        return value;
    }

    public static long Refuted(long value) {
        Contract.Ensures(Contract.Result<long>() > value);
        return value;
    }

    public static long Unknown(long value) {
        Contract.Ensures(Contract.Result<long>() >= 0);
        while (value < 0)
            value++;
        return value;
    }
}
