using System.Collections.Immutable;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

public sealed partial class ApiSpecRuntimeOracleTests
{
    private static string? NullText => null;
    private static readonly string EmbeddedNullLeft = new(['A', '\0', 'B']);
    private static readonly string EmbeddedNullRight = new(['A', '\0', 'B']);

    // Concat(string, string, string) and Concat(string, string, string,
    // string): a null argument counts as empty; the result is never null.
    private static RowWitness CreateStringConcatWitness(int arity)
    {
        var inputs = "all-null, one non-empty among nulls, and " + arity + " non-empty strings";
        ImmutableArray<Func<string>> calls = arity == 3
            ? [
                static () => string.Concat(NullText, NullText, NullText),
                static () => string.Concat(NullText, ConcatLeft, NullText),
                static () => string.Concat(ConcatLeft, ConcatRight, ConcatLeft)
            ]
            : [
                static () => string.Concat(NullText, NullText, NullText, NullText),
                static () => string.Concat(NullText, NullText, ConcatRight, NullText),
                static () => string.Concat(ConcatLeft, ConcatRight, ConcatLeft, ConcatRight)
            ];
        var edges = calls.Select(call => RuntimeEdge.For(() => s_stringSink = call())).ToImmutableArray();
        var observedThrows = new Lazy<ThrowObservation>(() => ObserveThrows(edges));
        return Row(
            effects: Effect(inputs, () => ObserveNoEffects([() => ConcatenatesInOrder(arity)]), SpecEffect.WritesArgumentState),
            allocation: Allocation(inputs, edges, SpecAllocationBehavior.None),
            throws: Throws(inputs, observedThrows, DoesNotThrowMutation),
            nullness: Nullness(inputs, () => ObserveNullness([.. calls.Select(static call => (Func<object?>)(() => call()))]), SpecNullness.Null),
            termination: Termination(inputs, observedThrows, SpecTerminationBehavior.Unknown));
    }

    private static bool ConcatenatesInOrder(int arity)
    {
        return arity == 3
            ? string.Concat(NullText, NullText, NullText).Length == 0 &&
                ReferenceEquals(string.Concat(NullText, ConcatLeft, NullText), ConcatLeft) &&
                string.Concat(ConcatLeft, ConcatRight, ConcatLeft) == "leftrightleft" &&
                ConcatLeft == "left" && ConcatRight == "right"
            : string.Concat(NullText, NullText, NullText, NullText).Length == 0 &&
                ReferenceEquals(string.Concat(NullText, NullText, ConcatRight, NullText), ConcatRight) &&
                string.Concat(ConcatLeft, ConcatRight, ConcatLeft, ConcatRight) == "leftrightleftright" &&
                ConcatLeft == "left" && ConcatRight == "right";
    }

    private static RowWitness CreateStringCompareOrdinalWitness()
    {
        const string inputs = "null/null, null/value, value/null, distinct equal instances with embedded nulls, prefixes, case and code-unit order";
        var edges = ImmutableArray.Create(
            RuntimeEdge.For(() => s_integerSink = string.CompareOrdinal(NullText, NullText)),
            RuntimeEdge.For(() => s_integerSink = string.CompareOrdinal(NullText, ConcatLeft)),
            RuntimeEdge.For(() => s_integerSink = string.CompareOrdinal(ConcatLeft, NullText)),
            RuntimeEdge.For(() => s_integerSink = string.CompareOrdinal(EmbeddedNullLeft, EmbeddedNullRight)),
            RuntimeEdge.For(() => s_integerSink = string.CompareOrdinal(ConcatLeft, ConcatRight)),
            RuntimeEdge.For(() => s_integerSink = string.CompareOrdinal("ab", "abc")),
            RuntimeEdge.For(() => s_integerSink = string.CompareOrdinal("i", "I")));
        var observedThrows = new Lazy<ThrowObservation>(() => ObserveThrows(edges));
        return Row(
            effects: Effect(inputs, () => ObserveNoEffects([ComparesOrdinally]), SpecEffect.WritesAmbientState),
            allocation: Allocation(inputs, edges, SpecAllocationBehavior.MayAllocate),
            throws: Throws(inputs, observedThrows, DoesNotThrowMutation),
            termination: Termination(inputs, observedThrows, SpecTerminationBehavior.Unknown));
    }

    // The order is by UTF-16 code unit, so it does not depend on the culture:
    // 'i' (0x69) sorts after 'I' (0x49) under every culture.
    private static bool ComparesOrdinally()
    {
        return !ReferenceEquals(EmbeddedNullLeft, EmbeddedNullRight) &&
            string.CompareOrdinal(NullText, NullText) is 0 &&
            string.CompareOrdinal(NullText, ConcatLeft) < 0 &&
            string.CompareOrdinal(ConcatLeft, NullText) > 0 &&
            string.CompareOrdinal(EmbeddedNullLeft, EmbeddedNullRight) is 0 &&
            string.CompareOrdinal(ConcatLeft, ConcatRight) < 0 &&
            string.CompareOrdinal("ab", "abc") < 0 &&
            string.CompareOrdinal("i", "I") > 0;
    }
}
