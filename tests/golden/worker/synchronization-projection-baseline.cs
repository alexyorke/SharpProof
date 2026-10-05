// golden-scenario: synchronization-projection
using SharpProof.Attributes;
public static class C
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Target() => 1;
}
