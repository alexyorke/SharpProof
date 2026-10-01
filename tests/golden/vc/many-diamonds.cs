// Retires the legacy many-diamonds regression through typed public verification.
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(bool a, bool b, bool c, bool d, bool e, bool f, bool g) { Contract.Ensures(Contract.Result<int>() >= 0 && Contract.Result<int>() <= 7); int value = 0; if (a) value++; if (b) value++; if (c) value++; if (d) value++; if (e) value++; if (f) value++; if (g) value++; return value; }
}
