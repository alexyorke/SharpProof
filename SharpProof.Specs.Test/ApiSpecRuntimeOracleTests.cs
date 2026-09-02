using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed partial class ApiSpecRuntimeOracleTests
{
    private static readonly Action<Exception> CallExceptionConstructor =
        CreateParameterlessConstructorInvoker<Exception>();
    private static readonly Action<Exception, string?> CallExceptionStringConstructor =
        CreateStringConstructorInvoker<Exception>();
    private static readonly Action<InvalidOperationException> CallInvalidOperationExceptionConstructor =
        CreateParameterlessConstructorInvoker<InvalidOperationException>();
    private static readonly Action<InvalidOperationException, string?>
        CallInvalidOperationExceptionStringConstructor =
            CreateStringConstructorInvoker<InvalidOperationException>();
    private static readonly Action<object> CallObjectConstructor =
        CreateParameterlessConstructorInvoker<object>();
    private static readonly object ListItem = new();
    private static readonly string ConcatLeft = new(['l', 'e', 'f', 't']);
    private static readonly string ConcatRight = new(['r', 'i', 'g', 'h', 't']);
    private static readonly ThrowClaim DoesNotThrowMutation = new(
        SpecThrowBehavior.MayThrow,
        ["System.Exception"]);
    private static readonly ImmutableDictionary<string, RowWitness> Witnesses =
        CreateWitnesses();

    private static Exception s_exceptionConstructorReceiver =
        CreateUninitializedException<Exception>();
    private static InvalidOperationException s_invalidOperationExceptionConstructorReceiver =
        CreateUninitializedException<InvalidOperationException>();
    private static object s_objectConstructorReceiver = new();
    private static GhostProbe s_ghostProbe = new();
    private static string? s_stringSink;
    private static List<object?> s_list = [];
    private static int s_integerSink;

    [Test]
    public void WitnessRegistryCoversEveryKnownFacetAndPostcondition()
    {
        var templates = ApiSpecTable.Default.Templates;

        Assert.That(
            Witnesses.Keys,
            Is.EquivalentTo(templates.Select(static template =>
                template.Target.WitnessIdentifier)));
        foreach (var template in templates)
        {
            var identifier = template.Target.WitnessIdentifier;
            var row = Witnesses[identifier];
            var expectedFacets = ExpectedFacets(template);
            var actualFacets = row.Facets.Select(static witness => witness.Facet).ToArray();

            Assert.Multiple(() =>
            {
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
    public void EveryFacetWitnessAcceptsTheDeclaredClaimOnEdgeInputs()
    {
        foreach (var template in ApiSpecTable.Default.Templates)
        {
            var identifier = template.Target.WitnessIdentifier;
            foreach (var witness in Witnesses[identifier].Facets)
            {
                Assert.That(
                    witness.AcceptsDeclared(template),
                    Is.True,
                    identifier + "/" + witness.Facet + " failed on " +
                    witness.EdgeInputs + ".");
            }
        }
    }

    [Test]
    public void EveryFacetWitnessRejectsADeterministicWrongClaim()
    {
        foreach (var template in ApiSpecTable.Default.Templates)
        {
            var identifier = template.Target.WitnessIdentifier;
            foreach (var witness in Witnesses[identifier].Facets)
            {
                Assert.That(
                    witness.AcceptsMutation(),
                    Is.False,
                    identifier + "/" + witness.Facet +
                    " accepted its deterministic mutation on " +
                    witness.EdgeInputs + ".");
            }
        }
    }

    [Test]
    public void EveryPostconditionWitnessAcceptsTheClaimAndRejectsItsNegation()
    {
        foreach (var template in ApiSpecTable.Default.Templates)
        {
            var identifier = template.Target.WitnessIdentifier;
            foreach (var witness in Witnesses[identifier].Postconditions)
            {
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

    private static ImmutableDictionary<string, RowWitness> CreateWitnesses()
    {
        return GeneratedRuntimeWitnesses.ToImmutableDictionary(
            static descriptor => descriptor.Identifier,
            static descriptor => descriptor.Factory(),
            StringComparer.Ordinal);
    }

    private static RowWitness CreateBclArrayEmptyWitness()
    {
        return Row(
            throws: Throws(
                "reference-type and value-type generic instantiations",
                [
                    RuntimeEdge.For(InvokeEmptyObjectArray),
                    RuntimeEdge.For(InvokeEmptyIntegerArray)
                ],
                DoesNotThrowMutation),
            nullness: Nullness(
                "reference-type and value-type generic instantiations",
                ObserveArrayEmptyNullness,
                SpecNullness.Null),
            cardinality: Cardinality(
                "reference-type and value-type generic instantiations",
                ObserveArrayEmptyCardinality,
                SpecCardinality.NonEmpty));
    }

    private static RowWitness CreateBclEnumerableEmptyWitness()
    {
        return Row(
            throws: Throws(
                "reference-type and value-type generic instantiations",
                [
                    RuntimeEdge.For(EnumerateEmptyObjects),
                    RuntimeEdge.For(EnumerateEmptyIntegers)
                ],
                DoesNotThrowMutation),
            nullness: Nullness(
                "reference-type and value-type generic instantiations",
                ObserveEnumerableEmptyNullness,
                SpecNullness.Null),
            cardinality: Cardinality(
                "reference-type and value-type generic instantiations",
                ObserveEnumerableEmptyCardinality,
                SpecCardinality.NonEmpty));
    }

    private static RowWitness CreateBclListAddWitness()
    {
        return Row(
            effects: Effect(
                "adding null and non-null values to empty receivers",
                ObserveListAddEffect,
                SpecEffect.None),
            allocation: Allocation(
                "first add of null and non-null values",
                [
                    new RuntimeEdge(
                        PrepareEmptyList,
                        AddNullToPreparedList),
                    new RuntimeEdge(
                        PrepareEmptyList,
                        AddItemToPreparedList)
                ],
                SpecAllocationBehavior.None));
    }

    private static RowWitness CreateBclMathAbsInt32Witness()
    {
        return Row(
            effects: Effect(
                "negative, zero, and maximum inputs",
                ObserveMathAbsEffect,
                SpecEffect.WritesAmbientState),
            allocation: Allocation(
                "negative, zero, and maximum inputs",
                [
                    RuntimeEdge.For(AbsNegative),
                    RuntimeEdge.For(AbsZero),
                    RuntimeEdge.For(AbsMaximum)
                ],
                SpecAllocationBehavior.MayAllocate),
            throws: Throws(
                "negative, zero, maximum, and minimum inputs",
                [
                    RuntimeEdge.For(AbsNegative),
                    RuntimeEdge.For(AbsZero),
                    RuntimeEdge.For(AbsMaximum),
                    RuntimeEdge.For(AbsMinimum)
                ],
                new ThrowClaim(
                    SpecThrowBehavior.MayThrow,
                    ["System.ArgumentException"])),
            postconditions: [
                Postcondition(
                    0,
                    "negative, zero, and maximum normal returns",
                    MathAbsNegativeCall,
                    MathAbsZeroCall,
                    MathAbsMaximumCall)
            ]);
    }

    private static RowWitness CreateBclExceptionCtorWitness()
    {
        return CreateBclConstructorWitness(
            "an already allocated Exception receiver, excluding newobj",
            ObserveExceptionConstructorEffect,
            SpecEffect.None,
            PrepareExceptionConstructorReceiver,
            InvokePreparedExceptionConstructor);
    }

    private static RowWitness CreateBclExceptionCtorStringWitness()
    {
        return CreateBclConstructorWitness(
            "an already allocated Exception receiver and null/non-null messages, excluding newobj",
            ObserveExceptionStringConstructorEffect,
            SpecEffect.None,
            PrepareExceptionConstructorReceiver,
            InvokePreparedExceptionStringConstructor,
            InvokePreparedExceptionNullStringConstructor);
    }

    private static RowWitness CreateBclInvalidOperationExceptionCtorWitness()
    {
        return CreateBclConstructorWitness(
            "an already allocated InvalidOperationException receiver, excluding newobj",
            ObserveInvalidOperationExceptionConstructorEffect,
            SpecEffect.None,
            PrepareInvalidOperationExceptionConstructorReceiver,
            InvokePreparedInvalidOperationExceptionConstructor);
    }

    private static RowWitness CreateBclInvalidOperationExceptionCtorStringWitness()
    {
        return CreateBclConstructorWitness(
            "an already allocated InvalidOperationException receiver and null/non-null messages, excluding newobj",
            ObserveInvalidOperationExceptionStringConstructorEffect,
            SpecEffect.None,
            PrepareInvalidOperationExceptionConstructorReceiver,
            InvokePreparedInvalidOperationExceptionStringConstructor,
            InvokePreparedInvalidOperationExceptionNullStringConstructor);
    }

    private static RowWitness CreateBclObjectCtorWitness()
    {
        return Row(
            effects: Effect(
                "an already allocated receiver",
                ObserveObjectConstructorEffect,
                SpecEffect.WritesAmbientState),
            allocation: Allocation(
                "an already allocated receiver, excluding newobj",
                [
                    new RuntimeEdge(
                        PrepareObjectConstructorReceiver,
                        InvokePreparedObjectConstructor)
                ],
                SpecAllocationBehavior.MayAllocate),
            throws: Throws(
                "an already allocated receiver",
                [
                    new RuntimeEdge(
                        PrepareObjectConstructorReceiver,
                        InvokePreparedObjectConstructor)
                ],
                DoesNotThrowMutation),
            termination: Termination(
                "an already allocated receiver",
                [
                    new RuntimeEdge(
                        PrepareObjectConstructorReceiver,
                        InvokePreparedObjectConstructor)
                ],
                SpecTerminationBehavior.Unknown));
    }

    private static RowWitness CreateBclStringConcatStringStringWitness()
    {
        return Row(
            effects: Effect(
                "null/null, null/value, and two non-empty strings",
                ObserveStringConcatEffect,
                SpecEffect.WritesArgumentState),
            allocation: Allocation(
                "null/null and two non-empty strings",
                [
                    RuntimeEdge.For(ConcatNulls),
                    RuntimeEdge.For(ConcatNonEmpty)
                ],
                SpecAllocationBehavior.None),
            throws: Throws(
                "null/null, null/value, and two non-empty strings",
                [
                    RuntimeEdge.For(ConcatNulls),
                    RuntimeEdge.For(ConcatNullAndValue),
                    RuntimeEdge.For(ConcatNonEmpty)
                ],
                DoesNotThrowMutation),
            nullness: Nullness(
                "null/null, null/value, and two non-empty strings",
                ObserveStringConcatNullness,
                SpecNullness.Null));
    }

    private static RowWitness CreateBclStringLengthWitness()
    {
        return Row(
            effects: Effect(
                "empty and embedded-null receivers",
                ObserveStringLengthEffect,
                SpecEffect.None),
            allocation: Allocation(
                "empty and embedded-null receivers",
                [
                    RuntimeEdge.For(ReadEmptyStringLength),
                    RuntimeEdge.For(ReadEmbeddedNullStringLength)
                ],
                SpecAllocationBehavior.MayAllocate),
            throws: Throws(
                "empty and embedded-null receivers",
                [
                    RuntimeEdge.For(ReadEmptyStringLength),
                    RuntimeEdge.For(ReadEmbeddedNullStringLength)
                ],
                DoesNotThrowMutation),
            postconditions: [
                Postcondition(
                    0,
                    "empty and embedded-null receivers",
                    EmptyStringLengthCall,
                    EmbeddedNullStringLengthCall)
            ]);
    }

    private static RowWitness CreateContractAssumeWitness()
    {
        return ContractConditionRow(
            ObserveContractAssumeEffect,
            InvokePreparedAssumeFalse,
            InvokePreparedAssumeTrue,
            InvokeAssumeFalseDirectly,
            InvokeAssumeTrueDirectly);
    }

    private static RowWitness CreateContractEnsuresWitness()
    {
        return ContractConditionRow(
            ObserveContractEnsuresEffect,
            InvokePreparedEnsuresFalse,
            InvokePreparedEnsuresTrue,
            InvokeEnsuresFalseDirectly,
            InvokeEnsuresTrueDirectly);
    }

    private static RowWitness CreateContractOldWitness()
    {
        return Row(
            effects: Effect(
                "direct null and non-null arguments",
                ObserveContractOldEffect,
                SpecEffect.WritesAmbientState),
            allocation: Allocation(
                "direct null and non-null arguments",
                [
                    new RuntimeEdge(
                        PrepareGhostProbe,
                        InvokeAndCatchOldNull),
                    new RuntimeEdge(
                        PrepareGhostProbe,
                        InvokeAndCatchOldItem)
                ],
                SpecAllocationBehavior.None),
            throws: Throws(
                "direct null and non-null arguments",
                [
                    RuntimeEdge.For(InvokeOldNullDirectly),
                    RuntimeEdge.For(InvokeOldItemDirectly)
                ],
                DoesNotThrowMutation));
    }

    private static RowWitness CreateContractRequiresWitness()
    {
        return ContractConditionRow(
            ObserveContractRequiresEffect,
            InvokePreparedRequiresFalse,
            InvokePreparedRequiresTrue,
            InvokeRequiresFalseDirectly,
            InvokeRequiresTrueDirectly);
    }

    private static RowWitness CreateContractResultWitness()
    {
        return Row(
            effects: Effect(
                "a direct reference result intrinsic call",
                ObserveContractResultEffect,
                SpecEffect.WritesAmbientState),
            allocation: Allocation(
                "a direct reference result intrinsic call",
                [
                    new RuntimeEdge(
                        PrepareGhostProbe,
                        InvokeAndCatchResult)
                ],
                SpecAllocationBehavior.None),
            throws: Throws(
                "a direct reference result intrinsic call",
                [RuntimeEdge.For(InvokeResultDirectly)],
                DoesNotThrowMutation));
    }

    private static RowWitness Row(
        IFacetWitness? effects = null,
        IFacetWitness? allocation = null,
        IFacetWitness? throws = null,
        IFacetWitness? nullness = null,
        IFacetWitness? cardinality = null,
        IFacetWitness? termination = null,
        ImmutableArray<PostconditionWitness> postconditions = default)
    {
        return new RowWitness(
            [.. new IFacetWitness?[] {
                effects, allocation, throws, nullness, cardinality, termination
            }.OfType<IFacetWitness>()],
            postconditions.IsDefault ? [] : postconditions);
    }

    private static FacetWitness<SpecEffect> Effect(
        string edgeInputs,
        Func<SpecEffect> observe,
        SpecEffect mutation)
    {
        return new(
            FacetKind.Effects,
            edgeInputs,
            static template => template.Facets.Effects.Effects,
            claim => observe() == claim,
            mutation);
    }

    private static RowWitness CreateBclConstructorWitness(
        string edgeInputs,
        Func<SpecEffect> observeEffect,
        SpecEffect effectMutation,
        Action prepare,
        params Action[] invokes)
    {
        return ConstructorRow(
            edgeInputs,
            observeEffect,
            effectMutation,
            [.. invokes.Select(invoke => new RuntimeEdge(prepare, invoke))]);
    }

    private static RowWitness ContractConditionRow(
        Func<SpecEffect> observeEffect,
        Action preparedFalse,
        Action preparedTrue,
        Action directFalse,
        Action directTrue)
    {
        const string edgeInputs = "false and true compiler-bound conditions";
        return Row(
            effects: Effect(
                edgeInputs,
                observeEffect,
                SpecEffect.WritesAmbientState),
            allocation: Allocation(
                edgeInputs,
                [
                    new RuntimeEdge(PrepareGhostProbe, preparedFalse),
                    new RuntimeEdge(PrepareGhostProbe, preparedTrue)
                ],
                SpecAllocationBehavior.MayAllocate),
            throws: Throws(
                edgeInputs,
                [RuntimeEdge.For(directFalse), RuntimeEdge.For(directTrue)],
                DoesNotThrowMutation));
    }

    private static RowWitness ConstructorRow(
        string edgeInputs,
        Func<SpecEffect> observeEffect,
        SpecEffect effectMutation,
        ImmutableArray<RuntimeEdge> edges)
    {
        return Row(
            effects: Effect(
                edgeInputs,
                observeEffect,
                effectMutation),
            allocation: Allocation(
                edgeInputs,
                edges,
                SpecAllocationBehavior.MayAllocate),
            throws: Throws(
                edgeInputs,
                edges,
                DoesNotThrowMutation),
            termination: Termination(
                edgeInputs,
                edges,
                SpecTerminationBehavior.Unknown));
    }

    private static FacetWitness<SpecAllocationBehavior> Allocation(
        string edgeInputs,
        ImmutableArray<RuntimeEdge> edges,
        SpecAllocationBehavior mutation)
    {
        return new(
            FacetKind.Allocation,
            edgeInputs,
            static template => template.Facets.Allocation.Behavior,
            claim => ObserveAllocation(edges) == claim,
            mutation);
    }

    private static FacetWitness<ThrowClaim> Throws(
        string edgeInputs,
        ImmutableArray<RuntimeEdge> edges,
        ThrowClaim mutation)
    {
        return new(
            FacetKind.Throws,
            edgeInputs,
            static template => new ThrowClaim(
                template.Facets.Throws.Behavior,
                template.Facets.Throws.ExceptionMetadataNames),
            claim => MatchesThrowClaim(ObserveThrows(edges), claim),
            mutation);
    }

    private static FacetWitness<SpecNullness> Nullness(
        string edgeInputs,
        Func<SpecNullness> observe,
        SpecNullness mutation)
    {
        return new(
            FacetKind.Nullness,
            edgeInputs,
            static template => template.Facets.Nullness.Result,
            claim => observe() == claim,
            mutation);
    }

    private static FacetWitness<SpecTerminationBehavior> Termination(
        string edgeInputs,
        ImmutableArray<RuntimeEdge> edges,
        SpecTerminationBehavior mutation)
    {
        return new(
            FacetKind.Termination,
            edgeInputs,
            static template => template.Facets.Termination!.Behavior,
            claim => ObserveTermination(edges) == claim,
            mutation);
    }

    private static FacetWitness<SpecCardinality> Cardinality(
        string edgeInputs,
        Func<SpecCardinality> observe,
        SpecCardinality mutation)
    {
        return new(
            FacetKind.Cardinality,
            edgeInputs,
            static template => template.Facets.Cardinality.Result,
            claim => observe() == claim,
            mutation);
    }

    private static PostconditionWitness Postcondition(
        int index,
        string edgeInputs,
        params Func<RuntimeCall>[] edges)
    {
        return new(index, edgeInputs, [.. edges]);
    }

    private static ImmutableArray<FacetKind> ExpectedFacets(ApiSpecTemplate template)
    {
        var facets = ImmutableArray.CreateBuilder<FacetKind>(6);
        if (template.Facets.Effects.Effects != SpecEffect.Unknown)
        {
            facets.Add(FacetKind.Effects);
        }

        if (template.Facets.Allocation.Behavior != SpecAllocationBehavior.Unknown)
        {
            facets.Add(FacetKind.Allocation);
        }

        if (template.Facets.Throws.Behavior != SpecThrowBehavior.Unknown)
        {
            facets.Add(FacetKind.Throws);
        }

        if (template.Facets.Nullness.Result is not (
                SpecNullness.Unknown or SpecNullness.NotApplicable))
        {
            facets.Add(FacetKind.Nullness);
        }

        if (template.Facets.Cardinality.Result is not (
                SpecCardinality.Unknown or SpecCardinality.NotApplicable))
        {
            facets.Add(FacetKind.Cardinality);
        }

        if (template.Facets.Termination?.Behavior is
            SpecTerminationBehavior.Terminates)
        {
            facets.Add(FacetKind.Termination);
        }

        return facets.ToImmutable();
    }

    private static SpecEffect ObserveNoEffects(params Func<bool>[] edges)
    {
        return edges.All(static edge => edge()) ? SpecEffect.None : SpecEffect.Unknown;
    }

    private static SpecEffect ObserveObjectConstructorEffect()
    {
        return ObserveNoEffects(ObjectConstructorEdge);
    }

    private static SpecEffect ObserveExceptionConstructorEffect()
    {
        return ObserveConstructorWrites(
            PrepareExceptionConstructorReceiver,
            static () => s_exceptionConstructorReceiver,
            InvokePreparedExceptionConstructor);
    }

    private static SpecEffect ObserveExceptionStringConstructorEffect()
    {
        return ObserveConstructorWrites(
            PrepareExceptionConstructorReceiver,
            static () => s_exceptionConstructorReceiver,
            InvokePreparedExceptionStringConstructor,
            InvokePreparedExceptionNullStringConstructor);
    }

    private static SpecEffect ObserveInvalidOperationExceptionConstructorEffect()
    {
        return ObserveConstructorWrites(
            PrepareInvalidOperationExceptionConstructorReceiver,
            static () => s_invalidOperationExceptionConstructorReceiver,
            InvokePreparedInvalidOperationExceptionConstructor);
    }

    private static SpecEffect ObserveInvalidOperationExceptionStringConstructorEffect()
    {
        return ObserveConstructorWrites(
            PrepareInvalidOperationExceptionConstructorReceiver,
            static () => s_invalidOperationExceptionConstructorReceiver,
            InvokePreparedInvalidOperationExceptionStringConstructor,
            InvokePreparedInvalidOperationExceptionNullStringConstructor);
    }

    private static SpecEffect ObserveConstructorWrites<TException>(
        Action prepare,
        Func<TException> receiver,
        params Action[] invokes)
        where TException : Exception
    {
        return ObserveReceiverWrites(
            [.. invokes.Select(invoke =>
                (Func<bool>)(() => ConstructorWritesReceiver(
                    prepare,
                    receiver,
                    invoke)))]);
    }

    private static SpecEffect ObserveReceiverWrites(
        params Func<bool>[] edges)
    {
        return edges.All(static edge => edge())
            ? SpecEffect.WritesReceiverState
            : SpecEffect.Unknown;
    }

    private static bool ConstructorWritesReceiver<TException>(
        Action prepare,
        Func<TException> receiver,
        Action invoke)
        where TException : Exception
    {
        prepare();
        var target = receiver();
        var fields = InstanceFields(target.GetType());
        var before = fields.Select(field => field.GetValue(target)).ToArray();
        invoke();
        for (var index = 0; index < fields.Length; index++)
        {
            if (!Equals(before[index], fields[index].GetValue(target)))
            {
                return true;
            }
        }

        return false;
    }

    private static ImmutableArray<FieldInfo> InstanceFields(Type type)
    {
        var fields = ImmutableArray.CreateBuilder<FieldInfo>();
        for (var current = type;
             current != null;
             current = current.BaseType)
        {
            fields.AddRange(current.GetFields(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly));
        }

        return fields.ToImmutable();
    }

    private static SpecEffect ObserveContractAssumeEffect()
    {
        return ObserveNoEffects(
            static () => ContractAssumeEdge(false),
            static () => ContractAssumeEdge(true));
    }

    private static SpecEffect ObserveContractEnsuresEffect()
    {
        return ObserveNoEffects(
            static () => ContractEnsuresEdge(false),
            static () => ContractEnsuresEdge(true));
    }

    private static SpecEffect ObserveContractOldEffect()
    {
        return ObserveNoEffects(
            static () => ContractOldDirectHasNoEffects(null),
            static () => ContractOldDirectHasNoEffects(ListItem));
    }

    private static SpecEffect ObserveContractRequiresEffect()
    {
        return ObserveNoEffects(
            static () => ContractRequiresEdge(false),
            static () => ContractRequiresEdge(true));
    }

    private static SpecEffect ObserveContractResultEffect()
    {
        return ObserveNoEffects(ContractResultDirectHasNoEffects);
    }

    private static SpecEffect ObserveStringConcatEffect()
    {
        return ObserveNoEffects(StringConcatEdge);
    }

    private static SpecEffect ObserveMathAbsEffect()
    {
        return ObserveNoEffects(MathAbsNormalEdges);
    }

    private static SpecEffect ObserveStringLengthEffect()
    {
        const string empty = "";
        const string embeddedNull = "A\0B";
        var emptyLength = empty.Length;
        var embeddedNullLength = embeddedNull.Length;
        return emptyLength == 0 && embeddedNullLength == 3
            ? SpecEffect.ReadsReceiverState
            : SpecEffect.Unknown;
    }

    private static SpecEffect ObserveListAddEffect()
    {
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
        ImmutableArray<RuntimeEdge> edges)
    {
        var observedAllocation = false;
        foreach (var edge in edges)
        {
            for (var iteration = 0; iteration < 128; iteration++)
            {
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

    private static ThrowObservation ObserveThrows(ImmutableArray<RuntimeEdge> edges)
    {
        var normalCompletions = 0;
        var exceptionTypes = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            edge.Prepare();
            try
            {
                edge.Invoke();
                normalCompletions++;
            }
            catch (Exception exception)
            {
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

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "The runtime oracle classifies every exceptional constructor exit as non-terminating evidence.")]
    private static SpecTerminationBehavior ObserveTermination(
        ImmutableArray<RuntimeEdge> edges)
    {
        foreach (var edge in edges)
        {
            edge.Prepare();
            try
            {
                edge.Invoke();
            }
            catch (Exception)
            {
                return SpecTerminationBehavior.Unknown;
            }
        }

        return SpecTerminationBehavior.Terminates;
    }

    private static bool MatchesThrowClaim(
        ThrowObservation observation,
        ThrowClaim claim)
    {
        return claim.Behavior switch
        {
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
    }

    private static SpecNullness ObserveNullness(params Func<object?>[] edges)
    {
        var sawNull = false;
        var sawNonNull = false;
        foreach (var edge in edges)
        {
            if (edge() == null)
            {
                sawNull = true;
            }
            else
            {
                sawNonNull = true;
            }
        }
        if (sawNull && sawNonNull)
        {
            return SpecNullness.MaybeNull;
        }

        if (sawNull)
        {
            return SpecNullness.Null;
        }

        return sawNonNull ? SpecNullness.NonNull : SpecNullness.Unknown;
    }

    private static SpecNullness ObserveArrayEmptyNullness()
    {
        return ObserveEmptySequence(static () => Array.Empty<object>(),
            static () => Array.Empty<int>(), ObserveNullness);
    }

    private static SpecNullness ObserveEnumerableEmptyNullness()
    {
        return ObserveEmptySequence(static () => Enumerable.Empty<object>(),
            static () => Enumerable.Empty<int>(), ObserveNullness);
    }

    private static SpecNullness ObserveStringConcatNullness()
    {
        return ObserveNullness(
            static () => string.Concat(null, null),
            static () => string.Concat(null, ConcatRight),
            static () => string.Concat(ConcatLeft, ConcatRight));
    }

    private static SpecCardinality ObserveCardinality(
        params Func<object?>[] edges)
    {
        var counts = edges.Select(static edge =>
                Count((IEnumerable)edge()!))
            .ToArray();
        if (counts.All(static count => count == 0))
        {
            return SpecCardinality.Empty;
        }

        if (counts.All(static count => count > 0))
        {
            return SpecCardinality.NonEmpty;
        }

        return SpecCardinality.Unknown;
    }

    private static SpecCardinality ObserveArrayEmptyCardinality()
    {
        return ObserveEmptySequence(static () => Array.Empty<object>(),
            static () => Array.Empty<int>(), ObserveCardinality);
    }

    private static SpecCardinality ObserveEnumerableEmptyCardinality()
    {
        return ObserveEmptySequence(static () => Enumerable.Empty<object>(),
            static () => Enumerable.Empty<int>(), ObserveCardinality);
    }

    private static T ObserveEmptySequence<T>(
        Func<object?> objectFactory,
        Func<object?> valueFactory,
        Func<Func<object?>[], T> observe)
    {
        return observe([objectFactory, valueFactory]);
    }

    private static int Count(IEnumerable sequence)
    {
        var count = 0;
        foreach (var unused in sequence)
        {
            _ = unused;
            count++;
        }
        return count;
    }

    private static bool ValidatePostcondition(
        ApiSpecTemplate template,
        SpecTermDeclaration condition,
        ImmutableArray<Func<RuntimeCall>> edges)
    {
        foreach (var edge in edges)
        {
            var call = edge();
            var bindings = new Dictionary<(SpecVariableRole, int), object?>();
            foreach (var variable in template.Variables)
            {
                var value = variable.Role switch
                {
                    SpecVariableRole.Receiver => call.Receiver,
                    SpecVariableRole.Parameter => call.Parameters[variable.Ordinal],
                    SpecVariableRole.Result => call.Result,
                    _ => throw new AssertionException("Unknown spec variable role.")
                };
                bindings.Add((variable.Role, variable.Ordinal), value);
            }
            if (Evaluate(condition, bindings) is not true)
            {
                return false;
            }
        }
        return true;
    }

    private static object? Evaluate(
        SpecTermDeclaration term,
        IReadOnlyDictionary<(SpecVariableRole, int), object?> bindings)
    {
        return term switch
        {
            SpecVariableDeclaration variable => bindings[(variable.Role, variable.Ordinal)],
            SpecBooleanDeclaration boolean => boolean.Value,
            SpecIntegerDeclaration integer => integer.Value,
            SpecStringDeclaration text => text.Value,
            SpecNullDeclaration => null,
            SpecUnaryDeclaration unary => EvaluateUnary(unary, bindings),
            SpecBinaryDeclaration binary => EvaluateBinary(binary, bindings),
            SpecConditionalDeclaration conditional => AsBoolean(
                Evaluate(conditional.Condition, bindings))
                ? Evaluate(conditional.WhenTrue, bindings)
                : Evaluate(conditional.WhenFalse, bindings),
            SpecLengthDeclaration length => EvaluateLength(length, bindings),
            _ => throw new AssertionException("Unknown spec term.")
        };
    }

    private static object EvaluateUnary(
        SpecUnaryDeclaration unary,
        IReadOnlyDictionary<(SpecVariableRole, int), object?> bindings)
    {
        return unary.Operator switch
        {
            IrUnaryOperator.Not => !AsBoolean(Evaluate(unary.Operand, bindings)),
            IrUnaryOperator.Negate => -AsInteger(Evaluate(unary.Operand, bindings)),
            _ => throw new AssertionException("Unknown unary spec operator.")
        };
    }

    private static object EvaluateBinary(
        SpecBinaryDeclaration binary,
        IReadOnlyDictionary<(SpecVariableRole, int), object?> bindings)
    {
        var left = Evaluate(binary.Left, bindings);
        var right = Evaluate(binary.Right, bindings);
        return binary.Operator switch
        {
            IrBinaryOperator.Add => AsInteger(left) + AsInteger(right),
            IrBinaryOperator.Subtract => AsInteger(left) - AsInteger(right),
            IrBinaryOperator.Multiply => AsInteger(left) * AsInteger(right),
            IrBinaryOperator.Divide => AsInteger(left) / AsInteger(right),
            IrBinaryOperator.Remainder => AsInteger(left) % AsInteger(right),
            IrBinaryOperator.AndAlso => AsBoolean(left) && AsBoolean(right),
            IrBinaryOperator.OrElse => AsBoolean(left) || AsBoolean(right),
            IrBinaryOperator.Equal => RuntimeEquals(left, right),
            IrBinaryOperator.NotEqual => !RuntimeEquals(left, right),
            IrBinaryOperator.LessThan => AsInteger(left) < AsInteger(right),
            IrBinaryOperator.LessThanOrEqual => AsInteger(left) <= AsInteger(right),
            IrBinaryOperator.GreaterThan => AsInteger(left) > AsInteger(right),
            IrBinaryOperator.GreaterThanOrEqual => AsInteger(left) >= AsInteger(right),
            IrBinaryOperator.StringConcat =>
                string.Concat(left as string, right as string),
            _ => throw new AssertionException("Unknown binary spec operator.")
        };
    }

    private static long EvaluateLength(
        SpecLengthDeclaration length,
        IReadOnlyDictionary<(SpecVariableRole, int), object?> bindings)
    {
        return Evaluate(length.Value, bindings) switch
        {
            string text => text.Length,
            IEnumerable sequence => Count(sequence),
            _ => throw new AssertionException(
                "A runtime length witness received a non-sequence value.")
        };
    }

    private static bool RuntimeEquals(object? left, object? right)
    {
        return IsInteger(left) && IsInteger(right)
            ? AsInteger(left) == AsInteger(right)
            : Equals(left, right);
    }

    private static bool IsInteger(object? value)
    {
        return value is sbyte or byte or short or ushort or int or uint or long or ulong;
    }

    private static long AsInteger(object? value)
    {
        return value == null
            ? throw new AssertionException("Expected an integer runtime value.")
            : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool AsBoolean(object? value)
    {
        return value is bool boolean
            ? boolean
            : throw new AssertionException("Expected a boolean runtime value.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ObjectConstructorEdge()
    {
        var receiver = new object();
        CallObjectConstructor(receiver);
        return receiver.GetType() == typeof(object);
    }

    private static Action<TReceiver> CreateParameterlessConstructorInvoker<TReceiver>()
        where TReceiver : class
    {
        var method = new DynamicMethod(
            "SharpProof_" + typeof(TReceiver).Name + "_ConstructorWitness",
            typeof(void),
            [typeof(TReceiver)],
            typeof(ApiSpecRuntimeOracleTests).Module,
            true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(
            OpCodes.Call,
            typeof(TReceiver).GetConstructor(Type.EmptyTypes) ??
            throw new AssertionException(
                typeof(TReceiver).FullName + " constructor was unavailable."));
        generator.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<TReceiver>>();
    }

    private static Action<TReceiver, string?> CreateStringConstructorInvoker<TReceiver>()
        where TReceiver : class
    {
        var method = new DynamicMethod(
            "SharpProof_" + typeof(TReceiver).Name + "_StringConstructorWitness",
            typeof(void),
            [typeof(TReceiver), typeof(string)],
            typeof(ApiSpecRuntimeOracleTests).Module,
            true);
        var generator = method.GetILGenerator();
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldarg_1);
        generator.Emit(
            OpCodes.Call,
            typeof(TReceiver).GetConstructor([typeof(string)]) ??
            throw new AssertionException(
                typeof(TReceiver).FullName + " string constructor was unavailable."));
        generator.Emit(OpCodes.Ret);
        return method.CreateDelegate<Action<TReceiver, string?>>();
    }

    private static void PrepareExceptionConstructorReceiver()
    {
        s_exceptionConstructorReceiver =
            CreateUninitializedException<Exception>();
    }

    private static TException CreateUninitializedException<TException>()
        where TException : Exception
    {
        return (TException)RuntimeHelpers.GetUninitializedObject(
            typeof(TException));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedExceptionConstructor()
    {
        CallExceptionConstructor(s_exceptionConstructorReceiver);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedExceptionStringConstructor()
    {
        CallExceptionStringConstructor(s_exceptionConstructorReceiver, "after");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedExceptionNullStringConstructor()
    {
        CallExceptionStringConstructor(s_exceptionConstructorReceiver, null);
    }

    private static void PrepareInvalidOperationExceptionConstructorReceiver()
    {
        s_invalidOperationExceptionConstructorReceiver =
            CreateUninitializedException<InvalidOperationException>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedInvalidOperationExceptionConstructor()
    {
        CallInvalidOperationExceptionConstructor(
            s_invalidOperationExceptionConstructorReceiver);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedInvalidOperationExceptionStringConstructor()
    {
        CallInvalidOperationExceptionStringConstructor(
            s_invalidOperationExceptionConstructorReceiver,
            "after");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedInvalidOperationExceptionNullStringConstructor()
    {
        CallInvalidOperationExceptionStringConstructor(
            s_invalidOperationExceptionConstructorReceiver,
            null);
    }

    private static void PrepareObjectConstructorReceiver()
    {
        s_objectConstructorReceiver = new object();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedObjectConstructor()
    {
        CallObjectConstructor(s_objectConstructorReceiver);
    }

    private static void PrepareGhostProbe()
    {
        s_ghostProbe = new GhostProbe();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedAssumeFalse()
    {
        Contract.Assume(s_ghostProbe.TouchBoolean(false));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedAssumeTrue()
    {
        Contract.Assume(s_ghostProbe.TouchBoolean(true));
    }

    private static void InvokeAssumeFalseDirectly()
    {
        _ = ContractAssumeEdge(false);
    }

    private static void InvokeAssumeTrueDirectly()
    {
        _ = ContractAssumeEdge(true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedEnsuresFalse()
    {
        Contract.Ensures(s_ghostProbe.TouchBoolean(false));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedEnsuresTrue()
    {
        Contract.Ensures(s_ghostProbe.TouchBoolean(true));
    }

    private static void InvokeEnsuresFalseDirectly()
    {
        _ = ContractEnsuresEdge(false);
    }

    private static void InvokeEnsuresTrueDirectly()
    {
        _ = ContractEnsuresEdge(true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeAndCatchOldNull()
    {
        try
        {
            _ = Contract.Old<object?>(null);
        }
        catch (InvalidOperationException)
        {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeAndCatchOldItem()
    {
        try
        {
            _ = Contract.Old(ListItem);
        }
        catch (InvalidOperationException)
        {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeOldNullDirectly()
    {
        _ = Contract.Old<object?>(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeOldItemDirectly()
    {
        _ = Contract.Old(ListItem);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedRequiresFalse()
    {
        Contract.Requires(s_ghostProbe.TouchBoolean(false));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokePreparedRequiresTrue()
    {
        Contract.Requires(s_ghostProbe.TouchBoolean(true));
    }

    private static void InvokeRequiresFalseDirectly()
    {
        _ = ContractRequiresEdge(false);
    }

    private static void InvokeRequiresTrueDirectly()
    {
        _ = ContractRequiresEdge(true);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeAndCatchResult()
    {
        try
        {
            _ = Contract.Result<object>();
        }
        catch (InvalidOperationException)
        {
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeResultDirectly()
    {
        _ = Contract.Result<object>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractAssumeEdge(bool condition)
    {
        var probe = new GhostProbe();
        Contract.Assume(probe.TouchBoolean(condition));
        return probe.Touches == 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractEnsuresEdge(bool condition)
    {
        var probe = new GhostProbe();
        Contract.Ensures(probe.TouchBoolean(condition));
        return probe.Touches == 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractOldDirectHasNoEffects(object? value)
    {
        try
        {
            _ = Contract.Old(value);
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractRequiresEdge(bool condition)
    {
        var probe = new GhostProbe();
        Contract.Requires(probe.TouchBoolean(condition));
        return probe.Touches == 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ContractResultDirectHasNoEffects()
    {
        try
        {
            _ = Contract.Result<object>();
            return false;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadEmptyStringLength()
    {
        s_integerSink = string.Empty.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadEmbeddedNullStringLength()
    {
        s_integerSink = "A\0B".Length;
    }

    private static RuntimeCall EmptyStringLengthCall()
    {
        return RuntimeCall.ForReceiver(string.Empty, string.Empty.Length);
    }

    private static RuntimeCall EmbeddedNullStringLengthCall()
    {
        return RuntimeCall.ForReceiver("A\0B", "A\0B".Length);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool StringConcatEdge()
    {
        return string.Concat(null, null).Length == 0 &&
        string.Concat(null, ConcatRight) == ConcatRight &&
        string.Concat(ConcatLeft, ConcatRight) == "leftright";
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConcatNulls()
    {
        s_stringSink = string.Concat(null, null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConcatNullAndValue()
    {
        s_stringSink = string.Concat(null, ConcatRight);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ConcatNonEmpty()
    {
        s_stringSink = string.Concat(ConcatLeft, ConcatRight);
    }

    private static void PrepareEmptyList()
    {
        s_list = [];
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddNullToPreparedList()
    {
        s_list.Add(null);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AddItemToPreparedList()
    {
        s_list.Add(ListItem);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool MathAbsNormalEdges()
    {
        return Math.Abs(-17) == 17 &&
        Math.Abs(0) == 0 &&
        Math.Abs(int.MaxValue) == int.MaxValue;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsNegative()
    {
        s_integerSink = Math.Abs(-17);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsZero()
    {
        s_integerSink = Math.Abs(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsMaximum()
    {
        s_integerSink = Math.Abs(int.MaxValue);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsMinimum()
    {
        s_integerSink = Math.Abs(int.MinValue);
    }

    private static RuntimeCall MathAbsNegativeCall()
    {
        return RuntimeCall.ForParameterAndResult(-17, Math.Abs(-17));
    }

    private static RuntimeCall MathAbsZeroCall()
    {
        return RuntimeCall.ForParameterAndResult(0, Math.Abs(0));
    }

    private static RuntimeCall MathAbsMaximumCall()
    {
        return RuntimeCall.ForParameterAndResult(
            int.MaxValue,
            Math.Abs(int.MaxValue));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeEmptyObjectArray()
    {
        _ = Array.Empty<object>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void InvokeEmptyIntegerArray()
    {
        _ = Array.Empty<int>();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnumerateEmptyObjects()
    {
        s_integerSink = Enumerable.Empty<object>().Count();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void EnumerateEmptyIntegers()
    {
        s_integerSink = Enumerable.Empty<int>().Count();
    }

    private enum FacetKind
    {
        Effects,
        Allocation,
        Throws,
        Nullness,
        Cardinality,
        Termination
    }

    private interface IFacetWitness
    {
        FacetKind Facet
        {
            get;
        }
        string EdgeInputs
        {
            get;
        }
        bool AcceptsDeclared(ApiSpecTemplate template);
        bool AcceptsMutation();
    }

    private sealed class FacetWitness<T>(
        FacetKind facet,
        string edgeInputs,
        Func<ApiSpecTemplate, T> selectClaim,
        Func<T, bool> validate,
        T mutation) : IFacetWitness
    {
        public FacetKind Facet { get; } = facet;
        public string EdgeInputs { get; } = edgeInputs;

        public bool AcceptsDeclared(ApiSpecTemplate template)
        {
            return validate(selectClaim(template));
        }

        public bool AcceptsMutation()
        {
            return validate(mutation);
        }
    }

    private sealed record RowWitness(
        ImmutableArray<IFacetWitness> Facets,
        ImmutableArray<PostconditionWitness> Postconditions);

    private sealed record RuntimeWitnessDescriptor(
        string Identifier,
        Func<RowWitness> Factory);

    private sealed record PostconditionWitness(
        int Index,
        string EdgeInputs,
        ImmutableArray<Func<RuntimeCall>> Edges)
    {
        public bool AcceptsDeclared(ApiSpecTemplate template)
        {
            return ValidatePostcondition(
                template,
                template.Postconditions[Index].Condition,
                Edges);
        }

        public bool AcceptsNegatedMutation(ApiSpecTemplate template)
        {
            var declared = template.Postconditions[Index].Condition;
            var mutation = new SpecUnaryDeclaration(
                IrUnaryOperator.Not,
                declared,
                IrTypeKind.Boolean);
            return ValidatePostcondition(template, mutation, Edges);
        }
    }

    private sealed record RuntimeEdge(Action Prepare, Action Invoke)
    {
        public static RuntimeEdge For(Action invoke)
        {
            return new(
            static () => { },
            invoke);
        }

        public static RuntimeEdge For(Func<bool> invoke)
        {
            return new(
            static () => { },
            () => _ = invoke());
        }
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
        object? Result)
    {
        public static RuntimeCall ForReceiver(object receiver, object? result)
        {
            return new(receiver, [], result);
        }

        public static RuntimeCall ForParameterAndResult(
            object? parameter,
            object? result)
        {
            return new(null, [parameter], result);
        }
    }

    private sealed class GhostProbe
    {
        public int Touches
        {
            get; private set;
        }

        public bool TouchBoolean(bool value)
        {
            Touches++;
            return value;
        }

    }
}
