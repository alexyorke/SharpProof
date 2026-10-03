// golden-scenario: typed-il-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static object Target(object x)
    {
        Contract.Requires(x != null);
        Contract.Ensures(Contract.Result<object>() == x);
        Contract.Ensures(Contract.Result<object>() == null);
        return Library.Target(x);
    }
    public static bool BStringNull(string x)
    {
        Contract.Ensures(Contract.Result<bool>() == (x == null));
        Contract.Ensures(Contract.Result<bool>() != (x == null));
        return Library.StringNull(x);
    }
    public static object CReset(object x)
    {
        Contract.Ensures(Contract.Result<object>() == null);
        Contract.Ensures(Contract.Result<object>() != null);
        return Library.Reset(x);
    }
}
// metadata-library
public static class Library
{
    public static object Target(object value) { return Again(value); }
    private static object Again(object value) { return value; }
    public static bool StringNull(string value) { return Copy(value) == null; }
    private static string Copy(string value) { return value; }
    public static object Reset(object value) { value = null; return value; }
}
