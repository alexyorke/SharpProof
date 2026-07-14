namespace SharpProof.Test;

internal static class SemanticOracleTestSources
{
    internal const string ModeEnum = @"
public enum Mode
{
    None = 0,
    Ready = 1
}

";

    internal const string CompoundAssignedNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor += 1;
        return 10 / divisor;
    }
}";

    internal const string IncrementedNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        divisor++;
        return 10 / divisor;
    }
}";

    internal const string TupleAssignedNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = 0;
        var other = 0;
        (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}";

    internal const string TupleDeconstructionDeclaredNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var (divisor, other) = (1, 2);
        return 10 / divisor;
    }
}";

    internal const string InlineFiniteArrayElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[0];
        return 10 / divisor;
    }
}";

    internal const string PriorFiniteArrayElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[0];
        return 10 / divisor;
    }
}";

    internal const string InlineFiniteArrayFromEndElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var divisor = (new[] { 1, 2 })[^1];
        return 10 / divisor;
    }
}";

    internal const string PriorFiniteArrayFromEndElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var values = new[] { 1, 2 };
        var divisor = values[^1];
        return 10 / divisor;
    }
}";

    internal const string ConditionalFiniteArrayElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod(bool flag)
    {
        var values = new[] { 1, 2 };
        var divisor = flag ? values[0] : values[1];
        return 10 / divisor;
    }
}";

    internal const string TupleElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = pair.Item1;
        return 10 / divisor;
    }
}";

    internal const string NamedTupleElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (divisor: 1, other: 2);
        var divisor = pair.divisor;
        return 10 / divisor;
    }
}";

    internal const string TupleLocalDeconstructionAssignedNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var divisor = 0;
        var other = 0;
        (divisor, other) = pair;
        return 10 / divisor;
    }
}";

    internal const string TupleLocalDeconstructionDeclaredNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        var pair = (1, 2);
        var (divisor, other) = pair;
        return 10 / divisor;
    }
}";
}
