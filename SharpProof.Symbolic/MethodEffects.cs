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
        SharpProofEffect.WritesAmbientState |
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
    public ImmutableArray<string> ThrownExceptions => [.. ExceptionFacts
        .Where(static fact => fact.Escape == SharpProofVerdict.Proven)
        .Select(static fact => fact.ExceptionType)
        .Distinct(StringComparer.Ordinal)];
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
    internal MethodEffects Analyze(IMethodSymbol method, SyntaxNode declaration, SemanticModel semanticModel) {
        lock (_gate) return AnalyzeCore(method, declaration, semanticModel);
    }
    private MethodEffects AnalyzeCore(IMethodSymbol method, SyntaxNode declaration, SemanticModel semanticModel) {
        method = method.OriginalDefinition;
        if (_cache.TryGetValue(method, out var cached)) return cached;
        if (!_active.Add(method)) return Unknown("recursive_call", declaration);
        try {
            var root = MethodBodyOperationResolver.GetMethodBodyRootOperation(declaration, semanticModel, cancellationToken, true);
            if (root == null) return Cache(method, AnalyzeMetadata(method, declaration));
            var builder = new Builder(IsCaught);
            var reachableOperations = GetReachableOperationSpans(declaration, semanticModel);
            MarkFlowUncertainLocals(root, declaration, builder);
            foreach (var operation in root.DescendantsAndSelf()) {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsVisible(operation, declaration, semanticModel)) continue;
                if (reachableOperations is { } reachableSpans &&
                    !reachableSpans.Any(span => span.OverlapsWith(operation.Syntax.Span)))
                    continue;
                if (operation is IVariableDeclaratorOperation { Symbol: var local, Initializer.Value: var value })
                    builder.AssignLocal(local, value);
                AnalyzeOperation(operation, semanticModel, builder);
            }
            AddCompilerGeneratedAllocations(declaration, semanticModel, builder, reachableOperations);
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
                                .Where(candidate => candidate.Syntax.Span.Contains(hazardSpan))
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
    private void AnalyzeOperation(IOperation operation, SemanticModel semanticModel, Builder builder) {
        switch (operation) {
            case ISimpleAssignmentOperation assignment:
                AddWrite(assignment.Target, builder);
                if (assignment.Target is ILocalReferenceOperation assignedLocal)
                    builder.AssignLocal(assignedLocal.Local, assignment.Value);
                if (assignment.Target is IPropertyReferenceOperation { Property.SetMethod: not null } propertyTarget)
                    AnalyzeCall(propertyTarget.Property.SetMethod, assignment, builder, propertyTarget.Instance);
                if (assignment.Target is IImplicitIndexerReferenceOperation implicitIndexerTarget)
                    AnalyzeImplicitIndexerAccess(implicitIndexerTarget, assignment, builder, reads: false, writes: true);
                break;
            case ICoalesceAssignmentOperation coalesceAssignment:
                AddWrite(coalesceAssignment.Target, builder);
                if (coalesceAssignment.Target is ILocalReferenceOperation coalescedLocal)
                    builder.AssignLocal(coalescedLocal.Local, coalesceAssignment.Value);
                if (coalesceAssignment.Target is IPropertyReferenceOperation coalescedProperty) {
                    AnalyzeCall(coalescedProperty.Property.GetMethod, coalesceAssignment, builder,
                        coalescedProperty.Instance);
                    AnalyzeCall(coalescedProperty.Property.SetMethod, coalesceAssignment, builder,
                        coalescedProperty.Instance);
                }
                if (coalesceAssignment.Target is IImplicitIndexerReferenceOperation coalescedIndexer)
                    AnalyzeImplicitIndexerAccess(coalescedIndexer, coalesceAssignment, builder, reads: true, writes: true);
                break;
            case IDeconstructionAssignmentOperation deconstruction:
                AnalyzeDeconstructionTarget(deconstruction.Target, builder);
                break;
            case ICompoundAssignmentOperation compound:
                AddWrite(compound.Target, builder);
                if (compound.Target is ILocalReferenceOperation compoundLocal &&
                    compoundLocal.Type?.TypeKind == TypeKind.Delegate)
                    builder.ApplyDelegateCompoundAssignment(
                        compoundLocal.Local,
                        compound.Value,
                        compound.OperatorKind == BinaryOperatorKind.Add);
                if (compound.Target is IPropertyReferenceOperation compoundProperty) {
                    AnalyzeCall(compoundProperty.Property.GetMethod, compound, builder, compoundProperty.Instance);
                    AnalyzeCall(compoundProperty.Property.SetMethod, compound, builder, compoundProperty.Instance);
                }
                if (compound.Target is IImplicitIndexerReferenceOperation compoundIndexer)
                    AnalyzeImplicitIndexerAccess(compoundIndexer, compound, builder, reads: true, writes: true);
                if (compound.OperatorMethod != null) AnalyzeCall(compound.OperatorMethod, compound, builder);
                break;
            case IIncrementOrDecrementOperation increment:
                AddWrite(increment.Target, builder);
                if (increment.Target is IPropertyReferenceOperation incrementProperty) {
                    AnalyzeCall(incrementProperty.Property.GetMethod, increment, builder, incrementProperty.Instance);
                    AnalyzeCall(incrementProperty.Property.SetMethod, increment, builder, incrementProperty.Instance);
                }
                if (increment.Target is IImplicitIndexerReferenceOperation incrementIndexer)
                    AnalyzeImplicitIndexerAccess(incrementIndexer, increment, builder, reads: true, writes: true);
                if (increment.OperatorMethod != null) AnalyzeCall(increment.OperatorMethod, increment, builder);
                break;
            case IFieldReferenceOperation { Field.IsConst: false, Field.IsStatic: true } field
                when field.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, field):
                builder.Add(SharpProofEffect.ReadsStaticState, field, field.Field, "static_field_read");
                break;
            case IFieldReferenceOperation field
                when field.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, field):
                builder.Add(GetInstanceReadEffect(field.Instance, builder), field, field.Field, "instance_field_read");
                break;
            case IPropertyReferenceOperation property
                when property.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, property):
                builder.Add(property.Property.IsStatic
                        ? SharpProofEffect.ReadsStaticState
                        : GetInstanceReadEffect(property.Instance, builder),
                    property,
                    property.Property,
                    "property_read");
                AnalyzeCall(property.Property.GetMethod, property, builder, property.Instance);
                break;
            case IInlineArrayAccessOperation inlineArray
                when inlineArray.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, inlineArray):
                builder.Add(GetInstanceReadEffect(inlineArray.Instance, builder), inlineArray, inlineArray.Type,
                    "inline_array_read");
                break;
            case IImplicitIndexerReferenceOperation implicitIndexer
                when implicitIndexer.Parent is not IAssignmentOperation { Target: var target } ||
                     !ReferenceEquals(target, implicitIndexer):
                builder.Add(GetInstanceReadEffect(implicitIndexer.Instance, builder), implicitIndexer,
                    implicitIndexer.Type, "implicit_indexer_read");
                AnalyzeImplicitIndexerAccess(implicitIndexer, implicitIndexer, builder, reads: true, writes: false);
                break;
            case var pointer when IsPointerIndirection(pointer) &&
                                  pointer.ChildOperations.FirstOrDefault() is { } pointerOperand &&
                                  (pointer.Parent is not IAssignmentOperation { Target: var target } ||
                                   !ReferenceEquals(target, pointer)):
                builder.Add(GetInstanceReadEffect(pointerOperand, builder), pointer, pointer.Type,
                    "pointer_indirection_read");
                break;
            case IObjectCreationOperation creation:
                builder.Add(SharpProofEffect.Allocates, creation, creation.Constructor, "object_allocation");
                AnalyzeCall(creation.Constructor, creation, builder);
                break;
            case IArrayCreationOperation array:
                builder.Add(SharpProofEffect.Allocates, array, array.Type, "array_allocation");
                break;
            case { Kind: OperationKind.CollectionExpression, Type: IArrayTypeSymbol } collection:
                builder.Add(SharpProofEffect.Allocates, collection, collection.Type, "array_collection_allocation");
                break;
            case IAnonymousObjectCreationOperation anonymousObject:
                builder.Add(SharpProofEffect.Allocates, anonymousObject, anonymousObject.Type, "anonymous_object_allocation");
                break;
            case IDelegateCreationOperation delegateCreation:
                builder.Add(SharpProofEffect.Allocates, delegateCreation, delegateCreation.Type, "delegate_allocation");
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
                builder.Add(SharpProofEffect.Synchronizes, SharpProofCapability.Synchronization, locked, null, "synchronization");
                break;
            case IInvocationOperation invocation:
                var preservesFreshArguments = true;
                if (invocation.TargetMethod.MethodKind == MethodKind.DelegateInvoke &&
                    builder.GetDelegateTargets(invocation.Instance) is { Length: > 0 } targets) {
                    foreach (var target in targets)
                        preservesFreshArguments &= AnalyzeCall(
                            target.Method,
                            invocation,
                            builder,
                            target.Receiver,
                            target.ReceiverReadEffect,
                            target.ReceiverWriteEffect,
                            target.CapturedReadEffect,
                            target.CapturedWriteEffect);
                }
                else
                    preservesFreshArguments = AnalyzeCall(invocation.TargetMethod, invocation, builder, invocation.Instance);
                builder.MarkEscapedArguments(invocation.Arguments, preservesFreshArguments);
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
                builder.Add(SharpProofEffect.Allocates, typeParameterCreation, typeParameterCreation.Type, "generic_object_allocation");
                builder.AddUnknown(typeParameterCreation, "generic_constructor_dispatch");
                break;
            case IDynamicInvocationOperation or IDynamicIndexerAccessOperation or
                IDynamicMemberReferenceOperation or IDynamicObjectCreationOperation:
                builder.AddUnknown(operation, "dynamic_dispatch");
                break;
        }
    }
    private void AnalyzeDeconstructionTarget(IOperation target, Builder builder) {
        while (target is IConversionOperation conversion) target = conversion.Operand;
        if (target is ITupleOperation tuple) {
            foreach (var element in tuple.Elements) AnalyzeDeconstructionTarget(element, builder);
            return;
        }
        AddWrite(target, builder);
        if (target is IPropertyReferenceOperation { Property.SetMethod: not null } property)
            AnalyzeCall(property.Property.SetMethod, property, builder, property.Instance);
        if (target is IImplicitIndexerReferenceOperation implicitIndexer)
            AnalyzeImplicitIndexerAccess(implicitIndexer, implicitIndexer, builder, reads: false, writes: true);
    }
    private void AnalyzeImplicitIndexerAccess(
        IImplicitIndexerReferenceOperation indexer,
        IOperation site,
        Builder builder,
        bool reads,
        bool writes) {
        if (indexer.LengthSymbol is IPropertySymbol length)
            AnalyzeCall(length.GetMethod, site, builder, indexer.Instance);
        switch (indexer.IndexerSymbol) {
            case IPropertySymbol property:
                if (reads) AnalyzeCall(property.GetMethod, site, builder, indexer.Instance);
                if (writes) AnalyzeCall(property.SetMethod, site, builder, indexer.Instance);
                break;
            case IMethodSymbol method when reads:
                AnalyzeCall(method, site, builder, indexer.Instance);
                break;
        }
    }
    private bool AnalyzeCall(
        IMethodSymbol? method,
        IOperation site,
        Builder builder,
        IOperation? receiver = null,
        SharpProofEffect? receiverReadEffect = null,
        SharpProofEffect? receiverWriteEffect = null,
        SharpProofEffect? capturedReadEffect = null,
        SharpProofEffect? capturedWriteEffect = null) {
        if (method == null) {
            builder.AddUnknown(site, "unresolved_call");
            return false;
        }
        method = (method.ReducedFrom ?? method).OriginalDefinition;
        var knownExactReceiverType = receiver is ILocalReferenceOperation localReceiver
            ? builder.GetExactType(localReceiver.Local)
            : null;
        var exactDispatchTarget = SymbolicDispatchFacts.ResolveExactDispatchTarget(
            method,
            receiver,
            knownExactReceiverType);
        method = exactDispatchTarget ?? method;
        builder.Add(SharpProofEffect.DirectCall, site, method, "direct_call");
        if (method.IsImplicitlyDeclared) return true;
        if (exactDispatchTarget == null &&
            (method.IsVirtual || method.ContainingType?.TypeKind == TypeKind.Interface)) {
            builder.Add(SharpProofEffect.DispatchUncertainty, site, method, "dispatch_uncertainty");
            builder.AddUnknown(site, "unresolved_dispatch", method);
            return false;
        }
        if (IsBodylessAutoPropertyAccessor(method)) return false;
        var hasContract = TryReadEffectContract(method, out var contracted);
        var configured = externalContractResolver?.Invoke(method);
        if (configured != null) {
            contracted = hasContract ? UnionContracts(contracted, configured) : configured;
            hasContract = true;
        }
        if (method.GetDllImportData() != null) {
            builder.Add(SharpProofEffect.UsesNativeCode, SharpProofCapability.NativeInterop, site, method, "native_call");
            if (hasContract && IsCompleteContract(contracted))
                AddCallEffects(contracted, site, method, "complete_native_effect_contract", receiver, builder,
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect);
            else
                builder.AddUnknown(site, "native_exception_boundary", method);
            return false;
        }
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        if (syntax == null) {
            if (IsStructurallyEffectFreeIntrinsic(method)) return true;
            if (TryGetKnownFrameworkSummary(method, out var frameworkSummary)) {
                var remappedFramework = AddCallEffects(
                    frameworkSummary, site, method, "framework_method_model", receiver, builder,
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect);
                return CanPreserveFreshArguments(
                    remappedFramework, site, receiver, builder, receiverWriteEffect);
            }
            var metadata = _metadata.Analyze(method);
            if (hasContract && metadata.Effects == SharpProofEffect.Unknown)
                AddCallEffects(contracted, site, method, "complete_effect_contract", receiver, builder,
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect);
            else {
                AddCallEffects(metadata, site, method, "metadata_call", receiver, builder,
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect);
                if (hasContract)
                    AddCallEffects(contracted, site, method, "effect_contract", receiver, builder,
                        receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect);
            }
            return false;
        }
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        if (method.MethodKind == MethodKind.LocalFunction &&
            model.GetOperation(syntax, cancellationToken) is ILocalFunctionOperation localFunction &&
            localFunction.Body is { } localFunctionBody &&
            builder.TryGetCapturedEffects(
                localFunction,
                localFunctionBody,
                localFunction.Symbol,
                out var localCapturedReadEffect,
                out var localCapturedWriteEffect)) {
            capturedReadEffect = localCapturedReadEffect;
            capturedWriteEffect = localCapturedWriteEffect;
        }
        var remappedSource = AddCallEffects(
            Analyze(method, syntax, model), site, method, "source_call", receiver, builder,
            receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect);
        var preservesFresh = CanPreserveFreshArguments(
            remappedSource, site, receiver, builder, receiverWriteEffect);
        if (hasContract) {
            var remappedContract = AddCallEffects(
                contracted, site, method, "effect_contract", receiver, builder,
                receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect);
            preservesFresh &= CanPreserveFreshArguments(
                remappedContract, site, receiver, builder, receiverWriteEffect);
        }
        return preservesFresh;
    }
    private static MethodEffects AddCallEffects(
        MethodEffects effects,
        IOperation site,
        IMethodSymbol method,
        string reason,
        IOperation? receiver,
        Builder builder,
        SharpProofEffect? receiverReadEffect = null,
        SharpProofEffect? receiverWriteEffect = null,
        SharpProofEffect? capturedReadEffect = null,
        SharpProofEffect? capturedWriteEffect = null) {
        const SharpProofEffect callRelativeEffects =
            SharpProofEffect.ReadsReceiverState |
            SharpProofEffect.WritesReceiverState |
            SharpProofEffect.ReadsArgumentState |
            SharpProofEffect.WritesArgumentState;
        var remapped = effects.Effects & ~callRelativeEffects;
        if ((effects.Effects & SharpProofEffect.ReadsReceiverState) != 0) {
            if (receiverReadEffect.HasValue)
                remapped |= receiverReadEffect.Value;
            else if (receiver != null)
                remapped |= GetInstanceReadEffect(receiver, builder);
            else if (site is not IObjectCreationOperation)
                remapped |= SharpProofEffect.Unknown;
        }
        if ((effects.Effects & SharpProofEffect.WritesReceiverState) != 0) {
            remapped |= receiverWriteEffect ??
                        (receiver != null
                            ? GetInstanceWriteEffect(receiver, builder)
                            : site is IObjectCreationOperation
                                ? SharpProofEffect.WritesFreshOwnedState
                                : SharpProofEffect.Unknown);
        }
        if ((effects.Effects & SharpProofEffect.ReadsArgumentState) != 0) {
            remapped |= GetArgumentEffect(site, builder, write: false);
        }
        if ((effects.Effects & SharpProofEffect.WritesArgumentState) != 0) {
            remapped |= GetArgumentEffect(site, builder, write: true);
        }
        if (capturedReadEffect.HasValue && (effects.Effects & SharpProofEffect.ReadsCapturedState) != 0) {
            remapped &= ~SharpProofEffect.ReadsCapturedState;
            remapped |= capturedReadEffect.Value;
        }
        if (capturedWriteEffect.HasValue && (effects.Effects & SharpProofEffect.WritesCapturedState) != 0) {
            remapped &= ~SharpProofEffect.WritesCapturedState;
            remapped |= capturedWriteEffect.Value;
        }
        var remappedEffects = effects with { Effects = remapped };
        builder.AddTransitive(remappedEffects, site, method, reason);
        return remappedEffects;
    }
    private static SharpProofEffect GetArgumentEffect(IOperation site, Builder builder, bool write) {
        var effect = SharpProofEffect.None;
        var hasCandidate = false;
        void AddCandidate(IOperation value, IParameterSymbol? parameter) {
            if (parameter == null ||
                parameter.Type.IsValueType &&
                parameter.Type.TypeKind is not (TypeKind.Pointer or TypeKind.FunctionPointer) &&
                parameter.RefKind == RefKind.None)
                return;
            hasCandidate = true;
            effect |= write
                ? GetInstanceWriteEffect(value, builder)
                : GetInstanceReadEffect(value, builder);
        }
        switch (site) {
            case IInvocationOperation invocation:
                foreach (var argument in invocation.Arguments)
                    AddCandidate(argument.Value, argument.Parameter);
                break;
            case IObjectCreationOperation creation:
                foreach (var argument in creation.Arguments)
                    AddCandidate(argument.Value, argument.Parameter);
                break;
            case ISimpleAssignmentOperation {
                Target: IPropertyReferenceOperation property,
                Value: var value
            }:
                foreach (var argument in property.Arguments)
                    AddCandidate(argument.Value, argument.Parameter);
                AddCandidate(value, property.Property.SetMethod?.Parameters.LastOrDefault());
                break;
            case ICoalesceAssignmentOperation {
                Target: IPropertyReferenceOperation property,
                Value: var value
            }:
                foreach (var argument in property.Arguments)
                    AddCandidate(argument.Value, argument.Parameter);
                AddCandidate(value, property.Property.SetMethod?.Parameters.LastOrDefault());
                break;
        }
        if (hasCandidate) return effect;
        return SharpProofEffect.Unknown |
               (write ? SharpProofEffect.WritesArgumentState : SharpProofEffect.ReadsArgumentState);
    }
    private static bool CanPreserveFreshArguments(
        MethodEffects effects,
        IOperation site,
        IOperation? receiver,
        Builder builder,
        SharpProofEffect? receiverWriteEffect = null) {
        const SharpProofEffect externalWrites =
            SharpProofEffect.WritesAmbientState |
            SharpProofEffect.WritesReceiverState |
            SharpProofEffect.WritesArgumentState |
            SharpProofEffect.WritesCapturedState |
            SharpProofEffect.WritesStaticState;
        if ((effects.Effects & (externalWrites | SharpProofEffect.Unknown)) != 0 ||
            !effects.UnknownReasons.IsDefaultOrEmpty)
            return false;
        if (receiverWriteEffect.HasValue) {
            if (receiverWriteEffect.Value != SharpProofEffect.WritesFreshOwnedState) return false;
        }
        else if (receiver != null && receiver.Type?.IsReferenceType == true &&
                 (!builder.TryGetFreshRootOrigin(receiver, out var receiverOrigin) ||
                  receiverOrigin != MethodEffectOrigin.FreshOwned))
            return false;
        var arguments = site is IInvocationOperation invocation
            ? invocation.Arguments
            : [];
        foreach (var argument in arguments) {
            if (argument.Parameter is not { Type.IsReferenceType: true }) continue;
            if (!builder.TryGetFreshRootOrigin(argument.Value, out var origin) ||
                origin != MethodEffectOrigin.FreshOwned)
                return false;
        }
        return true;
    }
    private bool IsBodylessAutoPropertyAccessor(IMethodSymbol method) {
        if (method.AssociatedSymbol is not IPropertySymbol) return false;
        foreach (var reference in method.DeclaringSyntaxReferences)
            if (reference.GetSyntax(cancellationToken) is AccessorDeclarationSyntax {
                Body: null,
                ExpressionBody: null,
                SemicolonToken.IsMissing: false
            })
                return true;
        return false;
    }
    private static bool IsStructurallyEffectFreeIntrinsic(IMethodSymbol method) =>
        method is { MethodKind: MethodKind.Constructor, ContainingType.SpecialType: SpecialType.System_Object } ||
        method.MethodKind == MethodKind.Conversion &&
        method.IsStatic &&
        string.Equals(
            method.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            "System.Index",
            StringComparison.Ordinal) ||
        method is {
            MethodKind: MethodKind.PropertyGet, AssociatedSymbol: IPropertySymbol {
                Name: "Length",
                IsStatic: false,
                Type.SpecialType: SpecialType.System_Int32,
                ContainingType.SpecialType: SpecialType.System_Array
            }
        } ||
        method is {
            MethodKind: MethodKind.PropertyGet,
            Name: "HasValue",
            ContainingType.SpecialType: SpecialType.System_Nullable_T
        };
    private static bool TryGetKnownFrameworkSummary(IMethodSymbol method, out MethodEffects effects) {
        var containingType = method.ContainingType;
        var isNumericToString = string.Equals(method.Name, nameof(ToString), StringComparison.Ordinal) &&
                                method.ReturnType.SpecialType == SpecialType.System_String &&
                                containingType?.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or
                                    SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or
                                    SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
                                    SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
        var isStringSplit = containingType?.SpecialType == SpecialType.System_String &&
                            string.Equals(method.Name, nameof(string.Split), StringComparison.Ordinal);
        var isStringSubstring = containingType?.SpecialType == SpecialType.System_String &&
                                string.Equals(method.Name, nameof(string.Substring), StringComparison.Ordinal);
        var isStringTrim = containingType?.SpecialType == SpecialType.System_String &&
                           method.Name is nameof(string.Trim) or nameof(string.TrimStart) or nameof(string.TrimEnd);
        var isStringNullPredicate = method.IsStatic &&
                                    containingType?.SpecialType == SpecialType.System_String &&
                                    method.Name is nameof(string.IsNullOrEmpty) or nameof(string.IsNullOrWhiteSpace);
        var typeDefinition = containingType?.OriginalDefinition.ToDisplayString();
        var isSpanToArray = string.Equals(method.Name, "ToArray", StringComparison.Ordinal) &&
                            typeDefinition is "System.Span<T>" or "System.ReadOnlySpan<T>";
        if (isStringNullPredicate) {
            effects = new MethodEffects(
                SharpProofEffect.None,
                SharpProofCapability.None,
                [],
                [],
                []);
            return true;
        }
        if (isNumericToString || isStringSplit || isStringSubstring || isStringTrim || isSpanToArray) {
            effects = new MethodEffects(
                SharpProofEffect.Allocates,
                SharpProofCapability.None,
                [],
                [],
                []);
            return true;
        }
        var isNumericParse = method.IsStatic &&
                             string.Equals(method.Name, "Parse", StringComparison.Ordinal) &&
                             containingType?.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or
                                 SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or
                                 SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
                                 SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
        var isNumericTryParse = method.IsStatic &&
                                string.Equals(method.Name, "TryParse", StringComparison.Ordinal) &&
                                method.ReturnType.SpecialType == SpecialType.System_Boolean &&
                                containingType?.SpecialType is SpecialType.System_Byte or SpecialType.System_SByte or
                                    SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or
                                    SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or
                                    SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal;
        if (isNumericTryParse) {
            effects = new MethodEffects(
                SharpProofEffect.None,
                SharpProofCapability.None,
                [],
                [],
                []);
            return true;
        }
        if (isNumericParse) {
            effects = new MethodEffects(
                SharpProofEffect.Throws,
                SharpProofCapability.None,
                [
                    MethodExceptionFact.Boundary("System.FormatException", MethodExceptionSource.Contract,
                        "framework_parse_model"),
                    MethodExceptionFact.Boundary("System.OverflowException", MethodExceptionSource.Contract,
                        "framework_parse_model")
                ],
                [],
                []);
            return true;
        }
        effects = null!;
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
        effects = null!;
        foreach (var attribute in method.GetAttributes().Concat(method.ContainingAssembly?.GetAttributes() ?? [])) {
            if (attribute.AttributeClass?.ToDisplayString() != "SharpProof.Attributes.EffectContractAttribute" ||
                attribute.ConstructorArguments.Length == 0)
                continue;
            if (attribute.AttributeClass?.ContainingAssembly != null &&
                attribute.ConstructorArguments.Length == 2 &&
                !string.Equals(attribute.ConstructorArguments[0].Value as string, canonicalKey, StringComparison.Ordinal))
                continue;
            var valueIndex = attribute.ConstructorArguments.Length == 1 ? 0 : 1;
            var declared = (SharpProofEffect)(attribute.ConstructorArguments[valueIndex].Value as long? ??
                                                Convert.ToInt64(attribute.ConstructorArguments[valueIndex].Value,
                                                    CultureInfo.InvariantCulture));
            var capabilities = SharpProofCapability.None;
            var deterministic = true;
            var complete = true;
            var exceptions = ImmutableArray.CreateBuilder<string>();
            var malformed = !HasOnlyDefinedFlags(declared);
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
                        if (item.Value is ITypeSymbol type)
                            exceptions.Add(type.ToDisplayString());
                        else
                            malformed = true;
            }
            malformed |= !HasOnlyDefinedFlags(capabilities);
            if (malformed) {
                var malformedContract = new MethodEffects(
                    SharpProofEffect.Unknown,
                    SharpProofCapability.None,
                    [MethodExceptionFact.Boundary(
                        "System.Exception",
                        MethodExceptionSource.Contract,
                        "malformed_effect_contract",
                        SharpProofVerdict.Unknown)],
                    [],
                    [CreateContractConfigurationReason("malformed_effect_contract")]);
                effects = effects == null ? malformedContract : UnionContracts(effects, malformedContract);
                continue;
            }
            if (!deterministic) declared |= SharpProofEffect.UsesNondeterminism;
            if (exceptions.Count != 0) declared |= SharpProofEffect.Throws;
            if (!complete) declared |= SharpProofEffect.Unknown;
            var normalizedExceptions = exceptions
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static type => type, StringComparer.Ordinal)
                .ToImmutableArray();
            var candidate = new MethodEffects(
                declared,
                capabilities,
                [.. normalizedExceptions.Select(static type
                    => MethodExceptionFact.Boundary(type, MethodExceptionSource.Contract, "effect_contract"))],
                [],
                complete
                    ? []
                    : [CreateUnknownReason("partial_effect_contract")]);
            effects = effects == null ? candidate : UnionContracts(effects, candidate);
        }
        return effects != null;
    }
    private static bool IsCompleteContract(MethodEffects effects) =>
        (effects.Effects & SharpProofEffect.Unknown) == 0 &&
        effects.UnknownReasons.IsDefaultOrEmpty &&
        !effects.ExceptionFacts.Any(static fact => fact.Escape == SharpProofVerdict.Unknown);
    private static bool HasOnlyDefinedFlags<T>(T value) where T : struct, Enum {
        ulong knownBits = 0;
        foreach (var defined in Enum.GetValues(typeof(T)))
            knownBits |= Convert.ToUInt64(defined, CultureInfo.InvariantCulture);
        var actualBits = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        return (actualBits & ~knownBits) == 0;
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
            [.. left.ExceptionFacts.AddRange(right.ExceptionFacts).Distinct()],
            left.Sites.AddRange(right.Sites),
            [.. unknowns.Distinct()]);
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
                builder.Add(GetInstanceWriteEffect(field.Instance, builder), field, field.Field, "instance_field_write");
                break;
            case IPropertyReferenceOperation { Property.IsStatic: true } property:
                builder.Add(SharpProofEffect.WritesStaticState, property, property.Property, "static_property_write");
                break;
            case IPropertyReferenceOperation property:
                builder.Add(GetInstanceWriteEffect(property.Instance, builder), property, property.Property, "instance_property_write");
                break;
            case IArrayElementReferenceOperation array:
                builder.Add(GetInstanceWriteEffect(array.ArrayReference, builder), array, array.Type, "array_element_write");
                break;
            case IInlineArrayAccessOperation inlineArray:
                builder.Add(GetInstanceWriteEffect(inlineArray.Instance, builder), inlineArray, inlineArray.Type,
                    "inline_array_write");
                break;
            case IImplicitIndexerReferenceOperation implicitIndexer:
                builder.Add(GetInstanceWriteEffect(implicitIndexer.Instance, builder), implicitIndexer,
                    implicitIndexer.Type, "implicit_indexer_write");
                break;
            case var pointer when IsPointerIndirection(pointer) &&
                                  pointer.ChildOperations.FirstOrDefault() is { } pointerOperand:
                builder.Add(GetInstanceWriteEffect(pointerOperand, builder), pointer, pointer.Type,
                    "pointer_indirection_write");
                break;
        }
    }
    private static bool IsPointerIndirection(IOperation operation) =>
        operation.Syntax.IsKind(SyntaxKind.PointerIndirectionExpression);
    private static SharpProofEffect GetInstanceWriteEffect(IOperation? instance, Builder builder) {
        if (builder.TryGetFreshRootOrigin(instance, out var origin)) return GetWriteEffect(origin);
        return instance switch {
            IInstanceReferenceOperation => SharpProofEffect.WritesReceiverState,
            IParameterReferenceOperation => SharpProofEffect.WritesArgumentState,
            IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.WritesStaticState,
            IFieldReferenceOperation field => GetInstanceWriteEffect(field.Instance, builder),
            IPropertyReferenceOperation { Property.IsStatic: true } => SharpProofEffect.WritesStaticState,
            IPropertyReferenceOperation property => GetInstanceWriteEffect(property.Instance, builder),
            IArrayElementReferenceOperation array => GetInstanceWriteEffect(array.ArrayReference, builder),
            IInlineArrayAccessOperation inlineArray => GetInstanceWriteEffect(inlineArray.Instance, builder),
            IImplicitIndexerReferenceOperation implicitIndexer =>
                GetInstanceWriteEffect(implicitIndexer.Instance, builder),
            IOperation pointer when IsPointerIndirection(pointer) =>
                GetInstanceWriteEffect(pointer.ChildOperations.FirstOrDefault(), builder),
            IConversionOperation conversion => GetInstanceWriteEffect(conversion.Operand, builder),
            ILocalReferenceOperation => SharpProofEffect.WritesCapturedState,
            _ => SharpProofEffect.Unknown
        };
    }
    private static SharpProofEffect GetInstanceReadEffect(IOperation? instance, Builder builder) {
        if (builder.TryGetFreshRootOrigin(instance, out var origin)) return GetReadEffect(origin);
        if (instance is { Type.SpecialType: SpecialType.System_String } or { Type.IsValueType: true })
            return SharpProofEffect.None;
        return instance switch {
            IInstanceReferenceOperation => SharpProofEffect.ReadsReceiverState,
            IParameterReferenceOperation => SharpProofEffect.ReadsArgumentState,
            IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.ReadsStaticState,
            IFieldReferenceOperation field => GetInstanceReadEffect(field.Instance, builder),
            IPropertyReferenceOperation { Property.IsStatic: true } => SharpProofEffect.ReadsStaticState,
            IPropertyReferenceOperation property => GetInstanceReadEffect(property.Instance, builder),
            IArrayElementReferenceOperation array => GetInstanceReadEffect(array.ArrayReference, builder),
            IInlineArrayAccessOperation inlineArray => GetInstanceReadEffect(inlineArray.Instance, builder),
            IImplicitIndexerReferenceOperation implicitIndexer =>
                GetInstanceReadEffect(implicitIndexer.Instance, builder),
            IOperation pointer when IsPointerIndirection(pointer) =>
                GetInstanceReadEffect(pointer.ChildOperations.FirstOrDefault(), builder),
            IConversionOperation conversion => GetInstanceReadEffect(conversion.Operand, builder),
            ILocalReferenceOperation => SharpProofEffect.ReadsCapturedState,
            _ => SharpProofEffect.Unknown
        };
    }
    private static SharpProofEffect GetWriteEffect(MethodEffectOrigin origin) => origin switch {
        MethodEffectOrigin.Receiver => SharpProofEffect.WritesReceiverState,
        MethodEffectOrigin.Argument => SharpProofEffect.WritesArgumentState,
        MethodEffectOrigin.Captured => SharpProofEffect.WritesCapturedState,
        MethodEffectOrigin.Static => SharpProofEffect.WritesStaticState,
        MethodEffectOrigin.FreshOwned => SharpProofEffect.WritesFreshOwnedState,
        _ => SharpProofEffect.Unknown
    };
    private static SharpProofEffect GetReadEffect(MethodEffectOrigin origin) => origin switch {
        MethodEffectOrigin.Receiver => SharpProofEffect.ReadsReceiverState,
        MethodEffectOrigin.Argument => SharpProofEffect.ReadsArgumentState,
        MethodEffectOrigin.Captured => SharpProofEffect.ReadsCapturedState,
        MethodEffectOrigin.Static => SharpProofEffect.ReadsStaticState,
        MethodEffectOrigin.FreshOwned => SharpProofEffect.None,
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
    private static bool IsVisible(IOperation operation, SyntaxNode declaration, SemanticModel semanticModel) {
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
            else if (current.FirstAncestorOrSelf<SwitchSectionSyntax>() is { } section &&
                     section.Parent is SwitchStatementSyntax switchStatement &&
                     TryGetSelectedConstantSwitchSection(switchStatement, semanticModel, out var selectedSection) &&
                     !ReferenceEquals(section, selectedSection))
                return false;
        return true;
    }
    private static bool TryGetSelectedConstantSwitchSection(
        SwitchStatementSyntax statement,
        SemanticModel semanticModel,
        out SwitchSectionSyntax selected) {
        selected = null!;
        var governing = semanticModel.GetConstantValue(statement.Expression);
        if (!governing.HasValue) return false;
        SwitchSectionSyntax? defaultSection = null;
        foreach (var section in statement.Sections)
            foreach (var label in section.Labels) {
                if (label is DefaultSwitchLabelSyntax) {
                    defaultSection = section;
                    continue;
                }
                if (label is not CaseSwitchLabelSyntax caseLabel) continue;
                var caseValue = semanticModel.GetConstantValue(caseLabel.Value);
                if (caseValue.HasValue && Equals(governing.Value, caseValue.Value)) {
                    selected = section;
                    return true;
                }
            }
        selected = defaultSection!;
        return true;
    }
    private static void MarkFlowUncertainLocals(IOperation root, SyntaxNode declaration, Builder builder) {
        var hasUnstructuredFlow = declaration.DescendantNodes().Any(static node =>
            node is GotoStatementSyntax or LabeledStatementSyntax);
        foreach (var operation in root.DescendantsAndSelf()) {
            ILocalSymbol? assigned = operation switch {
                IAssignmentOperation { Target: ILocalReferenceOperation local } => local.Local,
                IIncrementOrDecrementOperation { Target: ILocalReferenceOperation local } => local.Local,
                _ => null
            };
            if (assigned != null && (hasUnstructuredFlow || IsFlowDependent(operation.Syntax)))
                builder.MarkFlowUncertain(assigned);
            if (operation is not IArgumentOperation {
                Parameter.RefKind: RefKind.Ref or RefKind.Out,
                Value: ILocalReferenceOperation argumentLocal
            })
                continue;
            if (hasUnstructuredFlow || IsFlowDependent(operation.Syntax))
                builder.MarkFlowUncertain(argumentLocal.Local);
        }
    }
    private static bool IsFlowDependent(SyntaxNode syntax) => syntax.Ancestors().Any(static ancestor =>
        ancestor is IfStatementSyntax or ConditionalExpressionSyntax or SwitchStatementSyntax or
            SwitchExpressionSyntax or ForStatementSyntax or ForEachStatementSyntax or
            ForEachVariableStatementSyntax or WhileStatementSyntax or DoStatementSyntax or
            TryStatementSyntax or CatchClauseSyntax);
    private ImmutableArray<TextSpan>? GetReachableOperationSpans(SyntaxNode declaration, SemanticModel semanticModel) {
        try {
            var graph = ControlFlowGraph.Create(declaration, semanticModel, cancellationToken);
            if (graph == null) return null;
            var reachable = ImmutableArray.CreateBuilder<TextSpan>();
            foreach (var block in graph.Blocks.Where(static block => block.IsReachable)) {
                foreach (var operation in block.Operations)
                    reachable.Add(operation.Syntax.Span);
                if (block.BranchValue != null)
                    reachable.Add(block.BranchValue.Syntax.Span);
            }
            return reachable.Distinct().ToImmutableArray();
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) {
            return null;
        }
    }
    private static void AddCompilerGeneratedAllocations(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        Builder builder,
        ImmutableArray<TextSpan>? reachableOperations) {
        var nodes = CSharpSyntaxFacts.DescendantNodesInExecution(declaration).ToArray();
        if (nodes.OfType<YieldStatementSyntax>().Any())
            builder.Add(SharpProofEffect.Allocates, declaration, null, "iterator_state_machine_allocation");
        if (declaration is MethodDeclarationSyntax { Modifiers: var modifiers } &&
            modifiers.Any(SyntaxKind.AsyncKeyword) &&
            nodes.OfType<AwaitExpressionSyntax>().Any())
            builder.Add(SharpProofEffect.Allocates, declaration, null, "async_state_machine_allocation");
        foreach (var expression in nodes.OfType<ExpressionSyntax>()) {
            if (reachableOperations is { } reachableSpans &&
                !reachableSpans.Any(span => span.OverlapsWith(expression.Span)))
                continue;
            var typeInfo = semanticModel.GetTypeInfo(expression);
            if (expression is BinaryExpressionSyntax binary &&
                binary.IsKind(SyntaxKind.AddExpression) &&
                typeInfo.ConvertedType?.SpecialType == SpecialType.System_String &&
                !semanticModel.GetConstantValue(expression).HasValue) {
                builder.Add(SharpProofEffect.Allocates, expression, typeInfo.ConvertedType, "string_concat_allocation");
                continue;
            }
            if (expression is InterpolatedStringExpressionSyntax &&
                typeInfo.ConvertedType?.SpecialType == SpecialType.System_String) {
                builder.Add(SharpProofEffect.Allocates, expression, typeInfo.ConvertedType, "interpolated_string_allocation");
                continue;
            }
            if (expression is WithExpressionSyntax && typeInfo.ConvertedType?.IsReferenceType == true) {
                builder.Add(SharpProofEffect.Allocates, expression, typeInfo.ConvertedType, "with_clone_allocation");
                continue;
            }
            if (expression is AnonymousFunctionExpressionSyntax && typeInfo.ConvertedType?.TypeKind == TypeKind.Delegate) {
                builder.Add(SharpProofEffect.Allocates, expression, typeInfo.ConvertedType, "delegate_allocation");
                continue;
            }
            if (typeInfo.ConvertedType?.TypeKind == TypeKind.Delegate &&
                semanticModel.GetSymbolInfo(expression).Symbol is IMethodSymbol) {
                builder.Add(SharpProofEffect.Allocates, expression, typeInfo.ConvertedType, "method_group_delegate_allocation");
                continue;
            }
            if (typeInfo.Type?.IsValueType == true && typeInfo.ConvertedType?.IsReferenceType == true)
                builder.Add(SharpProofEffect.Allocates, expression, typeInfo.ConvertedType, "boxing_allocation");
        }
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
        [new MethodExceptionFact(
            "System.Exception",
            SharpProofVerdict.Unknown,
            MethodExceptionSource.Unknown,
            site.ToString(),
            string.Empty,
            site.SpanStart,
            site.Span.Length,
            false,
            reason)],
        [new MethodEffectSite(
            SharpProofEffect.Unknown,
            SharpProofCapability.None,
            site.ToString(),
            string.Empty,
            site.SpanStart,
            site.Span.Length,
            false,
            reason)],
        [CreateUnknownReason(reason)]);
    private static SharpProofUnknownReason CreateUnknownReason(string reason) => new("SP-EFFECT-UNKNOWN", "Effects", reason, false, false);
    private static SharpProofUnknownReason CreateContractConfigurationReason(string reason) =>
        new("SP-EFFECT-CONTRACT", "Configuration", reason, false, true);
    sealed class Builder(Func<IOperation, string, bool> isCaught) {
        internal sealed record DelegateTarget(
            IMethodSymbol Method,
            IOperation? Receiver,
            SharpProofEffect? ReceiverReadEffect,
            SharpProofEffect? ReceiverWriteEffect,
            SharpProofEffect? CapturedReadEffect,
            SharpProofEffect? CapturedWriteEffect);
        private readonly ImmutableArray<MethodExceptionFact>.Builder _exceptions =
            ImmutableArray.CreateBuilder<MethodExceptionFact>();
        private readonly ImmutableArray<MethodEffectSite>.Builder _sites =
            ImmutableArray.CreateBuilder<MethodEffectSite>();
        private readonly ImmutableArray<SharpProofUnknownReason>.Builder _unknowns =
            ImmutableArray.CreateBuilder<SharpProofUnknownReason>();
        private SharpProofCapability _capabilities;
        private SharpProofEffect _effects;
        private readonly HashSet<ILocalSymbol> _freshLocals = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, Dictionary<string, MethodEffectOrigin>> _memberOrigins =
            new(SymbolEqualityComparer.Default);
        private readonly HashSet<ILocalSymbol> _flowUncertainLocals = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, INamedTypeSymbol> _exactTypes = new(SymbolEqualityComparer.Default);
        private readonly Dictionary<ILocalSymbol, ImmutableArray<DelegateTarget>> _delegateTargets =
            new(SymbolEqualityComparer.Default);
        internal void MarkFresh(ILocalSymbol local) => _freshLocals.Add(local);
        internal bool IsFresh(ILocalSymbol local) => _freshLocals.Contains(local);
        internal bool TryGetFreshRootOrigin(IOperation? operation, out MethodEffectOrigin origin) {
            if (!TryGetLocalMemberPath(operation, out var local, out var path) || !IsFresh(local)) {
                origin = MethodEffectOrigin.Unknown;
                return false;
            }
            if (path.Length == 0) {
                origin = MethodEffectOrigin.FreshOwned;
                return true;
            }
            origin = _memberOrigins.TryGetValue(local, out var origins) && origins.TryGetValue(path, out var recorded)
                ? recorded
                : MethodEffectOrigin.Unknown;
            return true;
        }
        internal void MarkExactType(ILocalSymbol local, ITypeSymbol? type) {
            if (type is INamedTypeSymbol { TypeKind: not (TypeKind.Interface or TypeKind.Dynamic), IsAbstract: false } named)
                _exactTypes[local] = named;
        }
        internal INamedTypeSymbol? GetExactType(ILocalSymbol local) =>
            _exactTypes.TryGetValue(local, out var type) ? type : null;
        internal void MarkDelegateTargets(ILocalSymbol local, IOperation value) {
            var targets = GetDelegateTargets(value);
            if (!targets.IsDefaultOrEmpty) _delegateTargets[local] = targets;
        }
        internal ImmutableArray<DelegateTarget> GetDelegateTargets(IOperation? value) {
            if (value == null) return [];
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is ILocalReferenceOperation local &&
                _delegateTargets.TryGetValue(local.Local, out var localTargets))
                return localTargets;
            if (value is IDelegateCreationOperation creation) value = creation.Target;
            var target = value switch {
                IMethodReferenceOperation reference => new DelegateTarget(
                    reference.Method.OriginalDefinition,
                    reference.Instance,
                    reference.Instance == null ? null : GetInstanceReadEffect(reference.Instance, this),
                    reference.Instance == null ? null : GetInstanceWriteEffect(reference.Instance, this),
                    null,
                    null),
                IAnonymousFunctionOperation function => CreateAnonymousFunctionTarget(function),
                _ => null
            };
            return target == null ? [] : [target];
        }
        internal void ApplyDelegateCompoundAssignment(ILocalSymbol local, IOperation value, bool adds) {
            local = (ILocalSymbol)local.OriginalDefinition;
            if (!adds) {
                _delegateTargets.Remove(local);
                return;
            }
            var addedTargets = GetDelegateTargets(value);
            if (addedTargets.IsDefaultOrEmpty) {
                _delegateTargets.Remove(local);
                return;
            }
            var existingTargets = _delegateTargets.TryGetValue(local, out var existing) ? existing : [];
            _delegateTargets[local] = existingTargets.AddRange(addedTargets);
        }
        private DelegateTarget CreateAnonymousFunctionTarget(IAnonymousFunctionOperation function) {
            var hasCapture = TryGetCapturedEffects(
                function,
                function.Body,
                function.Symbol,
                out var capturedReadEffect,
                out var capturedWriteEffect);
            return new DelegateTarget(
                function.Symbol.OriginalDefinition,
                null,
                null,
                null,
                hasCapture ? capturedReadEffect : null,
                hasCapture ? capturedWriteEffect : null);
        }
        internal bool TryGetCapturedEffects(
            IOperation function,
            IOperation body,
            IMethodSymbol functionSymbol,
            out SharpProofEffect capturedReadEffect,
            out SharpProofEffect capturedWriteEffect) {
            capturedReadEffect = SharpProofEffect.None;
            capturedWriteEffect = SharpProofEffect.None;
            var hasCapture = false;
            foreach (var operation in body.DescendantsAndSelf()) {
                if (!BelongsDirectlyTo(function, operation)) continue;
                IOperation? captured = operation switch {
                    ILocalReferenceOperation local when
                        !SymbolEqualityComparer.Default.Equals(local.Local.ContainingSymbol, functionSymbol) => local,
                    IParameterReferenceOperation parameter when
                        !SymbolEqualityComparer.Default.Equals(parameter.Parameter.ContainingSymbol, functionSymbol) => parameter,
                    IInstanceReferenceOperation instance => instance,
                    _ => null
                };
                if (captured == null) continue;
                hasCapture = true;
                capturedReadEffect |= GetInstanceReadEffect(captured, this);
                capturedWriteEffect |= GetInstanceWriteEffect(captured, this);
            }
            return hasCapture;
        }
        private static bool BelongsDirectlyTo(IOperation function, IOperation operation) {
            for (var current = operation.Parent; current != null; current = current.Parent) {
                if (ReferenceEquals(current, function)) return true;
                if (current is IAnonymousFunctionOperation or ILocalFunctionOperation) return false;
            }
            return false;
        }
        internal void AssignLocal(ILocalSymbol local, IOperation value) {
            local = (ILocalSymbol)local.OriginalDefinition;
            _freshLocals.Remove(local);
            _memberOrigins.Remove(local);
            _exactTypes.Remove(local);
            _delegateTargets.Remove(local);
            if (_flowUncertainLocals.Contains(local) || IsFlowDependent(value.Syntax)) return;
            var unwrapped = value;
            while (unwrapped is IConversionOperation conversion) unwrapped = conversion.Operand;
            if (unwrapped is IObjectCreationOperation or IArrayCreationOperation or IAnonymousObjectCreationOperation or
                IDelegateCreationOperation) {
                _freshLocals.Add(local);
                var origins = new Dictionary<string, MethodEffectOrigin>(StringComparer.Ordinal);
                CollectMemberOrigins(unwrapped, string.Empty, origins);
                if (origins.Count != 0) _memberOrigins[local] = origins;
            }
            else if (unwrapped is ILocalReferenceOperation sourceLocal && IsFresh(sourceLocal.Local)) {
                _freshLocals.Add(local);
                if (_memberOrigins.TryGetValue(sourceLocal.Local, out var sourceOrigins))
                    _memberOrigins[local] = new Dictionary<string, MethodEffectOrigin>(sourceOrigins, StringComparer.Ordinal);
            }
            var exactType = unwrapped switch {
                IObjectCreationOperation { Type: INamedTypeSymbol created } => created,
                IAnonymousObjectCreationOperation { Type: INamedTypeSymbol created } => created,
                ILocalReferenceOperation sourceLocal => GetExactType(sourceLocal.Local),
                _ => unwrapped.Type is INamedTypeSymbol { IsSealed: true } sealedType ? sealedType : null
            };
            if (exactType != null) _exactTypes[local] = exactType;
            if (unwrapped is ILocalReferenceOperation delegateSource &&
                _delegateTargets.TryGetValue(delegateSource.Local, out var copiedTargets))
                _delegateTargets[local] = copiedTargets;
            else
                MarkDelegateTargets(local, value);
        }
        internal void MarkFlowUncertain(ILocalSymbol local) {
            local = (ILocalSymbol)local.OriginalDefinition;
            _flowUncertainLocals.Add(local);
            _freshLocals.Remove(local);
            _memberOrigins.Remove(local);
            _exactTypes.Remove(local);
            _delegateTargets.Remove(local);
        }
        internal void MarkEscapedArguments(
            ImmutableArray<IArgumentOperation> arguments,
            bool preservesFreshArguments) {
            if (preservesFreshArguments) return;
            foreach (var argument in arguments) {
                var value = argument.Value;
                while (value is IConversionOperation conversion) value = conversion.Operand;
                if (value is not ILocalReferenceOperation local) continue;
                _freshLocals.Remove(local.Local);
                _memberOrigins.Remove(local.Local);
                if (argument.Parameter?.RefKind is RefKind.Ref or RefKind.Out) {
                    _exactTypes.Remove(local.Local);
                    _delegateTargets.Remove(local.Local);
                }
            }
        }
        private void CollectMemberOrigins(
            IOperation value,
            string prefix,
            IDictionary<string, MethodEffectOrigin> origins) {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is not IObjectCreationOperation { Initializer: { } initializer }) return;
            foreach (var assignment in initializer.Initializers.OfType<ISimpleAssignmentOperation>()) {
                var member = assignment.Target switch {
                    IPropertyReferenceOperation property => (ISymbol)property.Property.OriginalDefinition,
                    IFieldReferenceOperation field => field.Field.OriginalDefinition,
                    _ => null
                };
                if (member == null) continue;
                var path = prefix.Length == 0 ? GetMemberPathPart(member) : prefix + "/" + GetMemberPathPart(member);
                var assigned = assignment.Value;
                while (assigned is IConversionOperation conversion) assigned = conversion.Operand;
                origins[path] = GetValueOrigin(assigned);
                if (assigned is not (IObjectCreationOperation or IArrayCreationOperation or
                    IAnonymousObjectCreationOperation or IDelegateCreationOperation))
                    continue;
                CollectMemberOrigins(assigned, path, origins);
            }
        }
        private MethodEffectOrigin GetValueOrigin(IOperation value) {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (TryGetFreshRootOrigin(value, out var freshRootOrigin)) return freshRootOrigin;
            return value switch {
                IObjectCreationOperation or IArrayCreationOperation or IAnonymousObjectCreationOperation or
                    IDelegateCreationOperation => MethodEffectOrigin.FreshOwned,
                IInstanceReferenceOperation => MethodEffectOrigin.Receiver,
                IParameterReferenceOperation => MethodEffectOrigin.Argument,
                IFieldReferenceOperation { Field.IsStatic: true } => MethodEffectOrigin.Static,
                IFieldReferenceOperation field => GetValueOrigin(field.Instance!),
                IPropertyReferenceOperation { Property.IsStatic: true } => MethodEffectOrigin.Static,
                IPropertyReferenceOperation property => GetValueOrigin(property.Instance!),
                IArrayElementReferenceOperation array => GetValueOrigin(array.ArrayReference),
                ILocalReferenceOperation => MethodEffectOrigin.Captured,
                _ => MethodEffectOrigin.Unknown
            };
        }
        private static bool TryGetLocalMemberPath(IOperation? operation, out ILocalSymbol local, out string path) {
            while (operation is IConversionOperation conversion) operation = conversion.Operand;
            switch (operation) {
                case ILocalReferenceOperation localReference:
                    local = (ILocalSymbol)localReference.Local.OriginalDefinition;
                    path = string.Empty;
                    return true;
                case IPropertyReferenceOperation { IsImplicit: false, Instance: { } instance } property
                    when TryGetLocalMemberPath(instance, out local, out var parentPath):
                    path = parentPath.Length == 0
                        ? GetMemberPathPart(property.Property.OriginalDefinition)
                        : parentPath + "/" + GetMemberPathPart(property.Property.OriginalDefinition);
                    return true;
                case IFieldReferenceOperation { Instance: { } instance } field
                    when TryGetLocalMemberPath(instance, out local, out var parentPath):
                    path = parentPath.Length == 0
                        ? GetMemberPathPart(field.Field.OriginalDefinition)
                        : parentPath + "/" + GetMemberPathPart(field.Field.OriginalDefinition);
                    return true;
                default:
                    local = null!;
                    path = string.Empty;
                    return false;
            }
        }
        private static string GetMemberPathPart(ISymbol member) =>
            member.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        internal void Add(SharpProofEffect effect, IOperation operation, ISymbol? symbol, string reason)
            => Add(effect, SharpProofCapability.None, operation, symbol, reason);
        internal void Add(SharpProofEffect effect, SyntaxNode syntax, ISymbol? symbol, string reason) {
            _effects |= effect;
            _sites.Add(new MethodEffectSite(
                effect,
                SharpProofCapability.None,
                syntax.ToString(),
                symbol?.ToDisplayString() ?? string.Empty,
                syntax.SpanStart,
                syntax.Span.Length,
                false,
                reason,
                GetOrigin(effect)));
        }
        internal void Add(SharpProofEffect effect, SharpProofCapability capabilities, IOperation operation, ISymbol? symbol,
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
            string reason) => AddException(
                type?.ToDisplayString() ?? "System.Exception",
                operation.Syntax,
                source,
                escape,
                reason,
                symbol: type?.ToDisplayString() ?? string.Empty);
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
                        syntaxSite.Span.OverlapsWith(new TextSpan(_exceptions[index].SpanStart, _exceptions[index].SpanLength)))
                        _exceptions.RemoveAt(index);
            AddException(exceptionType, syntaxSite, source, escape, reason, kind, exceptionType);
        }
        internal void AddUnknown(IOperation operation, string reason, ISymbol? symbol = null) {
            Add(SharpProofEffect.Unknown, operation, symbol, reason);
            _unknowns.Add(CreateUnknownReason(reason));
            AddException(
                "System.Exception",
                operation.Syntax,
                MethodExceptionSource.Unknown,
                SharpProofVerdict.Unknown,
                reason,
                symbol: symbol?.ToDisplayString() ?? string.Empty);
        }
        private void AddException(
            string exceptionType,
            SyntaxNode site,
            MethodExceptionSource source,
            SharpProofVerdict escape,
            string reason,
            string kind = "",
            string symbol = "") {
            if (escape == SharpProofVerdict.Proven) _effects |= SharpProofEffect.Throws;
            _exceptions.Add(new MethodExceptionFact(
                exceptionType, escape, source, site.ToString(), symbol,
                site.SpanStart, site.Span.Length, false, reason, kind));
        }
        internal void AddTransitive(MethodEffects effects, IOperation site, ISymbol symbol, string reason) {
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
            [.. _exceptions.Distinct()],
            _sites.ToImmutable(),
            [.. _unknowns.Distinct()]);
    }
}
