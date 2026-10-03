using SharpProof.Attributes;

public static class Guard
{
    public static int Need(int value)
    {
        Contract.Requires(value > 0);
        return value;
    }
}

public record SynthRecord
{
    private int later = Guard.Need(1);
}

public record ExplicitRecord
{
    private int later = Guard.Need(1);
    public ExplicitRecord() { }
    protected ExplicitRecord( ExplicitRecord source) { later = source.later; }
}

public class ClassControl
{
    private int later = Guard.Need(1);
    public ClassControl() { }
    protected ClassControl( ClassControl source) { later = source.later; }
}

public record struct StructControl
{
    private int later = Guard.Need(1);
    public StructControl() { }
    public StructControl( StructControl source) { later = source.later; }
}

public record RefRecord
{
    private int later = Guard.Need(1);
    public RefRecord() { }
    protected RefRecord(ref RefRecord source) { later = source.later; }
}

public record InRecord
{
    private int later = Guard.Need(1);
    public InRecord() { }
    protected InRecord(in InRecord source) { later = source.later; }
}
