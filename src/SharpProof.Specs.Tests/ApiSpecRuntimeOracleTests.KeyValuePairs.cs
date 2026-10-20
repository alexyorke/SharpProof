using System.Collections.Immutable;
using System.Reflection;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

public sealed partial class ApiSpecRuntimeOracleTests
{
    private static DateTime s_pairDateSink;
    private static object? s_pairReferenceSink;

    private static RowWitness CreateImmutablePairGetterWitness(bool readValue)
    {
        const string inputs = "default/null pairs; distinct signed limits, reference identities and struct values; getter IL is a readonly instance-field load; no equality/hash/string callbacks";
        var integers = new KeyValuePair<int, int>(int.MinValue, int.MaxValue);
        var references = new KeyValuePair<object?, object?>(new PairCallbackTrap(), null);
        var structures = new KeyValuePair<DateTime, DateTime>(DateTime.MinValue, DateTime.MaxValue);
        ImmutableArray<RuntimeEdge> edges = [
            RuntimeEdge.For(() => s_integerSink = readValue ? integers.Value : integers.Key),
            RuntimeEdge.For(() => s_pairReferenceSink = readValue ? references.Value : references.Key),
            RuntimeEdge.For(() => s_pairDateSink = readValue ? structures.Value : structures.Key),
            RuntimeEdge.For(() => s_pairReferenceSink = readValue ? default(KeyValuePair<object?, object?>).Value : default(KeyValuePair<object?, object?>).Key)
        ];
        var throws = new Lazy<ThrowObservation>(() => ObserveThrows(edges));
        return Row(
            effects: Effect(inputs, () => ObserveImmutablePairGetter(readValue), SpecEffect.WritesAmbientState),
            allocation: Allocation(inputs, edges, SpecAllocationBehavior.MayAllocate),
            throws: Throws(inputs, throws, DoesNotThrowMutation),
            termination: Termination(inputs, throws, SpecTerminationBehavior.Unknown));
    }

    private static SpecEffect ObserveImmutablePairGetter(bool readValue)
    {
        GC.KeepAlive(s_pairReferenceSink);
        s_integerSink = (int)(s_pairDateSink.Ticks & int.MaxValue);
        var pairType = typeof(KeyValuePair<,>);
        var getter = pairType.GetProperty(readValue ? "Value" : "Key")!.GetMethod!;
        var body = getter.GetMethodBody();
        var il = body?.GetILAsByteArray();
        if (!pairType.IsValueType || pairType.TypeInitializer != null || getter.IsStatic || getter.IsVirtual ||
            getter.GetParameters().Length != 0 || body == null || body.ExceptionHandlingClauses.Count != 0 ||
            il is not { Length: 7 } || il[0] != 0x02 || il[1] != 0x7b || il[6] != 0x2a)
        { return SpecEffect.Unknown; }
        var field = getter.Module.ResolveField(BitConverter.ToInt32(il, 2), pairType.GetGenericArguments(), Type.EmptyTypes);
        if (field is not { IsStatic: false, IsInitOnly: true } || field.FieldType != getter.ReturnType ||
            !field.FieldType.IsGenericParameter || field.FieldType.GenericParameterPosition != (readValue ? 1 : 0))
        { return SpecEffect.Unknown; }
        var first = new PairCallbackTrap();
        var second = new PairCallbackTrap();
        var references = new KeyValuePair<PairCallbackTrap, PairCallbackTrap>(first, second);
        var result = readValue ? references.Value : references.Key;
        var integers = new KeyValuePair<int, int>(int.MinValue, int.MaxValue);
        var integer = readValue ? integers.Value : integers.Key;
        return first.Calls == 0 && second.Calls == 0 && ReferenceEquals(result, readValue ? second : first) && integer == (readValue ? int.MaxValue : int.MinValue) &&
            default(KeyValuePair<int, int>).Key == 0 && default(KeyValuePair<int, int>).Value == 0 &&
            default(KeyValuePair<object?, object?>).Key == null && default(KeyValuePair<object?, object?>).Value == null
            ? SpecEffect.None : SpecEffect.Unknown;
    }

    private sealed class PairCallbackTrap
    {
        internal int Calls { get; private set; }

        public override bool Equals(object? obj)
        {
            Calls++;
            return ReferenceEquals(this, obj);
        }
        public override int GetHashCode()
        {
            Calls++;
            return 17;
        }
        public override string ToString()
        {
            Calls++;
            return nameof(PairCallbackTrap);
        }
    }
}
