namespace SharpProof.Attributes;

/// <summary>Requires a member to prove that no modeled synchronous exception can escape.</summary>
/// <remarks>
/// This is a static obligation over admitted behavior. On an async method returning a task, it states that the returned task
/// never faults or is canceled. It is not a catch handler and does not prove termination, completion of a task the member did not
/// create, or the absence of every runtime/environmental failure. Review Unknown and coverage records.
/// </remarks>
[AttributeUsage(SharpProofAttributeTargets.Contract, Inherited = false)]
public sealed class DoesNotThrowAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="DoesNotThrowAttribute"/> class.</summary>
    public DoesNotThrowAttribute()
    {
    }
}
