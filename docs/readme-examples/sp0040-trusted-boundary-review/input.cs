using SharpProof.Attributes;

public static class TrustedBoundary
{
    public static int Value(int value) => value;
}

public sealed class Consumer
{
    [EnforcePure]
    public int Read() => TrustedBoundary.Value(1);
}
