using System.Diagnostics;

namespace SharpProof.Dataflow;

/// <summary>
/// Provides shared operations for SharpProof's closed abstract domains.
/// </summary>
public abstract class ClosedAbstractDomain<T> : IAbstractDomain<T> {
    public abstract T Bottom { get; }
    public abstract T Top { get; }
    public abstract bool LessThanOrEqual(T left, T right);
    public abstract T Join(T left, T right);
    public abstract T Widen(T previous, T candidate);
    public abstract T Havoc(T value);

    public virtual bool AreEquivalent(T left, T right) =>
        LessThanOrEqual(left, right) && LessThanOrEqual(right, left);

    public T Merge(T value1, T value2) => Join(value1, value2);

    public int Compare(T oldValue, T newValue, bool assertMonotonicity = false) {
        if (AreEquivalent(oldValue, newValue)) return 0;
        if (LessThanOrEqual(oldValue, newValue)) return -1;
        Debug.Assert(!assertMonotonicity);
        return 1;
    }
}
