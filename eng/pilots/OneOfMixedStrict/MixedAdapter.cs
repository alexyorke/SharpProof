using OneOf;
using SharpProof.Attributes;

namespace SharpProof.Pilots.OneOfMixedStrict;

public static class MixedAdapter
{
    [EnforcePure]
    [ZeroAllocations]
    [DoesNotThrow]
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Identity(int value) => value;

    public static int Positive(int value)
    {
        Contract.Requires(value > 0);
        Contract.Ensures(Contract.Result<int>() > 0);
        return value;
    }

    public static int KnownGood() => Positive(1);

    public static OneOf<int, string> Wrap(int value) => value;
}
