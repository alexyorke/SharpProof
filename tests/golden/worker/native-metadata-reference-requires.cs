// golden-scenario: typed-il-call-requires
// golden-reference-inputs
public static class Subject
{
    public static string Target(string value) => Library.Target(value);
}

// golden-library:
using SharpProof.Attributes;
public static class Library
{
    public static string Target([NotNull] string value) => value;
}
