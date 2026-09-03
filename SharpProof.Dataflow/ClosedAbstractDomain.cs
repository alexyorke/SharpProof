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

    public bool AreEquivalent(T left, T right)
    {
        return LessThanOrEqual(left, right) && LessThanOrEqual(right, left);
    }

}
