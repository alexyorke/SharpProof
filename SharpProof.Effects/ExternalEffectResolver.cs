namespace SharpProof.Effects;

internal enum EffectContractResolutionKind
{
    Missing,
    Untrusted,
    Incomplete,
    Invalid,
    Valid
}
internal readonly record struct EffectContractResolution(
    EffectContractResolutionKind Kind,
    EffectSummary Summary,
    AttributeData? InvalidAttribute = null,
    string InvalidReason = "");

internal sealed class ExternalEffectResolver
{
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _effectContractAttribute;
    private readonly INamedTypeSymbol? _exceptionType;
    private readonly ResolvedApiSpecTable _specs;
    private readonly INamedTypeSymbol? _trustedAttribute;

    internal ExternalEffectResolver(Compilation compilation, ApiSpecTable apiSpecs)
        : this(
            compilation,
            new ApiSpecResolver(apiSpecs ?? throw new ArgumentNullException(nameof(apiSpecs)))
                .Resolve(compilation ?? throw new ArgumentNullException(nameof(compilation))))
    {
    }

    internal ExternalEffectResolver(Compilation compilation, ResolvedApiSpecTable apiSpecs)
    {
        _compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        _effectContractAttribute = compilation.GetTypeByMetadataName(EffectContractMetadata.AttributeMetadataName);
        _exceptionType = compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        _trustedAttribute = compilation.GetTypeByMetadataName(EffectContractMetadata.TrustedAttributeMetadataName);
        _specs = apiSpecs ?? throw new ArgumentNullException(nameof(apiSpecs));
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
        var attributes = EnumerateDirectContractAttributes(method).ToImmutableArray();
        if (attributes.IsDefaultOrEmpty)
        {
            return new(EffectContractResolutionKind.Missing, EffectSummary.Bottom);
        }

        EffectSummary? resolved = null;
        foreach (var attribute in attributes)
        {
            if (!TryDecodeContract(method, attribute, out var candidate))
            {
                return Invalid(attribute, "expected a complete, internally consistent effect summary");
            }
            if (resolved != null && !resolved.Equals(candidate))
            {
                return Invalid(attribute, "expected duplicate declarations to describe identical effects");
            }
            resolved = candidate;
        }
        if (!HasValidTrustReason(method))
        {
            return new(EffectContractResolutionKind.Untrusted, resolved!);
        }

        if (resolved!.Completeness != EffectCompleteness.Complete)
        {
            return new(EffectContractResolutionKind.Incomplete, EffectSummary.Top);
        }

        return new(EffectContractResolutionKind.Valid, resolved!);
    }

    private static EffectContractResolution Invalid(AttributeData attribute, string reason)
    {
        return new(
            EffectContractResolutionKind.Invalid,
            EffectSummaryOperations.UnknownBoundary(EffectUncertainty.InvalidContract),
            attribute, reason);
    }

    private bool HasValidTrustReason(IMethodSymbol method)
    {
        if (_trustedAttribute == null)
        {
            return false;
        }

        foreach (var symbol in EnumerateTrustScopes(method))
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                if (IsTrusted(attribute) &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string reason &&
                    !string.IsNullOrWhiteSpace(reason))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<ISymbol> EnumerateTrustScopes(IMethodSymbol method)
    {
        yield return method;
        if (method.AssociatedSymbol is IPropertySymbol property)
        {
            yield return property;
        }

        for (var type = method.ContainingType; type != null; type = type.ContainingType)
        {
            yield return type;
        }

        if (method.ContainingAssembly != null)
        {
            yield return method.ContainingAssembly;
        }
    }

    private IEnumerable<AttributeData> EnumerateDirectContractAttributes(IMethodSymbol method)
    {
        return method.GetAttributes()
            .Concat(method.AssociatedSymbol is IPropertySymbol property ? property.GetAttributes() : [])
            .Where(IsEffectContract);
    }

    private bool TryDecodeContract(
        IMethodSymbol method, AttributeData attribute, out EffectSummary summary)
    {
        summary = EffectSummary.Top;
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value == null ||
            !TryConvertEffects(attribute.ConstructorArguments[0].Value!, out var effects) ||
            (effects & ~EffectContractMetadata.AllEffects) != 0)
        {
            return false;
        }

        var capabilities = EffectContractCapabilityKind.None;
        var complete = false;
        var deterministic = false;
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
                    if (argument.Value.Value is not bool deterministicValue)
                    {
                        return false;
                    }

                    deterministic = deterministicValue;
                    break;
                case EffectContractMetadata.ThrownExceptionsPropertyName:
                    if (argument.Value.Kind != TypedConstantKind.Array ||
                        argument.Value.Values.IsDefault)
                    {
                        return false;
                    }

                    thrown = argument.Value.Values;
                    break;
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
        if ((effects & EffectContractKind.Throws) != 0 && exceptionTypes.Count == 0 ||
            (effects & EffectContractKind.Throws) == 0 && exceptionTypes.Count != 0)
        {
            return false;
        }

        if ((effects & (EffectContractKind.WritesReceiverState | EffectContractKind.ReadsReceiverState)) != 0 &&
            method.IsStatic)
        {
            return false;
        }

        if ((effects & (EffectContractKind.WritesArgumentState | EffectContractKind.ReadsArgumentState)) != 0 &&
            method.Parameters.IsDefaultOrEmpty)
        {
            return false;
        }

        var reads = EffectContractMappings.ToAnalysisRegions(effects, isWrite: false, method.Parameters.Length);
        var writes = EffectContractMappings.ToAnalysisRegions(effects, isWrite: true, method.Parameters.Length);
        var allocation = (effects & EffectContractKind.Allocates) != 0
            ? EffectAllocationKind.Managed
            : EffectAllocationKind.None;
        var capabilityKinds = ConvertCapabilities(capabilities);
        if ((effects & EffectContractKind.Synchronizes) != 0)
        {
            capabilityKinds |= EffectCapabilityKind.Synchronization;
        }

        if ((effects & EffectContractKind.UsesNondeterminism) != 0 || !deterministic)
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
        var reads = SpecRegions(effects, SpecEffect.ReadsReceiverState,
            SpecEffect.ReadsArgumentState, SpecEffect.ReadsAmbientState, spec.Target.ParameterTypes.Length);
        var writes = SpecRegions(effects, SpecEffect.WritesReceiverState,
            SpecEffect.WritesArgumentState, SpecEffect.WritesAmbientState, spec.Target.ParameterTypes.Length);
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

        var allocation = spec.Facets.Allocation.Behavior switch
        {
            SpecAllocationBehavior.None => EffectAllocationKind.None,
            SpecAllocationBehavior.MayAllocate => EffectAllocationKind.Managed,
            SpecAllocationBehavior.Unknown => EffectAllocationKind.Unknown,
            _ => EffectAllocationKind.Unknown
        };
        if (allocation == EffectAllocationKind.Unknown)
        {
            completeness = EffectCompleteness.Incomplete;
        }

        var throwBehavior = spec.Facets.Throws.Behavior;
        var exceptions = throwBehavior switch
        {
            SpecThrowBehavior.DoesNotThrow => EffectThrowSet.Empty,
            SpecThrowBehavior.MayThrow => ResolveExceptionSet(
                spec.Facets.Throws.ExceptionMetadataNames),
            _ => EffectThrowSet.Unknown
        };
        if (throwBehavior != SpecThrowBehavior.DoesNotThrow &&
            (throwBehavior != SpecThrowBehavior.MayThrow ||
             exceptions.IsEmpty || exceptions.IncludesUnknown))
        {
            exceptions = EffectThrowSet.Unknown;
            completeness = EffectCompleteness.Incomplete;
        }
        return new EffectSummary(
            reads, writes, allocation, new EffectCapabilitySet(capabilities),
            exceptions, EffectTermination.Unknown, completeness);
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

    private bool IsEffectContract(AttributeData attribute)
    {
        return IsAttribute(attribute, _effectContractAttribute);
    }

    private bool IsTrusted(AttributeData attribute)
    {
        return IsAttribute(attribute, _trustedAttribute);
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

    private static EffectCapabilityKind ConvertCapabilities(EffectContractCapabilityKind capabilities)
    {
        return EffectContractMappings.ToAnalysisCapabilities(capabilities);
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
