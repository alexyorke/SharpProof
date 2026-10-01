// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int AReplacement(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return ReplacingCallee(x); }
        catch (System.DivideByZeroException) when (++x > 100) { return 7; }
        catch (System.DivideByZeroException) when (++x > 0) { return x; }
        catch (System.OverflowException) when (++x > 0) { return x; }
    }
    public static int BFilterFault(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 1 && x == 1);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return 10 / x; }
        catch (System.DivideByZeroException) when (++x > 0 && FaultingFilter(x - 1)) { return 7; }
        catch (System.DivideByZeroException) { return x; }
    }
    public static int CCapturedReturn(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == unchecked(Contract.Old(x) + 10));
        Contract.Ensures(Contract.Result<int>() == x);
        try { return ReturningCallee(x); }
        catch (System.DivideByZeroException) when (++x > 0) { return x; }
        finally { x += 10; }
    }
    public static int DOwnFilterFault(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 2 && x == 0);
        Contract.Ensures(Contract.Result<int>() == 7);
        try { return OwnCallee(x); }
        catch (System.DivideByZeroException) when (++x > 0) { return 7; }
    }
    public static int ERepeatedFilter(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return RepeatedCallee(x); }
        catch (System.DivideByZeroException) when (++x > 0) { return x; }
    }
    private static int ReplacingCallee(int value)
    {
        try { return 10 / value; }
        finally { value = checked((byte)(value + 256)); }
    }
    private static bool FaultingFilter(int value)
    {
        try { return More(value); }
        finally { value++; }
    }
    private static bool More(int value)
    {
        try { return value > 0; }
        finally { value = checked((byte)(value + 256)); }
    }
    private static int ReturningCallee(int value)
    {
        try { return value; }
        finally { value += 100; }
    }
    private static int OwnCallee(int value)
    {
        try { return 10 / value; }
        catch (System.DivideByZeroException) when (OwnFilter(value)) { return 5; }
        catch (System.DivideByZeroException) { return 2; }
        finally { value++; }
    }
    private static bool OwnFilter(int value)
    {
        try { return true; }
        finally { value = 10 / value; }
    }
    private static int RepeatedCallee(int value)
    {
        try { return 10 / value; }
        finally { value = 10 / value; }
    }
}
