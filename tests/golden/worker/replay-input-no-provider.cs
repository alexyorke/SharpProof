// golden-scenario: replay-input-no-provider
// Total IR replays Input havoc from the bound entry model without an external provider.
using SharpProof.Attributes;

public static class Subject
{
    public static void Target(int value)
    {
        Contract.Ensures(value >= 0);
    }
}
