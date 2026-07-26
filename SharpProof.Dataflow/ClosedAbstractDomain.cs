using System.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis.DataFlow;

namespace SharpProof.Dataflow;

/// <summary>
/// Bridges SharpProof's closed domain contract to the roslyn-analyzers domain contract.
/// </summary>
public abstract class ClosedAbstractDomain<T> : AbstractDomain<T>, IAbstractDomain<T> {
    public abstract override T Bottom { get; }
    public abstract T Top { get; }
    public abstract bool LessThanOrEqual(T left, T right);
    public abstract T Join(T left, T right);
    public abstract T Widen(T previous, T candidate);
    public abstract T Havoc(T value);

    public virtual bool AreEquivalent(T left, T right) =>
        LessThanOrEqual(left, right) && LessThanOrEqual(right, left);

    public sealed override T Merge(T value1, T value2) => Join(value1, value2);

    public sealed override int Compare(T oldValue, T newValue, bool assertMonotonicity) {
        if (AreEquivalent(oldValue, newValue)) return 0;
        if (LessThanOrEqual(oldValue, newValue)) return -1;
        Debug.Assert(!assertMonotonicity);
        return 1;
    }
}
