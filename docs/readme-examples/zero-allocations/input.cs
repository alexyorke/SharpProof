using SharpProof.Attributes;
public sealed class Example
{
    [Impure]
    [ZeroAllocations]
    public object Create()
    {
        return new object();
    }
}
