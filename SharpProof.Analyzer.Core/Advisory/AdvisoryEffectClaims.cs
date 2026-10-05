namespace SharpProof.Analyzer;

// Which effect sites could violate a claim, with the same site goals the
// worker gives Z3. A site reported here is a may-violation; the build decides.
internal static class AdvisoryEffectClaims
{
    private const EffectContractKind AnyStateWrites = EffectContractKind.WritesReceiverState | EffectContractKind.WritesArgumentState |
        EffectContractKind.WritesCapturedState | EffectContractKind.WritesStaticState | EffectContractKind.WritesAmbientState;
    private const EffectContractKind AnyStateReads = EffectContractKind.ReadsReceiverState | EffectContractKind.ReadsArgumentState |
        EffectContractKind.ReadsCapturedState | EffectContractKind.ReadsStaticState | EffectContractKind.ReadsAmbientState;
    private const EffectContractKind AllEffects = AnyStateReads | AnyStateWrites | EffectContractKind.Allocates |
        EffectContractKind.Throws | EffectContractKind.Synchronizes | EffectContractKind.UsesNondeterminism |
        EffectContractKind.UsesNativeCode | EffectContractKind.UsesReflection;

    internal static (AdvisoryEffectSite Site, string Detail)? FindViolation(EffectEvaluationContractKind kind,
        EffectClaimConstraint constraint, ImmutableArray<AdvisoryEffectSite> sites, Compilation compilation)
    {
        foreach (var site in sites)
        {
            if (Violation(kind, constraint, site, compilation) is { } detail)
            { return (site, detail); }
        }
        return null;
    }

    private static string? Violation(EffectEvaluationContractKind kind, EffectClaimConstraint constraint,
        AdvisoryEffectSite site, Compilation compilation)
    {
        switch (kind)
        {
            case EffectEvaluationContractKind.ZeroAllocations:
                return site.Kind is AdvisoryEffectSiteKind.Allocation or AdvisoryEffectSiteKind.Lock or AdvisoryEffectSiteKind.Throw ||
                    site.Kind == AdvisoryEffectSiteKind.Call && (site.Effects & IrOpaqueCallEffects.Allocates) != 0
                    ? "may allocate" : null;
            case EffectEvaluationContractKind.EnforcePure:
                return site.Kind == AdvisoryEffectSiteKind.Write && site.Region != IrWriteRegion.Local ||
                    site.Kind == AdvisoryEffectSiteKind.Lock || site.Kind == AdvisoryEffectSiteKind.Read && site.Static ||
                    site.Kind == AdvisoryEffectSiteKind.Call && IrOpaqueCallSite.IsObservablyImpure(site.Effects)
                    ? "may not be observably pure" : null;
            case EffectEvaluationContractKind.AllowedCapabilities:
                var used = Capabilities(site) & ~constraint.Capabilities;
                return used != EffectContractCapabilityKind.None ? "may use capabilities: " + used : null;
            case EffectEvaluationContractKind.DoesNotThrow:
                return site.Escapes ? "may throw " + ThrownName(site) : null;
            case EffectEvaluationContractKind.AllowedExceptions:
                return site.Escapes && !Allowed(site, constraint.ExceptionTypes, compilation) ? "may throw " + ThrownName(site) : null;
            case EffectEvaluationContractKind.EffectContract:
                var effects = Effects(site) & ~constraint.Effects;
                if (site.Escapes && (constraint.Effects & EffectContractKind.Throws) != 0 && Allowed(site, constraint.ExceptionTypes, compilation))
                { effects &= ~EffectContractKind.Throws; }
                var capabilities = Capabilities(site) & ~constraint.Capabilities;
                return effects != EffectContractKind.None || capabilities != EffectContractCapabilityKind.None
                    ? "may have undeclared effects: " + string.Join(", ", new[] { effects.ToString(), capabilities.ToString() }
                        .Where(name => name != "None"))
                    : null;
            default:
                return null;
        }
    }

    private static EffectContractKind Effects(AdvisoryEffectSite site)
    {
        return site.Kind switch
        {
            AdvisoryEffectSiteKind.Allocation => EffectContractKind.Allocates,
            AdvisoryEffectSiteKind.Lock => EffectContractKind.Synchronizes,
            AdvisoryEffectSiteKind.Read => AnyStateReads,
            AdvisoryEffectSiteKind.Throw => site.Escapes ? EffectContractKind.Throws : EffectContractKind.None,
            AdvisoryEffectSiteKind.Write => site.Region switch
            {
                IrWriteRegion.Local => EffectContractKind.None,
                IrWriteRegion.Parameter => EffectContractKind.WritesArgumentState,
                IrWriteRegion.Static => EffectContractKind.WritesStaticState,
                _ => AnyStateWrites
            },
            AdvisoryEffectSiteKind.Call => CallEffects(site.Effects),
            _ => EffectContractKind.None
        };
    }

    // An unspecified call may have every effect.
    private static EffectContractKind CallEffects(IrOpaqueCallEffects call)
    {
        if (call == IrOpaqueCallEffects.All)
        { return AllEffects; }
        var effects = EffectContractKind.None;
        if ((call & IrOpaqueCallEffects.Writes) != 0)
        { effects |= AnyStateWrites; }
        if ((call & IrOpaqueCallEffects.Reads) != 0)
        { effects |= AnyStateReads; }
        if ((call & IrOpaqueCallEffects.Allocates) != 0)
        { effects |= EffectContractKind.Allocates; }
        if ((call & IrOpaqueCallEffects.Throws) != 0)
        { effects |= EffectContractKind.Throws; }
        if ((call & IrOpaqueCallEffects.Synchronizes) != 0)
        { effects |= EffectContractKind.Synchronizes; }
        if ((call & IrOpaqueCallEffects.InputOutput) != 0)
        { effects |= EffectContractKind.ReadsAmbientState | EffectContractKind.WritesAmbientState; }
        if ((call & IrOpaqueCallEffects.Nondeterminism) != 0)
        { effects |= EffectContractKind.UsesNondeterminism; }
        if ((call & IrOpaqueCallEffects.NativeCode) != 0)
        { effects |= EffectContractKind.UsesNativeCode; }
        if ((call & IrOpaqueCallEffects.Reflection) != 0)
        { effects |= EffectContractKind.UsesReflection; }
        return effects;
    }

    // A lock synchronizes; an unspecified call may use any capability.
    private static EffectContractCapabilityKind Capabilities(AdvisoryEffectSite site)
    {
        if (site.Kind == AdvisoryEffectSiteKind.Lock)
        { return EffectContractCapabilityKind.Synchronization; }
        if (site.Kind != AdvisoryEffectSiteKind.Call)
        { return EffectContractCapabilityKind.None; }
        if (IrOpaqueCallSite.Capabilities(site.Effects) is { } declared)
        { return (EffectContractCapabilityKind)declared; }
        if (site.Effects == IrOpaqueCallEffects.All)
        { return EffectContractMetadata.AllCapabilities; }
        var capabilities = EffectContractCapabilityKind.None;
        if ((site.Effects & IrOpaqueCallEffects.InputOutput) != 0)
        { capabilities |= EffectContractCapabilityKind.IO; }
        if ((site.Effects & IrOpaqueCallEffects.Synchronizes) != 0)
        { capabilities |= EffectContractCapabilityKind.Synchronization; }
        if ((site.Effects & IrOpaqueCallEffects.NativeCode) != 0)
        { capabilities |= EffectContractCapabilityKind.NativeInterop; }
        if ((site.Effects & IrOpaqueCallEffects.Reflection) != 0)
        { capabilities |= EffectContractCapabilityKind.Reflection; }
        if ((site.Effects & IrOpaqueCallEffects.Nondeterminism) != 0)
        { capabilities |= EffectContractCapabilityKind.Randomness; }
        return capabilities;
    }

    // An explicit throw is allowed when its static type derives from a listed
    // type; a runtime fault when its exception type does.
    private static bool Allowed(AdvisoryEffectSite site, ImmutableArray<INamedTypeSymbol> allowed, Compilation compilation)
    {
        if (site.Exception == IrExceptionKind.Explicit)
        {
            var listed = allowed.Select(CompilerExceptionTypeIdentity.Encode).ToImmutableHashSet(StringComparer.Ordinal);
            return site.ThrownTypes.Any(listed.Contains);
        }
        var runtime = compilation.GetTypeByMetadataName(CSharpOperationSemantics.ExceptionMetadataName(site.Exception));
        return runtime != null && allowed.Any(type => EffectTypeFacts.IsDerivedFrom(runtime, type));
    }

    private static string ThrownName(AdvisoryEffectSite site)
    {
        return site.Exception == IrExceptionKind.Explicit
            ? "an explicit exception"
            : CSharpOperationSemantics.ExceptionMetadataName(site.Exception);
    }
}
