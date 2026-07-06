#pragma warning disable SP0004
public sealed class TestClass
{
    public int Divide(int value)
    {
        return value / 0;
    }
}
