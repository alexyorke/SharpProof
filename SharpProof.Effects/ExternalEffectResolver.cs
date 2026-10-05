namespace SharpProof.Effects;

internal sealed class ExternalEffectResolver
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _effectContractAttribute;
    private readonly INamedTypeSymbol? _exceptionType;
    private readonly ResolvedApiSpecTable _specs;
    private readonly TrustedBoundaryPolicy _trustedBoundaries;

    internal ExternalEffectResolver(Compilation compilation, ApiSpecTable apiSpecs)
        : this(
            compilation,
            new ApiSpecResolver(ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs)))
                .Resolve(compilation))
    {
    }

    internal ExternalEffectResolver(Compilation compilation, ResolvedApiSpecTable apiSpecs)
    {
        _compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        var contractApi = ContractApiIdentityResolver.ForCompilation(compilation);
        _effectContractAttribute = contractApi.ResolveAttribute(
            EffectContractMetadata.AttributeMetadataName);
        _exceptionType = compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        _trustedBoundaries =
            TrustedBoundaryPolicy.ForCompilation(compilation);
        _specs = ArgumentNullGuard.NotNull(apiSpecs, nameof(apiSpecs));
    }

    internal ResolvedApiSpecTable ApiSpecs => _specs;

    internal EffectSummary Resolve(IMethodSymbol method)
    {
        var resolution = ResolveContract(method);
        if (resolution.Kind is EffectContractResolutionKind.Valid or
            EffectContractResolutionKind.Incomplete or EffectContractResolutionKind.Invalid)
        {
            return resolution.Summary;
        }

        if (_specs.TryGet(method, out var spec))
        {
            return ResolveSpec(spec.Template);
        }

        return EffectSummaryOperations.UnknownBoundary(EffectUncertainty.UnmodeledCall);
    }

    internal EffectThrowSet ResolveExceptionSet(IEnumerable<string> metadataNames)
    {
        var types = new List<INamedTypeSymbol>();
        foreach (var metadataName in metadataNames)
        {
            var type = _compilation.GetTypeByMetadataName(metadataName);
            if (type == null || !IsException(type))
            {
                return EffectThrowSet.Unknown;
            }

            types.Add(type);
        }
        return EffectThrowSet.Create(types);
    }

    internal EffectContractResolution ResolveContract(IMethodSymbol method)
    {
        EffectSummary? resolved = null;
        var preconditionFree = true;
        var sawAttribute = false;
        var invalidAttributes =
            ImmutableArray.CreateBuilder<EffectContractInvalidAttribute>();
        foreach (var attribute in EnumerateDirectContractAttributes(method))
        {
            sawAttribute = true;
            if (!TryDecodeContract(
                    method,
                    attribute,
                    out var candidate,
                    out var candidatePreconditionFree))
            {
                invalidAttributes.Add(new(
                    attribute,
                    "expected a complete, internally consistent effect summary"));
                continue;
            }
            if (resolved != null && !resolved.Equals(candidate))
            {
                invalidAttributes.Add(new(
                    attribute,
                    "expected duplicate declarations to describe identical effects"));
                continue;
            }
            resolved = candidate;
            preconditionFree &= candidatePreconditionFree;
        }
        if (!sawAttribute)
        {
            return new(EffectContractResolutionKind.Missing, EffectSummary.Bottom);
        }
        if (invalidAttributes.Count != 0)
        {
            return Invalid(invalidAttributes.ToImmutable());
        }
        if (!_trustedBoundaries.AuthorizesDeclaredContracts(method))
        {
            return new(EffectContractResolutionKind.Untrusted, resolved!);
        }

        if (resolved!.Completeness != EffectCompleteness.Complete)
        {
            return new(EffectContractResolutionKind.Incomplete, EffectSummary.Top);
        }

        if (method.DeclaringSyntaxReferences.Length == 0 &&
            !preconditionFree)
        {
            return new(
                EffectContractResolutionKind.Incomplete,
                EffectSummaryOperations.Join(
                    resolved,
                    EffectSummaryOperations.IncompleteAnalysis(
                        EffectAnalysisIncompleteReason
                            .CallPreconditionNotProven)));
        }

        return new(EffectContractResolutionKind.Valid, resolved!);
    }

    private static EffectContractResolution Invalid(
        ImmutableArray<EffectContractInvalidAttribute> invalidAttributes)
    {
        return new(
            EffectContractResolutionKind.Invalid,
            EffectSummaryOperations.UnknownBoundary(EffectUncertainty.InvalidContract),
            invalidAttributes);
    }

    private IEnumerable<AttributeData> EnumerateDirectContractAttributes(IMethodSymbol method)
    {
        return method.GetAttributes()
            .Concat(method.AssociatedSymbol is IPropertySymbol property ? property.GetAttributes() : [])
            .Where(attribute => IsAttribute(attribute, _effectContractAttribute));
    }

    private bool TryDecodeContract(
        IMethodSymbol method,
        AttributeData attribute,
        out EffectSummary summary,
        out bool preconditionFree)
    {
        summary = EffectSummary.Top;
        preconditionFree = false;
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value == null ||
            !TryConvertEffects(attribute.ConstructorArguments[0].Value!, out var effects) ||
            (effects & ~EffectContractMetadata.AllEffects) != 0)
        {
            return false;
        }

        var capabilities = EffectContractCapabilityKind.None;
        var complete = false;
        ImmutableArray<TypedConstant> thrown = [];
        foreach (var argument in attribute.NamedArguments)
        {
            switch (argument.Key)
            {
                case EffectContractMetadata.CapabilitiesPropertyName:
                    if (argument.Value.Value == null ||
                        !TryConvertCapabilities(argument.Value.Value, out capabilities) ||
                        (capabilities & ~EffectContractMetadata.AllCapabilities) != 0)
                    {
                        return false;
                    }

                    break;
                case EffectContractMetadata.CompletePropertyName:
                    if (argument.Value.Value is not bool completeValue)
                    {
                        return false;
                    }

                    complete = completeValue;
                    break;
                case EffectContractMetadata.IsDeterministicPropertyName:
                    if (argument.Value.Value is not bool)
                    {
                        return false;
                    }
                    break;
                case EffectContractMetadata.PreconditionFreePropertyName:
                    if (argument.Value.Value is not bool preconditionFreeValue)
                    {
                        return false;
                    }

                    preconditionFree = preconditionFreeValue;
                    break;
                case EffectContractMetadata.ThrownExceptionsPropertyName:
                    if (argument.Value.Kind != TypedConstantKind.Array ||
                        argument.Value.Values.IsDefault)
                    {
                        return false;
                    }

                    thrown = argument.Value.Values;
                    break;
                default:
                    return false;
            }
        }

        var exceptionTypes = new List<INamedTypeSymbol>();
        foreach (var constant in thrown)
        {
            if (constant.Value is not INamedTypeSymbol type || !IsException(type))
            {
                return false;
            }

            exceptionTypes.Add(type);
        }
        if (((effects & EffectContractKind.Throws) != 0) !=
            (exceptionTypes.Count != 0))
        {
            return false;
        }

        if ((effects & (EffectContractKind.WritesReceiverState | EffectContractKind.ReadsReceiverState)) != 0 &
            method.IsStatic)
        {
            return false;
        }

        if ((effects & (EffectContractKind.WritesArgumentState | EffectContractKind.ReadsArgumentState)) != 0 &&
            method.Parameters.IsDefaultOrEmpty)
        {
            return false;
        }

        var regionProjections = EffectContractMappings.ToAnalysisRegions(
            effects,
            method.Parameters.Length);
        var reads = regionProjections.Reads;
        var writes = regionProjections.Writes;
        var allocation = (effects & EffectContractKind.Allocates) != 0
            ? EffectAllocationKind.Managed
            : EffectAllocationKind.None;
        var capabilityKinds = EffectContractMappings.ToAnalysisCapabilities(capabilities);
        if ((effects & EffectContractKind.Synchronizes) != 0)
        {
            capabilityKinds |= EffectCapabilityKind.Synchronization;
        }

        if ((effects & EffectContractKind.UsesNondeterminism) != 0)
        {
            capabilityKinds |= EffectCapabilityKind.Randomness;
        }

        if ((effects & EffectContractKind.UsesNativeCode) != 0)
        {
            capabilityKinds |= EffectCapabilityKind.NativeInterop;
        }

        if ((effects & EffectContractKind.UsesReflection) != 0)
        {
            capabilityKinds |= EffectCapabilityKind.Reflection;
        }

        summary = new EffectSummary(
            reads, writes, allocation, new EffectCapabilitySet(capabilityKinds),
            EffectThrowSet.Create(exceptionTypes), EffectTermination.Unknown,
            complete ? EffectCompleteness.Complete : EffectCompleteness.Incomplete,
            EffectUncertainty.None);
        return true;
    }

    private EffectSummary ResolveSpec(ApiSpecTemplate spec)
    {
        var effects = spec.Facets.Effects.Effects;
        var reads = EffectRegionSet.Empty;
        var writes = EffectRegionSet.Empty;
        var capabilities = EffectCapabilityKind.None;
        var completeness = EffectCompleteness.Complete;
        if ((effects & SpecEffect.Unknown) != 0)
        {
            reads = EffectRegionSet.Unknown;
            writes = EffectRegionSet.Unknown;
            capabilities = EffectCapabilityKind.Unknown;
            completeness = EffectCompleteness.Incomplete;
        }
        else
        {
            reads = SpecRegions(effects, SpecEffect.ReadsReceiverState,
                SpecEffect.ReadsArgumentState, SpecEffect.ReadsAmbientState,
                spec.Target.ParameterTypes.Length);
            writes = SpecRegions(effects, SpecEffect.WritesReceiverState,
                SpecEffect.WritesArgumentState, SpecEffect.WritesAmbientState,
                spec.Target.ParameterTypes.Length);
            if ((effects & SpecEffect.InputOutput) != 0)
            {
                reads = reads.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
                writes = writes.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
                capabilities |= EffectCapabilityKind.IO;
            }
            if ((effects & SpecEffect.Synchronization) != 0)
            {
                capabilities |= EffectCapabilityKind.Synchronization;
            }

            if ((effects & SpecEffect.NativeCode) != 0)
            {
                capabilities |= EffectCapabilityKind.NativeInterop;
            }

            if ((effects & SpecEffect.Reflection) != 0)
            {
                capabilities |= EffectCapabilityKind.Reflection;
            }

            if ((effects & SpecEffect.Nondeterminism) != 0)
            {
                capabilities |= EffectCapabilityKind.Randomness;
            }
        }

        var allocation = EffectProjections.MapAllocation(
            spec.Facets.Allocation.Behavior);
        if (allocation == EffectAllocationKind.Unknown)
        {
            completeness = EffectCompleteness.Incomplete;
        }

        var throwBehavior = spec.Facets.Throws.Behavior;
        var exceptions = throwBehavior == SpecThrowBehavior.MayThrow
            ? ResolveExceptionSet(spec.Facets.Throws.ExceptionMetadataNames)
            : EffectProjections.MapNonResolvedThrowBehavior(throwBehavior);
        if (throwBehavior != SpecThrowBehavior.DoesNotThrow &&
            (throwBehavior != SpecThrowBehavior.MayThrow ||
             exceptions.IsEmpty || exceptions.IncludesUnknown))
        {
            exceptions = EffectThrowSet.Unknown;
            completeness = EffectCompleteness.Incomplete;
        }
        var termination = EffectProjections.MapTermination(
            spec.Facets.Termination?.Behavior);
        return new EffectSummary(
            reads, writes, allocation, new EffectCapabilitySet(capabilities),
            exceptions, termination, completeness);
    }

    private static EffectRegionSet SpecRegions(
        SpecEffect effects, SpecEffect receiverEffect, SpecEffect argumentEffect,
        SpecEffect ambientEffect, int parameterCount)
    {
        var regions = EffectRegionSet.Empty;
        if ((effects & receiverEffect) != 0)
        {
            regions = regions.Union(EffectRegionSet.Create(EffectRegionId.Receiver));
        }

        if ((effects & argumentEffect) != 0)
        {
            regions = regions.Union(EffectContractMappings.ParameterRegions(parameterCount));
        }

        if ((effects & ambientEffect) != 0)
        {
            regions = regions.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
        }

        return regions;
    }

    private static bool IsAttribute(AttributeData attribute, INamedTypeSymbol? attributeType)
    {
        return attributeType != null &&
        SymbolEqualityComparer.Default.Equals(attribute.AttributeClass?.OriginalDefinition, attributeType.OriginalDefinition);
    }

    private bool IsException(INamedTypeSymbol type)
    {
        return !type.IsUnboundGenericType &&
        _exceptionType != null &&
        EffectTypeFacts.IsDerivedFrom(type, _exceptionType);
    }

    private static bool TryConvertEffects(object value, out EffectContractKind effects)
    {
        var converted = EffectContractMetadata.TryConvertInt64(value, out var result);
        effects = converted ? (EffectContractKind)result : EffectContractKind.None;
        return converted;
    }

    private static bool TryConvertCapabilities(
        object value, out EffectContractCapabilityKind capabilities)
    {
        var converted = EffectContractMetadata.TryConvertInt64(value, out var result) &&
            result is >= int.MinValue and <= int.MaxValue;
        capabilities = converted ? (EffectContractCapabilityKind)result : EffectContractCapabilityKind.None;
        return converted;
    }
}
