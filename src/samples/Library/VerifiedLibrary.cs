using SharpProof.Attributes;

namespace SharpProof.Samples.Library;

public static class VerifiedLibrary {
    public static long Identity(long value) {
        Contract.Ensures(Contract.Result<long>() == value);
        return value;
    }
}
