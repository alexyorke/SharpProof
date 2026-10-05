// golden-mode: Total
// golden-contracts: true
using SharpProof.Attributes;
public static class C
{
    [ZeroAllocations]
    public static int Target() => 1;
    private const string Unused = "first";
}
