using SharpProof.Attributes;

public sealed class TestClass
{
    [Ensures("")]
    public int Value()
    {
        return 1;
    }
}
