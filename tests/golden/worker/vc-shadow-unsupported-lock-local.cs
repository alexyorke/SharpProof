// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    [AllowedCapabilities(SharpProofCapability.None)]
    public static void AEffectLock()
    {
        lock (new object()) { }
        return;
    }
    public static void BContractLock()
    {
        Contract.Ensures(true);
        lock (new object()) { }
        return;
    }
}
