// golden-scenario: typed-il-call-requires
public static class Subject
{
    public static int Target(int value) => Library.Target(value, value = 2);
}

// golden-library:
using SharpProof.Attributes;
public static class Library
{
    public static int Target([InRange(1, 1)] int first, int second) => first * 10 + second;
}
