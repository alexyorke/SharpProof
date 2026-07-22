using SharpProof.Attributes;
public sealed class Calculator
{
    [Requires("result > 0")]
    public static int Identity(int value) => value;
}
