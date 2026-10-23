namespace SharpProof.Attributes;

/// <summary>Declares a conditional probability model for a Boolean entry parameter.</summary>
/// <remarks>
/// The weights describe each invocation conditional on the other entry inputs.
/// They are assumptions, not probabilities inferred from source or observations.
/// At most one modeled Boolean parameter is currently admitted per callable.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ResourceBooleanInputAttribute : Attribute
{
    /// <summary>Creates an explicitly weighted Boolean input model.</summary>
    /// <param name="trueWeight">Weight of true, from zero to one million.</param>
    /// <param name="falseWeight">Weight of false, from zero to one million.</param>
    /// <exception cref="ArgumentOutOfRangeException">A weight is outside its domain or both are zero.</exception>
    public ResourceBooleanInputAttribute(int trueWeight, int falseWeight)
    {
        if (trueWeight is < 0 or > 1000000)
        { throw new ArgumentOutOfRangeException(nameof(trueWeight)); }
        if (falseWeight is < 0 or > 1000000 || trueWeight + falseWeight == 0)
        { throw new ArgumentOutOfRangeException(nameof(falseWeight)); }
        TrueWeight = trueWeight;
        FalseWeight = falseWeight;
    }

    /// <summary>Gets the weight assigned to true.</summary>
    /// <value>The nonnegative true weight.</value>
    public int TrueWeight { get; }

    /// <summary>Gets the weight assigned to false.</summary>
    /// <value>The nonnegative false weight.</value>
    public int FalseWeight { get; }
}
