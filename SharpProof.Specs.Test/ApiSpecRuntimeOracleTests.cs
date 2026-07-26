using System.Collections;
using System.Collections.Immutable;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class ApiSpecRuntimeOracleTests {
    private static readonly Action<object> CallObjectConstructor =
        CreateObjectConstructorInvoker();
    private static readonly object ListItem = new();
    private static readonly string ConcatLeft = new(['l', 'e', 'f', 't']);
    private static readonly string ConcatRight = new(['r', 'i', 'g', 'h', 't']);
    private static readonly ImmutableDictionary<string, RowWitness> Witnesses =
        CreateWitnesses();

    private static object s_objectConstructorReceiver = new();
    private static GhostProbe s_ghostProbe = new();
    private static string? s_stringSink;
    private static List<object?> s_list = [];
    private static int s_integerSink;

    [Test]
    public void WitnessRegistryCoversEveryKnownFacetAndPostcondition() {
        var templates = ApiSpecTable.Default.Templates;

        Assert.That(
            Witnesses.Keys,
            Is.EquivalentTo(templates.Select(static template =>
                template.Target.WitnessIdentifier)));
        foreach (var template in templates) {
            var identifier = template.Target.WitnessIdentifier;
            var row = Witnesses[identifier];
            var expectedFacets = ExpectedFacets(template);
            var actualFacets = row.Facets.Select(static witness => witness.Facet).ToArray();

            Assert.Multiple(() => {
                Assert.That(
                    actualFacets,
                    Is.Unique,
                    identifier + " registers a facet more than once.");
                Assert.That(
                    actualFacets,
                    Is.EquivalentTo(expectedFacets),
                    identifier + " does not cover exactly its known facets.");
                Assert.That(
                    row.Facets.Select(static witness => witness.EdgeInputs),
                    Has.All.Not.Empty,
                    identifier + " has a facet witness without named edge inputs.");
                Assert.That(
                    row.Postconditions.Select(static witness => witness.Index),
                    Is.EqualTo(Enumerable.Range(0, template.Postconditions.Length)),
                    identifier + " does not cover every postcondition exactly once.");
                Assert.That(
                    row.Postconditions.Select(static witness => witness.EdgeInputs),
                    Has.All.Not.Empty,
                    identifier + " has a postcondition witness without named edge inputs.");
            });
        }
    }

    [Test]
    public void EveryFacetWitnessAcceptsTheDeclaredClaimOnEdgeInputs() {
        foreach (var template in ApiSpecTable.Default.Templates) {
            var identifier = template.Target.WitnessIdentifier;
            foreach (var witness in Witnesses[identifier].Facets)
                Assert.That(
                    witness.AcceptsDeclared(template),
                    Is.True,
                    identifier + "/" + witness.Facet + " failed on " +
                    witness.EdgeInputs + ".");
        }
    }

    [Test]
    public void EveryFacetWitnessRejectsADeterministicWrongClaim() {
        foreach (var template in ApiSpecTable.Default.Templates) {
            var identifier = template.Target.WitnessIdentifier;
            foreach (var witness in Witnesses[identifier].Facets)
                Assert.That(
                    witness.AcceptsMutation(),
                    Is.False,
                    identifier + "/" + witness.Facet +
                    " accepted its deterministic mutation on " +
                    witness.EdgeInputs + ".");
        }
    }

    [Test]
    public void EveryPostconditionWitnessAcceptsTheClaimAndRejectsItsNegation() {
        foreach (var template in ApiSpecTable.Default.Templates) {
            var identifier = template.Target.WitnessIdentifier;
            foreach (var witness in Witnesses[identifier].Postconditions) {
                Assert.That(
                    witness.AcceptsDeclared(template),
                    Is.True,
                    identifier + "/postcondition[" + witness.Index +
                    "] failed on " + witness.EdgeInputs + ".");
                Assert.That(
                    witness.AcceptsNegatedMutation(template),
                    Is.False,
                    identifier + "/postcondition[" + witness.Index +
                    "] accepted its negation on " + witness.EdgeInputs + ".");
            }
        }
    }

    private static ImmutableDictionary<string, RowWitness> CreateWitnesses() {
        var doesNotThrowMutation = new ThrowClaim(
            SpecThrowBehavior.MayThrow,
            ["System.Exception"]);
        return new Dictionary<string, RowWitness>(StringComparer.Ordinal) {
            ["bcl.object.ctor"] = Row(
                Effect(
                    "an already allocated receiver",
                    static () => ObserveNoEffects(ObjectConstructorEdge),
                    SpecEffect.WritesAmbientState),
                allocation: Allocation(
                    "an already allocated receiver, excluding newobj",
                    [
                        new AllocationEdge(
                            PrepareObjectConstructorReceiver,
                            InvokePreparedObjectConstructor)
                    ],
                    SpecAllocationBehavior.MayAllocate),
                throws: Throws(
                    "an already allocated receiver",
                    [
                        new ThrowEdge(
                            PrepareObjectConstructorReceiver,
                            InvokePreparedObjectConstructor)
                    ],
                    doesNotThrowMutation)),
            ["contract.assume"] = Row(
                Effect(
                    "false and true compiler-bound conditions",
                    static () => ObserveNoEffects(
                        static () => ContractAssumeEdge(false),
                        static () => ContractAssumeEdge(true)),
                    SpecEffect.WritesAmbientState),
                allocation: Allocation(
                    "false and true compiler-bound conditions",
                    [
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokePreparedAssumeFalse),
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokePreparedAssumeTrue)
                    ],
                    SpecAllocationBehavior.MayAllocate),
                throws: Throws(
                    "false and true compiler-bound conditions",
                    [
                        ThrowEdge.For(static () => ContractAssumeEdge(false)),
                        ThrowEdge.For(static () => ContractAssumeEdge(true))
                    ],
                    doesNotThrowMutation)),
            ["contract.ensures"] = Row(
                Effect(
                    "false and true compiler-bound conditions",
                    static () => ObserveNoEffects(
                        static () => ContractEnsuresEdge(false),
                        static () => ContractEnsuresEdge(true)),
                    SpecEffect.WritesAmbientState),
                allocation: Allocation(
                    "false and true compiler-bound conditions",
                    [
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokePreparedEnsuresFalse),
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokePreparedEnsuresTrue)
                    ],
                    SpecAllocationBehavior.MayAllocate),
                throws: Throws(
                    "false and true compiler-bound conditions",
                    [
                        ThrowEdge.For(static () => ContractEnsuresEdge(false)),
                        ThrowEdge.For(static () => ContractEnsuresEdge(true))
                    ],
                    doesNotThrowMutation)),
            ["contract.old"] = Row(
                Effect(
                    "direct null and non-null arguments",
                    static () => ObserveNoEffects(
                        static () => ContractOldDirectHasNoEffects(null),
                        static () => ContractOldDirectHasNoEffects(ListItem)),
                    SpecEffect.WritesAmbientState),
                allocation: Allocation(
                    "direct null and non-null arguments",
                    [
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokeAndCatchOldNull),
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokeAndCatchOldItem)
                    ],
                    SpecAllocationBehavior.None),
                throws: Throws(
                    "direct null and non-null arguments",
                    [
                        ThrowEdge.For(InvokeOldNullDirectly),
                        ThrowEdge.For(InvokeOldItemDirectly)
                    ],
                    doesNotThrowMutation)),
            ["contract.requires"] = Row(
                Effect(
                    "false and true compiler-bound conditions",
                    static () => ObserveNoEffects(
                        static () => ContractRequiresEdge(false),
                        static () => ContractRequiresEdge(true)),
                    SpecEffect.WritesAmbientState),
                allocation: Allocation(
                    "false and true compiler-bound conditions",
                    [
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokePreparedRequiresFalse),
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokePreparedRequiresTrue)
                    ],
                    SpecAllocationBehavior.MayAllocate),
                throws: Throws(
                    "false and true compiler-bound conditions",
                    [
                        ThrowEdge.For(static () => ContractRequiresEdge(false)),
                        ThrowEdge.For(static () => ContractRequiresEdge(true))
                    ],
                    doesNotThrowMutation)),
            ["contract.result"] = Row(
                Effect(
                    "a direct reference result intrinsic call",
                    static () => ObserveNoEffects(
                        ContractResultDirectHasNoEffects),
                    SpecEffect.WritesAmbientState),
                allocation: Allocation(
                    "a direct reference result intrinsic call",
                    [
                        new AllocationEdge(
                            PrepareGhostProbe,
                            InvokeAndCatchResult)
                    ],
                    SpecAllocationBehavior.None),
                throws: Throws(
                    "a direct reference result intrinsic call",
                    [ThrowEdge.For(InvokeResultDirectly)],
                    doesNotThrowMutation)),
            ["bcl.string.length"] = Row(
                Effect(
                    "empty and embedded-null receivers",
                    ObserveStringLengthEffect,
                    SpecEffect.None),
                allocation: Allocation(
                    "empty and embedded-null receivers",
                    [
                        AllocationEdge.For(ReadEmptyStringLength),
                        AllocationEdge.For(ReadEmbeddedNullStringLength)
                    ],
                    SpecAllocationBehavior.MayAllocate),
                throws: Throws(
                    "empty and embedded-null receivers",
                    [
                        ThrowEdge.For(ReadEmptyStringLength),
                        ThrowEdge.For(ReadEmbeddedNullStringLength)
                    ],
                    doesNotThrowMutation),
                postconditions: [
                    Postcondition(
                        0,
                        "empty and embedded-null receivers",
                        static () => RuntimeCall.ForReceiver(
                            string.Empty,
                            string.Empty.Length),
                        static () => RuntimeCall.ForReceiver(
                            "A\0B",
                            "A\0B".Length))
                ]),
            ["bcl.string.concat.string-string"] = Row(
                Effect(
                    "null/null, null/value, and two non-empty strings",
                    static () => ObserveNoEffects(StringConcatEdge),
                    SpecEffect.WritesArgumentState),
                allocation: Allocation(
                    "null/null and two non-empty strings",
                    [
                        AllocationEdge.For(ConcatNulls),
                        AllocationEdge.For(ConcatNonEmpty)
                    ],
                    SpecAllocationBehavior.None),
                throws: Throws(
                    "null/null, null/value, and two non-empty strings",
                    [
                        ThrowEdge.For(ConcatNulls),
                        ThrowEdge.For(ConcatNullAndValue),
                        ThrowEdge.For(ConcatNonEmpty)
                    ],
                    doesNotThrowMutation),
                nullness: Nullness(
                    "null/null, null/value, and two non-empty strings",
                    static () => ObserveNullness(
                        static () => string.Concat(null, null),
                        static () => string.Concat(null, ConcatRight),
                        static () => string.Concat(ConcatLeft, ConcatRight)),
                    SpecNullness.Null)),
            ["bcl.list.add"] = Row(
                Effect(
                    "adding null and non-null values to empty receivers",
                    ObserveListAddEffect,
                    SpecEffect.None),
                allocation: Allocation(
                    "first add of null and non-null values",
                    [
                        new AllocationEdge(
                            PrepareEmptyList,
                            AddNullToPreparedList),
                        new AllocationEdge(
                            PrepareEmptyList,
                            AddItemToPreparedList)
                    ],
                    SpecAllocationBehavior.None)),
            ["bcl.math.abs.int32"] = Row(
                Effect(
                    "negative, zero, and maximum inputs",
                    static () => ObserveNoEffects(MathAbsNormalEdges),
                    SpecEffect.WritesAmbientState),
                allocation: Allocation(
                    "negative, zero, and maximum inputs",
                    [
                        AllocationEdge.For(AbsNegative),
                        AllocationEdge.For(AbsZero),
                        AllocationEdge.For(AbsMaximum)
                    ],
                    SpecAllocationBehavior.MayAllocate),
                throws: Throws(
                    "negative, zero, maximum, and minimum inputs",
                    [
                        ThrowEdge.For(AbsNegative),
                        ThrowEdge.For(AbsZero),
                        ThrowEdge.For(AbsMaximum),
                        ThrowEdge.For(AbsMinimum)
                    ],
                    new ThrowClaim(
                        SpecThrowBehavior.MayThrow,
                        ["System.ArgumentException"])),
                postconditions: [
                    Postcondition(
                        0,
                        "negative, zero, and maximum normal returns",
                        static () => RuntimeCall.ForParameterAndResult(
                            -17,
                            Math.Abs(-17)),
                        static () => RuntimeCall.ForParameterAndResult(
                            0,
                            Math.Abs(0)),
                        static () => RuntimeCall.ForParameterAndResult(
                            int.MaxValue,
                            Math.Abs(int.MaxValue)))
                ]),
            ["bcl.enumerable.empty"] = Row(
                Effect(
                    "reference-type and value-type generic instantiations",
                    static () => ObserveNoEffects(EnumerableEmptyEdge),
                    SpecEffect.WritesAmbientState),
                throws: Throws(
                    "reference-type and value-type generic instantiations",
                    [
                        ThrowEdge.For(EnumerateEmptyObjects),
                        ThrowEdge.For(EnumerateEmptyIntegers)
                    ],
                    doesNotThrowMutation),
                nullness: Nullness(
                    "reference-type and value-type generic instantiations",
                    static () => ObserveNullness(
                        static () => Enumerable.Empty<object>(),
                        static () => Enumerable.Empty<int>()),
                    SpecNullness.Null),
                cardinality: Cardinality(
                    "reference-type and value-type generic instantiations",
                    static () => ObserveCardinality(
                        static () => Enumerable.Empty<object>(),
                        static () => Enumerable.Empty<int>()),
                    SpecCardinality.NonEmpty))
        }.ToImmutableDictionary(StringComparer.Ordinal);
    }

    private static RowWitness Row(
        IFacetWitness effects,
        IFacetWitness? allocation = null,
        IFacetWitness? throws = null,
        IFacetWitness? nullness = null,
        IFacetWitness? cardinality = null,
        ImmutableArray<PostconditionWitness> postconditions = default) {
        var facets = ImmutableArray.CreateBuilder<IFacetWitness>(5);
        facets.Add(effects);
        if (allocation != null) facets.Add(allocation);
        if (throws != null) facets.Add(throws);
        if (nullness != null) facets.Add(nullness);
        if (cardinality != null) facets.Add(cardinality);
        return new RowWitness(
            facets.ToImmutable(),
            postconditions.IsDefault ? [] : postconditions);
    }

    private static IFacetWitness Effect(
        string edgeInputs,
        Func<SpecEffect> observe,
        SpecEffect mutation) =>
        new FacetWitness<SpecEffect>(
            FacetKind.Effects,
            edgeInputs,
            static template => template.Facets.Effects.Effects,
            claim => observe() == claim,
            mutation);

    private static IFacetWitness Allocation(
        string edgeInputs,
        ImmutableArray<AllocationEdge> edges,
        SpecAllocationBehavior mutation) =>
        new FacetWitness<SpecAllocationBehavior>(
            FacetKind.Allocation,
            edgeInputs,
            static template => template.Facets.Allocation.Behavior,
            claim => ObserveAllocation(edges) == claim,
            mutation);

    private static IFacetWitness Throws(
        string edgeInputs,
        ImmutableArray<ThrowEdge> edges,
        ThrowClaim mutation) =>
        new FacetWitness<ThrowClaim>(
            FacetKind.Throws,
            edgeInputs,
            static template => new ThrowClaim(
                template.Facets.Throws.Behavior,
                template.Facets.Throws.ExceptionMetadataNames),
            claim => MatchesThrowClaim(ObserveThrows(edges), claim),
            mutation);

    private static IFacetWitness Nullness(
        string edgeInputs,
        Func<SpecNullness> observe,
        SpecNullness mutation) =>
        new FacetWitness<SpecNullness>(
            FacetKind.Nullness,
            edgeInputs,
            static template => template.Facets.Nullness.Result,
            claim => observe() == claim,
            mutation);

    private static IFacetWitness Cardinality(
        string edgeInputs,
        Func<SpecCardinality> observe,
        SpecCardinality mutation) =>
        new FacetWitness<SpecCardinality>(
            FacetKind.Cardinality,
            edgeInputs,
            static template => template.Facets.Cardinality.Result,
            claim => observe() == claim,
            mutation);

    private static PostconditionWitness Postcondition(
        int index,
        string edgeInputs,
        params Func<RuntimeCall>[] edges) =>
        new(index, edgeInputs, [.. edges]);

    private static ImmutableArray<FacetKind> ExpectedFacets(ApiSpecTemplate template) {
        var facets = ImmutableArray.CreateBuilder<FacetKind>(5);
        if (template.Facets.Effects.Effects != SpecEffect.Unknown)
            facets.Add(FacetKind.Effects);
        if (template.Facets.Allocation.Behavior != SpecAllocationBehavior.Unknown)
            facets.Add(FacetKind.Allocation);
        if (template.Facets.Throws.Behavior != SpecThrowBehavior.Unknown)
            facets.Add(FacetKind.Throws);
        if (template.Facets.Nullness.Result is not (
                SpecNullness.Unknown or SpecNullness.NotApplicable))
            facets.Add(FacetKind.Nullness);
        if (template.Facets.Cardinality.Result is not (
                SpecCardinality.Unknown or SpecCardinality.NotApplicable))
            facets.Add(FacetKind.Cardinality);
        return facets.ToImmutable();
    }

    private static SpecEffect ObserveNoEffects(params Func<bool>[] edges) =>
        edges.All(static edge => edge()) ? SpecEffect.None : SpecEffect.Unknown;

    private static SpecEffect ObserveStringLengthEffect() {
        const string empty = "";
        const string embeddedNull = "A\0B";
        var emptyLength = empty.Length;
        var embeddedNullLength = embeddedNull.Length;
        return emptyLength == 0 && embeddedNullLength == 3 &&
               emptyLength != embeddedNullLength
            ? SpecEffect.ReadsReceiverState
            : SpecEffect.Unknown;
    }

    private static SpecEffect ObserveListAddEffect() {
        var item = new object();
        var withItem = new List<object?>();
        var withNull = new List<object?>();
        withItem.Add(item);
        withNull.Add(null);
        return withItem.Count == 1 &&
               ReferenceEquals(withItem[0], item) &&
               withNull.Count == 1 &&
               withNull[0] == null
            ? SpecEffect.WritesReceiverState
            : SpecEffect.Unknown;
    }

    private static SpecAllocationBehavior ObserveAllocation(
        ImmutableArray<AllocationEdge> edges) {
        var observedAllocation = false;
        foreach (var edge in edges) {
            for (var iteration = 0; iteration < 128; iteration++) {
                edge.Prepare();
                edge.Invoke();
            }
            edge.Prepare();
            var before = GC.GetAllocatedBytesForCurrentThread();
            edge.Invoke();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(s_stringSink);
            _ = s_integerSink;
            observedAllocation |= allocated > 0;
        }
        return observedAllocation
            ? SpecAllocationBehavior.MayAllocate
            : SpecAllocationBehavior.None;
    }

    private static ThrowObservation ObserveThrows(ImmutableArray<ThrowEdge> edges) {
        var normalCompletions = 0;
        var exceptionTypes = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var edge in edges) {
            edge.Prepare();
            try {
                edge.Invoke();
                normalCompletions++;
            }
            catch (Exception exception) {
                exceptionTypes.Add(
                    exception.GetType().FullName ??
                    throw new AssertionException("A runtime exception type had no metadata name."));
            }
        }
        return new ThrowObservation(
            edges.Length,
            normalCompletions,
            exceptionTypes.ToImmutable());
    }

    private static bool MatchesThrowClaim(
        ThrowObservation observation,
        ThrowClaim claim) =>
        claim.Behavior switch {
            SpecThrowBehavior.DoesNotThrow =>
                observation.NormalCompletions == observation.InvocationCount &&
                observation.ExceptionMetadataNames.IsEmpty &&
                claim.ExceptionMetadataNames.IsEmpty,
            SpecThrowBehavior.MayThrow =>
                !observation.ExceptionMetadataNames.IsEmpty &&
                observation.ExceptionMetadataNames.SetEquals(
                    claim.ExceptionMetadataNames),
            _ => false
        };

    private static SpecNullness ObserveNullness(params Func<object?>[] edges) {
        var sawNull = false;
        var sawNonNull = false;
        foreach (var edge in edges) {
            if (edge() == null) sawNull = true;
            else sawNonNull = true;
        }
        if (sawNull && sawNonNull) return SpecNullness.MaybeNull;
        if (sawNull) return SpecNullness.Null;
        return sawNonNull ? SpecNullness.NonNull : SpecNullness.Unknown;
    }

    private static SpecCardinality ObserveCardinality(
        params Func<IEnumerable>[] edges) {
        var counts = edges.Select(static edge => Count(edge())).ToArray();
        if (counts.All(static count => count == 0)) return SpecCardinality.Empty;
        if (counts.All(static count => count > 0)) return SpecCardinality.NonEmpty;
        return SpecCardinality.Unknown;
    }

    private static int Count(IEnumerable sequence) {
        var count = 0;
        foreach (var unused in sequence) {
            _ = unused;
            count++;
        }
        return count;
    }

    private static bool ValidatePostcondition(
        ApiSpecTemplate template,
        SpecTerm condition,
        ImmutableArray<Func<RuntimeCall>> edges) {
        foreach (var edge in edges) {
            var call = edge();
            var bindings = new Dictionary<SpecVarId, object?>();
            foreach (var variable in template.Variables) {
                var value = variable.Role switch {
                    SpecVariableRole.Receiver => call.Receiver,
                    SpecVariableRole.Parameter => call.Parameters[variable.Ordinal],
                    SpecVariableRole.Result => call.Result,
                    _ => throw new AssertionException("Unknown spec variable role.")
                };
                bindings.Add(variable.Id, value);
            }
            if (Evaluate(condition, bindings) is not true) return false;
        }
        return true;
    }

    private static object? Evaluate(
        SpecTerm term,
        IReadOnlyDictionary<SpecVarId, object?> bindings) =>
        term switch {
            SpecVariableTerm variable => bindings[variable.Variable],
            SpecBooleanTerm boolean => boolean.Value,
            SpecIntegerTerm integer => integer.Value,
            SpecStringTerm text => text.Value,
            SpecNullTerm => null,
            SpecUnaryTerm unary => EvaluateUnary(unary, bindings),
            SpecBinaryTerm binary => EvaluateBinary(binary, bindings),
            SpecConditionalTerm conditional => AsBoolean(
                Evaluate(conditional.Condition, bindings))
                ? Evaluate(conditional.WhenTrue, bindings)
                : Evaluate(conditional.WhenFalse, bindings),
            SpecLengthTerm length => EvaluateLength(length, bindings),
            _ => throw new AssertionException("Unknown spec term.")
        };

    private static object EvaluateUnary(
        SpecUnaryTerm unary,
        IReadOnlyDictionary<SpecVarId, object?> bindings) =>
        unary.Operator switch {
            SpecUnaryOperator.Not => !AsBoolean(Evaluate(unary.Operand, bindings)),
            SpecUnaryOperator.Negate => -AsInteger(Evaluate(unary.Operand, bindings)),
            _ => throw new AssertionException("Unknown unary spec operator.")
        };

    private static object EvaluateBinary(
        SpecBinaryTerm binary,
        IReadOnlyDictionary<SpecVarId, object?> bindings) {
        var left = Evaluate(binary.Left, bindings);
        var right = Evaluate(binary.Right, bindings);
        return binary.Operator switch {
            SpecBinaryOperator.Add => AsInteger(left) + AsInteger(right),
            SpecBinaryOperator.Subtract => AsInteger(left) - AsInteger(right),
            SpecBinaryOperator.Multiply => AsInteger(left) * AsInteger(right),
            SpecBinaryOperator.Divide => AsInteger(left) / AsInteger(right),
            SpecBinaryOperator.Remainder => AsInteger(left) % AsInteger(right),
            SpecBinaryOperator.AndAlso => AsBoolean(left) && AsBoolean(right),
            SpecBinaryOperator.OrElse => AsBoolean(left) || AsBoolean(right),
            SpecBinaryOperator.Equal => RuntimeEquals(left, right),
            SpecBinaryOperator.NotEqual => !RuntimeEquals(left, right),
            SpecBinaryOperator.LessThan => AsInteger(left) < AsInteger(right),
            SpecBinaryOperator.LessThanOrEqual => AsInteger(left) <= AsInteger(right),
            SpecBinaryOperator.GreaterThan => AsInteger(left) > AsInteger(right),
            SpecBinaryOperator.GreaterThanOrEqual => AsInteger(left) >= AsInteger(right),
            SpecBinaryOperator.StringConcat =>
                string.Concat(left as string, right as string),
            _ => throw new AssertionException("Unknown binary spec operator.")
        };
    }

    private static long EvaluateLength(
        SpecLengthTerm length,
        IReadOnlyDictionary<SpecVarId, object?> bindings) =>
        Evaluate(length.Value, bindings) switch {
            string text => text.Length,
            IEnumerable sequence => Count(sequence),
            _ => throw new AssertionException(
                "A runtime length witness received a non-sequence value.")
        };

    private static bool RuntimeEquals(object? left, object? right) =>
        IsInteger(left) && IsInteger(right)
            ? AsInteger(left) == AsInteger(right)
            : Equals(left, right);

    private static bool IsInteger(object? value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong;

    private static long AsInteger(object? value) =>
        value == null
            ? throw new AssertionException("Expected an integer runtime value.")
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);

    private static bool AsBoolean(object? value) =>
        value is bool boolean
            ? boolean
            : throw new AssertionException("Expected a boolean runtime value.");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ObjectConstructorEdge() {
        var receiver = new object();
        CallObjectConstructor(receiver);
        return receiver.GetType() == typeof(object);
    }

    private static Action<object> CreateObjectConstructorInvoker() {
        var method = new DynamicMethod(
            "SharpProof_ObjectConstructorWitness",
            typeof(void),
            [typeof(object)],
            typeof(ApiSpecRuntimeOracleTests).Module,
            true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(
            OpCodes.Call,
            typeof(object).GetConstructor(Type.EmptyTypes) ??
            throw new AssertionException("System.Object constructor was unavailable."));
        generator.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<object>>();
    }

    private static void PrepareObjectConstructorReceiver() =>
        s_objectConstructorReceiver = new object();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedObjectConstructor() =>
        CallObjectConstructor(s_objectConstructorReceiver);

    private static void PrepareGhostProbe() => s_ghostProbe = new GhostProbe();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedAssumeFalse() =>
        Contract.Assume(s_ghostProbe.TouchBoolean(false));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedAssumeTrue() =>
        Contract.Assume(s_ghostProbe.TouchBoolean(true));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedEnsuresFalse() =>
        Contract.Ensures(s_ghostProbe.TouchBoolean(false));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedEnsuresTrue() =>
        Contract.Ensures(s_ghostProbe.TouchBoolean(true));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeAndCatchOldNull() {
        try {
            _ = Contract.Old<object?>(null);
        }
        catch (InvalidOperationException) {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeAndCatchOldItem() {
        try {
            _ = Contract.Old(ListItem);
        }
        catch (InvalidOperationException) {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeOldNullDirectly() =>
        _ = Contract.Old<object?>(null);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeOldItemDirectly() =>
        _ = Contract.Old(ListItem);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedRequiresFalse() =>
        Contract.Requires(s_ghostProbe.TouchBoolean(false));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedRequiresTrue() =>
        Contract.Requires(s_ghostProbe.TouchBoolean(true));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeAndCatchResult() {
        try {
            _ = Contract.Result<object>();
        }
        catch (InvalidOperationException) {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeResultDirectly() =>
        _ = Contract.Result<object>();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractAssumeEdge(bool condition) {
        var probe = new GhostProbe();
        Contract.Assume(probe.TouchBoolean(condition));
        return probe.Touches == 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractEnsuresEdge(bool condition) {
        var probe = new GhostProbe();
        Contract.Ensures(probe.TouchBoolean(condition));
        return probe.Touches == 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractOldDirectHasNoEffects(object? value) {
        try {
            _ = Contract.Old(value);
            return false;
        }
        catch (InvalidOperationException) {
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractRequiresEdge(bool condition) {
        var probe = new GhostProbe();
        Contract.Requires(probe.TouchBoolean(condition));
        return probe.Touches == 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractResultDirectHasNoEffects() {
        try {
            _ = Contract.Result<object>();
            return false;
        }
        catch (InvalidOperationException) {
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadEmptyStringLength() =>
        s_integerSink = string.Empty.Length;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadEmbeddedNullStringLength() =>
        s_integerSink = "A\0B".Length;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool StringConcatEdge() =>
        string.Concat(null, null) == string.Empty &&
        string.Concat(null, ConcatRight) == ConcatRight &&
        string.Concat(ConcatLeft, ConcatRight) == "leftright";

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConcatNulls() =>
        s_stringSink = string.Concat(null, null);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConcatNullAndValue() =>
        s_stringSink = string.Concat(null, ConcatRight);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConcatNonEmpty() =>
        s_stringSink = string.Concat(ConcatLeft, ConcatRight);

    private static void PrepareEmptyList() => s_list = [];

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddNullToPreparedList() => s_list.Add(null);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddItemToPreparedList() => s_list.Add(ListItem);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool MathAbsNormalEdges() =>
        Math.Abs(-17) == 17 &&
        Math.Abs(0) == 0 &&
        Math.Abs(int.MaxValue) == int.MaxValue;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsNegative() => s_integerSink = Math.Abs(-17);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsZero() => s_integerSink = Math.Abs(0);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsMaximum() => s_integerSink = Math.Abs(int.MaxValue);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsMinimum() => s_integerSink = Math.Abs(int.MinValue);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool EnumerableEmptyEdge() =>
        Enumerable.Empty<object>().Count() == 0 &&
        Enumerable.Empty<int>().Count() == 0;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnumerateEmptyObjects() =>
        s_integerSink = Enumerable.Empty<object>().Count();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnumerateEmptyIntegers() =>
        s_integerSink = Enumerable.Empty<int>().Count();

    private enum FacetKind {
        Effects,
        Allocation,
        Throws,
        Nullness,
        Cardinality
    }

    private interface IFacetWitness {
        FacetKind Facet { get; }
        string EdgeInputs { get; }
        bool AcceptsDeclared(ApiSpecTemplate template);
        bool AcceptsMutation();
    }

    private sealed class FacetWitness<T>(
        FacetKind facet,
        string edgeInputs,
        Func<ApiSpecTemplate, T> selectClaim,
        Func<T, bool> validate,
        T mutation) : IFacetWitness {
        public FacetKind Facet { get; } = facet;
        public string EdgeInputs { get; } = edgeInputs;

        public bool AcceptsDeclared(ApiSpecTemplate template) =>
            validate(selectClaim(template));

        public bool AcceptsMutation() => validate(mutation);
    }

    private sealed record RowWitness(
        ImmutableArray<IFacetWitness> Facets,
        ImmutableArray<PostconditionWitness> Postconditions);

    private sealed record PostconditionWitness(
        int Index,
        string EdgeInputs,
        ImmutableArray<Func<RuntimeCall>> Edges) {
        public bool AcceptsDeclared(ApiSpecTemplate template) =>
            ValidatePostcondition(
                template,
                template.Postconditions[Index].Condition,
                Edges);

        public bool AcceptsNegatedMutation(ApiSpecTemplate template) {
            var declared = template.Postconditions[Index].Condition;
            var mutation = new SpecUnaryTerm(
                SpecUnaryOperator.Not,
                declared,
                SpecValueType.Boolean);
            return ValidatePostcondition(template, mutation, Edges);
        }
    }

    private sealed record AllocationEdge(Action Prepare, Action Invoke) {
        public static AllocationEdge For(Action invoke) => new(
            static () => { },
            invoke);

        public static AllocationEdge For(Func<bool> invoke) => new(
            static () => { },
            () => _ = invoke());
    }

    private sealed record ThrowEdge(Action Prepare, Action Invoke) {
        public static ThrowEdge For(Action invoke) => new(
            static () => { },
            invoke);

        public static ThrowEdge For(Func<bool> invoke) => new(
            static () => { },
            () => _ = invoke());
    }

    private sealed record ThrowClaim(
        SpecThrowBehavior Behavior,
        ImmutableArray<string> ExceptionMetadataNames);

    private sealed record ThrowObservation(
        int InvocationCount,
        int NormalCompletions,
        ImmutableHashSet<string> ExceptionMetadataNames);

    private sealed record RuntimeCall(
        object? Receiver,
        ImmutableArray<object?> Parameters,
        object? Result) {
        public static RuntimeCall ForReceiver(object receiver, object? result) =>
            new(receiver, [], result);

        public static RuntimeCall ForParameterAndResult(
            object? parameter,
            object? result) =>
            new(null, [parameter], result);
    }

    private sealed class GhostProbe {
        public int Touches { get; private set; }

        public bool TouchBoolean(bool value) {
            Touches++;
            return value;
        }

        public object? TouchObject(object? value) {
            Touches++;
            return value;
        }
    }
}
