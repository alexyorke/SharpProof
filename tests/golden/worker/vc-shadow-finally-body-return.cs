// golden-scenario: vc-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int ANormalNestedBody(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == 3 && x == 6);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return x; } finally { try { x++; } finally { x += 2; } }
    }
    public static int BCaughtNestedBody(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == 3 && x == 9);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return x; } finally {
            try { try { x = 10 / (x - 3); } finally { x = 7; } }
            catch (System.DivideByZeroException) { x = 9; }
        }
    }
    public static int CFilteredNestedBody(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == 3 && x == 9);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return x; } finally {
            try { try { x = 10 / (x - 3); } finally { x = 7; } }
            catch (System.DivideByZeroException) when (x == 3) { x = 9; }
        }
    }
    public static int DNonReturnOuterEntry(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 3 && x == 3);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { x++; } finally { try { x++; } finally { x++; } }
        return x;
    }
}
