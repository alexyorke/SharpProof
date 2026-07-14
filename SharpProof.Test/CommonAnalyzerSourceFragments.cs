namespace SharpProof.Test;

internal static class CommonAnalyzerSourceFragments
{
    internal const string GlobalState = """
public static class GlobalState
{
    public static int Count;
}
""" + "\n";

    internal const string MutableComparableKey = """
public sealed class MutableKey : IComparable<MutableKey>
{
    public int CompareTo(MutableKey other)
    {
        Console.WriteLine("compare");
        return 0;
    }
}
""" + "\n";
}
