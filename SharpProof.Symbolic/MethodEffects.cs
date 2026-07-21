using SharpProof.Attributes;

namespace SharpProof.Symbolic;

public enum SharpProofVerdict {
    Proven,
    Disproven,
    Unknown
}

public enum MethodEffectOrigin {
    Ambient,
    Receiver,
    Argument,
    Captured,
    Static,
    FreshOwned,
    Allocation,
    Synchronization,
    Native,
    Nondeterminism,
    Exception,
    Call,
    Unknown
}

public enum MethodExceptionSource {
    ExplicitThrow,
    RuntimeHazard,
    Callee,
    Metadata,
    Contract,
    Unknown
}

public sealed record MethodExceptionFact(
    string ExceptionType,
    SharpProofVerdict Escape,
    MethodExceptionSource Source,
    string Operation,
    string Symbol,
    int SpanStart,
    int SpanLength,
    bool IsTransitive,
    string Reason,
    string Kind = "") {
    public static MethodExceptionFact Boundary(
        string exceptionType,
        MethodExceptionSource source,
        string reason,
        SharpProofVerdict escape = SharpProofVerdict.Proven) => new(
        exceptionType,
        escape,
        source,
        string.Empty,
        string.Empty,
        0,
        0,
        true,
        reason);
}

public sealed record MethodEffectSite(
    SharpProofEffect Effect,
    SharpProofCapability Capabilities,
    string Operation,
    string Symbol,
    int SpanStart,
    int SpanLength,
    bool IsTransitive,
    string Reason,
    MethodEffectOrigin Origin = MethodEffectOrigin.Unknown,
    string? ExceptionType = null,
    string? TransitiveSource = null,
    SharpProofVerdict EscapeStatus = SharpProofVerdict.Unknown,
    SharpProofVerdict ProofStatus = SharpProofVerdict.Proven);

public sealed record MethodEffects(
    SharpProofEffect Effects,
    SharpProofCapability Capabilities,
    ImmutableArray<MethodExceptionFact> ExceptionFacts,
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

    public ImmutableArray<string> ThrownExceptions => ExceptionFacts
        .Where(static fact => fact.Escape == SharpProofVerdict.Proven)
        .Select(static fact => fact.ExceptionType)
        .Distinct(StringComparer.Ordinal)
        .ToImmutableArray();

    public SharpProofVerdict DoesNotThrow {
        get {
            if (ExceptionFacts.Any(static fact => fact.Escape == SharpProofVerdict.Proven))
                return SharpProofVerdict.Disproven;
            if (ExceptionFacts.Any(static fact => fact.Escape == SharpProofVerdict.Unknown))
                return SharpProofVerdict.Unknown;
            return (Effects & SharpProofEffect.Unknown) != 0 || !UnknownReasons.IsDefaultOrEmpty
                ? SharpProofVerdict.Unknown
                : SharpProofVerdict.Proven;
        }
    }

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
    Func<IMethodSymbol, MethodEffects?>? externalContractResolver = null,
    SmtAnalysisService? smtAnalysis = null) {
    private readonly HashSet<IMethodSymbol> _active = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, MethodEffects> _cache = new(SymbolEqualityComparer.Default);
    private readonly MetadataMethodEffectAnalyzer _metadata = new(compilation);
    private readonly object _gate = new();

    internal MethodEffects Analyze(
        IMethodSymbol method,
        SyntaxNode declaration,
        SemanticModel semanticModel) {
        lock (_gate) return AnalyzeCore(method, declaration, semanticModel);
    }

    private MethodEffects AnalyzeCore(
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

            var builder = new Builder(IsCaught);
            foreach (var operation in root.DescendantsAndSelf())
                if (operation is IVariableDeclaratorOperation { Symbol: var local, Initializer.Value: var value }) {
                    if (value is IObjectCreationOperation or IArrayCreationOperation or IAnonymousObjectCreationOperation or IDelegateCreationOperation) builder.MarkFresh(local);
                    builder.MarkExactType(local, value.Type);
                    builder.MarkDelegateTargets(local, value);
                }
            foreach (var operation in root.DescendantsAndSelf()) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsVisible(operation, declaration, semanticModel)) continue;
                AnalyzeOperation(operation, semanticModel, builder);
            }

            if (smtAnalysis != null)
                AddRuntimeHazards(root, declaration, semanticModel, smtAnalysis, builder);

            return Cache(method, builder.Build());
        }
        finally {
            _active.Remove(method);
        }
    }

    private void AddRuntimeHazards(
        IOperation root,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        SmtAnalysisService analysis,
        Builder builder) {
        var hazards = new SymbolicRuntimeHazardQueryService().QueryNodeRuntimeHazards(
            declaration,
            semanticModel,
            analysis,
            cancellationToken,
            new SymbolicRuntimeHazardQueryOptions(includeUnprovenCandidates: true));
        foreach (var hazard in hazards.Hazards) {
            if (hazard.Kind == SymbolicRuntimeHazardKind.DirectThrow &&
                hazard.Category.IndexOf("throw_null", StringComparison.Ordinal) < 0)
                continue;
            var hazardSpan = TextSpan.FromBounds(hazard.SpanStart, hazard.SpanEnd);
            var syntaxSite = declaration.DescendantNodesAndSelf()
                .Where(candidate => candidate.Span.Contains(hazardSpan))
                .OrderBy(static candidate => candidate.Span.Length)
                                .FirstOrDefault() ?? declaration;
            if (hazard.Kind == SymbolicRuntimeHazardKind.DirectThrow)
                syntaxSite = syntaxSite.AncestorsAndSelf().OfType<ThrowStatementSyntax>().FirstOrDefault() ?? syntaxSite;
            var operation = root.DescendantsAndSelf().FirstOrDefault(candidate =>
                                candidate.Syntax.SpanStart == hazard.SpanStart &&
                                candidate.Syntax.Span.End == hazard.SpanEnd) ??
                            root.DescendantsAndSelf()
                                .Where(candidate => candidate.Syntax.Span.Contains(
                                    hazardSpan))
                                .OrderBy(static candidate => candidate.Syntax.Span.Length)
                                .FirstOrDefault() ?? root;
            var escape = hazard.Status switch {
                SymbolicRuntimeHazardStatus.Proven => SharpProofVerdict.Proven,
                SymbolicRuntimeHazardStatus.Unreachable => SharpProofVerdict.Disproven,
                _ => SharpProofVerdict.Unknown
            };
            if (escape == SharpProofVerdict.Proven && IsCaught(syntaxSite, hazard.ExceptionType))
                escape = SharpProofVerdict.Disproven;
            builder.AddRuntimeHazard(
                hazard.ExceptionType,
                syntaxSite,
                MethodExceptionSource.RuntimeHazard,
                escape,
                hazard.Category,
                hazard.Kind.ToString());
        }
    }

    private void AnalyzeOperation(
        IOperation operation,
        SemanticModel semanticModel,
        Builder builder) {
        switch (operation) {
            case ISimpleAssignmentOperation assignment:
                AddWrite(assignment.Target, builder);
                if (assignment.Target is IPropertyReferenceOperation { Property.SetMethod: not null } propertyTarget)
                    AnalyzeCall(propertyTarget.Property.SetMethod, assignment, builder);
                break;
            case ICompoundAssignmentOperation compound:
                AddWrite(compound.Target, builder);
                if (compound.Target is IPropertyReferenceOperation compoundProperty)
                    AnalyzeCall(compoundProperty.Property.SetMethod, compound, builder);
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
                AnalyzeCall(property.Property.GetMethod, property, builder, property.Instance);
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
                if (IsNullConstant(thrown.Exception)) break;
                var thrownType = thrown.Exception is IConversionOperation thrownConversion
                    ? thrownConversion.Operand.Type ?? thrownConversion.Type
                    : thrown.Exception?.Type;
                builder.AddException(
                    thrownType,
                    thrown,
                    MethodExceptionSource.ExplicitThrow,
                    IsCaught(thrown, thrownType?.ToDisplayString() ?? "System.Exception")
                        ? SharpProofVerdict.Disproven
                        : SharpProofVerdict.Proven,
                    "explicit_throw");
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
                if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                    invocation.Instance is ILocalReferenceOperation delegateLocal &&
                    builder.GetDelegateTargets(delegateLocal.Local) is { Length: > 0 } targets) {
                    foreach (var target in targets) AnalyzeCall(target, invocation, builder);
                }
                else
                    AnalyzeCall(invocation.TargetMethod, invocation, builder, invocation.Instance);
                break;
            case IBinaryOperation { OperatorMethod: not null } binary:
                AnalyzeCall(binary.OperatorMethod, binary, builder);
                break;
            case IUnaryOperation { OperatorMethod: not null } unary:
                AnalyzeCall(unary.OperatorMethod, unary, builder);
                break;
            case IConversionOperation { Conversion.IsUserDefined: true } userConversion:
                AnalyzeCall(userConversion.Conversion.MethodSymbol, userConversion, builder);
                break;
            case IForEachLoopOperation { Syntax: CommonForEachStatementSyntax syntax } loop:
                var info = semanticModel.GetForEachStatementInfo(syntax);
                AnalyzeCall(info.GetEnumeratorMethod, loop, builder);
                AnalyzeCall(info.MoveNextMethod, loop, builder);
                AnalyzeCall(info.CurrentProperty?.GetMethod, loop, builder);
                AnalyzeCall(info.DisposeMethod, loop, builder);
                break;
            case IUsingOperation usingOperation:
                AnalyzeDisposal(usingOperation.Resources.Type, usingOperation, builder);
                break;
            case IUsingDeclarationOperation usingDeclaration:
                foreach (var declarator in usingDeclaration.DeclarationGroup.Declarations
                             .SelectMany(static declaration => declaration.Declarators))
                    AnalyzeDisposal(declarator.Symbol.Type, usingDeclaration, builder);
                break;
            case IEventAssignmentOperation { EventReference: IEventReferenceOperation eventReference } eventAssignment:
                builder.Add(eventReference.Event.IsStatic
                        ? SharpProofEffect.WritesStaticState
                        : GetInstanceWriteEffect(eventReference.Instance, builder),
                    eventAssignment,
                    eventReference.Event,
                    "event_assignment");
                AnalyzeCall(eventAssignment.Adds
                    ? eventReference.Event.AddMethod
                    : eventReference.Event.RemoveMethod, eventAssignment, builder, eventReference.Instance);
                break;
            case IFunctionPointerInvocationOperation:
                builder.AddUnknown(operation, "function_pointer_dispatch");
                break;
            case ITypeParameterObjectCreationOperation typeParameterCreation:
                builder.Add(SharpProofEffect.Allocates, typeParameterCreation, typeParameterCreation.Type,
                    "generic_object_allocation");
                builder.AddUnknown(typeParameterCreation, "generic_constructor_dispatch");
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
        Builder builder,
        IOperation? receiver = null) {
        if (method == null) {
            builder.AddUnknown(site, "unresolved_call");
            return;
        }

        method = (method.ReducedFrom ?? method).OriginalDefinition;
        var exactDispatchTarget = ResolveExactDispatchTarget(method, receiver, builder);
        method = exactDispatchTarget ?? method;
        builder.Add(SharpProofEffect.DirectCall, site, method, "direct_call");
        if (method.IsImplicitlyDeclared) return;
        if (exactDispatchTarget == null &&
            (method.IsVirtual || method.ContainingType?.TypeKind == TypeKind.Interface)) {
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
            if (method is { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object } ||
                SymbolicTypeFacts.IsBuiltInIntegralType(method.ContainingType) ||
                method.ContainingType?.SpecialType is SpecialType.System_String or
                    SpecialType.System_Boolean or
                    SpecialType.System_Single or
                    SpecialType.System_Double or
                    SpecialType.System_Decimal ||
                method.ContainingType?.OriginalDefinition.ToDisplayString() is
                    "System.Span<T>" or
                    "System.ReadOnlySpan<T>" or
                    "System.Nullable<T>" or
                    "System.Index" or
                    "System.Range")
                return;
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

    private static IMethodSymbol? ResolveExactDispatchTarget(
        IMethodSymbol method,
        IOperation? receiver,
        Builder builder) {
        if (!method.IsVirtual && method.ContainingType?.TypeKind != TypeKind.Interface) return method;
        var exactType = receiver switch {
            IObjectCreationOperation { Type: INamedTypeSymbol created } => created,
            IConversionOperation { Operand.Type: INamedTypeSymbol converted } => converted,
            ILocalReferenceOperation local => builder.GetExactType(local.Local),
            _ => receiver?.Type as INamedTypeSymbol
        };
        if (exactType == null || exactType.TypeKind == TypeKind.Interface || exactType.IsAbstract) return null;
        if (method.ContainingType?.TypeKind == TypeKind.Interface)
            return exactType.FindImplementationForInterfaceMember(method) as IMethodSymbol;
        return exactType.GetMembers(method.Name).OfType<IMethodSymbol>()
            .FirstOrDefault(candidate => Overrides(candidate, method));
    }

    private static bool Overrides(IMethodSymbol candidate, IMethodSymbol method) {
        for (var current = candidate; current != null; current = current.OverriddenMethod)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, method.OriginalDefinition))
                return true;
        return false;
    }

    private void AnalyzeDisposal(ITypeSymbol? type, IOperation site, Builder builder) {
        if (type is not INamedTypeSymbol named) return;
        var disposable = compilation.GetTypeByMetadataName("System.IDisposable");
        var member = disposable?.GetMembers("Dispose").OfType<IMethodSymbol>().FirstOrDefault();
        var implementation = member == null ? null : named.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        implementation ??= named.GetMembers("Dispose").OfType<IMethodSymbol>()
            .FirstOrDefault(static method => !method.IsStatic && method.Parameters.Length == 0);
        if (implementation != null) AnalyzeCall(implementation, site, builder);
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
                exceptions.Select(static type => MethodExceptionFact.Boundary(
                    type,
                    MethodExceptionSource.Contract,
                    "effect_contract")).ToImmutableArray(),
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
            left.ExceptionFacts.AddRange(right.ExceptionFacts).Distinct().ToImmutableArray(),
            left.Sites.AddRange(right.Sites),
            unknowns.Distinct().ToImmutableArray());
    }

    private static void AddWrite(IOperation target, Builder builder) {
        if (target.Syntax.Ancestors().Any(static syntax =>
                syntax is InitializerExpressionSyntax or WithExpressionSyntax or
                    AnonymousObjectCreationExpressionSyntax)) {
            builder.Add(SharpProofEffect.WritesFreshOwnedState, target, target.Type, "fresh_owned_write");
            return;
        }
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
        { Type.SpecialType: SpecialType.System_String } => SharpProofEffect.None,
        { Type.IsValueType: true } => SharpProofEffect.None,
        IInstanceReferenceOperation => SharpProofEffect.ReadsReceiverState,
        IParameterReferenceOperation => SharpProofEffect.ReadsArgumentState,
        IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.ReadsStaticState,
        ILocalReferenceOperation => SharpProofEffect.ReadsCapturedState,
        _ => SharpProofEffect.Unknown
    };

    private static MethodEffectOrigin GetOrigin(SharpProofEffect effect) {
        if ((effect & (SharpProofEffect.ReadsAmbientState | SharpProofEffect.WritesAmbientState)) != 0)
            return MethodEffectOrigin.Ambient;
        if ((effect & (SharpProofEffect.ReadsReceiverState | SharpProofEffect.WritesReceiverState)) != 0)
            return MethodEffectOrigin.Receiver;
        if ((effect & (SharpProofEffect.ReadsArgumentState | SharpProofEffect.WritesArgumentState)) != 0)
            return MethodEffectOrigin.Argument;
        if ((effect & (SharpProofEffect.ReadsCapturedState | SharpProofEffect.WritesCapturedState)) != 0)
            return MethodEffectOrigin.Captured;
        if ((effect & (SharpProofEffect.ReadsStaticState | SharpProofEffect.WritesStaticState)) != 0)
            return MethodEffectOrigin.Static;
        if ((effect & SharpProofEffect.WritesFreshOwnedState) != 0) return MethodEffectOrigin.FreshOwned;
        if ((effect & SharpProofEffect.Allocates) != 0) return MethodEffectOrigin.Allocation;
        if ((effect & SharpProofEffect.Synchronizes) != 0) return MethodEffectOrigin.Synchronization;
        if ((effect & SharpProofEffect.UsesNativeCode) != 0) return MethodEffectOrigin.Native;
        if ((effect & SharpProofEffect.UsesNondeterminism) != 0) return MethodEffectOrigin.Nondeterminism;
        if ((effect & (SharpProofEffect.DirectCall | SharpProofEffect.DispatchUncertainty)) != 0)
            return MethodEffectOrigin.Call;
        return MethodEffectOrigin.Unknown;
    }

    private bool IsCaught(IOperation operation, string exceptionTypeName) {
        var exceptionType = compilation.GetTypeByMetadataName(exceptionTypeName) ?? operation switch {
            IThrowOperation thrown => thrown.Exception?.Type,
            _ => null
        };
        return exceptionType != null && IsCaught(operation.Syntax, exceptionType);
    }

    private bool IsCaught(SyntaxNode site, string exceptionTypeName) {
        var exceptionType = compilation.GetTypeByMetadataName(exceptionTypeName);
        return exceptionType != null && IsCaught(site, exceptionType);
    }

    private bool IsCaught(SyntaxNode site, ITypeSymbol exceptionType) {
        foreach (var tryStatement in site.Ancestors().OfType<TryStatementSyntax>()) {
            if (!tryStatement.Block.Span.Contains(site.Span)) continue;
            foreach (var clause in tryStatement.Catches) {
                if (clause.Filter != null) continue;
                if (clause.Declaration?.Type == null) return true;
                var caughtType = compilation.GetSemanticModel(clause.SyntaxTree)
                    .GetTypeInfo(clause.Declaration.Type, cancellationToken).Type;
                if (caughtType != null && compilation.ClassifyConversion(exceptionType, caughtType).IsImplicit)
                    return true;
            }
        }
        return false;
    }

    private static bool IsNullConstant(IOperation? operation) => operation switch {
        { ConstantValue: { HasValue: true, Value: null } } => true,
        IConversionOperation conversion => IsNullConstant(conversion.Operand),
        _ => false
    };

    private static bool IsVisible(
        IOperation operation,
        SyntaxNode declaration,
        SemanticModel semanticModel) {
        for (var current = operation.Syntax; current != null && current != declaration; current = current.Parent)
            if (current is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                return false;
            else if (current.Parent is IfStatementSyntax conditional &&
                     semanticModel.GetConstantValue(conditional.Condition).Value is bool condition &&
                     ((conditional.Statement.Span.Contains(operation.Syntax.Span) && !condition) ||
                      (conditional.Else?.Statement.Span.Contains(operation.Syntax.Span) == true && condition)))
                return false;
            else if (current.Parent is ConditionalExpressionSyntax choice &&
                     semanticModel.GetConstantValue(choice.Condition).Value is bool chooseTrue &&
                     ((choice.WhenTrue.Span.Contains(operation.Syntax.Span) && !chooseTrue) ||
                      (choice.WhenFalse.Span.Contains(operation.Syntax.Span) && chooseTrue)))
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
        ImmutableArray.Create(new MethodExceptionFact(
            "System.Exception",
            SharpProofVerdict.Unknown,
            MethodExceptionSource.Unknown,
            site.ToString(),
            string.Empty,
            site.SpanStart,
            site.Span.Length,
            false,
            reason)),
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

    sealed class Builder(Func<IOperation, string, bool> isCaught) {
        private readonly ImmutableArray<MethodExceptionFact>.Builder _exceptions =
            ImmutableArray.CreateBuilder<MethodExceptionFact>();
        private readonly ImmutableArray<MethodEffectSite>.Builder _sites =
            ImmutableArray.CreateBuilder<MethodEffectSite>();
        private readonly ImmutableArray<SharpProofUnknownReason>.Builder _unknowns =
            ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        private SharpProofCapability _capabilities;
        private SharpProofEffect _effects;
        private readonly HashSet<ILocalSymbol> _freshLocals = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, INamedTypeSymbol> _exactTypes = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, ImmutableArray<IMethodSymbol>> _delegateTargets =
            new(SymbolEqualityComparer.Default);

        internal void MarkFresh(ILocalSymbol local) => _freshLocals.Add(local);

        internal bool IsFresh(ILocalSymbol local) => _freshLocals.Contains(local);

        internal void MarkExactType(ILocalSymbol local, ITypeSymbol? type) {
            if (type is INamedTypeSymbol { TypeKind: not (TypeKind.Interface or TypeKind.Dynamic), IsAbstract: false } named)
                _exactTypes[local] = named;
        }

        internal INamedTypeSymbol? GetExactType(ILocalSymbol local) =>
            _exactTypes.TryGetValue(local, out var type) ? type : null;

        internal void MarkDelegateTargets(ILocalSymbol local, IOperation value) {
            var methods = value.DescendantsAndSelf()
                .OfType<IMethodReferenceOperation>()
                .Select(static reference => reference.Method.OriginalDefinition)
                .Concat(value.DescendantsAndSelf()
                    .OfType<IAnonymousFunctionOperation>()
                    .Select(static function => function.Symbol.OriginalDefinition))
                .ToImmutableArray();
            if (!methods.IsDefaultOrEmpty) _delegateTargets[local] = methods;
        }

        internal ImmutableArray<IMethodSymbol> GetDelegateTargets(ILocalSymbol local) =>
            _delegateTargets.TryGetValue(local, out var methods) ? methods : ImmutableArray<IMethodSymbol>.Empty;

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
                reason,
                GetOrigin(effect)));
        }

        internal void AddException(
            ITypeSymbol? type,
            IOperation operation,
            MethodExceptionSource source,
            SharpProofVerdict escape,
            string reason) {
            var exceptionType = type?.ToDisplayString() ?? "System.Exception";
            if (escape == SharpProofVerdict.Proven) _effects |= SharpProofEffect.Throws;
            _exceptions.Add(new MethodExceptionFact(
                exceptionType,
                escape,
                source,
                operation.Syntax.ToString(),
                type?.ToDisplayString() ?? string.Empty,
                operation.Syntax.SpanStart,
                operation.Syntax.Span.Length,
                false,
                reason));
        }

        internal void AddRuntimeHazard(
            string exceptionType,
            SyntaxNode syntaxSite,
            MethodExceptionSource source,
            SharpProofVerdict escape,
            string reason,
            string kind) {
            if (reason.IndexOf("throw_null", StringComparison.Ordinal) >= 0)
                for (var index = _exceptions.Count - 1; index >= 0; index--)
                    if (_exceptions[index].Source == MethodExceptionSource.ExplicitThrow &&
                        syntaxSite.Span.OverlapsWith(new TextSpan(
                            _exceptions[index].SpanStart,
                            _exceptions[index].SpanLength)))
                        _exceptions.RemoveAt(index);
            if (escape == SharpProofVerdict.Proven) _effects |= SharpProofEffect.Throws;
            _exceptions.Add(new MethodExceptionFact(
                exceptionType,
                escape,
                source,
                syntaxSite.ToString(),
                exceptionType,
                syntaxSite.SpanStart,
                syntaxSite.Span.Length,
                false,
                reason,
                kind));
        }

        internal void AddException(
            string exceptionType,
            IOperation operation,
            MethodExceptionSource source,
            SharpProofVerdict escape,
            string reason,
            string kind = "") {
            if (escape == SharpProofVerdict.Proven) _effects |= SharpProofEffect.Throws;
            _exceptions.Add(new MethodExceptionFact(
                exceptionType,
                escape,
                source,
                operation.Syntax.ToString(),
                exceptionType,
                operation.Syntax.SpanStart,
                operation.Syntax.Span.Length,
                false,
                reason,
                kind));
        }

        internal void AddUnknown(IOperation operation, string reason, ISymbol? symbol = null) {
            Add(SharpProofEffect.Unknown, operation, symbol, reason);
            _unknowns.Add(CreateUnknownReason(reason));
            _exceptions.Add(new MethodExceptionFact(
                "System.Exception",
                SharpProofVerdict.Unknown,
                MethodExceptionSource.Unknown,
                operation.Syntax.ToString(),
                symbol?.ToDisplayString() ?? string.Empty,
                operation.Syntax.SpanStart,
                operation.Syntax.Span.Length,
                false,
                reason));
        }

        internal void AddTransitive(
            MethodEffects effects,
            IOperation site,
            ISymbol symbol,
            string reason) {
            _effects |= effects.Effects;
            _capabilities |= effects.Capabilities;
            foreach (var exception in effects.ExceptionFacts) {
                var escape = exception.Escape == SharpProofVerdict.Proven && isCaught(site, exception.ExceptionType)
                    ? SharpProofVerdict.Disproven
                    : exception.Escape;
                _exceptions.Add(exception with {
                    Escape = escape,
                    Source = MethodExceptionSource.Callee,
                    Operation = site.Syntax.ToString(),
                    Symbol = symbol.ToDisplayString(),
                    SpanStart = site.Syntax.SpanStart,
                    SpanLength = site.Syntax.Span.Length,
                    IsTransitive = true,
                    Reason = exception.Reason
                });
            }
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
                    reason,
                    GetOrigin(effects.Effects),
                    effects.ThrownExceptions.FirstOrDefault(),
                    symbol.ToDisplayString()));
        }

        internal MethodEffects Build() => new(
            _effects,
            _capabilities,
            _exceptions.Distinct().ToImmutableArray(),
            _sites.ToImmutable(),
            _unknowns.Distinct().ToImmutableArray());
    }
}
