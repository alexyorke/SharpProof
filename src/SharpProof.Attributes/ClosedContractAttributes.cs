namespace SharpProof.Attributes;

/// <summary>Declares that a parameter or return value is non-null.</summary>
[AttributeUsage(
    SharpProofAttributeTargets.ParameterOrReturn,
    Inherited = false)]
public sealed class NotNullAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="NotNullAttribute"/> class.</summary>
    public NotNullAttribute()
    {
    }
}

/// <summary>Declares that a parameter or return value is greater than zero.</summary>
[AttributeUsage(
    SharpProofAttributeTargets.ParameterOrReturn,
    Inherited = false)]
public sealed class PositiveAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="PositiveAttribute"/> class.</summary>
    public PositiveAttribute()
    {
    }
}

/// <summary>Declares an inclusive integral range for a parameter or return value.</summary>
[AttributeUsage(
    SharpProofAttributeTargets.ParameterOrReturn,
    Inherited = false)]
public sealed class InRangeAttribute : Attribute
{
    /// <summary>Creates an inclusive range contract.</summary>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    public InRangeAttribute(long minimum, long maximum)
    {
        if (minimum > maximum)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "Minimum cannot exceed maximum.");
        }

        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>Gets the inclusive minimum.</summary>
    /// <value>The inclusive minimum.</value>
    public long Minimum
    {
        get;
    }
    /// <summary>Gets the inclusive maximum.</summary>
    /// <value>The inclusive maximum.</value>
    public long Maximum
    {
        get;
    }
}
