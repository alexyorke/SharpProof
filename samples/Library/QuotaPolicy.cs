using SharpProof.Attributes;

namespace SharpProof.Samples.Library;

public static class QuotaPolicy {
    public static long SelectLimit(
        bool premium,
        long standardLimit,
        long premiumLimit) {
        Contract.Ensures(
            Contract.Result<long>() ==
            (premium ? premiumLimit : standardLimit));
        if (premium)
            return premiumLimit;
        return standardLimit;
    }

    public static bool Flip(bool enabled) {
        Contract.Ensures(
            Contract.Result<bool>() != Contract.Old(enabled));
        enabled = !enabled;
        return enabled;
    }
}
