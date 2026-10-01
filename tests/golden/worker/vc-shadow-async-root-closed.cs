// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static async void Target(int x)
    {
        Contract.Ensures(false);
        throw null!;
    }
}
