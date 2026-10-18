namespace SharpProof.Attributes;

/// <summary>Declares that a parameter or return value is non-null.</summary>
/// <remarks>
/// Applies to reference-capable parameters and return values. Parameter use is an incoming-value precondition;
/// <c>[return: NotNull]</c> describes normal return. An <c>out</c> parameter has no incoming value and is rejected.
/// This is a static contract and inserts no runtime null check.
/// </remarks>
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
/// <remarks>
/// Use on supported integral parameters or return values, for example <c>int Count([Positive] int value)</c>.
/// Floating-point, decimal, enum, native-sized and nullable integral types are not admitted closed-contract targets.
/// Incoming <c>out</c> values are unsupported. This selects a static condition, not a runtime validation call.
/// </remarks>
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
/// <remarks>
/// Bounds are signed <see cref="long"/> constants with minimum no larger than maximum, for example <c>[InRange(0, 10)]</c>.
/// The target must be an admitted integral parameter or return value; <c>out</c>, nullable integral, floating-point,
/// decimal, enum and native-sized targets are rejected. Parameter ranges describe entry; return ranges describe normal return.
/// The attribute does not insert runtime range checks or guarantee that the method terminates.
/// </remarks>
[AttributeUsage(
    SharpProofAttributeTargets.ParameterOrReturn,
    Inherited = false)]
public sealed class InRangeAttribute : Attribute
{
    /// <summary>Creates an inclusive range contract.</summary>
    /// <param name="minimum">The inclusive minimum.</param>
    /// <param name="maximum">The inclusive maximum.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minimum"/> is greater than <paramref name="maximum"/>.</exception>
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
