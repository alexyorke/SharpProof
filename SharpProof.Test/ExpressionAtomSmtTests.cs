using NUnit.Framework;
using static SharpProof.Test.SymbolicProofTestAssertions;

namespace SharpProof.Test;

[TestFixture]
[Category("SmtHeavy")]
public sealed class ExpressionAtomSmtTests {
    public sealed record Expectation(string Marker, string Condition, bool Proven = true);

    public sealed record ExpressionCase(string Source, Expectation[] Expectations);

    private const string Holder = "public sealed class Holder\n{\n    public string Text;\n}";
    private const string Mode = "public enum Mode\n{\n    None = 0,\n    Ready = 1\n}";

    private static IEnumerable<TestCaseData> Cases() {
        yield return Case("SymbolicSourceQueryService_ProvesConditionalNullableMemberFacts",
            Method("int", "bool flag, int? left, int? right", "if (flag && left.HasValue && left.Value == 5)\n{\n    return left.Value;\n}\n\nreturn 0;"),
            Yes("return left.Value;", "(flag ? left : right).HasValue && (flag ? left : right).Value == 5"));
        yield return Case("SymbolicSourceQueryService_ProvesNullableValueComparisonHasValueFacts",
            Method("int", "int? value", "if (value.Value == 5)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "value.HasValue"), Yes("return 1;", "value.Value == 5"));
        yield return Case("SymbolicSourceQueryService_ProvesConditionalAccessReferenceNullCheck",
            Method("string", "Holder holder", "if (holder != null && holder.Text != null)\n{\n    return holder?.Text;\n}\n\nreturn null;", prefix: Holder),
            Yes("return holder?.Text;", "holder?.Text != null"));
        yield return Case("SymbolicSourceQueryService_ProvesConditionalAccessStringEqualityFacts",
            Method("int", "Holder holder", "if (holder?.Text == \"ABC\")\n{\n    return 1;\n}\n\nreturn 0;", prefix: Holder),
            Yes("return 1;", "holder != null && holder.Text == \"ABC\""));
        yield return Case("SymbolicSourceQueryService_ProvesConditionalAccessStringCoalesceLengthFacts",
            Method("int", "Holder holder, string fallback", "if ((holder?.Text ?? fallback) == \"OK\")\n{\n    return 1;\n}\n\nreturn 0;", prefix: Holder),
            Yes("return 1;", "(holder?.Text ?? fallback).Length == 2"));
        yield return Case("SymbolicSourceQueryService_ProvesTupleEqualityElementRelation",
            Method("int", "(int A, int B) left, (int A, int B) right", "if (left == right)\n{\n    return left.A;\n}\n\nreturn 0;"),
            Yes("return left.A;", "left.B == right.B"));
        yield return Case("SymbolicSourceQueryService_ProvesIdentityBooleanCastFacts",
            Method("int", "bool flag", "if ((bool)flag)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "flag == true"));
        yield return Case("SymbolicSourceQueryService_ProvesIdentityStringCastLengthFacts",
            Method("int", "string text", "if (text != null && ((string)text).Length == 3)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "text.Length != 4"));
        yield return Case("SymbolicSourceQueryService_ProvesTupleLiteralElementArithmeticFacts",
            Method("int", "int value, bool flag", "if ((value + 1, flag).Item1 == 5)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "value == 4"));
        yield return Case("SymbolicSourceQueryService_ProvesCheckedArithmeticAtomFacts",
            Method("int", "int value", "if (checked(value + 1) == 5)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "value == 4"));
        yield return Case("SymbolicSourceQueryService_ProvesCheckedNarrowingCastAtomFacts",
            Method("int", "int value", "if (checked((byte)value) == 5)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "value >= 0"), Yes("return 1;", "value <= 255"), Yes("return 1;", "value == 5"));
        yield return Case("SymbolicSourceQueryService_ProvesUncheckedEnumCastAtomFacts",
            Method("int", "Mode mode", "if (unchecked((int)mode) == 1)\n{\n    return 1;\n}\n\nreturn 0;", prefix: Mode),
            Yes("return 1;", "mode == Mode.Ready"));
        yield return Case("SymbolicSourceQueryService_ProvesCheckedIndexAtomFacts",
            Method("int", "int[] values, int index", "if (values != null && index >= 0 && index < values.Length && values[checked(index)] == 7)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "values[index] == 7"));
        yield return Case("SymbolicSourceQueryService_ProvesConditionalTupleElementFacts",
            Method("int", "bool flag, (int A, int B) left, (int A, int B) right", "if ((flag ? left : right).A > 0)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "(flag ? left : right).A != 0"));
        yield return Case("SymbolicSourceQueryService_ProvesConditionalBooleanAtomNullFacts",
            Method("int", "string text", "if (text != null ? text.Length == 3 : false)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "text != null"), Yes("return 1;", "text.Length == 3"));
        yield return Case("SymbolicSourceQueryService_ProvesConditionalBooleanAtomGuardedDivisionFacts",
            Method("int", "int value, int divisor", "if (divisor != 0 ? value / divisor == 3 : false)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "divisor != 0"));
        yield return Case("SymbolicSourceQueryService_ProvesEnumConstantComparison",
            Method("int", "Mode mode", "if (mode == Mode.Ready)\n{\n    return 1;\n}\n\nreturn 0;", prefix: Mode),
            Yes("return 1;", "mode != Mode.None"));
        yield return Case("SymbolicSourceQueryService_ProvesTypeOfStableConstantFacts",
            Method("int", string.Empty, "if (typeof(string) != typeof(object) && typeof(int) == typeof(int) && typeof(string) != null)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "typeof(string) != typeof(object)"), Yes("return 1;", "typeof(int) == typeof(int)"),
            Yes("return 1;", "typeof(string) != null"));
        yield return Case("SymbolicSourceQueryService_ProvesNullableEnumCoalesceComparisonFacts",
            Method("int", "Mode? left, Mode? right", "if ((left ?? right) == Mode.Ready)\n{\n    return 1;\n}\n\nreturn 0;", prefix: Mode),
            Yes("return 1;", "(left ?? right).HasValue && (left ?? right).Value != Mode.None"));
        yield return Case("SymbolicSourceQueryService_ProvesStringIndexCharAtom",
            Method("int", "string text", "if (text != null && text.Length > 0 && text[0] == 'A')\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "text[0] != 'B'"));
        yield return Case("SymbolicSourceQueryService_ProvesDefaultStaticStringEqualsFacts",
            Method("int", "string left, string right", "if (string.Equals(left, right))\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "left == right"));
        yield return Case("SymbolicSourceQueryService_ProvesDefaultInstanceStringEqualsFacts",
            Method("int", "string text", "if (text != null && text.Equals(\"ABC\"))\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "text == \"ABC\""));
        yield return Case("SymbolicSourceQueryService_ProvesDefaultStringContainsFacts",
            Method("int", "string text", "if (text != null && text.Contains(\"Z\"))\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "text != \"ABC\""));
        yield return Case("SymbolicSourceQueryService_DefaultStringStartsWithRemainsConservative",
            Method("int", "string text, string prefix", "if (text != null && prefix != null && text.StartsWith(prefix))\n{\n    return 1;\n}\n\nreturn 0;"),
            No("return 1;", "text.StartsWith(prefix, System.StringComparison.Ordinal)"));
        yield return Case("SymbolicSourceQueryService_ProvesAsExpressionNonNullImpliesSourceNonNull",
            Method("int", "object value", "if ((value as string) != null)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "value != null"));
        yield return Case("SymbolicSourceQueryService_ProvesIdentityReferenceCastNullRelation",
            Method("int", "string text", "if ((object)text != null)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "text != null"));
        yield return Case("SymbolicSourceQueryService_ProvesAsExpressionPreservesNullEquality",
            Method("int", "string text", "if ((text as object) == null)\n{\n    return 1;\n}\n\nreturn 0;"),
            Yes("return 1;", "text == null"));
        yield return Case("SymbolicSourceQueryService_NonNullObjectDoesNotProveTypeTest",
            Method("int", "object value", "if (value != null)\n{\n    return 1;\n}\n\nreturn 0;"),
            No("return 1;", "value is string"));
    }

    [TestCaseSource(nameof(Cases))]
    public void ExpressionAtomMatrix(ExpressionCase testCase) {
        foreach (var expectation in testCase.Expectations)
            if (expectation.Proven)
                AssertConditionProven(testCase.Source, expectation.Marker, expectation.Condition);
            else
                AssertConditionUnknown(testCase.Source, expectation.Marker, expectation.Condition);
    }

    private static string Method(
        string returnType,
        string parameters,
        string body,
        string? usings = null,
        string? prefix = null) => SemanticTestSource.Method(returnType, parameters, body, usings, prefix);

    private static Expectation Yes(string marker, string condition) => new(marker, condition);

    private static Expectation No(string marker, string condition) => new(marker, condition, false);

    private static TestCaseData Case(string name, string source, params Expectation[] expectations) =>
        new TestCaseData(new ExpressionCase(source, expectations)).SetName(name);
}
