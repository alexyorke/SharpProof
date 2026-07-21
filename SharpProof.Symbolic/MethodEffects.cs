using SharpProof.Attributes;

namespace SharpProof.Symbolic;

public enum SharpProofVerdict {
    Proven,
    Disproven,
    Unknown
}

public sealed record MethodEffectSite(
    SharpProofEffect Effect,
    SharpProofCapability Capabilities,
    string Operation,
    string Symbol,
    int SpanStart,
    int SpanLength,
    bool IsTransitive,
    string Reason);

public sealed record MethodEffects(
    SharpProofEffect Effects,
    SharpProofCapability Capabilities,
    ImmutableArray<string> ThrownExceptions,
    ImmutableArray<MethodEffectSite> Sites,
    ImmutableArray<SharpProofUnknownReason> UnknownReasons) {
    private const SharpProofEffect ImpureEffects =
        SharpProofEffect.ReadsAmbientState |
        SharpProofEffect.ReadsStaticState |
        SharpProofEffect.WritesReceiverState |
        SharpProofEffect.WritesArgumentState |
        SharpProofEffect.WritesCapturedState |
        SharpProofEffect.WritesStaticState |
        SharpProofEffect.Synchronizes |
        SharpProofEffect.UsesNondeterminism |
        SharpProofEffect.UsesNativeCode |
        SharpProofEffect.UsesReflection;

    public SharpProofVerdict Purity => GetVerdict(ImpureEffects, Capabilities != SharpProofCapability.None);

    public SharpProofVerdict AllocationFree => GetVerdict(SharpProofEffect.Allocates, false);

    public SharpProofVerdict DoesNotThrow => GetVerdict(SharpProofEffect.Throws, false);

    private SharpProofVerdict GetVerdict(SharpProofEffect prohibited, bool hasProhibitedCapability) {
        if ((Effects & prohibited) != 0 || hasProhibitedCapability) return SharpProofVerdict.Disproven;
        return (Effects & SharpProofEffect.Unknown) != 0 || !UnknownReasons.IsDefaultOrEmpty
            ? SharpProofVerdict.Unknown
            : SharpProofVerdict.Proven;
    }
}

internal sealed class MethodEffectAnalysisSession(
    Compilation compilation,
    CancellationToken cancellationToken,
    Func<IMethodSymbol, MethodEffects?>? externalContractResolver = null) {
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, MethodEffects> _cache = new(SymbolEqualityComparer.Default);
    private readonly MetadataMethodEffectAnalyzer _metadata = new(compilation);

    internal MethodEffects Analyze(
        IMethodSymbol method,
        SyntaxNode declaration,
        SemanticModel semanticModel) {
        method = method.OriginalDefinition;
        if (_cache.TryGetValue(method, out var cached)) return cached;
        if (!_active.Add(method)) return Unknown("recursive_call", declaration);

        try {
            var root = MethodBodyOperationResolver.GetMethodBodyRootOperation(
                declaration,
                semanticModel,
                cancellationToken,
                true);
            if (root == null) return Cache(method, AnalyzeMetadata(method, declaration));

            var builder = new Builder();
            foreach (var operation in root.DescendantsAndSelf())
                if (operation is IVariableDeclaratorOperation { Symbol: var local, Initializer.Value: var value } &&
                    IsAllocation(value))
                    builder.MarkFresh(local);
            foreach (var operation in root.DescendantsAndSelf()) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsVisible(operation, declaration)) continue;
                AnalyzeOperation(operation, builder);
            }

            return Cache(method, builder.Build());
        }
        finally {
            _active.Remove(method);
        }
    }

    private void AnalyzeOperation(
        IOperation operation,
        Builder builder) {
        switch (operation) {
            case ISimpleAssignmentOperation assignment:
                AddWrite(assignment.Target, builder);
                break;
            case ICompoundAssignmentOperation compound:
                AddWrite(compound.Target, builder);
                break;
            case IIncrementOrDecrementOperation increment:
                AddWrite(increment.Target, builder);
                break;
            case IFieldReferenceOperation { Field.IsConst: false, Field.IsStatic: true } field
                when field.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, field):
                builder.Add(SharpProofEffect.ReadsStaticState, field, field.Field, "static_field_read");
                break;
            case IFieldReferenceOperation field
                when field.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, field):
                builder.Add(GetInstanceReadEffect(field.Instance), field, field.Field, "instance_field_read");
                break;
            case IPropertyReferenceOperation property
                when property.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, property):
                builder.Add(property.Property.IsStatic
                        ? SharpProofEffect.ReadsStaticState
                        : GetInstanceReadEffect(property.Instance),
                    property,
                    property.Property,
                    "property_read");
                AnalyzeCall(property.Property.GetMethod, property, builder);
                break;
            case IObjectCreationOperation creation:
                builder.Add(SharpProofEffect.Allocates, creation, creation.Constructor, "object_allocation");
                AnalyzeCall(creation.Constructor, creation, builder);
                break;
            case IArrayCreationOperation array:
                builder.Add(SharpProofEffect.Allocates, array, array.Type, "array_allocation");
                break;
            case IAnonymousObjectCreationOperation anonymousObject:
                builder.Add(SharpProofEffect.Allocates, anonymousObject, anonymousObject.Type,
                    "anonymous_object_allocation");
                break;
            case IDelegateCreationOperation delegateCreation:
                builder.Add(SharpProofEffect.Allocates, delegateCreation, delegateCreation.Type,
                    "delegate_allocation");
                break;
            case IConversionOperation conversion when conversion.Conversion.IsImplicit &&
                                                     conversion.Operand.Type?.IsValueType == true &&
                                                     conversion.Type?.IsReferenceType == true:
                builder.Add(SharpProofEffect.Allocates, conversion, conversion.Type, "boxing_allocation");
                break;
            case IThrowOperation thrown:
                builder.Add(SharpProofEffect.Throws, thrown, thrown.Exception?.Type, "explicit_throw");
                if (thrown.Exception?.Type != null)
                    builder.AddException(thrown.Exception.Type.ToDisplayString());
                break;
            case ILockOperation locked:
                builder.Add(
                    SharpProofEffect.Synchronizes,
                    SharpProofCapability.Synchronization,
                    locked,
                    null,
                    "synchronization");
                break;
            case IInvocationOperation invocation:
                AnalyzeCall(invocation.TargetMethod, invocation, builder);
                break;
            case IDynamicInvocationOperation or IDynamicIndexerAccessOperation or
                IDynamicMemberReferenceOperation or IDynamicObjectCreationOperation:
                builder.AddUnknown(operation, "dynamic_dispatch");
                break;
        }
    }

    private void AnalyzeCall(
        IMethodSymbol? method,
        IOperation site,
        Builder builder) {
        if (method == null) {
            builder.AddUnknown(site, "unresolved_call");
            return;
        }

        method = (method.ReducedFrom ?? method).OriginalDefinition;
        builder.Add(SharpProofEffect.DirectCall, site, method, "direct_call");
        if (method.IsImplicitlyDeclared) return;
        if (method.IsVirtual || method.ContainingType?.TypeKind == TypeKind.Interface) {
            builder.Add(SharpProofEffect.DispatchUncertainty, site, method, "dispatch_uncertainty");
            builder.AddUnknown(site, "unresolved_dispatch", method);
            return;
        }
        if (method.GetDllImportData() != null) {
            builder.Add(
                SharpProofEffect.UsesNativeCode,
                SharpProofCapability.NativeInterop,
                site,
                method,
                "native_call");
            return;
        }

        var hasContract = TryReadEffectContract(method, out var contracted);
        var configured = externalContractResolver?.Invoke(method);
        if (configured != null) {
            contracted = hasContract ? UnionContracts(contracted, configured) : configured;
            hasContract = true;
        }

        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        if (syntax == null) {
            if (IsIntrinsicMetadataMethod(method)) return;
            var metadata = _metadata.Analyze(method);
            if (hasContract && metadata.Effects == SharpProofEffect.Unknown)
                builder.AddTransitive(contracted, site, method, "complete_effect_contract");
            else {
                builder.AddTransitive(metadata, site, method, "metadata_call");
                if (hasContract) builder.AddTransitive(contracted, site, method, "effect_contract");
            }
            return;
        }

        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        builder.AddTransitive(Analyze(method, syntax, model), site, method, "source_call");
        if (hasContract) builder.AddTransitive(contracted, site, method, "effect_contract");
    }

    private static bool TryReadEffectContract(IMethodSymbol method, out MethodEffects effects) {
        var canonicalKey = RoslynStructuralMethodIdentity.GetCanonicalKey(method);
        foreach (var attribute in method.GetAttributes().Concat(method.ContainingAssembly?.GetAttributes() ?? [])) {
            if (attribute.AttributeClass?.ToDisplayString() != "SharpProof.Attributes.EffectContractAttribute" ||
                attribute.ConstructorArguments.Length == 0)
                continue;

            if (attribute.AttributeClass?.ContainingAssembly != null &&
                attribute.ConstructorArguments.Length == 2 &&
                !string.Equals(attribute.ConstructorArguments[0].Value as string, canonicalKey,
                    StringComparison.Ordinal))
                continue;

            var valueIndex = attribute.ConstructorArguments.Length == 1 ? 0 : 1;
            var declared = (SharpProofEffect)(attribute.ConstructorArguments[valueIndex].Value as long? ??
                                                Convert.ToInt64(attribute.ConstructorArguments[valueIndex].Value,
                                                    CultureInfo.InvariantCulture));
            var capabilities = SharpProofCapability.None;
            var deterministic = true;
            var complete = true;
            var exceptions = ImmutableArray.CreateBuilder<string>();
            foreach (var pair in attribute.NamedArguments) {
                if (pair.Key == nameof(EffectContractAttribute.Capabilities) && pair.Value.Value != null)
                    capabilities = (SharpProofCapability)Convert.ToInt32(pair.Value.Value, CultureInfo.InvariantCulture);
                else if (pair.Key == nameof(EffectContractAttribute.IsDeterministic) &&
                         pair.Value.Value is bool deterministicValue)
                    deterministic = deterministicValue;
                else if (pair.Key == nameof(EffectContractAttribute.Complete) &&
                         pair.Value.Value is bool completeValue)
                    complete = completeValue;
                else if (pair.Key == nameof(EffectContractAttribute.ThrownExceptions))
                    foreach (var item in pair.Value.Values)
                        if (item.Value is ITypeSymbol type) exceptions.Add(type.ToDisplayString());
            }

            if (!deterministic) declared |= SharpProofEffect.UsesNondeterminism;
            if (exceptions.Count != 0) declared |= SharpProofEffect.Throws;
            if (!complete) declared |= SharpProofEffect.Unknown;
            effects = new MethodEffects(
                declared,
                capabilities,
                exceptions.ToImmutable(),
                ImmutableArray<MethodEffectSite>.Empty,
                complete
                    ? ImmutableArray<SharpProofUnknownReason>.Empty
                    : ImmutableArray.Create(CreateUnknownReason("partial_effect_contract")));
            return true;
        }

        effects = null!;
        return false;
    }

    private static MethodEffects UnionContracts(MethodEffects left, MethodEffects right) {
        var conflicts = left.Effects != right.Effects || left.Capabilities != right.Capabilities ||
                        !left.ThrownExceptions.SequenceEqual(right.ThrownExceptions);
        var unknowns = left.UnknownReasons.AddRange(right.UnknownReasons);
        if (conflicts)
            unknowns = unknowns.Add(CreateUnknownReason("conflicting_effect_contracts"));
        return new MethodEffects(
            left.Effects | right.Effects | (conflicts ? SharpProofEffect.Unknown : SharpProofEffect.None),
            left.Capabilities | right.Capabilities,
            left.ThrownExceptions.Concat(right.ThrownExceptions).Distinct(StringComparer.Ordinal).ToImmutableArray(),
            left.Sites.AddRange(right.Sites),
            unknowns.Distinct().ToImmutableArray());
    }

    private static bool IsIntrinsicMetadataMethod(IMethodSymbol method) =>
        method is { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object } ||
        method.ContainingType?.SpecialType is SpecialType.System_String or
            SpecialType.System_Boolean or
            SpecialType.System_Char or
            SpecialType.System_SByte or
            SpecialType.System_Byte or
            SpecialType.System_Int16 or
            SpecialType.System_UInt16 or
            SpecialType.System_Int32 or
            SpecialType.System_UInt32 or
            SpecialType.System_Int64 or
            SpecialType.System_UInt64 or
            SpecialType.System_Single or
            SpecialType.System_Double or
            SpecialType.System_Decimal;

    private static void AddWrite(IOperation target, Builder builder) {
        switch (target) {
            case IFieldReferenceOperation { Field.IsStatic: true } field:
                builder.Add(SharpProofEffect.WritesStaticState, field, field.Field, "static_field_write");
                break;
            case IFieldReferenceOperation field:
                builder.Add(GetInstanceWriteEffect(field.Instance, builder), field, field.Field,
                    "instance_field_write");
                break;
            case IPropertyReferenceOperation { Property.IsStatic: true } property:
                builder.Add(SharpProofEffect.WritesStaticState, property, property.Property,
                    "static_property_write");
                break;
            case IPropertyReferenceOperation property:
                builder.Add(GetInstanceWriteEffect(property.Instance, builder), property, property.Property,
                    "instance_property_write");
                break;
            case IArrayElementReferenceOperation array:
                builder.Add(GetInstanceWriteEffect(array.ArrayReference, builder), array, array.Type,
                    "array_element_write");
                break;
        }
    }

    private static SharpProofEffect GetInstanceWriteEffect(IOperation? instance, Builder builder) => instance switch {
        IInstanceReferenceOperation => SharpProofEffect.WritesReceiverState,
        IParameterReferenceOperation => SharpProofEffect.WritesArgumentState,
        IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.WritesStaticState,
        ILocalReferenceOperation local when builder.IsFresh(local.Local) => SharpProofEffect.WritesFreshOwnedState,
        ILocalReferenceOperation => SharpProofEffect.WritesCapturedState,
        _ => SharpProofEffect.Unknown
    };

    private static SharpProofEffect GetInstanceReadEffect(IOperation? instance) => instance switch {
        IInstanceReferenceOperation => SharpProofEffect.ReadsReceiverState,
        IParameterReferenceOperation => SharpProofEffect.ReadsArgumentState,
        IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.ReadsStaticState,
        ILocalReferenceOperation => SharpProofEffect.ReadsCapturedState,
        _ => SharpProofEffect.Unknown
    };

    private static bool IsAllocation(IOperation operation) => operation is
        IObjectCreationOperation or IArrayCreationOperation or IAnonymousObjectCreationOperation or
        IDelegateCreationOperation;

    private static bool IsVisible(IOperation operation, SyntaxNode declaration) {
        for (var current = operation.Syntax; current != null && current != declaration; current = current.Parent)
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                return false;
        return true;
    }

    private MethodEffects AnalyzeMetadata(IMethodSymbol method, SyntaxNode site) {
        if (TryReadEffectContract(method, out var contracted)) return contracted;
        return Unknown("method_body_unavailable", site);
    }

    private MethodEffects Cache(IMethodSymbol method, MethodEffects effects) {
        _cache[method] = effects;
        return effects;
    }

    private static MethodEffects Unknown(string reason, SyntaxNode site) => new(
        SharpProofEffect.Unknown,
        SharpProofCapability.None,
        ImmutableArray<string>.Empty,
        ImmutableArray.Create(new MethodEffectSite(
            SharpProofEffect.Unknown,
            SharpProofCapability.None,
            site.ToString(),
            string.Empty,
            site.SpanStart,
            site.Span.Length,
            false,
            reason)),
        ImmutableArray.Create(CreateUnknownReason(reason)));

    private static SharpProofUnknownReason CreateUnknownReason(string reason) => new(
        "SP-EFFECT-UNKNOWN",
        "Effects",
        reason,
        false,
        false);

    private sealed class Builder {
        private readonly ImmutableArray<string>.Builder _exceptions = ImmutableArray.CreateBuilder<string>();
        private readonly ImmutableArray<MethodEffectSite>.Builder _sites =
            ImmutableArray.CreateBuilder<MethodEffectSite>();
        private readonly ImmutableArray<SharpProofUnknownReason>.Builder _unknowns =
            ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        private SharpProofCapability _capabilities;
        private SharpProofEffect _effects;
        private readonly HashSet<ILocalSymbol> _freshLocals = new(SymbolEqualityComparer.Default);

        internal void MarkFresh(ILocalSymbol local) => _freshLocals.Add(local);

        internal bool IsFresh(ILocalSymbol local) => _freshLocals.Contains(local);

        internal void Add(
            SharpProofEffect effect,
            IOperation operation,
            ISymbol? symbol,
            string reason) => Add(effect, SharpProofCapability.None, operation, symbol, reason);

        internal void Add(
            SharpProofEffect effect,
            SharpProofCapability capabilities,
            IOperation operation,
            ISymbol? symbol,
            string reason) {
            _effects |= effect;
            _capabilities |= capabilities;
            _sites.Add(new MethodEffectSite(
                effect,
                capabilities,
                operation.Syntax.ToString(),
                symbol?.ToDisplayString() ?? string.Empty,
                operation.Syntax.SpanStart,
                operation.Syntax.Span.Length,
                false,
                reason));
        }

        internal void AddException(string type) {
            if (!_exceptions.Contains(type, StringComparer.Ordinal)) _exceptions.Add(type);
        }

        internal void AddUnknown(IOperation operation, string reason, ISymbol? symbol = null) {
            Add(SharpProofEffect.Unknown, operation, symbol, reason);
            _unknowns.Add(CreateUnknownReason(reason));
        }

        internal void AddTransitive(
            MethodEffects effects,
            IOperation site,
            ISymbol symbol,
            string reason) {
            _effects |= effects.Effects;
            _capabilities |= effects.Capabilities;
            foreach (var exception in effects.ThrownExceptions) AddException(exception);
            _unknowns.AddRange(effects.UnknownReasons);
            if (effects.Effects != SharpProofEffect.None || effects.Capabilities != SharpProofCapability.None)
                _sites.Add(new MethodEffectSite(
                    effects.Effects,
                    effects.Capabilities,
                    site.Syntax.ToString(),
                    symbol.ToDisplayString(),
                    site.Syntax.SpanStart,
                    site.Syntax.Span.Length,
                    true,
                    reason));
        }

        internal MethodEffects Build() => new(
            _effects,
            _capabilities,
            _exceptions.Distinct(StringComparer.Ordinal).ToImmutableArray(),
            _sites.ToImmutable(),
            _unknowns.Distinct().ToImmutableArray());
    }
}
