// golden-scenario: shadow-call-ancestry-tampered-predicate
using SharpProof.Attributes;
public static class C
{
    private static int Leaf(int x) { Contract.Requires(x > 0); return x; }
    public static int Root() => Leaf(0);
}
