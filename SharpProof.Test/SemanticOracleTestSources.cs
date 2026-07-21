namespace SharpProof.Test;

internal static class SemanticOracleTestSources {
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

    internal const string SwitchStatementPatternBoundNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case > 0 and var divisor:
                return 10 / divisor;
            default:
                return 0;
        }
    }
}";

    internal const string SwitchStatementPriorSectionExcludesZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        switch (value)
        {
            case 0:
                return 0;
            case var divisor:
                return 10 / divisor;
        }
    }
}";

    internal const string SwitchExpressionPatternBoundNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            > 0 and var divisor => 10 / divisor,
            _ => 0
        };
    }
}";

    internal const string SwitchExpressionFallbackExcludesZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        return value switch
        {
            0 => 0,
            _ => 10 / value
        };
    }
}";

    internal const string RelationalPatternBoundNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int value)
    {
        if (value is > 0 and var divisor)
        {
            return 10 / divisor;
        }

        return 0;
    }
}";

    internal const string PropertyPatternBoundNonZeroLength = @"
public class TestClass
{
    public int TestMethod(string text)
    {
        if (text is { Length: > 0 and var length })
        {
            return 10 / length;
        }

        return 0;
    }
}";

    internal const string ListPatternFirstElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0 and var divisor, ..])
        {
            return 10 / divisor;
        }

        return 0;
    }
}";

    internal const string ListPatternTrailingElementNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [.., > 0 and var divisor])
        {
            return 10 / divisor;
        }

        return 0;
    }
}";

    internal const string ArrayElementReadFromListPatternNonZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int[] values)
    {
        if (values is [> 0, ..])
        {
            var divisor = values[0];
            return 10 / divisor;
        }

        return 0;
    }
}";

    internal const string IfElseElseExitZeroDivisor = @"
public class TestClass
{
    public int TestMethod(int divisor)
    {
        if (divisor == 0)
        {
        }
        else
        {
            return 0;
        }

        return 10 / divisor;
    }
}";

    internal const string DefaultIntegralZeroDivisor = @"
public class TestClass
{
    public int TestMethod()
    {
        int divisor = default;
        return 10 / divisor;
    }
}";

    internal const string DefaultReferenceNull = @"
public class TestClass
{
    public int TestMethod()
    {
        string value = default;
        return value.Length;
    }
}";
}
