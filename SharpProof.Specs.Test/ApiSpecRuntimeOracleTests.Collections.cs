using System.Collections.Immutable;
using SharpProof.Specs;

namespace SharpProof.Specs.Test;

public sealed partial class ApiSpecRuntimeOracleTests
{
    private static object? s_collectionSink;
    private sealed record CountReceiver(object Receiver, Func<int> Read);

    private static RowWitness CreateCollectionCountWitness(string kind)
    {
        const string inputs = "empty, singleton, grown to 65 then reduced; dictionary comparer callbacks forbidden during reads";
        var current = CreateCountReceiver(kind, 0);
        var edges = ImmutableArray.Create(0, 1, 65).Select(size => new RuntimeEdge(
            () => current = CreateCountReceiver(kind, size), () => s_integerSink = current.Read())).ToImmutableArray();
        var throws = new Lazy<ThrowObservation>(() => ObserveThrows(edges));
        return Row(
            effects: Effect(inputs, () => ObserveCountReadEffects(kind), SpecEffect.None),
            allocation: Allocation(inputs, edges, SpecAllocationBehavior.MayAllocate),
            throws: Throws(inputs, throws, DoesNotThrowMutation),
            termination: Termination(inputs, throws, SpecTerminationBehavior.Unknown));
    }

    private static SpecEffect ObserveCountReadEffects(string kind)
    {
        var empty = CreateCountReceiver(kind, 0);
        var populated = CreateCountReceiver(kind, 65);
        var fields = InstanceFields(populated.Receiver.GetType());
        var before = fields.Select(field => field.GetValue(populated.Receiver)).ToArray();
        var emptyCount = empty.Read();
        var populatedCount = populated.Read();
        return emptyCount == 0 && populatedCount > 0 && populated.Read() == populatedCount &&
            fields.Select((field, index) => Equals(before[index], field.GetValue(populated.Receiver))).All(equal => equal)
            ? SpecEffect.ReadsReceiverState : SpecEffect.Unknown;
    }

    private static CountReceiver CreateCountReceiver(string kind, int size)
    {
        var values = Enumerable.Range(0, size).Select(value => (object)value).ToArray();
        switch (kind)
        {
            case "List":
                var list = new List<object>(values);
                if (size > 1)
                { list.RemoveRange(0, size / 2); }
                return new(list, () => list.Count);
            case "Queue":
                var queue = new Queue<object>(values);
                for (var index = 0; index < size / 2; index++)
                { queue.Dequeue(); }
                return new(queue, () => queue.Count);
            case "Stack":
                var stack = new Stack<object>(values);
                for (var index = 0; index < size / 2; index++)
                { stack.Pop(); }
                return new(stack, () => stack.Count);
            case "Dictionary":
                var comparer = new GuardedCountComparer();
                var dictionary = new Dictionary<object, object>(comparer);
                foreach (var value in values)
                { dictionary.Add(value, value); }
                foreach (var value in values.Take(size / 2))
                { dictionary.Remove(value); }
                comparer.Armed = true;
                return new(dictionary, () =>
                {
                    var count = dictionary.Count;
                    if (comparer.Callbacks != 0)
                    { throw new InvalidOperationException("Count invoked a user comparer callback."); }
                    return count;
                });
            default:
                throw new InvalidOperationException("Unknown concrete count fixture.");
        }
    }

    private sealed class GuardedCountComparer : IEqualityComparer<object>
    {
        internal bool Armed { get; set; }
        internal int Callbacks { get; private set; }
        public new bool Equals(object? left, object? right)
        {
            if (Armed)
            { Callbacks++; }
            return ReferenceEquals(left, right);
        }
        public int GetHashCode(object value)
        {
            if (Armed)
            { Callbacks++; }
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
        }
    }

    private static RowWitness CreateCollectionItemWitness()
    {
        const string inputs = "null/non-null elements; empty list; -1, int.MinValue, Count, int.MaxValue indexes";
        var list = new List<object?> { null, ListItem };
        var valid = ImmutableArray.Create(RuntimeEdge.For(() => s_collectionSink = list[0]),
            RuntimeEdge.For(() => s_collectionSink = list[1]));
        var invalid = ImmutableArray.Create(-1, int.MinValue, list.Count, int.MaxValue).Select(index =>
            RuntimeEdge.For(() => s_collectionSink = list[index])).Append(
                RuntimeEdge.For(() => s_collectionSink = new List<object?>()[0])).ToImmutableArray();
        var allocation = RuntimeEdge.For(() =>
        {
            try
            { s_collectionSink = list[-1]; }
            catch (ArgumentOutOfRangeException exception)
            { s_collectionSink = exception; }
            GC.KeepAlive(s_collectionSink);
        });
        return Row(
            allocation: Allocation(inputs, [.. valid, allocation], SpecAllocationBehavior.None),
            throws: Throws(inputs, [.. valid, .. invalid], DoesNotThrowMutation));
    }
}
