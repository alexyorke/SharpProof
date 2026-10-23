namespace SharpProof.Attributes;

/// <summary>Identifies a caller-supplied runtime quantity with fixed units and scope.</summary>
/// <remarks>
/// Fault episodes are outermost entries, including processing existing exceptions, rethrow, dispatch
/// capture/restore, recording and notifications. All nested construction, trace and referent work belongs to
/// that episode; re-entry after exit starts another episode. Allocation attempts include failed,
/// abandoned, losing and retried candidates. No quantity has an inferred or default value.
/// </remarks>
public enum ResourceTaskRuntimeQuantity
{
    /// <summary>No quantity; this value cannot declare a bound.</summary>
    Unspecified = 0,
    /// <summary>Raw trace array allocation attempts per fault episode, including affinity, restore and dispatch copies.</summary>
    RawTraceArrayAttemptsPerFaultEpisode = 1,
    /// <summary>Payload bytes per raw trace array allocation attempt, before modeled header and alignment.</summary>
    RawTracePayloadBytesPerArrayAttempt = 2,
    /// <summary>Resolver array allocation attempts per fault episode, including affinity, restore and dispatch copies.</summary>
    ResolverArrayAttemptsPerFaultEpisode = 3,
    /// <summary>Reference slots per resolver array allocation attempt.</summary>
    ResolverSlotsPerArrayAttempt = 4,
    /// <summary>Cumulative managed instance bytes for transitive fault construction, messages and replacement retries per episode.</summary>
    FaultConstructionBytesPerFaultEpisode = 5,
    /// <summary>Cumulative managed instance bytes for fresh loader/resolver referents and losing candidates per fault episode.</summary>
    ResolverReferentBytesPerFaultEpisode = 6,
    /// <summary>Cumulative managed instance bytes for remaining transitive scheduler, queue and worker lifecycle per child start attempt, including builder, recording, notification and later internal finalization.</summary>
    SchedulingBytesPerChildStartAttempt = 7,
    /// <summary>Cumulative managed instance bytes for remaining transitive runtime join lifecycle per attempt, including builder, recording, notification and later internal finalization.</summary>
    JoinBytesPerJoinAttempt = 8,
    /// <summary>Cumulative managed instance bytes for remaining transitive runtime await lifecycle, including builder, recording, notification and later internal finalization.</summary>
    AwaitBytesPerConfiguredAwait = 9,
    /// <summary>Cumulative managed instance bytes for all causally reached transitive cold initialization and retries per root invocation.</summary>
    InitializationBytesPerInvocation = 10,
    /// <summary>Total outermost source, helper, runtime, scheduler, initialization and existing-exception processing episodes per root invocation.</summary>
    TotalFaultEpisodesPerInvocation = 11
}

/// <summary>Declares one immutable quantitative runtime premise for the annotated root method.</summary>
/// <remarks>
/// All eleven quantities and one deployment declaration are required. Fixed compiler/source allocation
/// sites are derived independently and excluded from runtime domains. Classification of remaining allocation
/// gives initialization precedence, then trace arrays, resolver referents, fault construction,
/// and remaining control work. Source callback allocation is derived independently.
/// These are reported assumptions, not measurements or compiler-established runtime facts.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ResourceTaskRuntimeUpperBoundAttribute : Attribute
{
    /// <summary>Creates an explicit upper bound in the quantity's fixed units and scope.</summary>
    /// <param name="quantity">The runtime quantity to bound.</param>
    /// <param name="upperBound">The caller-provided nonnegative upper bound.</param>
    /// <param name="evidence">A nonempty printable reference or explanation supporting the declaration.</param>
    /// <exception cref="ArgumentOutOfRangeException">The quantity is unsupported or the value is negative.</exception>
    /// <exception cref="ArgumentException">The evidence is malformed.</exception>
    public ResourceTaskRuntimeUpperBoundAttribute(ResourceTaskRuntimeQuantity quantity, long upperBound, string evidence)
    {
        if (quantity == ResourceTaskRuntimeQuantity.Unspecified || !Enum.IsDefined(typeof(ResourceTaskRuntimeQuantity), quantity))
        { throw new ArgumentOutOfRangeException(nameof(quantity)); }
        if (upperBound < 0)
        { throw new ArgumentOutOfRangeException(nameof(upperBound)); }
        if (string.IsNullOrWhiteSpace(evidence) || evidence.Length > 1024 || evidence.Any(static character => char.IsControl(character)))
        { throw new ArgumentException("Printable runtime quantity evidence is required.", nameof(evidence)); }
        Quantity = quantity;
        UpperBound = upperBound;
        Evidence = evidence;
    }

    /// <summary>Gets the declared runtime quantity.</summary>
    /// <value>The quantity with fixed units and scope.</value>
    public ResourceTaskRuntimeQuantity Quantity { get; }
    /// <summary>Gets the caller-provided upper bound.</summary>
    /// <value>The nonnegative upper bound.</value>
    public long UpperBound { get; }
    /// <summary>Gets the caller's supporting evidence reference.</summary>
    /// <value>The printable runtime quantity evidence.</value>
    public string Evidence { get; }
}
