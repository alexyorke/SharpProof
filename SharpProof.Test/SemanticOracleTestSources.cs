namespace SharpProof.Test;

internal static class SemanticOracleTestSources {
    internal const string ModeEnum = """
        public enum Mode
        {
            None = 0,
            Ready = 1
        }
        """;

    internal static readonly string CompoundAssignedNonZeroDivisor = Method("", """
        var divisor = 0;
        divisor += 1;
        return 10 / divisor;
        """);

    internal static readonly string IncrementedNonZeroDivisor = Method("", """
        var divisor = 0;
        divisor++;
        return 10 / divisor;
        """);

    internal static readonly string TupleAssignedNonZeroDivisor = Method("", """
        var divisor = 0;
        var other = 0;
        (divisor, other) = (1, 2);
        return 10 / divisor;
        """);

    internal static readonly string TupleDeconstructionDeclaredNonZeroDivisor = Method("", """
        var (divisor, other) = (1, 2);
        return 10 / divisor;
        """);

    internal static readonly string InlineFiniteArrayElementNonZeroDivisor = Method("", """
        var divisor = (new[] { 1, 2 })[0];
        return 10 / divisor;
        """);

    internal static readonly string PriorFiniteArrayElementNonZeroDivisor = Method("", """
        var values = new[] { 1, 2 };
        var divisor = values[0];
        return 10 / divisor;
        """);

    internal static readonly string InlineFiniteArrayFromEndElementNonZeroDivisor = Method("", """
        var divisor = (new[] { 1, 2 })[^1];
        return 10 / divisor;
        """);

    internal static readonly string PriorFiniteArrayFromEndElementNonZeroDivisor = Method("", """
        var values = new[] { 1, 2 };
        var divisor = values[^1];
        return 10 / divisor;
        """);

    internal static readonly string ConditionalFiniteArrayElementNonZeroDivisor = Method("bool flag", """
        var values = new[] { 1, 2 };
        var divisor = flag ? values[0] : values[1];
        return 10 / divisor;
        """);

    internal static readonly string TupleElementNonZeroDivisor = Method("", """
        var pair = (1, 2);
        var divisor = pair.Item1;
        return 10 / divisor;
        """);

    internal static readonly string NamedTupleElementNonZeroDivisor = Method("", """
        var pair = (divisor: 1, other: 2);
        var divisor = pair.divisor;
        return 10 / divisor;
        """);

    internal static readonly string TupleLocalDeconstructionAssignedNonZeroDivisor = Method("", """
        var pair = (1, 2);
        var divisor = 0;
        var other = 0;
        (divisor, other) = pair;
        return 10 / divisor;
        """);

    internal static readonly string TupleLocalDeconstructionDeclaredNonZeroDivisor = Method("", """
        var pair = (1, 2);
        var (divisor, other) = pair;
        return 10 / divisor;
        """);

    internal static readonly string SwitchStatementPatternBoundNonZeroDivisor = Method("int value", """
        switch (value)
        {
            case > 0 and var divisor:
                return 10 / divisor;
            default:
                return 0;
        }
        """);

    internal static readonly string SwitchStatementPriorSectionExcludesZeroDivisor = Method("int value", """
        switch (value)
        {
            case 0:
                return 0;
            case var divisor:
                return 10 / divisor;
        }
        """);

    internal static readonly string SwitchExpressionPatternBoundNonZeroDivisor = Method("int value", """
        return value switch
        {
            > 0 and var divisor => 10 / divisor,
            _ => 0
        };
        """);

    internal static readonly string SwitchExpressionFallbackExcludesZeroDivisor = Method("int value", """
        return value switch
        {
            0 => 0,
            _ => 10 / value
        };
        """);

    internal static readonly string RelationalPatternBoundNonZeroDivisor = Method("int value", """
        if (value is > 0 and var divisor)
        {
            return 10 / divisor;
        }
        return 0;
        """);

    internal static readonly string PropertyPatternBoundNonZeroLength = Method("string text", """
        if (text is { Length: > 0 and var length })
        {
            return 10 / length;
        }
        return 0;
        """);

    internal static readonly string ListPatternFirstElementNonZeroDivisor = Method("int[] values", """
        if (values is [> 0 and var divisor, ..])
        {
            return 10 / divisor;
        }
        return 0;
        """);

    internal static readonly string ListPatternTrailingElementNonZeroDivisor = Method("int[] values", """
        if (values is [.., > 0 and var divisor])
        {
            return 10 / divisor;
        }
        return 0;
        """);

    internal static readonly string ArrayElementReadFromListPatternNonZeroDivisor = Method("int[] values", """
        if (values is [> 0, ..])
        {
            var divisor = values[0];
            return 10 / divisor;
        }
        return 0;
        """);

    internal static readonly string IfElseElseExitZeroDivisor = Method("int divisor", """
        if (divisor == 0)
        {
        }
        else
        {
            return 0;
        }
        return 10 / divisor;
        """);

    internal static readonly string DefaultIntegralZeroDivisor = Method("", """
        int divisor = default;
        return 10 / divisor;
        """);

    internal static readonly string DefaultReferenceNull = Method("", """
        string value = default;
        return value.Length;
        """);

    private static string Method(string parameters, string body) => $$"""
        public class TestClass
        {
            public int TestMethod({{parameters}})
            {
        {{body}}
            }
        }
        """;
}
