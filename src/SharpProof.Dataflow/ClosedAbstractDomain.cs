namespace SharpProof.Dataflow;

/// <summary>
/// Provides shared operations for SharpProof's closed abstract domains.
/// </summary>
internal abstract class ClosedAbstractDomain<T> : IAbstractDomain<T>
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
    public virtual T Widen(T previous /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */, T candidate /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */)
    {
        return Join(previous, candidate);
    }
    public abstract T Havoc(T value);

    public bool AreEquivalent(T left /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */, T right /* SPINT001: all values valid - generic values are forwarded or serialized, including null when the type permits it */)
    {
        return LessThanOrEqual(left, right) && LessThanOrEqual(right, left);
    }

}
