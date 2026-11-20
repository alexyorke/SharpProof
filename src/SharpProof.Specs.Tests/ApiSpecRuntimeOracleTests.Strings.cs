using System.Collections.Immutable;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

public sealed partial class ApiSpecRuntimeOracleTests
{
    private static string? NullText => null;
    private static readonly string EmbeddedNullLeft = new(['A', '\0', 'B']);
    private static readonly string EmbeddedNullRight = new(['A', '\0', 'B']);

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
