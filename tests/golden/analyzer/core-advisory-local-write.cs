// golden-scenario: core-advisory-calls
using SharpProof.Attributes;

public static class Subject
{
    private static void Need(int value) { Contract.Requires(value > 0); }

    public static void Loop(int value)
    {
        for (var index = 0; index < 2; index++) { Need(index); }
    }

    public static void Overwrite(int value)
    {
        var candidate = 1;
        candidate = 0;
        Need(candidate);
    }
}
