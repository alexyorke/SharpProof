using System;
using SharpProof.Attributes;

public sealed class Example
{
    [EnforcePure]
    public int ReadClock()
    {
        return DateTime.Now.Second;
    }
}
