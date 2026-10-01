// golden-scenario: typed-il-shadow
using SharpProof.Attributes;
public static class Subject
{
    public static int Target(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == 4 && x == 3);
        Contract.Ensures(Contract.Result<int>() == 3);
        return Forward(x);
    }
    private static int Forward(int value) { return Library.Target(value); }
    public static int BCapturedArguments(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == 33 && x == 4);
        Contract.Ensures(Contract.Result<int>() == x * 11);
        return Library.Pack(first: x, second: x++);
    }
    public static int CNamedArguments(int x)
    {
        Contract.Requires(x == 3);
        Contract.Ensures(Contract.Result<int>() == 43 && x == 4);
        Contract.Ensures(Contract.Result<int>() == 33);
        return Library.Pack(second: x++, first: x);
    }
    public static byte DStorage(byte x)
    {
        Contract.Requires(x == 255);
        Contract.Ensures(Contract.Result<byte>() == 0 && x == Contract.Old(x));
        Contract.Ensures(Contract.Result<byte>() == x);
        return Library.Store(x);
    }
    public static ulong EUlong(ulong x)
    {
        Contract.Requires(x == 18446744073709551615UL);
        Contract.Ensures(Contract.Result<ulong>() == 0UL);
        Contract.Ensures(Contract.Result<ulong>() == 1UL);
        return Library.UAdd(x);
    }
    public static int FFilterAndFinally(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 1 && x == 11);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return Library.Div(x); }
        catch (System.DivideByZeroException) when (++x > 0) { return x; }
        finally { x += 10; }
    }
    public static int GFilterFault(int x)
    {
        Contract.Requires(x == 0);
        Contract.Ensures(Contract.Result<int>() == 1 && x == 11);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return 10 / x; }
        catch (System.DivideByZeroException) when (++x > 0 && Library.Div(x - 1) > 0) { return 7; }
        catch (System.DivideByZeroException) { return x; }
        finally { x += 10; }
    }
    public static int HOverflow(int x)
    {
        Contract.Requires(x == int.MaxValue);
        Contract.Ensures(Contract.Result<int>() == Contract.Old(x) && x == int.MinValue);
        Contract.Ensures(Contract.Result<int>() == 0);
        try { return Library.Target(x); }
        catch (System.OverflowException) { return x; }
        finally { x = unchecked(x + 1); }
    }
    public static int IExceptionRegionsClosed(int x)
    {
        Contract.Ensures(Contract.Result<int>() == 1);
        return Library.Handled(x);
    }
    public static int JInitializationClosed(int x)
    {
        Contract.Ensures(Contract.Result<int>() == x);
        return Initialized.Target(x);
    }
}
// metadata-library
public static class Library
{
    public static int Target(int value) { return Again(value); }
    private static int Again(int value) { return checked(value + 1); }
    public static int Pack(int first, int second) { return first * 10 + second; }
    public static ulong UAdd(ulong value) { return unchecked(value + 1UL); }
    public static int Div(int value) { return 10 / value; }
    public static byte Store(byte value) { value++; return value; }
    public static int Handled(int value)
    {
        try { return 10 / value; }
        catch (System.DivideByZeroException) { return 1; }
    }
}
public static class Initialized
{
    static Initialized() { }
    public static int Target(int value) { return value; }
}
