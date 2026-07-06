using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public int TestMethod(int value)
    {
        return System.Experimental.NumericFacts.Normalize(value);
    }
}
