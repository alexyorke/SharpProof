// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int ARepeatedFilter(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 1);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { try { return 10 / x; } finally { x = 10 / x; } }
        catch (System.Exception) when (x == 0) { return 1; }
    }
    public static int BCaughtLoop(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        int i = 0;
        while (i < 2)
        {
            try { x = 10 / x; }
            catch (System.DivideByZeroException) { x = 1; }
            finally { i++; }
        }
        return x;
    }
    public static int CConditionalLoop(int x)
    {
        Contract.Assume(x == 0);
        Contract.Ensures(Contract.Result<int>() == x && Contract.Old(x) == 0);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        int i = 0;
        while (i < 2)
        {
            try { x = 10 / x; }
            catch (System.DivideByZeroException) { x = 1; }
            finally { i++; }
        }
        return x;
    }
    public static int DBeyondSearch(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x));
        int i = 0;
        while (i < 8)
        {
            try { x = 10 / x; }
            catch (System.DivideByZeroException) { x = 1; }
            finally { i++; }
        }
        return x;
    }
    public static int ECapturedLoop(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == x);
        try
        {
            for (int i = 0; i < 2; i++)
            {
                try { x = checked(x + 1); }
                finally { x = checked(x + 1); }
            }
            return x;
        }
        finally { x = checked(x + 10); }
    }
    public static int FGuardedRethrow(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(false);
        try { return 10 / x; }
        catch (System.DivideByZeroException) { if (x != 0) throw; return 1; }
    }
    public static int GConditionalFinallyCompletion(int x)
    {
        Contract.Requires(x == 1);
        Contract.Ensures(Contract.Result<int>() == 3);
        try { x++; }
        finally { if (x++ == 1) x = 7; }
        return x;
    }
}
