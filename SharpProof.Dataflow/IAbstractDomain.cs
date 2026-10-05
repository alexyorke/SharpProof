namespace SharpProof.Dataflow;

/// <summary>
/// Defines a closed abstract domain used by the forward fixpoint engine.
/// </summary>
/// <remarks>
/// <see cref="AreEquivalent"/> must agree with mutual ordering, and
/// <see cref="Join"/> must be a least upper bound modulo that equivalence.
/// In particular, if the first operand is below the second, joining them must
/// be equivalent to the second operand. Plain implementations retain the
/// solver's normalizing join path; implementations that also satisfy the
/// canonical-transfer contract may derive from
/// <see cref="CanonicalAbstractDomain{T}"/> to enable the direct path.
/// </remarks>
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
