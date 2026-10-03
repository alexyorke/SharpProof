// golden-scenario: synchronization-projection
using SharpProof.Attributes;
public static class C
{
    private static int Helper(object gate, int x)
    {
        while (x > 0) { lock (gate) { x--; } }
        return x;
    }
    [AllowedCapabilities(SharpProofCapability.None)]
    public static int Target(object gate, int x) => Helper(gate, x);
}
