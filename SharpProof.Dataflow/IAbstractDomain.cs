namespace SharpProof.Dataflow;

/// <summary>
/// Defines a closed abstract domain used by the forward fixpoint engine.
/// </summary>
/// <typeparam name="T">The immutable abstract value type.</typeparam>
public interface IAbstractDomain<T>
{
    T Bottom
    {
        get;
    }
    T Top
    {
        get;
    }
    bool LessThanOrEqual(T left, T right);
    bool AreEquivalent(T left, T right);
    T Join(T left, T right);
    T Widen(T previous, T candidate);
    T Havoc(T value);
}
