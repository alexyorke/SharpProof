using SharpProof.Attributes;

namespace SharpProof.Demo;

public class Demo
{
    private int _counter;

    // Impure under [Pure] (mutates instance state) -> SP0002
    [Pure]
    public int AddImpure(int a, int b)
    {
        _counter++;
        return a + b + _counter;
    }

    // Pure without [EnforcePure] -> SP0004
    public static int PureAdd(int a, int b)
    {
        return a + b;
    }

    // Pure and correctly annotated (using [Pure]) -> no diagnostic
    [Pure]
    public static int ProperPureAdd(int a, int b)
    {
        return a + b;
    }
}

public static class ImpureScenarios
{
    private static int _global;

    // I/O under [Pure] -> SP0002
    [Pure]
    public static void Log(string message)
    {
        Console.WriteLine(message);
    }

    // Static state mutation under [Pure] -> SP0002
    [Pure]
    public static int IncrementGlobal(int delta)
    {
        _global += delta;
        return _global;
    }

    // Using [Pure] as enforcement, still impure -> SP0002
    [Pure]
    public static void MutateThroughPureAlias()
    {
        _global++;
    }
}

public class PureScenarios
{
    // Pure constructor without [EnforcePure] -> SP0004
    public PureScenarios(int x)
    {
        X = x;
    }

    public int X { get; }

    // Pure property getter without [EnforcePure] -> SP0004
    public int DoubleX => X * 2;

    // Pure method without [EnforcePure] -> SP0004
    public static string Concat(string a, string b)
    {
        return a + b;
    }

    // Properly annotated pure method (using [Pure]) -> no diagnostic
    [Pure]
    public static bool IsEven(int v)
    {
        return (v & 1) == 0;
    }
}

internal static class Program
{
    private static void Main()
    {
        Console.WriteLine(Demo.PureAdd(1, 2));
        Console.WriteLine(Demo.ProperPureAdd(2, 3));
        Console.WriteLine(new Demo().AddImpure(3, 4));

        ImpureScenarios.Log("hello");
        Console.WriteLine(ImpureScenarios.IncrementGlobal(5));
        ImpureScenarios.MutateThroughPureAlias();

        var p = new PureScenarios(10);
        Console.WriteLine(PureScenarios.Concat("A", "B"));
        Console.WriteLine(p.DoubleX);
        Console.WriteLine(PureScenarios.IsEven(4));
    }
}