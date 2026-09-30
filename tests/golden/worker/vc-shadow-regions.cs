// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int ACapturedReturn(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == 7);
        Contract.Ensures(Contract.Result<int>() == 7);
        try { return x; } finally { x = 7; }
    }
    public static int BMixedFinally(int x)
    {
        Contract.Requires(x == 0 || x == 1);
        Contract.Ensures(Contract.Result<int>() == 7);
        try { try { if (x == 0) return 10 / x; x = 3; } finally { x = 7; } }
        catch (System.DivideByZeroException) { return x; }
        return x;
    }
    public static int CNestedRethrow(int x)
    {
        Contract.Requires(x == 0 || x == 1);
        Contract.Ensures(Contract.Result<int>() == 7);
        Contract.Ensures(Contract.Result<int>() == 9);
        try
        {
            try { if (x == 0) return 10 / x; return 20 / (x - 1); }
            catch (System.DivideByZeroException)
            {
                try { x = checked((byte)(unchecked(x + 256))); }
                catch (System.OverflowException) { }
                throw;
            }
        }
        catch (System.DivideByZeroException) { return 7; }
        catch (System.OverflowException) { return 9; }
    }
    public static int DAssumeRegion(int x)
    {
        Contract.Assume(x == 0);
        Contract.Ensures(Contract.Result<int>() == 7 && x == 7 && Contract.Old(x) == 0);
        try { try { return 10 / x; } finally { x = 7; } }
        catch (System.DivideByZeroException) { return x; }
    }
    public static int EAllThrow(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(false);
        try { return 10 / x; } finally { x = 7; }
    }
    public static int FCatchAll(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 9);
        try { return 10 / x; } catch { return 9; }
    }
    public static int GNestedFinallyClosed(int x)
    {
        Contract.Ensures(Contract.Result<int>() == x);
        try { try { return x; } finally { x++; } } finally { x++; }
    }
}
