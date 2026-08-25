namespace SharpProof.Dataflow;

/// <summary>
/// Provides shared operations for SharpProof's closed abstract domains.
/// </summary>
public abstract class ClosedAbstractDomain<T> : IAbstractDomain<T>
{
    public abstract T Bottom
    {
        get;
    }
    public abstract T Top
    {
        get;
    }
    public abstract bool LessThanOrEqual(T left, T right);
    public abstract T Join(T left, T right);
    public abstract T Widen(T previous, T candidate);
    public abstract T Havoc(T value);

    public virtual bool AreEquivalent(T left, T right)
    {
        return LessThanOrEqual(left, right) && LessThanOrEqual(right, left);
    }

    public T Merge(T value1, T value2)
    {
        return Join(value1, value2);
    }

    public int Compare(T oldValue, T newValue, bool assertMonotonicity = false)
    {
        if (AreEquivalent(oldValue, newValue))
        {
            return 0;
        }

        if (LessThanOrEqual(oldValue, newValue))
        {
            return -1;
        }

        if (assertMonotonicity)
        {
            throw new InvalidOperationException(
                "Abstract-domain comparison is not monotone: the new value is not above the old value.");
        }

        if (LessThanOrEqual(newValue, oldValue))
        {
            return 1;
        }

        throw new InvalidOperationException(
            "Abstract-domain values are incomparable; a three-way comparison is undefined.");
    }
}
