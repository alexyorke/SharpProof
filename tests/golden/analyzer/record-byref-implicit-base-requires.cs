using SharpProof.Attributes;

public record Base
{
    public Base() { Contract.Requires(false); }
}

public record RefSubject : Base
{
    protected RefSubject(ref RefSubject source) { }
}

public record InSubject : Base
{
    protected InSubject(in InSubject source) { }
}
