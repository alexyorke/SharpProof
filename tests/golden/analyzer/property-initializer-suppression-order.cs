using SharpProof.Attributes;
using System.CodeDom.Compiler;

public static class Guard
{
    public static int Need(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
}

public class Subject
{
    private int before = Guard.Need(0);
    [SharpProofSuppress("reviewed property initializer")]
    public int Property { get; } = Guard.Need(0);
    private int fault = (new int[0])[0];
    private int afterFault = Guard.Need(0);

    public Subject() { }
    [SharpProofTrusted("reviewed constructor boundary")]
    public Subject(int marker) { }
    [SharpProofSuppress("reviewed constructor")]
    public Subject(bool marker) { }
    [GeneratedCode("test", "1")]
    public Subject(string marker) { }
    public Subject(double marker) : this() { }
}
