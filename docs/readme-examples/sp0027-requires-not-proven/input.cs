using SharpProof.Attributes;

public sealed class Calculator
{
    [Requires("value > 0")]
    public static int Identity(int value) => value;

    public static int Demo()
    {
        return Identity(0);
    }
}
