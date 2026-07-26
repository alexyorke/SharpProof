using System.Globalization;

namespace SharpProof.Effects;

internal sealed class ExternalEffectResolver {
    private static readonly SharpProofEffect DefinedEffects =
        Enum.GetValues(typeof(SharpProofEffect))
            .Cast<SharpProofEffect>()
            .Aggregate(SharpProofEffect.None, static (left, right) => left | right);
    private static readonly SharpProofCapability DefinedCapabilities =
        Enum.GetValues(typeof(SharpProofCapability))
            .Cast<SharpProofCapability>()
            .Aggregate(SharpProofCapability.None, static (left, right) => left | right);
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _effectContractAttribute;
    private readonly INamedTypeSymbol? _exceptionType;
    private readonly ResolvedApiSpecTable _specs;
    private readonly INamedTypeSymbol? _trustedAttribute;

    internal ExternalEffectResolver(
        Compilation compilation,
        ApiSpecTable apiSpecs)
        : this(
            compilation,
            new ApiSpecResolver(
                    apiSpecs ?? throw new ArgumentNullException(nameof(apiSpecs)))
                .Resolve(
                    compilation ??
                    throw new ArgumentNullException(nameof(compilation)))) {
    }

    internal ExternalEffectResolver(
        Compilation compilation,
        ResolvedApiSpecTable apiSpecs) {
        _compilation = compilation ??
            throw new ArgumentNullException(nameof(compilation));
        _effectContractAttribute = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.EffectContractAttribute");
        _exceptionType = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.Exception);
        _trustedAttribute = compilation.GetTypeByMetadataName(
            "SharpProof.Attributes.SharpProofTrustedAttribute");
        _specs = apiSpecs ?? throw new ArgumentNullException(nameof(apiSpecs));
    }

    internal ResolvedApiSpecTable ApiSpecs => _specs;

    internal EffectSummary Resolve(IMethodSymbol method) {
        if (TryResolveContract(method, out var contract))
            return contract;
        if (_specs.TryGet(method, out var spec))
            return ResolveSpec(spec.Template);
        return EffectSummaryOperations.UnknownBoundary(
            EffectUncertainty.UnmodeledCall);
    }

    internal EffectThrowSet ResolveExceptionSet(IEnumerable<string> metadataNames) {
        var types = new List<INamedTypeSymbol>();
        foreach (var metadataName in metadataNames) {
            var type = _compilation.GetTypeByMetadataName(metadataName);
            if (type == null || !IsException(type))
                return EffectThrowSet.Unknown;
            types.Add(type);
        }
        return EffectThrowSet.Create(types);
    }

    private bool TryResolveContract(
        IMethodSymbol method,
        out EffectSummary summary) {
        var attributes = EnumerateDirectContractAttributes(method).ToImmutableArray();
        if (attributes.IsDefaultOrEmpty || !HasValidTrustReason(method)) {
            summary = EffectSummary.Bottom;
            return false;
        }
        EffectSummary? resolved = null;
        foreach (var attribute in attributes) {
            if (!TryDecodeContract(method, attribute, out var candidate)) {
                summary = EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.InvalidContract);
                return true;
            }
            if (resolved != null && !resolved.Equals(candidate)) {
                summary = EffectSummaryOperations.UnknownBoundary(
                    EffectUncertainty.InvalidContract);
                return true;
            }
            resolved = candidate;
        }
        summary = resolved!;
        return true;
    }

    private bool HasValidTrustReason(IMethodSymbol method) {
        if (_trustedAttribute == null) return false;
        foreach (var symbol in EnumerateTrustScopes(method))
            foreach (var attribute in symbol.GetAttributes())
                if (IsTrusted(attribute) &&
                    attribute.ConstructorArguments.Length == 1 &&
                    attribute.ConstructorArguments[0].Value is string reason &&
                    !string.IsNullOrWhiteSpace(reason))
                    return true;
        return false;
    }

    private static IEnumerable<ISymbol> EnumerateTrustScopes(
        IMethodSymbol method) {
        yield return method;
        if (method.AssociatedSymbol is IPropertySymbol property)
            yield return property;
        for (var type = method.ContainingType; type != null; type = type.ContainingType)
            yield return type;
        if (method.ContainingAssembly != null)
            yield return method.ContainingAssembly;
    }

    private IEnumerable<AttributeData> EnumerateDirectContractAttributes(
        IMethodSymbol method) {
        foreach (var attribute in method.GetAttributes())
            if (IsEffectContract(attribute))
                yield return attribute;
        if (method.AssociatedSymbol is IPropertySymbol property)
            foreach (var attribute in property.GetAttributes())
                if (IsEffectContract(attribute))
                    yield return attribute;
    }

    private bool TryDecodeContract(
        IMethodSymbol method,
        AttributeData attribute,
        out EffectSummary summary) {
        summary = EffectSummary.Top;
        if (attribute.ConstructorArguments.Length != 1 ||
            attribute.ConstructorArguments[0].Value == null ||
            !TryConvertEffects(attribute.ConstructorArguments[0].Value!, out var effects) ||
            (effects & ~DefinedEffects) != 0)
            return false;
        var capabilities = SharpProofCapability.None;
        var complete = true;
        var deterministic = true;
        ImmutableArray<TypedConstant> thrown = [];
        foreach (var argument in attribute.NamedArguments) {
            switch (argument.Key) {
                case nameof(EffectContractAttribute.Capabilities):
                    if (argument.Value.Value == null ||
                        !TryConvertCapabilities(
                            argument.Value.Value,
                            out capabilities) ||
                        (capabilities & ~DefinedCapabilities) != 0)
                        return false;
                    break;
                case nameof(EffectContractAttribute.Complete):
                    if (argument.Value.Value is not bool completeValue)
                        return false;
                    complete = completeValue;
                    break;
                case nameof(EffectContractAttribute.IsDeterministic):
                    if (argument.Value.Value is not bool deterministicValue)
                        return false;
                    deterministic = deterministicValue;
                    break;
                case nameof(EffectContractAttribute.ThrownExceptions):
                    if (argument.Value.Kind != TypedConstantKind.Array ||
                        argument.Value.Values.IsDefault)
                        return false;
                    thrown = argument.Value.Values;
                    break;
            }
        }

        if (!complete)
            return false;
        var exceptionTypes = new List<INamedTypeSymbol>();
        foreach (var constant in thrown) {
            if (constant.Value is not INamedTypeSymbol type || !IsException(type))
                return false;
            exceptionTypes.Add(type);
        }
        if ((effects & SharpProofEffect.Throws) != 0 && exceptionTypes.Count == 0 ||
            (effects & SharpProofEffect.Throws) == 0 && exceptionTypes.Count != 0)
            return false;
        if ((effects &
             (SharpProofEffect.WritesReceiverState |
              SharpProofEffect.ReadsReceiverState)) != 0 &&
            method.IsStatic)
            return false;
        if ((effects &
             (SharpProofEffect.WritesArgumentState |
              SharpProofEffect.ReadsArgumentState)) != 0 &&
            method.Parameters.IsDefaultOrEmpty)
            return false;

        var reads = ContractRegions(method, effects, isWrite: false);
        var writes = ContractRegions(method, effects, isWrite: true);
        var allocation = (effects & SharpProofEffect.Allocates) != 0
            ? EffectAllocationKind.Managed
            : EffectAllocationKind.None;
        var capabilityKinds = ConvertCapabilities(capabilities);
        if ((effects & SharpProofEffect.Synchronizes) != 0)
            capabilityKinds |= EffectCapabilityKind.Synchronization;
        if ((effects & SharpProofEffect.UsesNondeterminism) != 0 ||
            !deterministic)
            capabilityKinds |= EffectCapabilityKind.Randomness;
        if ((effects & SharpProofEffect.UsesNativeCode) != 0)
            capabilityKinds |= EffectCapabilityKind.NativeInterop;
        if ((effects & SharpProofEffect.UsesReflection) != 0)
            capabilityKinds |= EffectCapabilityKind.Reflection;
        summary = new EffectSummary(
            reads,
            writes,
            allocation,
            new EffectCapabilitySet(capabilityKinds),
            EffectThrowSet.Create(exceptionTypes),
            EffectTermination.Unknown,
            EffectCompleteness.Complete,
            EffectUncertainty.None);
        return true;
    }

    private EffectSummary ResolveSpec(ApiSpecTemplate spec) {
        var effects = spec.Facets.Effects.Effects;
        var reads = EffectRegionSet.Empty;
        var writes = EffectRegionSet.Empty;
        var capabilities = EffectCapabilityKind.None;
        var completeness = EffectCompleteness.Complete;
        if ((effects & SpecEffect.Unknown) != 0) {
            reads = EffectRegionSet.Unknown;
            writes = EffectRegionSet.Unknown;
            capabilities = EffectCapabilityKind.Unknown;
            completeness = EffectCompleteness.Incomplete;
        }
        else {
            if ((effects & SpecEffect.ReadsReceiverState) != 0)
                reads = reads.Union(
                    EffectRegionSet.Create(EffectRegionId.Receiver));
            if ((effects & SpecEffect.ReadsArgumentState) != 0)
                reads = reads.Union(ParameterRegions(spec.Target.ParameterTypes.Length));
            if ((effects & SpecEffect.ReadsAmbientState) != 0)
                reads = reads.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
            if ((effects & SpecEffect.WritesReceiverState) != 0)
                writes = writes.Union(
                    EffectRegionSet.Create(EffectRegionId.Receiver));
            if ((effects & SpecEffect.WritesArgumentState) != 0)
                writes = writes.Union(ParameterRegions(spec.Target.ParameterTypes.Length));
            if ((effects & SpecEffect.WritesAmbientState) != 0)
                writes = writes.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
            if ((effects & SpecEffect.InputOutput) != 0) {
                reads = reads.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
                writes = writes.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
                capabilities |= EffectCapabilityKind.IO;
            }
            if ((effects & SpecEffect.Synchronization) != 0)
                capabilities |= EffectCapabilityKind.Synchronization;
            if ((effects & SpecEffect.NativeCode) != 0)
                capabilities |= EffectCapabilityKind.NativeInterop;
            if ((effects & SpecEffect.Reflection) != 0)
                capabilities |= EffectCapabilityKind.Reflection;
            if ((effects & SpecEffect.Nondeterminism) != 0)
                capabilities |= EffectCapabilityKind.Randomness;
        }

        var allocation = spec.Facets.Allocation.Behavior switch {
            SpecAllocationBehavior.None => EffectAllocationKind.None,
            SpecAllocationBehavior.MayAllocate => EffectAllocationKind.Managed,
            SpecAllocationBehavior.Unknown => EffectAllocationKind.Unknown,
            _ => EffectAllocationKind.Unknown
        };
        if (allocation == EffectAllocationKind.Unknown)
            completeness = EffectCompleteness.Incomplete;
        EffectThrowSet throws;
        switch (spec.Facets.Throws.Behavior) {
            case SpecThrowBehavior.DoesNotThrow:
                throws = EffectThrowSet.Empty;
                break;
            case SpecThrowBehavior.MayThrow:
                throws = ResolveExceptionSet(
                    spec.Facets.Throws.ExceptionMetadataNames);
                if (throws.IsEmpty || throws.IncludesUnknown) {
                    throws = EffectThrowSet.Unknown;
                    completeness = EffectCompleteness.Incomplete;
                }
                break;
            case SpecThrowBehavior.Unknown:
                throws = EffectThrowSet.Unknown;
                completeness = EffectCompleteness.Incomplete;
                break;
            default:
                throws = EffectThrowSet.Unknown;
                completeness = EffectCompleteness.Incomplete;
                break;
        }
        return new EffectSummary(
            reads,
            writes,
            allocation,
            new EffectCapabilitySet(capabilities),
            throws,
            EffectTermination.Unknown,
            completeness);
    }

    private bool IsEffectContract(AttributeData attribute) =>
        _effectContractAttribute != null &&
        SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition,
            _effectContractAttribute.OriginalDefinition);

    private bool IsTrusted(AttributeData attribute) =>
        _trustedAttribute != null &&
        SymbolEqualityComparer.Default.Equals(
            attribute.AttributeClass?.OriginalDefinition,
            _trustedAttribute.OriginalDefinition);

    private bool IsException(INamedTypeSymbol type) {
        if (_exceptionType == null) return false;
        for (var current = type; current != null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(
                    current.OriginalDefinition,
                    _exceptionType.OriginalDefinition))
                return true;
        return false;
    }

    private static EffectRegionSet ContractRegions(
        IMethodSymbol method,
        SharpProofEffect effects,
        bool isWrite) {
        var result = EffectRegionSet.Empty;
        var receiverFlag = isWrite
            ? SharpProofEffect.WritesReceiverState
            : SharpProofEffect.ReadsReceiverState;
        var argumentFlag = isWrite
            ? SharpProofEffect.WritesArgumentState
            : SharpProofEffect.ReadsArgumentState;
        var capturedFlag = isWrite
            ? SharpProofEffect.WritesCapturedState
            : SharpProofEffect.ReadsCapturedState;
        var staticFlag = isWrite
            ? SharpProofEffect.WritesStaticState
            : SharpProofEffect.ReadsStaticState;
        var ambientFlag = isWrite
            ? SharpProofEffect.WritesAmbientState
            : SharpProofEffect.ReadsAmbientState;
        if ((effects & receiverFlag) != 0)
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Receiver));
        if ((effects & argumentFlag) != 0)
            result = result.Union(ParameterRegions(method.Parameters.Length));
        if ((effects & capturedFlag) != 0)
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Captured(0)));
        if ((effects & staticFlag) != 0)
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Static()));
        if ((effects & ambientFlag) != 0)
            result = result.Union(EffectRegionSet.Create(EffectRegionId.Ambient));
        return result;
    }

    private static EffectRegionSet ParameterRegions(int count) {
        var regions = ImmutableArray.CreateBuilder<EffectRegionId>(count);
        for (var ordinal = 0; ordinal < count; ordinal++)
            regions.Add(EffectRegionId.Parameter(ordinal));
        return EffectRegionSet.Create(regions);
    }

    private static EffectCapabilityKind ConvertCapabilities(
        SharpProofCapability capabilities) =>
        (EffectCapabilityKind)(int)capabilities;

    private static bool TryConvertEffects(
        object value,
        out SharpProofEffect effects) {
        try {
            effects = (SharpProofEffect)Convert.ToInt64(
                value,
                CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidCastException or
            FormatException or
            OverflowException) {
            effects = SharpProofEffect.None;
            return false;
        }
    }

    private static bool TryConvertCapabilities(
        object value,
        out SharpProofCapability capabilities) {
        try {
            capabilities = (SharpProofCapability)Convert.ToInt32(
                value,
                CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidCastException or
            FormatException or
            OverflowException) {
            capabilities = SharpProofCapability.None;
            return false;
        }
    }
}
