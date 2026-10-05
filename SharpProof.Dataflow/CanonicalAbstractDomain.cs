namespace SharpProof.Dataflow;

/// <summary>
/// Identifies an abstract domain that can validate canonical transfer
/// representatives.
/// </summary>
/// <remarks>
/// A canonical domain guarantees that, when
/// <see cref="IAbstractDomain{T}.LessThanOrEqual"/> reports that the first
/// operand is below the second, joining those operands is equivalent to the
/// second operand. For a transfer result accepted by <see cref="IsCanonical"/>,
/// the forward solver can therefore retain a strictly growing result without
/// performing a normalizing join. Implementations must preserve these
/// guarantees for every accepted transfer result.
/// </remarks>
public abstract class CanonicalAbstractDomain<T> : ClosedAbstractDomain<T>
{
    /// <summary>
    /// Determines whether a transfer result is a canonical representative.
    /// </summary>
    /// <param name="value">The value returned by a transfer function.</param>
    /// <returns>
    /// <see langword="true"/> when the value may be stored without a
    /// normalizing join; otherwise, <see langword="false"/>.
    /// </returns>
    protected abstract bool IsCanonical(T value);

    internal bool IsCanonicalTransfer(T value)
    {
        return IsCanonical(value);
    }
}
