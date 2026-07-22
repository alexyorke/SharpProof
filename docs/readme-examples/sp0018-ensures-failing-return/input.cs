using SharpProof.Attributes;
public sealed class TestClass
{
    [Ensures("result > 0")]
    public int Identity()
    {
        return 0;
    }
}
