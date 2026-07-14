namespace SharpProof.Test;

internal static class MutableObjectTestSources
{
    internal const string SystemUsings = "\nusing System;\nusing SharpProof.Attributes;\n\n";
    internal const string AttributeUsings = "\nusing SharpProof.Attributes;\n\n";

    internal const string Box = """
public sealed class Box
{
    public int Value;
}
""" + "\n\n";

    internal const string BoxAndHolder = """
public sealed class Box
{
    public int Value;
}

public sealed class Holder
{
    public readonly Box Value;

    [EnforcePure]
    public Holder(Box value)
    {
        Value = value;
    }
}
""" + "\n\n";
}
