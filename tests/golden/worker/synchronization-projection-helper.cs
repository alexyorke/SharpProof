// golden-scenario: synchronization-projection
using SharpProof.Attributes;
public static class C
{
    private static int Helper(object gate, int x)
    {
        lock (gate) { return x; }
    }
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Target(object gate, int x) => Helper(gate, x);
}
