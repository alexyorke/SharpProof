using SharpProof.Attributes;

public static class Subject
{
    private static int Positive(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }

    public static int Checked() => checked(Positive(-2));
    public static int Unchecked() => unchecked((Positive(-3)));
    public static long Conversion() => (long)Positive(-1);
    public static int NullableSuppression() => Positive(-4)!;
}
