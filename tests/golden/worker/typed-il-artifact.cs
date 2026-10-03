// golden-scenario: typed-il-artifact
using SharpProof.Attributes;
public static class Subject
{
    public static ulong Target(ulong x)
    {
        Contract.Ensures(Contract.Result<ulong>() == unchecked(x + 1UL));
        return Library.Target(x);
    }
}
// metadata-library
public static class Library
{
    public static ulong Target(ulong value) { return unchecked(value + 1UL); }
}
