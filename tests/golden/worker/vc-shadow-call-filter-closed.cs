// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int ADirect(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return Callee(x); }
        catch (System.DivideByZeroException) when (++x > 0) { return 7; }
        catch (System.OverflowException) { return x; }
    }
    public static int BTransitive(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return Forward(x); }
        catch (System.DivideByZeroException) when (++x > 0) { return 7; }
        catch (System.OverflowException) { return x; }
    }
    private static int Forward(int value) { return Callee(value); }
    private static int Callee(int value)
    {
        try { return 10 / value; }
        finally { value = checked((byte)(value + 256)); }
    }
}
