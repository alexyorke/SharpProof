// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int ASearchBeforeUnwind(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 123 && x == 12 && Contract.Old(x) == 0);
        Contract.Ensures(Contract.Result<int>() == 321);
        try { try { return 10 / x; } finally { x = x * 10 + 2; } }
        catch (System.DivideByZeroException) when ((x = x * 10 + 1) > 0) { return x * 10 + 3; }
    }
    public static int BFilterFaultEffects(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 7 && x == 7 && Contract.Old(x) == 0);
        Contract.Ensures(Contract.Result<int>() == 1);
        try { return 10 / x; }
        catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; }
        catch (System.DivideByZeroException) when (x == 7) { return x; }
    }
    public static int CNestedCapturedReturn(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == 312);
        Contract.Ensures(Contract.Result<int>() == x);
        try { try { return x; } finally { x = x * 10 + 1; } }
        finally { x = x * 10 + 2; }
    }
    public static int DFinallyReplacesSelectedHandler(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 7 && x == 7);
        Contract.Ensures(Contract.Result<int>() == 1);
        try { try { try { return 10 / x; } finally { x = checked((byte)(x + 256)); } }
            catch (System.DivideByZeroException) when ((x = 7) == 7) { return 1; } }
        catch (System.OverflowException) { return x; }
    }
    public static int ESiblingFinally(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 122);
        try { x++; } finally { x = x * 10 + 1; }
        try { x++; } finally { x = x * 10 + 2; }
        return x;
    }
    public static int FAssumeBeforeFilter(int x)
    {
        Contract.Assume(x == 0);
        Contract.Ensures(Contract.Result<int>() == 7);
        try { return 10 / x; }
        catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; }
        catch (System.DivideByZeroException) when (x == 7) { return x; }
    }
    public static int GExplicitFilterThrow(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 7);
        try { return 10 / x; }
        catch (System.DivideByZeroException) when ((x = 7) > 0 ? throw null! : true) { return 1; }
        catch (System.DivideByZeroException) { return x; }
    }
    public static int HHandlerBeforeOuterFinally(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 123);
        try { try { try { x = 10 / x; } finally { x = x * 10 + 1; } }
            catch (System.DivideByZeroException) when (x == 0) { x = x * 10 + 2; } }
        finally { x = x * 10 + 3; }
        return x;
    }
    public static int IRepeatedFilterClosed(int x)
    {
        Contract.Ensures(Contract.Result<int>() == 1);
        try { try { return 10 / x; } finally { x = 10 / x; } }
        catch (System.Exception) when (x == 0) { return 1; }
    }
    public static int JFaultingFilterAllThrow(int x)
    {
        Contract.Requires(x == 0); Contract.Ensures(false);
        try { try {
            try { return 10 / x; }
            catch (System.DivideByZeroException) when ((x = 7) / (x - 7) > 0) { return 1; }
        } finally { x = 9; } } finally { x = 11; }
    }
}
