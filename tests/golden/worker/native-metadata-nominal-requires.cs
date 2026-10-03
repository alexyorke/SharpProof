// golden-scenario: typed-il-call-requires
// golden-nominal-inputs
public sealed class Token { }
public static class Subject
{
    public static object Target(Token value) => Library.Target(value);
}

// golden-library:
using SharpProof.Attributes;
public static class Library
{
    public static object Target([NotNull] object value) => value;
}
