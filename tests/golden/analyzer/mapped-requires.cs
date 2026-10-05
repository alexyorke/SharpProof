using SharpProof.Attributes;

public static class Subject
{
    public static void Required(int value) { Contract.Requires(value > 0); }
#line 100 "Mapped.cs"
    public static void Target() { Required(0); }
#line default
}
