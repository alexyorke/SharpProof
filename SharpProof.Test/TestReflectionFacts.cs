namespace SharpProof.Test;

internal static class TestReflectionFacts
{
    internal static int GetCount(object instance)
    {
        return (int)instance.GetType().GetProperty("Count")!.GetValue(instance)!;
    }
}
