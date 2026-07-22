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
                    AssignTrackedLocal(builder, local, value);
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
    private bool ReturnsFreshValue(IMethodSymbol? method) {
        if (method == null) return false;
        method = method.ReducedFrom ?? method;
        method = (method.PartialImplementationPart ?? method).OriginalDefinition;
        var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        if (declaration == null) return false;
        var model = compilation.GetSemanticModel(declaration.SyntaxTree);
        var root = MethodBodyOperationResolver.GetMethodBodyRootOperation(
            declaration,
            model,
            cancellationToken,
            true);
        if (root == null) return false;
        var returnedValues = root.DescendantsAndSelf()
            .OfType<IReturnOperation>()
            .Where(returned => IsVisible(returned, declaration, model))
            .Select(static returned => returned.ReturnedValue)
            .ToArray();
        if (returnedValues.Length == 0) {
            var expression = declaration switch {
                MethodDeclarationSyntax methodDeclaration => methodDeclaration.ExpressionBody?.Expression,
                LocalFunctionStatementSyntax localFunction => localFunction.ExpressionBody?.Expression,
                OperatorDeclarationSyntax operatorDeclaration => operatorDeclaration.ExpressionBody?.Expression,
                ConversionOperatorDeclarationSyntax conversion => conversion.ExpressionBody?.Expression,
                _ => null
            };
            return expression != null && IsFreshReturnedValue(model.GetOperation(expression, cancellationToken));
        }
        return returnedValues.Length != 0 && returnedValues.All(IsFreshReturnedValue);
    }
    private static bool IsFreshReturnedValue(IOperation? value) {
        while (value is IConversionOperation conversion) value = conversion.Operand;
        return value switch {
            IObjectCreationOperation or IArrayCreationOperation or IAnonymousObjectCreationOperation or
                IDelegateCreationOperation => true,
            ICollectionExpressionOperation collection =>
                collection.Type is IArrayTypeSymbol || collection.Type?.IsReferenceType == true,
            IConditionalOperation conditional =>
                IsFreshReturnedValue(conditional.WhenTrue) && IsFreshReturnedValue(conditional.WhenFalse),
            _ => false
        };
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
            case IVariableDeclaratorOperation {
                Syntax: VariableDeclaratorSyntax declarator,
                Initializer.Value: { } initializer
            } when declarator.Ancestors().OfType<FixedStatementSyntax>().Any():
                AnalyzePinnableProtocol(initializer, semanticModel, builder);
                break;
            case ISimpleAssignmentOperation assignment:
                if (!assignment.IsRef) AddWrite(assignment.Target, builder);
                if (assignment.Target is ILocalReferenceOperation assignedLocal &&
                    (assignedLocal.Local.RefKind == RefKind.None || assignment.IsRef))
                    AssignTrackedLocal(builder, assignedLocal.Local, assignment.Value);
                if (!assignment.IsRef &&
                    assignment.Target is IPropertyReferenceOperation { Property.SetMethod: not null } propertyTarget) {
                    var isFreshInitializerAssignment = assignment.Syntax.AncestorsAndSelf().Any(static syntax =>
                        syntax is InitializerExpressionSyntax or
                            WithExpressionSyntax or
                            AnonymousObjectCreationExpressionSyntax);
                    AnalyzeCall(
                        propertyTarget.Property.SetMethod,
                        assignment,
                        builder,
                        propertyTarget.Instance,
                        isFreshInitializerAssignment ? SharpProofEffect.None : null,
                        isFreshInitializerAssignment ? SharpProofEffect.WritesFreshOwnedState : null);
                }
                if (!assignment.IsRef &&
                    assignment.Target is IImplicitIndexerReferenceOperation implicitIndexerTarget)
                    AnalyzeImplicitIndexerAccess(implicitIndexerTarget, assignment, builder, reads: false, writes: true);
                break;
            case ICoalesceAssignmentOperation coalesceAssignment:
                AddWrite(coalesceAssignment.Target, builder);
                if (coalesceAssignment.Target is ILocalReferenceOperation coalescedLocal)
                    AssignTrackedLocal(builder, coalescedLocal.Local, coalesceAssignment.Value);
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
                AnalyzeDeconstructionProtocol(deconstruction, semanticModel, builder);
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
            case ILocalReferenceOperation local when local.Local.RefKind != RefKind.None &&
                                                     (local.Parent is not IAssignmentOperation { Target: var target } ||
                                                      !ReferenceEquals(target, local)):
                builder.Add(GetInstanceReadEffect(local, builder), local, local.Local, "ref_local_read");
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
            case ICollectionExpressionOperation collection:
                if (collection.Type is IArrayTypeSymbol ||
                    collection.ConstructMethod?.MethodKind == MethodKind.Constructor ||
                    collection.ConstructMethod == null && collection.Type?.IsReferenceType == true)
                    builder.Add(SharpProofEffect.Allocates, collection, collection.Type,
                        "collection_expression_allocation");
                if (collection.ConstructMethod is { } constructMethod) {
                    var isConstructor = constructMethod.MethodKind == MethodKind.Constructor;
                    AnalyzeCall(
                        constructMethod,
                        collection,
                        builder,
                        receiverReadEffect: isConstructor ? SharpProofEffect.None : null,
                        receiverWriteEffect: isConstructor ? SharpProofEffect.WritesFreshOwnedState : null);
                    if (isConstructor) {
                        foreach (var element in collection.Elements) {
                            if (element is ISpreadOperation) continue;
                            AnalyzeCall(
                                ResolveCollectionAddMethod(collection, element, semanticModel),
                                element,
                                builder,
                                receiverReadEffect: SharpProofEffect.None,
                                receiverWriteEffect: SharpProofEffect.WritesFreshOwnedState);
                        }
                    }
                }
                foreach (var spread in collection.Elements.OfType<ISpreadOperation>())
                    AnalyzeCollectionSpread(spread, semanticModel, builder);
                break;
            case IAnonymousObjectCreationOperation anonymousObject:
                builder.Add(SharpProofEffect.Allocates, anonymousObject, anonymousObject.Type, "anonymous_object_allocation");
                break;
            case IWithOperation withOperation:
                AnalyzeWithClone(withOperation, builder);
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
                            target.CapturedWriteEffect,
                            target.ArgumentReadEffect,
                            target.ArgumentWriteEffect);
                }
                else {
                    var isFreshInitializerCall = invocation.IsImplicit &&
                                                 invocation.Syntax.AncestorsAndSelf().Any(static syntax =>
                                                     syntax is InitializerExpressionSyntax or
                                                         WithExpressionSyntax or
                                                         AnonymousObjectCreationExpressionSyntax);
                    preservesFreshArguments = AnalyzeCall(
                        invocation.TargetMethod,
                        invocation,
                        builder,
                        invocation.Instance,
                        isFreshInitializerCall ? SharpProofEffect.None : null,
                        isFreshInitializerCall ? SharpProofEffect.WritesFreshOwnedState : null);
                }
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
            case IRecursivePatternOperation { DeconstructSymbol: IMethodSymbol deconstruct } recursivePattern:
                AnalyzeCall(
                    deconstruct,
                    recursivePattern,
                    builder,
                    FindPatternInput(recursivePattern),
                    argumentReadEffect: SharpProofEffect.None,
                    argumentWriteEffect: SharpProofEffect.WritesFreshOwnedState);
                break;
            case IListPatternOperation listPattern:
                var patternInput = FindPatternInput(listPattern);
                if (listPattern.LengthSymbol is IPropertySymbol lengthProperty)
                    AnalyzeCall(lengthProperty.GetMethod, listPattern, builder, patternInput);
                if (listPattern.Patterns.Any(static pattern => pattern is not ISlicePatternOperation) &&
                    listPattern.IndexerSymbol is IPropertySymbol indexerProperty)
                    AnalyzeCall(indexerProperty.GetMethod, listPattern, builder, patternInput);
                break;
            case ISlicePatternOperation { Pattern: not null } slicePattern:
                var sliceInput = slicePattern.Parent is IListPatternOperation containingList
                    ? FindPatternInput(containingList)
                    : null;
                if (slicePattern.SliceSymbol is IPropertySymbol sliceProperty)
                    AnalyzeCall(sliceProperty.GetMethod, slicePattern, builder, sliceInput);
                else if (slicePattern.SliceSymbol is IMethodSymbol sliceMethod)
                    AnalyzeCall(sliceMethod, slicePattern, builder, sliceInput);
                break;
            case IAwaitOperation { Syntax: AwaitExpressionSyntax syntax } awaited:
                var awaitInfo = semanticModel.GetAwaitExpressionInfo(syntax);
                AnalyzeCall(awaitInfo.GetAwaiterMethod, awaited, builder, awaited.Operation);
                AnalyzeCall(awaitInfo.IsCompletedProperty?.GetMethod, awaited, builder);
                AnalyzeCall(awaitInfo.GetResultMethod, awaited, builder);
                if (awaitInfo.GetAwaiterMethod?.ReturnType is { } awaiterType)
                    AnalyzeCall(
                        FindProtocolMethod(awaiterType, "UnsafeOnCompleted", parameterCount: 1) ??
                        FindProtocolMethod(awaiterType, "OnCompleted", parameterCount: 1),
                        awaited,
                        builder);
                break;
            case IForEachLoopOperation { Syntax: CommonForEachStatementSyntax syntax } loop:
                var foreachCollection = loop.Collection;
                while (foreachCollection is IConversionOperation collectionConversion)
                    foreachCollection = collectionConversion.Operand;
                AnalyzeForEachDeconstruction(loop, syntax, semanticModel, builder);
                if (foreachCollection.Type is IArrayTypeSymbol arrayType) {
                    TrackIntrinsicForEachRefLocal(loop, foreachCollection, builder);
                    builder.Add(
                        GetInstanceReadEffect(foreachCollection, builder),
                        loop,
                        arrayType,
                        "array_foreach_read");
                    break;
                }
                if (foreachCollection.Type?.SpecialType == SpecialType.System_String) {
                    builder.Add(
                        GetInstanceReadEffect(foreachCollection, builder),
                        loop,
                        foreachCollection.Type,
                        "string_foreach_read");
                    break;
                }
                var foreachTypeDefinition = foreachCollection.Type?.OriginalDefinition.ToDisplayString();
                if (foreachTypeDefinition is "System.Span<T>" or "System.ReadOnlySpan<T>") {
                    TrackIntrinsicForEachRefLocal(loop, foreachCollection, builder);
                    builder.Add(
                        GetInstanceReadEffect(foreachCollection, builder),
                        loop,
                        foreachCollection.Type,
                        "span_foreach_read");
                    break;
                }
                if (foreachCollection.Type is INamedTypeSymbol inlineArrayType &&
                    inlineArrayType.GetAttributes().Any(static attribute =>
                        attribute.AttributeClass?.ToDisplayString() ==
                        "System.Runtime.CompilerServices.InlineArrayAttribute")) {
                    TrackIntrinsicForEachRefLocal(loop, foreachCollection, builder);
                    builder.Add(
                        GetInstanceReadEffect(foreachCollection, builder),
                        loop,
                        inlineArrayType,
                        "inline_array_foreach_read");
                    break;
                }
                var info = semanticModel.GetForEachStatementInfo(syntax);
                AnalyzeCall(info.GetEnumeratorMethod, loop, builder, loop.Collection);
                var enumeratorType = info.GetEnumeratorMethod?.ReturnType;
                var ownsEnumerator = enumeratorType?.IsValueType == true ||
                                     ReturnsFreshValue(info.GetEnumeratorMethod);
                SharpProofEffect? enumeratorReadEffect = ownsEnumerator
                    ? SharpProofEffect.None
                    : enumeratorType?.IsReferenceType == true
                        ? GetInstanceReadEffect(loop.Collection, builder)
                        : null;
                SharpProofEffect? enumeratorWriteEffect = ownsEnumerator
                    ? SharpProofEffect.WritesFreshOwnedState
                    : enumeratorType?.IsReferenceType == true
                        ? GetInstanceWriteEffect(loop.Collection, builder)
                        : null;
                AnalyzeCall(
                    info.MoveNextMethod,
                    loop,
                    builder,
                    receiverReadEffect: enumeratorReadEffect,
                    receiverWriteEffect: enumeratorWriteEffect);
                AnalyzeCall(
                    info.CurrentProperty?.GetMethod,
                    loop,
                    builder,
                    receiverReadEffect: enumeratorReadEffect,
                    receiverWriteEffect: enumeratorWriteEffect);
                if (info.DisposeMethod != null)
                    AnalyzeCall(
                        info.DisposeMethod,
                        loop,
                        builder,
                        receiverReadEffect: enumeratorReadEffect,
                        receiverWriteEffect: enumeratorWriteEffect);
                if (loop.IsAsynchronous) {
                    if (info.MoveNextMethod?.ReturnType is { } moveNextAwaitable)
                        AnalyzeAwaitableProtocol(moveNextAwaitable, loop, semanticModel, builder);
                    if (info.DisposeMethod?.ReturnType is { } disposeAwaitable)
                        AnalyzeAwaitableProtocol(disposeAwaitable, loop, semanticModel, builder);
                }
                break;
            case IUsingOperation usingOperation:
                if (usingOperation.Resources is IVariableDeclarationGroupOperation resourceDeclarations) {
                    foreach (var declarator in resourceDeclarations.Declarations
                                 .SelectMany(static declaration => declaration.Declarators))
                        AnalyzeDisposal(
                            declarator.Symbol.Type,
                            usingOperation,
                            builder,
                            usingOperation.IsAsynchronous,
                            semanticModel,
                            declarator.Initializer?.Value);
                }
                else
                    AnalyzeDisposal(
                        usingOperation.Resources.Type,
                        usingOperation,
                        builder,
                        usingOperation.IsAsynchronous,
                        semanticModel,
                        usingOperation.Resources);
                break;
            case IUsingDeclarationOperation usingDeclaration:
                foreach (var declarator in usingDeclaration.DeclarationGroup.Declarations
                             .SelectMany(static declaration => declaration.Declarators))
                    AnalyzeDisposal(
                        declarator.Symbol.Type,
                        usingDeclaration,
                        builder,
                        usingDeclaration.IsAsynchronous,
                        semanticModel,
                        declarator.Initializer?.Value);
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
    private static IMethodSymbol? ResolveCollectionAddMethod(
        ICollectionExpressionOperation collection,
        IOperation element,
        SemanticModel semanticModel) {
        if (collection.Type == null || element.Syntax is not ExpressionSyntax expression) return null;
        var receiver = SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(
            collection.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                SyntaxFactory.IdentifierName("Add")),
            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(expression.WithoutTrivia()))));
        return semanticModel.GetSpeculativeSymbolInfo(
            collection.Syntax.SpanStart,
            invocation,
            SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
    }
    private static IOperation? FindPatternInput(IOperation pattern) {
        for (var parent = pattern.Parent; parent != null; parent = parent.Parent) {
            if (parent is IRecursivePatternOperation or IListPatternOperation) return null;
            if (parent is IIsPatternOperation isPattern) return isPattern.Value;
            if (parent is ISwitchOperation switchOperation) return switchOperation.Value;
            if (parent is ISwitchExpressionOperation switchExpression) return switchExpression.Value;
        }
        return null;
    }
    private void AnalyzeCollectionSpread(
        ISpreadOperation spread,
        SemanticModel semanticModel,
        Builder builder) {
        if (spread.Operand.Type is IArrayTypeSymbol ||
            spread.Operand.Type?.SpecialType == SpecialType.System_String)
            return;
        if (spread.Operand.Syntax is not ExpressionSyntax expression) {
            builder.AddUnknown(spread, "unresolved_collection_spread");
            return;
        }
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("GetEnumerator")));
        var getEnumerator = semanticModel.GetSpeculativeSymbolInfo(
            spread.Syntax.SpanStart,
            invocation,
            SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
        AnalyzeCall(getEnumerator, spread, builder, spread.Operand);
        if (getEnumerator == null) return;
        var enumeratorType = getEnumerator.ReturnType;
        AnalyzeCall(FindProtocolMethod(enumeratorType, "MoveNext"), spread, builder);
        AnalyzeCall(FindProtocolProperty(enumeratorType, "Current")?.GetMethod, spread, builder);
        AnalyzeCall(FindProtocolMethod(enumeratorType, "Dispose"), spread, builder);
    }
    private void AnalyzePinnableProtocol(
        IOperation initializer,
        SemanticModel semanticModel,
        Builder builder) {
        if (initializer.Syntax is not ExpressionSyntax expression) return;
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                expression.WithoutTrivia(),
                SyntaxFactory.IdentifierName("GetPinnableReference")));
        var getPinnableReference = semanticModel.GetSpeculativeSymbolInfo(
            initializer.Syntax.SpanStart,
            invocation,
            SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
        if (getPinnableReference != null)
            AnalyzeCall(getPinnableReference, initializer, builder, initializer);
    }
    private void AnalyzeWithClone(IWithOperation operation, Builder builder) {
        if (operation.CloneMethod is not { } cloneMethod) return;
        var containingType = cloneMethod.ContainingType;
        var exactType = operation.Operand switch {
            IObjectCreationOperation { Type: INamedTypeSymbol createdType } => createdType,
            ILocalReferenceOperation local => builder.GetExactType(local.Local),
            _ => null
        };
        if (cloneMethod.IsVirtual && !containingType.IsSealed && exactType == null) {
            builder.Add(
                SharpProofEffect.DispatchUncertainty,
                operation,
                cloneMethod,
                "with_clone_dispatch_uncertainty");
            builder.AddUnknown(operation, "unresolved_with_clone_dispatch", cloneMethod);
            return;
        }
        var concreteType = exactType ?? containingType;
        var copyConstructor = concreteType.InstanceConstructors.FirstOrDefault(constructor =>
            constructor.Parameters.Length == 1 &&
            SymbolEqualityComparer.Default.Equals(constructor.Parameters[0].Type, concreteType));
        if (copyConstructor == null) {
            builder.AddUnknown(operation, "unresolved_with_copy_constructor", cloneMethod);
            return;
        }
        AnalyzeCall(
            copyConstructor,
            operation,
            builder,
            receiverReadEffect: SharpProofEffect.None,
            receiverWriteEffect: SharpProofEffect.WritesFreshOwnedState,
            argumentReadEffect: GetInstanceReadEffect(operation.Operand, builder),
            argumentWriteEffect: GetInstanceWriteEffect(operation.Operand, builder));
    }
    private static IMethodSymbol? FindProtocolMethod(
        ITypeSymbol type,
        string name,
        int parameterCount = 0) =>
        GetProtocolTypes(type)
            .SelectMany(candidate => candidate.GetMembers(name).OfType<IMethodSymbol>())
            .FirstOrDefault(method => method.Parameters.Length == parameterCount);
    private static IPropertySymbol? FindProtocolProperty(ITypeSymbol type, string name) =>
        GetProtocolTypes(type)
            .SelectMany(candidate => candidate.GetMembers(name).OfType<IPropertySymbol>())
            .FirstOrDefault(static property => property.Parameters.Length == 0);
    private static IEnumerable<INamedTypeSymbol> GetProtocolTypes(ITypeSymbol type) {
        if (type is not INamedTypeSymbol named) yield break;
        for (var current = named; current != null; current = current.BaseType) yield return current;
        foreach (var @interface in named.AllInterfaces) yield return @interface;
    }
    private void AssignTrackedLocal(Builder builder, ILocalSymbol local, IOperation value) {
        builder.AssignLocal(local, value);
        if (local.RefKind == RefKind.None && local.Type.TypeKind != TypeKind.Pointer) return;
        var writeEffect = GetAliasStorageWriteEffect(value, builder);
        builder.SetRefLocalEffects(local, GetAliasReadEffect(writeEffect), writeEffect);
    }
    private void TrackIntrinsicForEachRefLocal(
        IForEachLoopOperation loop,
        IOperation collection,
        Builder builder) {
        var local = loop.LoopControlVariable
            .DescendantsAndSelf()
            .OfType<IVariableDeclaratorOperation>()
            .Select(static declarator => declarator.Symbol)
            .FirstOrDefault();
        if (local?.RefKind == RefKind.None || local == null) return;
        var writeEffect = GetAliasStorageWriteEffect(collection, builder);
        builder.SetRefLocalEffects(local, GetAliasReadEffect(writeEffect), writeEffect);
    }
    private SharpProofEffect GetAliasStorageWriteEffect(IOperation value, Builder builder) {
        value = UnwrapAliasSource(value);
        return value switch {
            IInvocationOperation invocation when invocation.TargetMethod.ReturnsByRef ||
                                                 invocation.TargetMethod.ReturnsByRefReadonly =>
                GetRefReturnWriteEffect(invocation.TargetMethod, invocation.Instance, invocation, builder),
            IPropertyReferenceOperation { Property.GetMethod: { } getter } property
                when getter.ReturnsByRef || getter.ReturnsByRefReadonly =>
                GetRefReturnWriteEffect(getter, property.Instance, property, builder),
            IConditionalOperation conditional =>
                GetAliasStorageWriteEffect(conditional.WhenTrue, builder) |
                (conditional.WhenFalse == null
                    ? SharpProofEffect.Unknown
                    : GetAliasStorageWriteEffect(conditional.WhenFalse, builder)),
            _ => GetStorageWriteEffect(value, builder)
        };
    }
    private static IOperation UnwrapAliasSource(IOperation value) {
        while (true) {
            switch (value) {
                case IConversionOperation conversion:
                    value = conversion.Operand;
                    continue;
                case IAddressOfOperation address:
                    value = address.Reference;
                    continue;
                case { Kind: OperationKind.None } transparent:
                    var children = transparent.ChildOperations.Take(2).ToArray();
                    if (children.Length != 1) return value;
                    value = children[0];
                    continue;
                default:
                    return value;
            }
        }
    }
    private static SharpProofEffect GetAliasReadEffect(SharpProofEffect writeEffect) {
        var readEffect = writeEffect & SharpProofEffect.Unknown;
        if ((writeEffect & SharpProofEffect.WritesAmbientState) != 0)
            readEffect |= SharpProofEffect.ReadsAmbientState;
        if ((writeEffect & SharpProofEffect.WritesReceiverState) != 0)
            readEffect |= SharpProofEffect.ReadsReceiverState;
        if ((writeEffect & SharpProofEffect.WritesArgumentState) != 0)
            readEffect |= SharpProofEffect.ReadsArgumentState;
        if ((writeEffect & SharpProofEffect.WritesCapturedState) != 0)
            readEffect |= SharpProofEffect.ReadsCapturedState;
        if ((writeEffect & SharpProofEffect.WritesStaticState) != 0)
            readEffect |= SharpProofEffect.ReadsStaticState;
        return readEffect;
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
    private void AnalyzeDeconstructionProtocol(
        IDeconstructionAssignmentOperation deconstruction,
        SemanticModel semanticModel,
        Builder builder) {
        if (deconstruction.Syntax is not AssignmentExpressionSyntax syntax) return;
        AnalyzeDeconstructionInfo(
            semanticModel.GetDeconstructionInfo(syntax),
            deconstruction.Value,
            deconstruction,
            builder);
    }
    private void AnalyzeForEachDeconstruction(
        IForEachLoopOperation loop,
        CommonForEachStatementSyntax syntax,
        SemanticModel semanticModel,
        Builder builder) {
        if (syntax is not ForEachVariableStatementSyntax variableSyntax) return;
        var info = semanticModel.GetDeconstructionInfo(variableSyntax);
        AnalyzeDeconstructionInfo(
            info,
            loop.Collection,
            loop,
            builder,
            receiverReadEffect: GetInstanceReadEffect(loop.Collection, builder),
            receiverWriteEffect: info.Method?.ContainingType.IsValueType == true
                ? SharpProofEffect.WritesFreshOwnedState
                : GetInstanceWriteEffect(loop.Collection, builder));
    }
    private void AnalyzeDeconstructionInfo(
        DeconstructionInfo info,
        IOperation? receiver,
        IOperation site,
        Builder builder,
        SharpProofEffect? receiverReadEffect = null,
        SharpProofEffect? receiverWriteEffect = null) {
        if (info.Method is { } method) {
            var isExtensionMethod = method.ReducedFrom != null || method.IsExtensionMethod;
            SharpProofEffect? argumentReadEffect = isExtensionMethod
                ? receiver == null ? null : GetInstanceReadEffect(receiver, builder)
                : SharpProofEffect.None;
            SharpProofEffect? argumentWriteEffect = isExtensionMethod
                ? receiver == null ? null : GetInstanceWriteEffect(receiver, builder)
                : SharpProofEffect.WritesFreshOwnedState;
            AnalyzeCall(
                method,
                site,
                builder,
                receiver,
                receiverReadEffect,
                receiverWriteEffect,
                argumentReadEffect: argumentReadEffect,
                argumentWriteEffect: argumentWriteEffect);
        }
        foreach (var nested in info.Nested)
            AnalyzeDeconstructionInfo(nested, null, site, builder);
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
        SharpProofEffect? capturedWriteEffect = null,
        SharpProofEffect? argumentReadEffect = null,
        SharpProofEffect? argumentWriteEffect = null) {
        if (method == null) {
            builder.AddUnknown(site, "unresolved_call");
            return false;
        }
        if (site is not IInvocationOperation &&
            receiver != null &&
            (method.ReducedFrom != null || method.IsExtensionMethod)) {
            argumentReadEffect ??= GetInstanceReadEffect(receiver, builder);
            argumentWriteEffect ??= GetInstanceWriteEffect(receiver, builder);
        }
        method = method.ReducedFrom ?? method;
        method = (method.PartialImplementationPart ?? method).OriginalDefinition;
        var dispatchReceiver = receiver is IConditionalAccessInstanceOperation conditionalAccess
            ? FindConditionalAccessReceiver(conditionalAccess) ?? receiver
            : receiver;
        var knownExactReceiverType = dispatchReceiver switch {
            ILocalReferenceOperation localReceiver => builder.GetExactType(localReceiver.Local),
            IObjectCreationOperation { Type: INamedTypeSymbol createdType } => createdType,
            IInvocationOperation returnedInvocation =>
                GetReturnedExactResultType(returnedInvocation.TargetMethod, returnedInvocation, builder),
            IPropertyReferenceOperation { Property.GetMethod: { } getter } returnedProperty =>
                GetReturnedExactResultType(getter, returnedProperty, builder),
            _ => null
        };
        var exactDispatchTarget = SymbolicDispatchFacts.ResolveExactDispatchTarget(
            method,
            dispatchReceiver,
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
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect,
                    argumentReadEffect, argumentWriteEffect);
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
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect,
                    argumentReadEffect, argumentWriteEffect);
                return CanPreserveFreshArguments(
                    remappedFramework, site, receiver, builder, receiverWriteEffect);
            }
            var metadata = _metadata.Analyze(method);
            if (hasContract && metadata.Effects == SharpProofEffect.Unknown)
                AddCallEffects(contracted, site, method, "complete_effect_contract", receiver, builder,
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect,
                    argumentReadEffect, argumentWriteEffect);
            else {
                AddCallEffects(metadata, site, method, "metadata_call", receiver, builder,
                    receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect,
                    argumentReadEffect, argumentWriteEffect);
                if (hasContract)
                    AddCallEffects(contracted, site, method, "effect_contract", receiver, builder,
                        receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect,
                        argumentReadEffect, argumentWriteEffect);
            }
            return false;
        }
        var model = compilation.GetSemanticModel(syntax.SyntaxTree);
        if (method.MethodKind == MethodKind.Constructor &&
            syntax is TypeDeclarationSyntax { ParameterList: not null } primaryType &&
            IsEffectFreePrimaryConstructor(primaryType, model))
            return true;
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
            receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect,
            argumentReadEffect, argumentWriteEffect);
        var preservesFresh = CanPreserveFreshArguments(
            remappedSource, site, receiver, builder, receiverWriteEffect);
        if (hasContract) {
            var remappedContract = AddCallEffects(
                contracted, site, method, "effect_contract", receiver, builder,
                receiverReadEffect, receiverWriteEffect, capturedReadEffect, capturedWriteEffect,
                argumentReadEffect, argumentWriteEffect);
            preservesFresh &= CanPreserveFreshArguments(
                remappedContract, site, receiver, builder, receiverWriteEffect);
        }
        return preservesFresh;
    }
    private static bool IsEffectFreePrimaryConstructor(
        TypeDeclarationSyntax declaration,
        SemanticModel model) {
        if (declaration.BaseList?.Types.Any(type => type is PrimaryConstructorBaseTypeSyntax) == true)
            return false;
        var initializers = declaration.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(field => field.Declaration.Variables)
            .Select(variable => variable.Initializer)
            .Where(initializer => initializer != null)
            .Cast<EqualsValueClauseSyntax>()
            .Concat(declaration.Members
                .OfType<PropertyDeclarationSyntax>()
                .Select(property => property.Initializer)
                .Where(initializer => initializer != null)
                .Cast<EqualsValueClauseSyntax>());
        foreach (var initializer in initializers) {
            if (model.GetOperation(initializer.Value) is not { } operation ||
                !IsEffectFreePrimaryConstructorInitializer(operation))
                return false;
        }
        return true;
    }
    private static bool IsEffectFreePrimaryConstructorInitializer(IOperation operation) {
        while (operation is IConversionOperation conversion) operation = conversion.Operand;
        if (operation is IParenthesizedOperation parenthesized)
            return IsEffectFreePrimaryConstructorInitializer(parenthesized.Operand);
        return operation switch {
            IParameterReferenceOperation or ILiteralOperation or IDefaultValueOperation => true,
            IConditionalOperation { WhenFalse: { } whenFalse } conditional =>
                IsEffectFreePrimaryConstructorInitializer(conditional.Condition) &&
                IsEffectFreePrimaryConstructorInitializer(conditional.WhenTrue) &&
                IsEffectFreePrimaryConstructorInitializer(whenFalse),
            ICoalesceOperation coalesce =>
                IsEffectFreePrimaryConstructorInitializer(coalesce.Value) &&
                IsEffectFreePrimaryConstructorInitializer(coalesce.WhenNull),
            _ => false
        };
    }
    private static INamedTypeSymbol? GetReturnedExactResultType(
        IMethodSymbol method,
        IOperation callSite,
        Builder builder) {
        var targetMethod = method.ReducedFrom ?? method;
        targetMethod = (targetMethod.PartialImplementationPart ?? targetMethod).OriginalDefinition;
        var declaration = targetMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var expressions = GetDirectReturnExpressions(declaration);
        if (expressions.IsDefaultOrEmpty || callSite.SemanticModel?.Compilation is not { } compilation)
            return null;
        INamedTypeSymbol? exactType = null;
        foreach (var expression in expressions) {
            var model = compilation.GetSemanticModel(expression.SyntaxTree);
            var candidate = GetExactReturnedExpressionType(
                expression, model, targetMethod, callSite, builder);
            if (candidate == null) return null;
            if (exactType != null && !SymbolEqualityComparer.Default.Equals(exactType, candidate)) return null;
            exactType = candidate;
        }
        return exactType;
    }
    private static INamedTypeSymbol? GetExactReturnedExpressionType(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        IMethodSymbol targetMethod,
        IOperation callSite,
        Builder builder) {
        expression = UnwrapReturnExpression(expression);
        if (expression is ConditionalExpressionSyntax conditional) {
            var whenTrue = GetExactReturnedExpressionType(
                conditional.WhenTrue, semanticModel, targetMethod, callSite, builder);
            var whenFalse = GetExactReturnedExpressionType(
                conditional.WhenFalse, semanticModel, targetMethod, callSite, builder);
            return whenTrue != null && SymbolEqualityComparer.Default.Equals(whenTrue, whenFalse)
                ? whenTrue
                : null;
        }
        if (expression is SwitchExpressionSyntax switchExpression) {
            INamedTypeSymbol? exactType = null;
            foreach (var arm in switchExpression.Arms) {
                var candidate = GetExactReturnedExpressionType(
                    arm.Expression, semanticModel, targetMethod, callSite, builder);
                if (candidate == null) return null;
                if (exactType != null && !SymbolEqualityComparer.Default.Equals(exactType, candidate)) return null;
                exactType = candidate;
            }
            return exactType;
        }
        if (semanticModel.GetSymbolInfo(expression).Symbol is IParameterSymbol parameter &&
            callSite is IInvocationOperation invocation) {
            IOperation? source = invocation.Arguments.FirstOrDefault(argument =>
                string.Equals(argument.Parameter?.Name, parameter.Name, StringComparison.Ordinal))?.Value;
            if (source == null && parameter.Ordinal == 0 && invocation.TargetMethod.ReducedFrom != null)
                source = invocation.Instance;
            return GetOperationExactType(source, builder);
        }
        return expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax
            ? semanticModel.GetTypeInfo(expression).Type as INamedTypeSymbol
            : null;
    }
    private static INamedTypeSymbol? GetOperationExactType(IOperation? operation, Builder builder) {
        while (operation is IConversionOperation conversion) operation = conversion.Operand;
        if (operation is IParenthesizedOperation parenthesized) operation = parenthesized.Operand;
        return operation switch {
            IObjectCreationOperation { Type: INamedTypeSymbol createdType } => createdType,
            ILocalReferenceOperation local => builder.GetExactType(local.Local),
            IInvocationOperation invocation =>
                GetReturnedExactResultType(invocation.TargetMethod, invocation, builder),
            IPropertyReferenceOperation { Property.GetMethod: { } getter } property =>
                GetReturnedExactResultType(getter, property, builder),
            IConditionalOperation conditional => GetCommonExactType(
                GetOperationExactType(conditional.WhenTrue, builder),
                GetOperationExactType(conditional.WhenFalse, builder)),
            ICoalesceOperation coalesce => GetCommonExactType(
                GetOperationExactType(coalesce.Value, builder),
                GetOperationExactType(coalesce.WhenNull, builder)),
            _ => null
        };
    }
    private static INamedTypeSymbol? GetCommonExactType(
        INamedTypeSymbol? left,
        INamedTypeSymbol? right) =>
        left != null && SymbolEqualityComparer.Default.Equals(left, right) ? left : null;
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
        SharpProofEffect? capturedWriteEffect = null,
        SharpProofEffect? argumentReadEffect = null,
        SharpProofEffect? argumentWriteEffect = null) {
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
            remapped |= argumentReadEffect ?? GetArgumentEffect(site, builder, write: false);
        }
        if ((effects.Effects & SharpProofEffect.WritesArgumentState) != 0) {
            remapped |= argumentWriteEffect ?? GetArgumentEffect(site, builder, write: true);
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
            case IPropertyReferenceOperation property:
                foreach (var argument in property.Arguments)
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
        var isListType = typeDefinition == "System.Collections.Generic.List<T>";
        var isDictionaryType = typeDefinition == "System.Collections.Generic.Dictionary<TKey, TValue>";
        var isCollectionIndexerGet = (isListType || isDictionaryType) &&
                                     (method.MethodKind == MethodKind.PropertyGet &&
                                      method.AssociatedSymbol is IPropertySymbol { IsIndexer: true } ||
                                      string.Equals(method.Name, "get_Item", StringComparison.Ordinal));
        var isCollectionIndexerSet = (isListType || isDictionaryType) &&
                                     (method.MethodKind == MethodKind.PropertySet &&
                                      method.AssociatedSymbol is IPropertySymbol { IsIndexer: true } ||
                                      string.Equals(method.Name, "set_Item", StringComparison.Ordinal));
        var isSpanToArray = string.Equals(method.Name, "ToArray", StringComparison.Ordinal) &&
                            typeDefinition is "System.Span<T>" or "System.ReadOnlySpan<T>";
        if (isCollectionIndexerGet) {
            effects = new MethodEffects(
                SharpProofEffect.ReadsReceiverState,
                SharpProofCapability.None,
                [],
                [],
                []);
            return true;
        }
        if (isCollectionIndexerSet) {
            effects = new MethodEffects(
                SharpProofEffect.WritesReceiverState,
                SharpProofCapability.None,
                [],
                [],
                []);
            return true;
        }
        if (isListType && (method.MethodKind == MethodKind.Constructor ||
                           string.Equals(method.Name, "Add", StringComparison.Ordinal) &&
                           method.Parameters.Length == 1)) {
            effects = new MethodEffects(
                SharpProofEffect.WritesReceiverState |
                (method.MethodKind == MethodKind.Constructor ? SharpProofEffect.None : SharpProofEffect.Allocates),
                SharpProofCapability.None,
                [],
                [],
                []);
            return true;
        }
        if (isDictionaryType &&
            (method.MethodKind == MethodKind.Constructor ||
             string.Equals(method.Name, "Add", StringComparison.Ordinal) &&
             method.Parameters.Length == 2)) {
            effects = new MethodEffects(
                SharpProofEffect.WritesReceiverState |
                (method.MethodKind == MethodKind.Constructor ? SharpProofEffect.None : SharpProofEffect.Allocates),
                SharpProofCapability.None,
                [],
                [],
                []);
            return true;
        }
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
    private void AnalyzeDisposal(
        ITypeSymbol? type,
        IOperation site,
        Builder builder,
        bool asynchronous,
        SemanticModel semanticModel,
        IOperation? receiver) {
        if (type is not INamedTypeSymbol named) return;
        var interfaceName = asynchronous ? "System.IAsyncDisposable" : "System.IDisposable";
        var methodName = asynchronous ? "DisposeAsync" : "Dispose";
        var disposable = compilation.GetTypeByMetadataName(interfaceName);
        var member = disposable?.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
        var implementation = member == null ? null : named.FindImplementationForInterfaceMember(member) as IMethodSymbol;
        implementation ??= named.GetMembers(methodName).OfType<IMethodSymbol>()
            .FirstOrDefault(static method => !method.IsStatic && method.Parameters.Length == 0);
        if (implementation == null) return;
        AnalyzeCall(implementation, site, builder, receiver);
        if (asynchronous) AnalyzeAwaitableProtocol(implementation.ReturnType, site, semanticModel, builder);
    }
    private void AnalyzeAwaitableProtocol(
        ITypeSymbol awaitableType,
        IOperation site,
        SemanticModel semanticModel,
        Builder builder) {
        var awaitableDefinition = awaitableType.OriginalDefinition.ToDisplayString();
        if (awaitableDefinition is "System.Threading.Tasks.Task" or
            "System.Threading.Tasks.Task<TResult>" or
            "System.Threading.Tasks.ValueTask" or
            "System.Threading.Tasks.ValueTask<TResult>")
            return;
        var getAwaiter = FindProtocolMethod(awaitableType, "GetAwaiter") ??
                         ResolveExtensionAwaiter(awaitableType, site, semanticModel);
        AnalyzeCall(
            getAwaiter,
            site,
            builder,
            receiverReadEffect: SharpProofEffect.None,
            receiverWriteEffect: SharpProofEffect.WritesFreshOwnedState);
        if (getAwaiter?.ReturnType is not { } awaiterType) return;
        AnalyzeCall(
            FindProtocolProperty(awaiterType, "IsCompleted")?.GetMethod,
            site,
            builder,
            receiverReadEffect: SharpProofEffect.None,
            receiverWriteEffect: SharpProofEffect.WritesFreshOwnedState);
        AnalyzeCall(
            FindProtocolMethod(awaiterType, "GetResult"),
            site,
            builder,
            receiverReadEffect: SharpProofEffect.None,
            receiverWriteEffect: SharpProofEffect.WritesFreshOwnedState);
        AnalyzeCall(
            FindProtocolMethod(awaiterType, "UnsafeOnCompleted", parameterCount: 1) ??
            FindProtocolMethod(awaiterType, "OnCompleted", parameterCount: 1),
            site,
            builder,
            receiverReadEffect: SharpProofEffect.None,
            receiverWriteEffect: SharpProofEffect.WritesFreshOwnedState);
    }
    private static IMethodSymbol? ResolveExtensionAwaiter(
        ITypeSymbol awaitableType,
        IOperation site,
        SemanticModel semanticModel) {
        var receiver = SyntaxFactory.DefaultExpression(SyntaxFactory.ParseTypeName(
            awaitableType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
        var invocation = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                SyntaxFactory.IdentifierName("GetAwaiter")));
        return semanticModel.GetSpeculativeSymbolInfo(
            site.Syntax.SpanStart,
            invocation,
            SpeculativeBindingOption.BindAsExpression).Symbol as IMethodSymbol;
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
    private void AddWrite(IOperation target, Builder builder) {
        if (target.Syntax.Ancestors().Any(static syntax =>
                syntax is InitializerExpressionSyntax or WithExpressionSyntax or
                    AnonymousObjectCreationExpressionSyntax)) {
            builder.Add(SharpProofEffect.WritesFreshOwnedState, target, target.Type, "fresh_owned_write");
            return;
        }
        switch (target) {
            case IConditionalOperation conditional:
                AddWrite(conditional.WhenTrue, builder);
                if (conditional.WhenFalse != null) AddWrite(conditional.WhenFalse, builder);
                else builder.Add(SharpProofEffect.Unknown, conditional, conditional.Type,
                    "conditional_ref_target_unavailable");
                break;
            case ILocalReferenceOperation local when local.Local.RefKind != RefKind.None:
                builder.Add(GetInstanceWriteEffect(local, builder), local, local.Local, "ref_local_write");
                break;
            case IFieldReferenceOperation { Field.IsStatic: true } field:
                builder.Add(SharpProofEffect.WritesStaticState, field, field.Field, "static_field_write");
                break;
            case IFieldReferenceOperation field:
                builder.Add(GetInstanceWriteEffect(field.Instance, builder), field, field.Field, "instance_field_write");
                break;
            case IPropertyReferenceOperation { Property.GetMethod: { } getter } property
                when getter.ReturnsByRef || getter.ReturnsByRefReadonly:
                builder.Add(GetRefReturnWriteEffect(getter, property.Instance, property, builder), property,
                    property.Property, "ref_return_property_write");
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
            case IInvocationOperation invocation when invocation.TargetMethod.ReturnsByRef ||
                                                      invocation.TargetMethod.ReturnsByRefReadonly:
                builder.Add(GetRefReturnWriteEffect(
                        invocation.TargetMethod, invocation.Instance, invocation, builder),
                    invocation, invocation.TargetMethod, "ref_return_write");
                break;
            case var pointer when IsPointerIndirection(pointer) &&
                                  pointer.ChildOperations.FirstOrDefault() is { } pointerOperand:
                builder.Add(GetInstanceWriteEffect(pointerOperand, builder), pointer, pointer.Type,
                    "pointer_indirection_write");
                break;
        }
    }
    private SharpProofEffect GetRefReturnWriteEffect(
        IMethodSymbol targetMethod,
        IOperation? receiver,
        IOperation site,
        Builder builder) {
        var method = targetMethod.OriginalDefinition;
        var syntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
        if (syntax != null) {
            var model = compilation.GetSemanticModel(syntax.SyntaxTree);
            var root = MethodBodyOperationResolver.GetMethodBodyRootOperation(
                syntax, model, cancellationToken, includeConversionOperators: true);
            if (root != null) {
                var returnedValues = root.DescendantsAndSelf()
                    .OfType<IReturnOperation>()
                    .Select(static returned => returned.ReturnedValue)
                    .Where(static value => value != null)
                    .Cast<IOperation>()
                    .ToArray();
                if (returnedValues.Length == 0) returnedValues = [root];
                var relative = returnedValues.Aggregate(
                    SharpProofEffect.None,
                    static (effect, value) => effect | GetRelativeRefWriteEffect(value));
                var remapped = relative & ~(SharpProofEffect.WritesReceiverState |
                                            SharpProofEffect.WritesArgumentState);
                if ((relative & SharpProofEffect.WritesReceiverState) != 0)
                    remapped |= receiver != null
                        ? GetInstanceWriteEffect(receiver, builder)
                        : SharpProofEffect.Unknown;
                if ((relative & SharpProofEffect.WritesArgumentState) != 0)
                    remapped |= GetArgumentEffect(site, builder, write: true);
                return remapped == SharpProofEffect.None ? SharpProofEffect.Unknown : remapped;
            }
        }
        var fallback = SharpProofEffect.Unknown;
        if (receiver != null) fallback |= GetInstanceWriteEffect(receiver, builder);
        fallback |= GetArgumentEffect(site, builder, write: true);
        return fallback;
    }
    private static SharpProofEffect GetRelativeRefWriteEffect(IOperation value) {
        while (value is IConversionOperation conversion) value = conversion.Operand;
        return value switch {
            IParameterReferenceOperation => SharpProofEffect.WritesArgumentState,
            IInstanceReferenceOperation => SharpProofEffect.WritesReceiverState,
            IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.WritesStaticState,
            IFieldReferenceOperation field when field.Instance != null => GetRelativeRefWriteEffect(field.Instance),
            IArrayElementReferenceOperation array => GetRelativeRefWriteEffect(array.ArrayReference),
            IInlineArrayAccessOperation inlineArray => GetRelativeRefWriteEffect(inlineArray.Instance),
            IImplicitIndexerReferenceOperation implicitIndexer => GetRelativeRefWriteEffect(implicitIndexer.Instance),
            IConditionalOperation conditional =>
                GetRelativeRefWriteEffect(conditional.WhenTrue) |
                (conditional.WhenFalse == null
                    ? SharpProofEffect.Unknown
                    : GetRelativeRefWriteEffect(conditional.WhenFalse)),
            IOperation pointer when IsPointerIndirection(pointer) &&
                                    pointer.ChildOperations.FirstOrDefault() is { } operand =>
                GetRelativeRefWriteEffect(operand),
            _ => SharpProofEffect.Unknown
        };
    }
    private static bool IsPointerIndirection(IOperation operation) =>
        operation.Syntax.IsKind(SyntaxKind.PointerIndirectionExpression);
    private static SharpProofEffect GetStorageWriteEffect(IOperation target, Builder builder) => target switch {
        IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.WritesStaticState,
        IFieldReferenceOperation field => GetInstanceWriteEffect(field.Instance, builder),
        IPropertyReferenceOperation { Property.IsStatic: true } => SharpProofEffect.WritesStaticState,
        IPropertyReferenceOperation property => GetInstanceWriteEffect(property.Instance, builder),
        IArrayElementReferenceOperation array => GetInstanceWriteEffect(array.ArrayReference, builder),
        IInlineArrayAccessOperation inlineArray => GetInstanceWriteEffect(inlineArray.Instance, builder),
        IImplicitIndexerReferenceOperation implicitIndexer =>
            GetInstanceWriteEffect(implicitIndexer.Instance, builder),
        IConditionalOperation conditional =>
            GetStorageWriteEffect(conditional.WhenTrue, builder) |
            (conditional.WhenFalse == null
                ? SharpProofEffect.Unknown
                : GetStorageWriteEffect(conditional.WhenFalse, builder)),
        IOperation pointer when IsPointerIndirection(pointer) &&
                                pointer.ChildOperations.FirstOrDefault() is { } operand =>
            GetInstanceWriteEffect(operand, builder),
        _ => GetInstanceWriteEffect(target, builder)
    };
    private static SharpProofEffect GetStorageReadEffect(IOperation target, Builder builder) => target switch {
        IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.ReadsStaticState,
        IFieldReferenceOperation field => GetInstanceReadEffect(field.Instance, builder),
        IPropertyReferenceOperation { Property.IsStatic: true } => SharpProofEffect.ReadsStaticState,
        IPropertyReferenceOperation property => GetInstanceReadEffect(property.Instance, builder),
        IArrayElementReferenceOperation array => GetInstanceReadEffect(array.ArrayReference, builder),
        IInlineArrayAccessOperation inlineArray => GetInstanceReadEffect(inlineArray.Instance, builder),
        IImplicitIndexerReferenceOperation implicitIndexer =>
            GetInstanceReadEffect(implicitIndexer.Instance, builder),
        IConditionalOperation conditional =>
            GetStorageReadEffect(conditional.WhenTrue, builder) |
            (conditional.WhenFalse == null
                ? SharpProofEffect.Unknown
                : GetStorageReadEffect(conditional.WhenFalse, builder)),
        IOperation pointer when IsPointerIndirection(pointer) &&
                                pointer.ChildOperations.FirstOrDefault() is { } operand =>
            GetInstanceReadEffect(operand, builder),
        _ => GetInstanceReadEffect(target, builder)
    };
    private static SharpProofEffect GetInstanceWriteEffect(IOperation? instance, Builder builder) {
        if (builder.TryGetRefLocalEffects(instance, out _, out var refWriteEffect)) return refWriteEffect;
        if (builder.TryGetFreshRootOrigin(instance, out var origin)) return GetWriteEffect(origin);
        return instance switch {
            IObjectCreationOperation or IArrayCreationOperation or IAnonymousObjectCreationOperation or
                IDelegateCreationOperation => SharpProofEffect.WritesFreshOwnedState,
            ICollectionExpressionOperation collection when collection.Type is IArrayTypeSymbol ||
                                                           collection.Type?.IsReferenceType == true =>
                SharpProofEffect.WritesFreshOwnedState,
            IInstanceReferenceOperation => SharpProofEffect.WritesReceiverState,
            IParameterReferenceOperation => SharpProofEffect.WritesArgumentState,
            IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.WritesStaticState,
            IFieldReferenceOperation field => GetInstanceWriteEffect(field.Instance, builder),
            IPropertyReferenceOperation { Property.IsStatic: true } => SharpProofEffect.WritesStaticState,
            IPropertyReferenceOperation property => GetPropertyResultEffect(property, builder, write: true),
            IArrayElementReferenceOperation array => GetInstanceWriteEffect(array.ArrayReference, builder),
            IInlineArrayAccessOperation inlineArray => GetInstanceWriteEffect(inlineArray.Instance, builder),
            IImplicitIndexerReferenceOperation implicitIndexer =>
                GetInstanceWriteEffect(implicitIndexer.Instance, builder),
            ICoalesceOperation coalesce =>
                GetInstanceWriteEffect(coalesce.Value, builder) |
                GetInstanceWriteEffect(coalesce.WhenNull, builder),
            ISimpleAssignmentOperation assignment => GetInstanceWriteEffect(assignment.Value, builder),
            ICoalesceAssignmentOperation assignment =>
                GetInstanceWriteEffect(assignment.Target, builder) |
                GetInstanceWriteEffect(assignment.Value, builder),
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.Aggregate(
                SharpProofEffect.None,
                (effects, arm) => effects | GetInstanceWriteEffect(arm.Value, builder)),
            IConditionalOperation conditional =>
                GetInstanceWriteEffect(conditional.WhenTrue, builder) |
                GetInstanceWriteEffect(conditional.WhenFalse, builder),
            IConditionalAccessInstanceOperation conditionalAccess =>
                GetInstanceWriteEffect(FindConditionalAccessReceiver(conditionalAccess), builder),
            IInvocationOperation invocation => GetInvocationResultEffect(invocation, builder, write: true),
            IOperation pointer when IsPointerIndirection(pointer) =>
                GetInstanceWriteEffect(pointer.ChildOperations.FirstOrDefault(), builder),
            IParenthesizedOperation parenthesized => GetInstanceWriteEffect(parenthesized.Operand, builder),
            IConversionOperation conversion => GetInstanceWriteEffect(conversion.Operand, builder),
            ILocalReferenceOperation => SharpProofEffect.WritesCapturedState,
            _ => SharpProofEffect.Unknown
        };
    }
    private static SharpProofEffect GetInstanceReadEffect(IOperation? instance, Builder builder) {
        if (builder.TryGetRefLocalEffects(instance, out var refReadEffect, out _)) return refReadEffect;
        if (builder.TryGetFreshRootOrigin(instance, out var origin)) return GetReadEffect(origin);
        if (instance is { Type.SpecialType: SpecialType.System_String } or { Type.IsValueType: true })
            return SharpProofEffect.None;
        return instance switch {
            IObjectCreationOperation or IArrayCreationOperation or IAnonymousObjectCreationOperation or
                IDelegateCreationOperation => SharpProofEffect.None,
            ICollectionExpressionOperation collection when collection.Type is IArrayTypeSymbol ||
                                                           collection.Type?.IsReferenceType == true =>
                SharpProofEffect.None,
            IInstanceReferenceOperation => SharpProofEffect.ReadsReceiverState,
            IParameterReferenceOperation => SharpProofEffect.ReadsArgumentState,
            IFieldReferenceOperation { Field.IsStatic: true } => SharpProofEffect.ReadsStaticState,
            IFieldReferenceOperation field => GetInstanceReadEffect(field.Instance, builder),
            IPropertyReferenceOperation { Property.IsStatic: true } => SharpProofEffect.ReadsStaticState,
            IPropertyReferenceOperation property => GetPropertyResultEffect(property, builder, write: false),
            IArrayElementReferenceOperation array => GetInstanceReadEffect(array.ArrayReference, builder),
            IInlineArrayAccessOperation inlineArray => GetInstanceReadEffect(inlineArray.Instance, builder),
            IImplicitIndexerReferenceOperation implicitIndexer =>
                GetInstanceReadEffect(implicitIndexer.Instance, builder),
            ICoalesceOperation coalesce =>
                GetInstanceReadEffect(coalesce.Value, builder) |
                GetInstanceReadEffect(coalesce.WhenNull, builder),
            ISimpleAssignmentOperation assignment => GetInstanceReadEffect(assignment.Value, builder),
            ICoalesceAssignmentOperation assignment =>
                GetInstanceReadEffect(assignment.Target, builder) |
                GetInstanceReadEffect(assignment.Value, builder),
            ISwitchExpressionOperation switchExpression => switchExpression.Arms.Aggregate(
                SharpProofEffect.None,
                (effects, arm) => effects | GetInstanceReadEffect(arm.Value, builder)),
            IConditionalOperation conditional =>
                GetInstanceReadEffect(conditional.WhenTrue, builder) |
                GetInstanceReadEffect(conditional.WhenFalse, builder),
            IConditionalAccessInstanceOperation conditionalAccess =>
                GetInstanceReadEffect(FindConditionalAccessReceiver(conditionalAccess), builder),
            IInvocationOperation invocation => GetInvocationResultEffect(invocation, builder, write: false),
            IOperation pointer when IsPointerIndirection(pointer) =>
                GetInstanceReadEffect(pointer.ChildOperations.FirstOrDefault(), builder),
            IParenthesizedOperation parenthesized => GetInstanceReadEffect(parenthesized.Operand, builder),
            IConversionOperation conversion => GetInstanceReadEffect(conversion.Operand, builder),
            ILocalReferenceOperation => SharpProofEffect.ReadsCapturedState,
            _ => SharpProofEffect.Unknown
        };
    }
    private static SharpProofEffect GetInvocationResultEffect(
        IInvocationOperation invocation,
        Builder builder,
        bool write) {
        var targetMethod = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
        targetMethod = (targetMethod.PartialImplementationPart ?? targetMethod).OriginalDefinition;
        var declaration = targetMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var expressions = GetDirectReturnExpressions(declaration);
        if (expressions.IsDefaultOrEmpty) return SharpProofEffect.Unknown;
        return expressions.Aggregate(
            SharpProofEffect.None,
            (effects, expression) => effects |
                                     GetInvocationReturnedExpressionEffect(
                                         expression, targetMethod, invocation, builder, write));
    }
    private static SharpProofEffect GetInvocationReturnedExpressionEffect(
        ExpressionSyntax expression,
        IMethodSymbol targetMethod,
        IInvocationOperation invocation,
        Builder builder,
        bool write) {
        expression = UnwrapReturnExpression(expression);
        if (expression is ConditionalExpressionSyntax conditional)
            return GetInvocationReturnedExpressionEffect(
                       conditional.WhenTrue, targetMethod, invocation, builder, write) |
                   GetInvocationReturnedExpressionEffect(
                       conditional.WhenFalse, targetMethod, invocation, builder, write);
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression))
            return GetInvocationReturnedExpressionEffect(binary.Left, targetMethod, invocation, builder, write) |
                   GetInvocationReturnedExpressionEffect(binary.Right, targetMethod, invocation, builder, write);
        if (expression is SwitchExpressionSyntax switchExpression)
            return switchExpression.Arms.Aggregate(
                SharpProofEffect.None,
                (effects, arm) => effects |
                                  GetInvocationReturnedExpressionEffect(
                                      arm.Expression, targetMethod, invocation, builder, write));
        if (expression is MemberAccessExpressionSyntax memberAccess) {
            if (GetExpressionSymbol(memberAccess, invocation)?.IsStatic == true)
                return write ? SharpProofEffect.WritesStaticState : SharpProofEffect.ReadsStaticState;
            return GetInvocationReturnedExpressionEffect(
                memberAccess.Expression, targetMethod, invocation, builder, write);
        }
        if (expression is ElementAccessExpressionSyntax elementAccess)
            return GetInvocationReturnedExpressionEffect(
                elementAccess.Expression, targetMethod, invocation, builder, write);
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            return GetInvocationReturnedExpressionEffect(
                conditionalAccess.Expression, targetMethod, invocation, builder, write);
        if (expression is IdentifierNameSyntax identifier) {
            var parameter = targetMethod.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, identifier.Identifier.ValueText, StringComparison.Ordinal));
            if (parameter != null) {
                IOperation? source = invocation.Arguments.FirstOrDefault(argument =>
                    string.Equals(argument.Parameter?.Name, parameter.Name, StringComparison.Ordinal))?.Value;
                if (source == null && parameter.Ordinal == 0 && invocation.TargetMethod.ReducedFrom != null)
                    source = invocation.Instance;
                return write
                    ? GetInstanceWriteEffect(source, builder)
                    : GetInstanceReadEffect(source, builder);
            }
            var member = targetMethod.ContainingType.GetMembers(identifier.Identifier.ValueText)
                .FirstOrDefault(candidate => candidate is IFieldSymbol or IPropertySymbol);
            if (member?.IsStatic == true)
                return write ? SharpProofEffect.WritesStaticState : SharpProofEffect.ReadsStaticState;
            if (member != null)
                return write
                    ? GetInstanceWriteEffect(invocation.Instance, builder)
                    : GetInstanceReadEffect(invocation.Instance, builder);
            if (string.Equals(
                    identifier.Identifier.ValueText,
                    targetMethod.ContainingType.Name,
                    StringComparison.Ordinal))
                return write ? SharpProofEffect.WritesStaticState : SharpProofEffect.ReadsStaticState;
        }
        if (expression is ThisExpressionSyntax)
            return write
                ? GetInstanceWriteEffect(invocation.Instance, builder)
                : GetInstanceReadEffect(invocation.Instance, builder);
        if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
            ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or
            AnonymousObjectCreationExpressionSyntax or CollectionExpressionSyntax)
            return write ? SharpProofEffect.WritesFreshOwnedState : SharpProofEffect.None;
        return SharpProofEffect.Unknown;
    }
    private static SharpProofEffect GetPropertyResultEffect(
        IPropertyReferenceOperation property,
        Builder builder,
        bool write) {
        var getter = property.Property.GetMethod;
        var declaration = getter?.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        var expressions = GetDirectReturnExpressions(declaration);
        if (!expressions.IsDefaultOrEmpty)
            return expressions.Aggregate(
                SharpProofEffect.None,
                (effects, expression) => effects |
                                         GetPropertyReturnedExpressionEffect(
                                             expression, property, builder, write));
        return write
            ? GetInstanceWriteEffect(property.Instance, builder)
            : GetInstanceReadEffect(property.Instance, builder);
    }
    private static SharpProofEffect GetPropertyReturnedExpressionEffect(
        ExpressionSyntax expression,
        IPropertyReferenceOperation property,
        Builder builder,
        bool write) {
        expression = UnwrapReturnExpression(expression);
        if (expression is ConditionalExpressionSyntax conditional)
            return GetPropertyReturnedExpressionEffect(conditional.WhenTrue, property, builder, write) |
                   GetPropertyReturnedExpressionEffect(conditional.WhenFalse, property, builder, write);
        if (expression is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.CoalesceExpression))
            return GetPropertyReturnedExpressionEffect(binary.Left, property, builder, write) |
                   GetPropertyReturnedExpressionEffect(binary.Right, property, builder, write);
        if (expression is SwitchExpressionSyntax switchExpression)
            return switchExpression.Arms.Aggregate(
                SharpProofEffect.None,
                (effects, arm) => effects |
                                  GetPropertyReturnedExpressionEffect(
                                      arm.Expression, property, builder, write));
        if (expression is MemberAccessExpressionSyntax memberAccess) {
            if (GetExpressionSymbol(memberAccess, property)?.IsStatic == true)
                return write ? SharpProofEffect.WritesStaticState : SharpProofEffect.ReadsStaticState;
            return GetPropertyReturnedExpressionEffect(memberAccess.Expression, property, builder, write);
        }
        if (expression is ElementAccessExpressionSyntax elementAccess)
            return GetPropertyReturnedExpressionEffect(elementAccess.Expression, property, builder, write);
        if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            return GetPropertyReturnedExpressionEffect(conditionalAccess.Expression, property, builder, write);
        if (expression is IdentifierNameSyntax identifier) {
            var member = property.Property.ContainingType.GetMembers(identifier.Identifier.ValueText)
                .FirstOrDefault(candidate => candidate is IFieldSymbol or IPropertySymbol);
            if (member?.IsStatic == true)
                return write ? SharpProofEffect.WritesStaticState : SharpProofEffect.ReadsStaticState;
            if (member != null)
                return write
                    ? GetInstanceWriteEffect(property.Instance, builder)
                    : GetInstanceReadEffect(property.Instance, builder);
            if (string.Equals(
                    identifier.Identifier.ValueText,
                    property.Property.ContainingType.Name,
                    StringComparison.Ordinal))
                return write ? SharpProofEffect.WritesStaticState : SharpProofEffect.ReadsStaticState;
        }
        if (expression is ThisExpressionSyntax)
            return write
                ? GetInstanceWriteEffect(property.Instance, builder)
                : GetInstanceReadEffect(property.Instance, builder);
        if (expression is ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax or
            ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or
            AnonymousObjectCreationExpressionSyntax or CollectionExpressionSyntax)
            return write ? SharpProofEffect.WritesFreshOwnedState : SharpProofEffect.None;
        return SharpProofEffect.Unknown;
    }
    private static ISymbol? GetExpressionSymbol(ExpressionSyntax expression, IOperation callSite) {
        if (callSite.SemanticModel?.Compilation is not { } compilation) return null;
        var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
        return semanticModel.GetSymbolInfo(expression).Symbol;
    }
    private static ImmutableArray<ExpressionSyntax> GetDirectReturnExpressions(SyntaxNode? declaration) {
        ExpressionSyntax? expression = declaration switch {
            MethodDeclarationSyntax method => method.ExpressionBody?.Expression,
            LocalFunctionStatementSyntax localFunction => localFunction.ExpressionBody?.Expression,
            OperatorDeclarationSyntax @operator => @operator.ExpressionBody?.Expression,
            ConversionOperatorDeclarationSyntax conversion => conversion.ExpressionBody?.Expression,
            PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
            AccessorDeclarationSyntax accessor => accessor.ExpressionBody?.Expression,
            ArrowExpressionClauseSyntax arrow => arrow.Expression,
            _ => null
        };
        if (expression != null) return [expression];
        var body = declaration switch {
            MethodDeclarationSyntax method => method.Body,
            LocalFunctionStatementSyntax localFunction => localFunction.Body,
            OperatorDeclarationSyntax @operator => @operator.Body,
            ConversionOperatorDeclarationSyntax conversion => conversion.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null
        };
        if (body == null) return [];
        return [.. body.DescendantNodes(node =>
                node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)
            .OfType<ReturnStatementSyntax>()
            .Select(static returned => returned.Expression)
            .OfType<ExpressionSyntax>()];
    }
    private static ExpressionSyntax UnwrapReturnExpression(ExpressionSyntax expression) {
        while (true) {
            switch (expression) {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax suppressed
                    when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    expression = suppressed.Operand;
                    continue;
                default:
                    return expression;
            }
        }
    }
    private static IOperation? FindConditionalAccessReceiver(IOperation operation) {
        for (var parent = operation.Parent; parent != null; parent = parent.Parent)
            if (parent is IConditionalAccessOperation conditionalAccess)
                return conditionalAccess.Operation;
        return null;
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
        for (var current = operation; current != null; current = current.Parent)
            if (current is IInvocationOperation invocation &&
                IsOmittedInvocation(invocation, semanticModel))
                return false;
        for (var parent = operation.Parent; parent != null; parent = parent.Parent) {
            if (parent is INameOfOperation)
                return false;
            if (parent is IConditionalAccessOperation conditionalAccess &&
                IsNullConstant(conditionalAccess.Operation) &&
                !IsWithinOperation(operation, conditionalAccess.Operation))
                return false;
            if (parent is ICoalesceOperation coalesce &&
                IsNonNullConstant(coalesce.Value) &&
                IsWithinOperation(operation, coalesce.WhenNull))
                return false;
            if (parent is IBinaryOperation binary &&
                IsWithinOperation(operation, binary.RightOperand) &&
                binary.LeftOperand.ConstantValue is { HasValue: true, Value: bool left } &&
                (binary.OperatorKind == BinaryOperatorKind.ConditionalAnd && !left ||
                 binary.OperatorKind == BinaryOperatorKind.ConditionalOr && left))
                return false;
        }
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
            else if (current.FirstAncestorOrSelf<SwitchExpressionArmSyntax>() is { } arm &&
                     arm.Parent is SwitchExpressionSyntax switchExpression &&
                     TryGetSelectedConstantSwitchArm(switchExpression, semanticModel, out var selectedArm) &&
                     !ReferenceEquals(arm, selectedArm))
                return false;
        return true;
    }
    private static bool IsWithinOperation(IOperation operation, IOperation root) {
        for (var current = operation; current != null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;
        return false;
    }
    private static bool IsNonNullConstant(IOperation operation) =>
        operation.ConstantValue is { HasValue: true, Value: not null } ||
        operation is IConversionOperation conversion && IsNonNullConstant(conversion.Operand);
    private static bool IsOmittedInvocation(
        IInvocationOperation invocation,
        SemanticModel semanticModel) =>
        invocation.TargetMethod is { IsPartialDefinition: true, PartialImplementationPart: null } ||
        IsOmittedConditionalCall(invocation, semanticModel);
    private static bool IsOmittedConditionalCall(
        IInvocationOperation invocation,
        SemanticModel semanticModel) {
        var symbols = invocation.TargetMethod.GetAttributes()
            .Where(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Diagnostics.ConditionalAttribute")
            .Select(static attribute => attribute.ConstructorArguments.FirstOrDefault().Value as string)
            .Where(static symbol => symbol != null)
            .ToArray();
        if (symbols.Length == 0) return false;
        var defined = semanticModel.SyntaxTree.Options is CSharpParseOptions options
            ? new HashSet<string>(options.PreprocessorSymbolNames, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        foreach (var directive in semanticModel.SyntaxTree.GetRoot()
                     .DescendantNodes(descendIntoTrivia: true)
                     .OfType<DirectiveTriviaSyntax>()) {
            if (directive.SpanStart >= invocation.Syntax.SpanStart) break;
            if (!directive.IsActive) continue;
            if (directive is DefineDirectiveTriviaSyntax define)
                defined.Add(define.Name.ValueText);
            else if (directive is UndefDirectiveTriviaSyntax undefine)
                defined.Remove(undefine.Name.ValueText);
        }
        return !symbols.Any(symbol => defined.Contains(symbol!));
    }
    private static bool TryGetSelectedConstantSwitchArm(
        SwitchExpressionSyntax expression,
        SemanticModel semanticModel,
        out SwitchExpressionArmSyntax selected) {
        selected = null!;
        var governing = semanticModel.GetConstantValue(expression.GoverningExpression);
        if (!governing.HasValue) return false;
        foreach (var arm in expression.Arms) {
            if (!TryMatchConstantPattern(arm.Pattern, governing.Value, semanticModel, out var matches))
                return false;
            if (!matches) continue;
            if (arm.WhenClause != null) {
                var guard = semanticModel.GetConstantValue(arm.WhenClause.Condition);
                if (!guard.HasValue || guard.Value is not bool enabled) return false;
                if (!enabled) continue;
            }
            selected = arm;
            return true;
        }
        return false;
    }
    private static bool TryMatchConstantPattern(
        PatternSyntax pattern,
        object? value,
        SemanticModel semanticModel,
        out bool matches) {
        while (pattern is ParenthesizedPatternSyntax parenthesized) pattern = parenthesized.Pattern;
        if (pattern is DiscardPatternSyntax or VarPatternSyntax) {
            matches = true;
            return true;
        }
        if (pattern is ConstantPatternSyntax constant) {
            var patternValue = semanticModel.GetConstantValue(constant.Expression);
            matches = patternValue.HasValue && Equals(value, patternValue.Value);
            return patternValue.HasValue;
        }
        if (pattern is RelationalPatternSyntax relational) {
            var patternValue = semanticModel.GetConstantValue(relational.Expression);
            if (patternValue.HasValue)
                return TryCompareConstants(value, patternValue.Value, relational.OperatorToken.Kind(), out matches);
            matches = false;
            return false;
        }
        var typeSyntax = pattern switch {
            TypePatternSyntax typePattern => typePattern.Type,
            DeclarationPatternSyntax declarationPattern => declarationPattern.Type,
            _ => null
        };
        if (typeSyntax != null) {
            if (value == null) {
                matches = false;
                return true;
            }
            var valueType = GetConstantType(value, semanticModel.Compilation);
            var patternType = semanticModel.GetTypeInfo(typeSyntax).Type;
            if (valueType == null || patternType == null) {
                matches = false;
                return false;
            }
            var conversion = semanticModel.Compilation.ClassifyConversion(valueType, patternType);
            matches = conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing;
            return true;
        }
        if (pattern is BinaryPatternSyntax binary) {
            if (!TryMatchConstantPattern(binary.Left, value, semanticModel, out var left) ||
                !TryMatchConstantPattern(binary.Right, value, semanticModel, out var right)) {
                matches = false;
                return false;
            }
            if (binary.IsKind(SyntaxKind.AndPattern)) {
                matches = left && right;
                return true;
            }
            if (binary.IsKind(SyntaxKind.OrPattern)) {
                matches = left || right;
                return true;
            }
        }
        if (pattern is UnaryPatternSyntax unary && unary.IsKind(SyntaxKind.NotPattern)) {
            if (TryMatchConstantPattern(unary.Pattern, value, semanticModel, out var nested)) {
                matches = !nested;
                return true;
            }
            matches = false;
            return false;
        }
        matches = false;
        return false;
    }
    private static ITypeSymbol? GetConstantType(object value, Compilation compilation) {
        var specialType = value switch {
            bool => SpecialType.System_Boolean,
            byte => SpecialType.System_Byte,
            sbyte => SpecialType.System_SByte,
            short => SpecialType.System_Int16,
            ushort => SpecialType.System_UInt16,
            int => SpecialType.System_Int32,
            uint => SpecialType.System_UInt32,
            long => SpecialType.System_Int64,
            ulong => SpecialType.System_UInt64,
            char => SpecialType.System_Char,
            float => SpecialType.System_Single,
            double => SpecialType.System_Double,
            decimal => SpecialType.System_Decimal,
            string => SpecialType.System_String,
            _ => SpecialType.None
        };
        return specialType == SpecialType.None ? null : compilation.GetSpecialType(specialType);
    }
    private static bool TryCompareConstants(
        object? left,
        object? right,
        SyntaxKind operatorKind,
        out bool matches) {
        matches = false;
        if (left == null || right == null || !IsNumericConstant(left) || !IsNumericConstant(right))
            return false;
        if (left is float or double || right is float or double) {
            var first = Convert.ToDouble(left, CultureInfo.InvariantCulture);
            var second = Convert.ToDouble(right, CultureInfo.InvariantCulture);
            matches = operatorKind switch {
                SyntaxKind.LessThanToken => first < second,
                SyntaxKind.LessThanEqualsToken => first <= second,
                SyntaxKind.GreaterThanToken => first > second,
                SyntaxKind.GreaterThanEqualsToken => first >= second,
                _ => false
            };
        }
        else {
            var first = Convert.ToDecimal(left, CultureInfo.InvariantCulture);
            var second = Convert.ToDecimal(right, CultureInfo.InvariantCulture);
            matches = operatorKind switch {
                SyntaxKind.LessThanToken => first < second,
                SyntaxKind.LessThanEqualsToken => first <= second,
                SyntaxKind.GreaterThanToken => first > second,
                SyntaxKind.GreaterThanEqualsToken => first >= second,
                _ => false
            };
        }
        return operatorKind is SyntaxKind.LessThanToken or SyntaxKind.LessThanEqualsToken or
            SyntaxKind.GreaterThanToken or SyntaxKind.GreaterThanEqualsToken;
    }
    private static bool IsNumericConstant(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or char or float or double or decimal;
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
                if (label is CaseSwitchLabelSyntax caseLabel) {
                    var caseValue = semanticModel.GetConstantValue(caseLabel.Value);
                    if (!caseValue.HasValue) return false;
                    if (Equals(governing.Value, caseValue.Value)) {
                        selected = section;
                        return true;
                    }
                    continue;
                }
                if (label is CasePatternSwitchLabelSyntax patternLabel) {
                    if (!TryMatchConstantPattern(
                            patternLabel.Pattern, governing.Value, semanticModel, out var matches))
                        return false;
                    if (!matches) continue;
                    if (patternLabel.WhenClause != null) {
                        var guard = semanticModel.GetConstantValue(patternLabel.WhenClause.Condition);
                        if (!guard.HasValue || guard.Value is not bool enabled) return false;
                        if (!enabled) continue;
                    }
                    selected = section;
                    return true;
                }
                return false;
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
            SharpProofEffect? CapturedWriteEffect,
            SharpProofEffect? ArgumentReadEffect,
            SharpProofEffect? ArgumentWriteEffect);
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
        private readonly Dictionary<ILocalSymbol, (SharpProofEffect Read, SharpProofEffect Write)> _refLocalEffects =
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
            if (value is IConditionalAccessInstanceOperation conditionalAccess &&
                FindConditionalAccessReceiver(conditionalAccess) is { } conditionalReceiver)
                value = conditionalReceiver;
            if (value is ILocalReferenceOperation local &&
                _delegateTargets.TryGetValue(local.Local, out var localTargets))
                return localTargets;
            if (value is IInvocationOperation invocation &&
                GetReturnedDelegateTargets(invocation.TargetMethod, invocation) is {
                    IsDefaultOrEmpty: false
                } invocationTargets)
                return invocationTargets;
            if (value is IPropertyReferenceOperation { Property.GetMethod: { } getter } property &&
                GetReturnedDelegateTargets(getter, property) is { IsDefaultOrEmpty: false } propertyTargets)
                return propertyTargets;
            if (value is IDelegateCreationOperation creation) value = creation.Target;
            var target = CreateDelegateTarget(value);
            return target == null ? [] : [target];
        }
        private ImmutableArray<DelegateTarget> GetReturnedDelegateTargets(
            IMethodSymbol method,
            IOperation callSite) {
            var targetMethod = method.ReducedFrom ?? method;
            targetMethod = (targetMethod.PartialImplementationPart ?? targetMethod).OriginalDefinition;
            var declaration = targetMethod.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var expressions = GetDirectReturnExpressions(declaration);
            if (expressions.IsDefaultOrEmpty || callSite.SemanticModel?.Compilation is not { } compilation)
                return [];
            var targets = ImmutableArray.CreateBuilder<DelegateTarget>();
            foreach (var expression in expressions) {
                var model = compilation.GetSemanticModel(expression.SyntaxTree);
                var value = model.GetOperation(expression);
                var returnedTargets = GetReturnedDelegateOperationTargets(value, callSite);
                if (returnedTargets.IsDefaultOrEmpty) return [];
                targets.AddRange(returnedTargets);
            }
            return targets.ToImmutable();
        }
        private ImmutableArray<DelegateTarget> GetReturnedDelegateOperationTargets(
            IOperation? value,
            IOperation callSite) {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is IDelegateCreationOperation creation) value = creation.Target;
            if (value is IConditionalOperation conditional) {
                var whenTrue = GetReturnedDelegateOperationTargets(conditional.WhenTrue, callSite);
                var whenFalse = GetReturnedDelegateOperationTargets(conditional.WhenFalse, callSite);
                return whenTrue.IsDefaultOrEmpty || whenFalse.IsDefaultOrEmpty
                    ? []
                    : whenTrue.AddRange(whenFalse);
            }
            if (value is ISwitchExpressionOperation switchExpression) {
                var targets = ImmutableArray.CreateBuilder<DelegateTarget>();
                foreach (var arm in switchExpression.Arms) {
                    var armTargets = GetReturnedDelegateOperationTargets(arm.Value, callSite);
                    if (armTargets.IsDefaultOrEmpty) return [];
                    targets.AddRange(armTargets);
                }
                return targets.ToImmutable();
            }
            if (value is ICoalesceOperation coalesce) {
                var primary = GetReturnedDelegateOperationTargets(coalesce.Value, callSite);
                var fallback = GetReturnedDelegateOperationTargets(coalesce.WhenNull, callSite);
                return primary.IsDefaultOrEmpty || fallback.IsDefaultOrEmpty
                    ? []
                    : primary.AddRange(fallback);
            }
            IOperation? receiverOverride = value switch {
                IMethodReferenceOperation { Instance: IInstanceReferenceOperation } => callSite switch {
                    IInvocationOperation invocation => invocation.Instance,
                    IPropertyReferenceOperation property => property.Instance,
                    _ => null
                },
                IMethodReferenceOperation { Instance: IParameterReferenceOperation parameter }
                    when callSite is IInvocationOperation invocation =>
                    invocation.Arguments.FirstOrDefault(argument =>
                        string.Equals(
                            argument.Parameter?.Name,
                            parameter.Parameter.Name,
                            StringComparison.Ordinal))?.Value ??
                    (parameter.Parameter.Ordinal == 0 && invocation.TargetMethod.ReducedFrom != null
                        ? invocation.Instance
                        : null),
                _ => null
            };
            var target = value == null ? null : CreateDelegateTarget(value, receiverOverride);
            if (target != null &&
                value is IAnonymousFunctionOperation function &&
                function.Symbol.Parameters.Length == 0 &&
                TryGetReturnedLambdaArgumentEffects(
                    function,
                    callSite,
                    out var argumentReadEffect,
                    out var argumentWriteEffect))
                target = target with {
                    ArgumentReadEffect = argumentReadEffect,
                    ArgumentWriteEffect = argumentWriteEffect
                };
            if (target != null &&
                value is IAnonymousFunctionOperation receiverFunction &&
                TryGetReturnedLambdaReceiverEffects(
                    receiverFunction,
                    callSite,
                    out var receiverReadEffect,
                    out var receiverWriteEffect))
                target = target with {
                    ReceiverReadEffect = receiverReadEffect,
                    ReceiverWriteEffect = receiverWriteEffect
                };
            if (target != null &&
                value is IAnonymousFunctionOperation capturedFunction &&
                TryGetReturnedLambdaCapturedEffects(
                    capturedFunction,
                    callSite,
                    out var capturedReadEffect,
                    out var capturedWriteEffect))
                target = target with {
                    CapturedReadEffect = capturedReadEffect,
                    CapturedWriteEffect = capturedWriteEffect
                };
            return target == null ? [] : [target];
        }
        private bool TryGetReturnedLambdaReceiverEffects(
            IAnonymousFunctionOperation function,
            IOperation callSite,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            var receiver = callSite switch {
                IInvocationOperation invocation => invocation.Instance,
                IPropertyReferenceOperation property => property.Instance,
                _ => null
            };
            if (receiver == null) return false;
            var found = false;
            foreach (var operation in function.Body.DescendantsAndSelf()) {
                if (!BelongsDirectlyTo(function, operation)) continue;
                if (operation is IInstanceReferenceOperation) found = true;
            }
            if (!found) return false;
            readEffect = GetInstanceReadEffect(receiver, this);
            writeEffect = GetInstanceWriteEffect(receiver, this);
            return true;
        }
        private bool TryGetReturnedLambdaArgumentEffects(
            IAnonymousFunctionOperation function,
            IOperation callSite,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            if (callSite is not IInvocationOperation invocation) return false;
            var found = false;
            foreach (var parameter in function.Body.DescendantsAndSelf().OfType<IParameterReferenceOperation>()) {
                if (!BelongsDirectlyTo(function, parameter) ||
                    SymbolEqualityComparer.Default.Equals(
                        parameter.Parameter.ContainingSymbol,
                        function.Symbol))
                    continue;
                IOperation? source = invocation.Arguments.FirstOrDefault(argument =>
                    string.Equals(
                        argument.Parameter?.Name,
                        parameter.Parameter.Name,
                        StringComparison.Ordinal))?.Value;
                if (source == null && parameter.Parameter.Ordinal == 0 && invocation.TargetMethod.ReducedFrom != null)
                    source = invocation.Instance;
                if (source == null) return false;
                found = true;
                readEffect |= GetInstanceReadEffect(source, this);
                writeEffect |= GetInstanceWriteEffect(source, this);
            }
            return found;
        }
        private bool TryGetReturnedLambdaLocalEffects(
            IAnonymousFunctionOperation function,
            IOperation callSite,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            if (callSite.SemanticModel?.Compilation is not { } compilation) return false;
            var locals = function.Body.DescendantsAndSelf()
                .OfType<ILocalReferenceOperation>()
                .Where(local => BelongsDirectlyTo(function, local) &&
                                !SymbolEqualityComparer.Default.Equals(
                                    local.Local.ContainingSymbol,
                                    function.Symbol))
                .Select(local => local.Local)
                .Distinct<ILocalSymbol>(SymbolEqualityComparer.Default)
                .ToArray();
            if (locals.Length == 0) return false;
            var visited = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
            foreach (var local in locals) {
                if (!TryGetCapturedLocalEffects(
                        local,
                        callSite,
                        compilation,
                        visited,
                        out var localReadEffect,
                        out var localWriteEffect))
                    return false;
                readEffect |= localReadEffect;
                writeEffect |= localWriteEffect;
                foreach (var reference in function.Body.DescendantsAndSelf()
                             .OfType<ILocalReferenceOperation>()
                             .Where(reference => BelongsDirectlyTo(function, reference) &&
                                                 SymbolEqualityComparer.Default.Equals(
                                                     reference.Local,
                                                     local))) {
                    if (!TryGetCapturedMemberInitializers(
                            local,
                            reference,
                            compilation,
                            out var hasMemberPath,
                            out var memberInitializers)) {
                        if (hasMemberPath) return false;
                        continue;
                    }
                    foreach (var memberInitializer in memberInitializers) {
                        if (!TryGetCapturedValueEffects(
                                memberInitializer,
                                callSite,
                                compilation,
                                visited,
                                out var memberReadEffect,
                                out var memberWriteEffect))
                            return false;
                        readEffect |= memberReadEffect;
                        writeEffect |= memberWriteEffect;
                    }
                }
            }
            return true;
        }
        private static bool TryGetCapturedMemberInitializers(
            ILocalSymbol local,
            ILocalReferenceOperation reference,
            Compilation compilation,
            out bool hasMemberPath,
            out ImmutableArray<IOperation> initializers) {
            initializers = [];
            var path = new List<string>();
            var hasUnresolvedPath = false;
            IOperation current = reference;
            while (true) {
                switch (current.Parent) {
                    case IFieldReferenceOperation { Instance: { } instance } field
                        when ReferenceEquals(instance, current):
                        path.Add(GetMemberPathPart(field.Field.OriginalDefinition));
                        current = field;
                        continue;
                    case IPropertyReferenceOperation { Instance: { } instance } property
                        when ReferenceEquals(instance, current):
                        if (property.Property.IsIndexer &&
                            property.Arguments.Length == 1 &&
                            TryGetConstantPathPart(
                                property.Arguments[0].Value,
                                "#indexer:",
                                out var indexerPath))
                            path.Add(indexerPath);
                        else if (property.Property.IsIndexer)
                            hasUnresolvedPath = true;
                        else
                            path.Add(GetMemberPathPart(property.Property.OriginalDefinition));
                        current = property;
                        continue;
                    case IArrayElementReferenceOperation { ArrayReference: { } arrayReference } array
                        when ReferenceEquals(arrayReference, current):
                        if (array.Indices.Length == 1 &&
                            array.Indices[0].ConstantValue is { HasValue: true, Value: int arrayIndex })
                            path.Add("#array:" + arrayIndex.ToString(CultureInfo.InvariantCulture));
                        else
                            hasUnresolvedPath = true;
                        current = array;
                        continue;
                }
                break;
            }
            var isInvocationReceiver = current.Parent is IInvocationOperation invocation &&
                                       ReferenceEquals(invocation.Instance, current);
            if (!isInvocationReceiver && path.Count != 0) path.RemoveAt(path.Count - 1);
            hasMemberPath = hasUnresolvedPath || path.Count != 0;
            if (hasUnresolvedPath || !hasMemberPath ||
                !TryGetStableLocalInitializers(
                    local,
                    compilation,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                    out var values))
                return false;
            var members = ImmutableArray.CreateBuilder<IOperation>();
            foreach (var value in values) {
                if (!TryGetObjectInitializerMember(
                        value, path, 0, compilation, out var valueInitializers))
                    return false;
                members.AddRange(valueInitializers);
            }
            initializers = members.ToImmutable();
            return !initializers.IsDefaultOrEmpty;
        }
        private static bool TryGetStableLocalInitializers(
            ILocalSymbol local,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out ImmutableArray<IOperation> initializers) {
            initializers = [];
            if (!visited.Add(local) || local.DeclaringSyntaxReferences.Length != 1) return false;
            try {
                var syntax = local.DeclaringSyntaxReferences[0].GetSyntax();
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                if (model.GetOperation(syntax) is not IVariableDeclaratorOperation declarator ||
                    declarator.Initializer?.Value is not { } value)
                    return false;
                var root = (IOperation)declarator;
                while (root.Parent != null) root = root.Parent;
                if (root.DescendantsAndSelf()
                    .OfType<ILocalReferenceOperation>()
                    .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Local, local) &&
                                      IsDirectLocalWrite(reference)))
                    return false;
                return TryCollectStableInitializerValues(
                    value, compilation, visited, out initializers);
            }
            finally {
                visited.Remove(local);
            }
        }
        private static bool TryCollectStableInitializerValues(
            IOperation value,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out ImmutableArray<IOperation> initializers) {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is IParenthesizedOperation parenthesized)
                return TryCollectStableInitializerValues(
                    parenthesized.Operand, compilation, visited, out initializers);
            if (value is ILocalReferenceOperation source)
                return TryGetStableLocalInitializers(
                    source.Local, compilation, visited, out initializers);
            if (value is IConditionalOperation { WhenFalse: { } whenFalse } conditional)
                return TryCollectCompositeStableInitializerValues(
                    conditional.WhenTrue, whenFalse, compilation, visited, out initializers);
            if (value is ICoalesceOperation coalesce)
                return TryCollectCompositeStableInitializerValues(
                    coalesce.Value, coalesce.WhenNull, compilation, visited, out initializers);
            if (value is ISwitchExpressionOperation switchExpression) {
                var values = ImmutableArray.CreateBuilder<IOperation>();
                foreach (var arm in switchExpression.Arms) {
                    if (!TryCollectStableInitializerValues(
                            arm.Value, compilation, visited, out var armValues)) {
                        initializers = [];
                        return false;
                    }
                    values.AddRange(armValues);
                }
                initializers = values.ToImmutable();
                return !initializers.IsDefaultOrEmpty;
            }
            initializers = [value];
            return true;
        }
        private static bool TryCollectCompositeStableInitializerValues(
            IOperation left,
            IOperation right,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out ImmutableArray<IOperation> initializers) {
            if (!TryCollectStableInitializerValues(left, compilation, visited, out var leftValues) ||
                !TryCollectStableInitializerValues(right, compilation, visited, out var rightValues)) {
                initializers = [];
                return false;
            }
            initializers = leftValues.AddRange(rightValues);
            return true;
        }
        private static bool TryGetObjectInitializerMember(
            IOperation value,
            IReadOnlyList<string> path,
            int index,
            Compilation compilation,
            out ImmutableArray<IOperation> initializers) {
            initializers = [];
            while (value is IConversionOperation conversion) value = conversion.Operand;
            const string indexerPrefix = "#indexer:";
            if (path[index].StartsWith(indexerPrefix, StringComparison.Ordinal)) {
                const string intIndexPrefix = "#indexer:System.Int32:";
                if (value is ICollectionExpressionOperation collection &&
                    TryGetStaticCollectionElements(collection, out var collectionElements) &&
                    path[index].StartsWith(intIndexPrefix, StringComparison.Ordinal) &&
                    int.TryParse(
                        path[index].Substring(intIndexPrefix.Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                    out var collectionIndex) &&
                    collectionIndex >= 0 &&
                    collectionIndex < collectionElements.Length) {
                    var collectionElement = collectionElements[collectionIndex];
                    if (index == path.Count - 1) {
                        initializers = [collectionElement];
                        return true;
                    }
                    return TryGetObjectInitializerMember(
                        collectionElement,
                        path,
                        index + 1,
                        compilation,
                        out initializers);
                }
                if (value is not IObjectCreationOperation { Initializer: { } collectionInitializer })
                    return false;
                var assignedElement = collectionInitializer.Initializers
                    .OfType<ISimpleAssignmentOperation>()
                    .FirstOrDefault(assignment =>
                        assignment.Target is IPropertyReferenceOperation {
                            Property.IsIndexer: true,
                            Arguments.Length: 1
                        } indexer &&
                        TryGetConstantPathPart(
                            indexer.Arguments[0].Value,
                            indexerPrefix,
                            out var keyPath) &&
                        string.Equals(keyPath, path[index], StringComparison.Ordinal))?.Value;
                if (assignedElement != null) {
                    if (index == path.Count - 1) {
                        initializers = [assignedElement];
                        return true;
                    }
                    return TryGetObjectInitializerMember(
                        assignedElement,
                        path,
                        index + 1,
                        compilation,
                        out initializers);
                }
                var additions = collectionInitializer.Initializers
                    .OfType<IInvocationOperation>()
                    .Where(invocation => invocation.Arguments.Length != 0)
                    .ToArray();
                IOperation? element;
                if (additions.Any(invocation => invocation.Arguments.Length >= 2)) {
                    element = additions.FirstOrDefault(invocation =>
                        invocation.Arguments.Length >= 2 &&
                        TryGetConstantPathPart(
                            invocation.Arguments[0].Value,
                            indexerPrefix,
                            out var keyPath) &&
                        string.Equals(keyPath, path[index], StringComparison.Ordinal))?.Arguments[1].Value;
                }
                else {
                    element = path[index].StartsWith(intIndexPrefix, StringComparison.Ordinal) &&
                              int.TryParse(
                                  path[index].Substring(intIndexPrefix.Length),
                                  NumberStyles.None,
                                  CultureInfo.InvariantCulture,
                                  out var elementIndex) &&
                              elementIndex >= 0 && elementIndex < additions.Length
                        ? additions[elementIndex].Arguments[0].Value
                        : null;
                }
                if (element == null) return false;
                if (index == path.Count - 1) {
                    initializers = [element];
                    return true;
                }
                return TryGetObjectInitializerMember(
                    element, path, index + 1, compilation, out initializers);
            }
            const string arrayPrefix = "#array:";
            if (path[index].StartsWith(arrayPrefix, StringComparison.Ordinal)) {
                ImmutableArray<IOperation> elements = value switch {
                    IArrayCreationOperation { Initializer: { } arrayInitializer } =>
                        arrayInitializer.ElementValues,
                    ICollectionExpressionOperation collection
                        when TryGetStaticCollectionElements(collection, out var collectionElements) =>
                        collectionElements,
                    _ => []
                };
                if (!int.TryParse(
                        path[index].Substring(arrayPrefix.Length),
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out var elementIndex) ||
                    elementIndex < 0 ||
                    elementIndex >= elements.Length)
                    return false;
                var element = elements[elementIndex];
                if (index == path.Count - 1) {
                    initializers = [element];
                    return true;
                }
                return TryGetObjectInitializerMember(
                    element, path, index + 1, compilation, out initializers);
            }
            if (value is ITupleOperation tuple && value.Type is INamedTypeSymbol tupleType) {
                for (var elementIndex = 0;
                     elementIndex < tuple.Elements.Length && elementIndex < tupleType.TupleElements.Length;
                     elementIndex++) {
                    var field = tupleType.TupleElements[elementIndex];
                    var matches = string.Equals(
                        GetMemberPathPart(field.OriginalDefinition),
                        path[index],
                        StringComparison.Ordinal);
                    if (!matches && field.CorrespondingTupleField is { } corresponding)
                        matches = string.Equals(
                            GetMemberPathPart(corresponding.OriginalDefinition),
                            path[index],
                            StringComparison.Ordinal);
                    if (!matches) continue;
                    var element = tuple.Elements[elementIndex];
                    if (index == path.Count - 1) {
                        initializers = [element];
                        return true;
                    }
                    return TryGetObjectInitializerMember(
                        element, path, index + 1, compilation, out initializers);
                }
                return false;
            }
            IEnumerable<ISimpleAssignmentOperation> assignments = value switch {
                IObjectCreationOperation { Initializer: { } objectInitializer } =>
                    objectInitializer.Initializers.OfType<ISimpleAssignmentOperation>(),
                IAnonymousObjectCreationOperation anonymousObject =>
                    anonymousObject.Initializers.OfType<ISimpleAssignmentOperation>(),
                _ => []
            };
            foreach (var assignment in assignments) {
                var member = assignment.Target switch {
                    IFieldReferenceOperation field => (ISymbol)field.Field.OriginalDefinition,
                    IPropertyReferenceOperation property => property.Property.OriginalDefinition,
                    _ => null
                };
                if (member == null ||
                    !string.Equals(GetMemberPathPart(member), path[index], StringComparison.Ordinal))
                    continue;
                if (index == path.Count - 1) {
                    initializers = [assignment.Value];
                    return true;
                }
                return TryGetObjectInitializerMember(
                    assignment.Value,
                    path,
                    index + 1,
                    compilation,
                    out initializers);
            }
            if (value is IObjectCreationOperation creation &&
                TryGetConstructorAssignedMember(
                    creation, path[index], compilation, out var constructorValues)) {
                if (index == path.Count - 1) {
                    initializers = constructorValues;
                    return true;
                }
                var nestedInitializers = ImmutableArray.CreateBuilder<IOperation>();
                foreach (var constructorValue in constructorValues) {
                    if (!TryGetObjectInitializerMember(
                            constructorValue,
                            path,
                            index + 1,
                            compilation,
                            out var nested))
                        return false;
                    nestedInitializers.AddRange(nested);
                }
                initializers = nestedInitializers.ToImmutable();
                return !initializers.IsDefaultOrEmpty;
            }
            return false;
        }
        private static bool TryGetConstructorAssignedMember(
            IObjectCreationOperation creation,
            string memberPath,
            Compilation compilation,
            out ImmutableArray<IOperation> values) {
            values = [];
            if (creation.Constructor is not { } constructor) return false;
            var declaration = constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            if (declaration is TypeDeclarationSyntax { ParameterList: not null } primaryType &&
                TryGetPrimaryConstructorMemberInitializer(
                    creation, primaryType, memberPath, compilation, out values)) {
                return true;
            }
            if (declaration == null) {
                return TryGetImplicitConstructorMemberInitializer(
                    constructor,
                    creation,
                    memberPath,
                    compilation,
                    new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                    out values);
            }
            return TryGetConstructorDeclarationMemberOrigins(
                constructor,
                creation,
                declaration,
                memberPath,
                compilation,
                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                out values);
        }
        private static bool TryGetConstructorDeclarationMemberOrigins(
            IMethodSymbol constructor,
            IOperation constructorCallSite,
            SyntaxNode declaration,
            string memberPath,
            Compilation compilation,
            HashSet<IMethodSymbol> visitedConstructors,
            out ImmutableArray<IOperation> values) {
            values = [];
            constructor = constructor.OriginalDefinition;
            if (!visitedConstructors.Add(constructor)) return false;
            try {
                var candidates = GetConstructorMemberAssignments(
                    declaration, memberPath, compilation);
                if (candidates.Length == 0)
                    return TryGetConstructorFallbackMemberOrigins(
                        constructor,
                        constructorCallSite,
                        declaration,
                        memberPath,
                        compilation,
                        visitedConstructors,
                        out values);
                var assignmentSites = candidates
                    .GroupBy(candidate => candidate.Syntax.Span)
                    .Select(group => group.First())
                    .ToArray();
                if (assignmentSites.Length > 1 &&
                    !AreExhaustiveAlternativeAssignments(assignmentSites))
                    return false;
                var needsFallback = assignmentSites.Length == 1 &&
                                    IsPotentiallyConditionalAssignment(assignmentSites[0], declaration);
                var mappedValues = ImmutableArray.CreateBuilder<IOperation>();
                foreach (var candidate in candidates) {
                    IOperation assignedValue = candidate.Value;
                    while (assignedValue is IConversionOperation conversion)
                        assignedValue = conversion.Operand;
                    var visited = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
                    ImmutableArray<IOperation> assignedValues;
                    if (assignedValue is ILocalReferenceOperation local) {
                        if (!TryGetStableLocalInitializers(
                                local.Local, compilation, visited, out assignedValues))
                            return false;
                    }
                    else if (!TryCollectStableInitializerValues(
                                 assignedValue, compilation, visited, out assignedValues))
                        return false;
                    foreach (var assigned in assignedValues) {
                        if (!TryMapConstructorAssignedValue(
                                assigned,
                                constructor,
                                constructorCallSite,
                                compilation,
                                new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                                out var mapped))
                            return false;
                        mappedValues.AddRange(mapped);
                    }
                }
                if (needsFallback) {
                    if (!TryGetConstructorFallbackMemberOrigins(
                            constructor,
                            constructorCallSite,
                            declaration,
                            memberPath,
                            compilation,
                            visitedConstructors,
                            out var fallbackValues))
                        return false;
                    mappedValues.AddRange(fallbackValues);
                }
                values = mappedValues.ToImmutable();
                return !values.IsDefaultOrEmpty;
            }
            finally {
                visitedConstructors.Remove(constructor);
            }
        }
        private static bool IsPotentiallyConditionalAssignment(
            ConstructorMemberAssignment assignment,
            SyntaxNode declaration) => assignment.Syntax.Ancestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, declaration))
            .Any(ancestor => ancestor is IfStatementSyntax or SwitchStatementSyntax or
                ForStatementSyntax or ForEachStatementSyntax or WhileStatementSyntax or
                DoStatementSyntax or TryStatementSyntax);
        private static bool TryGetConstructorFallbackMemberOrigins(
            IMethodSymbol constructor,
            IOperation constructorCallSite,
            SyntaxNode declaration,
            string memberPath,
            Compilation compilation,
            HashSet<IMethodSymbol> visitedConstructors,
            out ImmutableArray<IOperation> values) {
            if (TryGetChainedConstructorMemberOrigins(
                    constructor,
                    constructorCallSite,
                    declaration,
                    memberPath,
                    compilation,
                    visitedConstructors,
                    out values))
                return true;
            if (declaration is ConstructorDeclarationSyntax {
                Initializer.ThisOrBaseKeyword.RawKind: (int)SyntaxKind.ThisKeyword
            }) {
                values = [];
                return false;
            }
            return TryGetDeclaredMemberInitializer(
                declaration, memberPath, compilation, out values);
        }
        private static bool TryGetDeclaredMemberInitializer(
            SyntaxNode declaration,
            string memberPath,
            Compilation compilation,
            out ImmutableArray<IOperation> values) {
            values = [];
            var type = declaration.AncestorsAndSelf().OfType<TypeDeclarationSyntax>().FirstOrDefault();
            if (type == null) return false;
            var model = compilation.GetSemanticModel(type.SyntaxTree);
            var initializers = new List<IOperation>();
            foreach (var member in type.Members) {
                switch (member) {
                    case FieldDeclarationSyntax field:
                        foreach (var variable in field.Declaration.Variables) {
                            if (variable.Initializer?.Value is not { } expression ||
                                !MemberNameMatches(
                                    model.GetDeclaredSymbol(variable),
                                    variable.Identifier.ValueText,
                                    memberPath) ||
                                model.GetOperation(expression) is not { } operation)
                                continue;
                            initializers.Add(operation);
                        }
                        break;
                    case PropertyDeclarationSyntax { Initializer.Value: { } expression } property
                        when MemberNameMatches(
                                 model.GetDeclaredSymbol(property),
                                 property.Identifier.ValueText,
                                 memberPath) &&
                             model.GetOperation(expression) is { } operation:
                        initializers.Add(operation);
                        break;
                }
            }
            if (initializers.Count != 1) return false;
            values = [initializers[0]];
            return true;
        }
        private static bool TryGetImplicitConstructorMemberInitializer(
            IMethodSymbol constructor,
            IOperation constructorCallSite,
            string memberPath,
            Compilation compilation,
            HashSet<IMethodSymbol> visitedConstructors,
            out ImmutableArray<IOperation> values) {
            values = [];
            var current = constructor;
            while (current.IsImplicitlyDeclared) {
                var typeDeclaration = current.ContainingType.DeclaringSyntaxReferences
                    .FirstOrDefault()?.GetSyntax();
                if (typeDeclaration != null &&
                    TryGetDeclaredMemberInitializer(
                        typeDeclaration, memberPath, compilation, out values))
                    return true;
                var baseType = current.ContainingType.BaseType;
                if (baseType == null) return false;
                var next = baseType.InstanceConstructors
                    .Where(candidate => candidate.Parameters.All(parameter => parameter.IsOptional))
                    .OrderBy(candidate => candidate.Parameters.Length)
                    .FirstOrDefault();
                if (next == null) return false;
                if (!next.IsImplicitlyDeclared) {
                    var declaration = next.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    return declaration != null && TryGetConstructorDeclarationMemberOrigins(
                        next,
                        constructorCallSite,
                        declaration,
                        memberPath,
                        compilation,
                        visitedConstructors,
                        out values);
                }
                current = next;
            }
            return false;
        }
        private static bool TryGetChainedConstructorMemberOrigins(
            IMethodSymbol constructor,
            IOperation constructorCallSite,
            SyntaxNode declaration,
            string memberPath,
            Compilation compilation,
            HashSet<IMethodSymbol> visitedConstructors,
            out ImmutableArray<IOperation> values) {
            values = [];
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            if (model.GetOperation(declaration) is not IConstructorBodyOperation body)
                return false;
            var initializer = body.Initializer switch {
                IInvocationOperation invocation => invocation,
                IExpressionStatementOperation { Operation: IInvocationOperation invocation } => invocation,
                _ => null
            };
            if (initializer == null) return false;
            var targetConstructor = initializer.TargetMethod.OriginalDefinition;
            var targetDeclaration = targetConstructor.DeclaringSyntaxReferences
                .FirstOrDefault()?.GetSyntax();
            ImmutableArray<IOperation> chainedValues;
            if (targetDeclaration != null) {
                if (!TryGetConstructorDeclarationMemberOrigins(
                        targetConstructor,
                        initializer,
                        targetDeclaration,
                        memberPath,
                        compilation,
                        visitedConstructors,
                        out chainedValues))
                    return false;
            }
            else {
                if (!TryGetImplicitConstructorMemberInitializer(
                        targetConstructor,
                        initializer,
                        memberPath,
                        compilation,
                        visitedConstructors,
                        out chainedValues))
                    return false;
            }
            var mappedValues = ImmutableArray.CreateBuilder<IOperation>();
            foreach (var chainedValue in chainedValues) {
                if (!TryMapConstructorAssignedValue(
                        chainedValue,
                        constructor,
                        constructorCallSite,
                        compilation,
                        new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default),
                        out var mapped))
                    return false;
                mappedValues.AddRange(mapped);
            }
            values = mappedValues.ToImmutable();
            return !values.IsDefaultOrEmpty;
        }
        private readonly record struct ConstructorMemberAssignment(
            SyntaxNode Syntax,
            IOperation Value);
        private readonly record struct ConstructorValueCallSite(
            IMethodSymbol Method,
            IOperation Operation);
        private static bool AreExhaustiveAlternativeAssignments(
            IReadOnlyList<ConstructorMemberAssignment> assignments) {
            if (assignments.Count != 2) return false;
            var first = assignments[0].Syntax;
            var second = assignments[1].Syntax;
            foreach (var conditional in first.Ancestors().OfType<IfStatementSyntax>()) {
                if (conditional.Else == null ||
                    !second.Ancestors().Contains(conditional))
                    continue;
                var firstInThen = IsSoleBranchAssignment(conditional.Statement, first);
                var firstInElse = IsSoleBranchAssignment(conditional.Else.Statement, first);
                var secondInThen = IsSoleBranchAssignment(conditional.Statement, second);
                var secondInElse = IsSoleBranchAssignment(conditional.Else.Statement, second);
                return firstInThen && secondInElse || firstInElse && secondInThen;
            }
            return false;
        }
        private static bool IsSoleBranchAssignment(StatementSyntax branch, SyntaxNode assignment) {
            while (branch is BlockSyntax { Statements.Count: 1 } block)
                branch = block.Statements[0];
            return branch is ExpressionStatementSyntax expression &&
                   ReferenceEquals(expression.Expression, assignment);
        }
        private static bool TryMapConstructorAssignedValue(
            IOperation value,
            IMethodSymbol constructor,
            IOperation constructorCallSite,
            Compilation compilation,
            HashSet<IMethodSymbol> visitedMethods,
            out ImmutableArray<IOperation> values) {
            values = [];
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is IParameterReferenceOperation parameter &&
                SymbolEqualityComparer.Default.Equals(
                    parameter.Parameter.ContainingSymbol.OriginalDefinition,
                    constructor.OriginalDefinition)) {
                var argument = GetConstructorCallArgument(
                    constructorCallSite, parameter.Parameter.Name);
                if (argument == null) return false;
                values = [argument];
                return true;
            }
            if (value is IInvocationOperation invocation)
                return TryGetConstructorHelperOrigins(
                    invocation,
                    constructor,
                    constructorCallSite,
                    compilation,
                    visitedMethods,
                    out values);
            values = [value];
            return true;
        }
        private static bool TryGetConstructorHelperOrigins(
            IInvocationOperation invocation,
            IMethodSymbol constructor,
            IOperation constructorCallSite,
            Compilation compilation,
            HashSet<IMethodSymbol> visitedMethods,
            out ImmutableArray<IOperation> values) {
            values = [];
            var method = invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod;
            method = (method.PartialImplementationPart ?? method).OriginalDefinition;
            if (!visitedMethods.Add(method)) return false;
            try {
                var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                var expressions = GetDirectReturnExpressions(declaration);
                if (expressions.IsDefaultOrEmpty) return false;
                var mappedValues = ImmutableArray.CreateBuilder<IOperation>();
                foreach (var expression in expressions) {
                    var model = compilation.GetSemanticModel(expression.SyntaxTree);
                    if (model.GetOperation(expression) is not { } returnedValue ||
                        !TryCollectStableInitializerValues(
                            returnedValue,
                            compilation,
                            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                            out var returnedValues))
                        return false;
                    foreach (var returned in returnedValues) {
                        var mapped = returned;
                        while (mapped is IConversionOperation conversion)
                            mapped = conversion.Operand;
                        if (mapped is IParameterReferenceOperation parameter &&
                            SymbolEqualityComparer.Default.Equals(
                                parameter.Parameter.ContainingSymbol.OriginalDefinition,
                                method)) {
                            mapped = invocation.Arguments.FirstOrDefault(argument =>
                                string.Equals(
                                    argument.Parameter?.Name,
                                    parameter.Parameter.Name,
                                    StringComparison.Ordinal))?.Value!;
                            if (mapped == null) return false;
                        }
                        else if (mapped is IInstanceReferenceOperation) {
                            mapped = invocation.Instance!;
                            if (mapped == null) return false;
                        }
                        if (!TryMapConstructorAssignedValue(
                                mapped,
                                constructor,
                                constructorCallSite,
                                compilation,
                                visitedMethods,
                                out var origins))
                            return false;
                        mappedValues.AddRange(origins);
                    }
                }
                values = mappedValues.ToImmutable();
                return !values.IsDefaultOrEmpty;
            }
            finally {
                visitedMethods.Remove(method);
            }
        }
        private static IOperation? GetConstructorCallArgument(
            IOperation callSite,
            string parameterName) => callSite switch {
                IObjectCreationOperation creation => creation.Arguments.FirstOrDefault(argument =>
                    string.Equals(
                        argument.Parameter?.Name,
                        parameterName,
                        StringComparison.Ordinal))?.Value,
                IInvocationOperation invocation => invocation.Arguments.FirstOrDefault(argument =>
                    string.Equals(
                        argument.Parameter?.Name,
                        parameterName,
                        StringComparison.Ordinal))?.Value,
                IPropertyReferenceOperation property => property.Arguments.FirstOrDefault(argument =>
                    string.Equals(
                        argument.Parameter?.Name,
                        parameterName,
                        StringComparison.Ordinal))?.Value,
                _ => null
            };
        private static ConstructorMemberAssignment[] GetConstructorMemberAssignments(
            SyntaxNode declaration,
            string memberPath,
            Compilation compilation) {
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var assignments = new List<ConstructorMemberAssignment>();
            foreach (var syntax in declaration.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(node => !node.Ancestors()
                    .TakeWhile(ancestor => !ReferenceEquals(ancestor, declaration))
                    .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or
                        LocalFunctionStatementSyntax))) {
                switch (model.GetOperation(syntax)) {
                    case ISimpleAssignmentOperation assignment
                        when ConstructorTargetMatches(assignment.Target, memberPath):
                        assignments.Add(new ConstructorMemberAssignment(syntax, assignment.Value));
                        break;
                    case IDeconstructionAssignmentOperation deconstruction:
                        AddDeconstructionMemberAssignments(
                            deconstruction.Target,
                            deconstruction.Value,
                            syntax,
                            memberPath,
                            compilation,
                            new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                            [],
                            assignments);
                        break;
                }
            }
            return [.. assignments];
        }
        private static void AddDeconstructionMemberAssignments(
            IOperation target,
            IOperation value,
            SyntaxNode syntax,
            string memberPath,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            ImmutableArray<ConstructorValueCallSite> callSites,
            List<ConstructorMemberAssignment> assignments) {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is IParameterReferenceOperation parameter) {
                for (var index = 0; index < callSites.Length; index++) {
                    var callSite = callSites[index];
                    if (!SymbolEqualityComparer.Default.Equals(
                            parameter.Parameter.ContainingSymbol.OriginalDefinition,
                            callSite.Method))
                        continue;
                    var mapped = GetConstructorCallArgument(
                        callSite.Operation, parameter.Parameter.Name);
                    if (mapped == null) return;
                    AddDeconstructionMemberAssignments(
                        target,
                        mapped,
                        syntax,
                        memberPath,
                        compilation,
                        visited,
                        callSites.RemoveAt(index),
                        assignments);
                    return;
                }
            }
            if (value is IInvocationOperation invocation) {
                AddDeconstructionCallResultAssignments(
                    target,
                    invocation,
                    invocation.TargetMethod,
                    syntax,
                    memberPath,
                    compilation,
                    visited,
                    callSites,
                    assignments);
                return;
            }
            if (value is IPropertyReferenceOperation { Property.GetMethod: { } getter } property) {
                AddDeconstructionCallResultAssignments(
                    target,
                    property,
                    getter,
                    syntax,
                    memberPath,
                    compilation,
                    visited,
                    callSites,
                    assignments);
                return;
            }
            if (value is ILocalReferenceOperation local &&
                TryGetStableLocalInitializers(
                    local.Local, compilation, visited, out var initializers)) {
                var resolved = new List<ConstructorMemberAssignment>();
                foreach (var initializer in initializers) {
                    var count = resolved.Count;
                    AddDeconstructionMemberAssignments(
                        target,
                        initializer,
                        syntax,
                        memberPath,
                        compilation,
                        visited,
                        callSites,
                        resolved);
                    if (resolved.Count == count) return;
                }
                assignments.AddRange(resolved);
                return;
            }
            if (target is ITupleOperation targetTuple && value is ITupleOperation valueTuple &&
                targetTuple.Elements.Length == valueTuple.Elements.Length) {
                for (var index = 0; index < targetTuple.Elements.Length; index++)
                    AddDeconstructionMemberAssignments(
                        targetTuple.Elements[index],
                        valueTuple.Elements[index],
                        syntax,
                        memberPath,
                        compilation,
                        visited,
                        callSites,
                        assignments);
                return;
            }
            if (ConstructorTargetMatches(target, memberPath))
                assignments.Add(new ConstructorMemberAssignment(syntax, value));
        }
        private static void AddDeconstructionCallResultAssignments(
            IOperation target,
            IOperation callSite,
            IMethodSymbol targetMethod,
            SyntaxNode syntax,
            string memberPath,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            ImmutableArray<ConstructorValueCallSite> callSites,
            List<ConstructorMemberAssignment> assignments) {
            var method = targetMethod.ReducedFrom ?? targetMethod;
            method = (method.PartialImplementationPart ?? method).OriginalDefinition;
            if (callSites.Any(candidate =>
                    SymbolEqualityComparer.Default.Equals(candidate.Method, method)))
                return;
            var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var expressions = GetDirectReturnExpressions(declaration);
            if (expressions.IsDefaultOrEmpty) return;
            var resolved = new List<ConstructorMemberAssignment>();
            foreach (var expression in expressions) {
                var model = compilation.GetSemanticModel(expression.SyntaxTree);
                if (model.GetOperation(expression) is not { } returnedValue ||
                    !TryCollectStableInitializerValues(
                        returnedValue,
                        compilation,
                        visited,
                        out var returnedValues))
                    return;
                foreach (var returned in returnedValues) {
                    var count = resolved.Count;
                    AddDeconstructionMemberAssignments(
                        target,
                        returned,
                        syntax,
                        memberPath,
                        compilation,
                        visited,
                        callSites.Insert(0, new ConstructorValueCallSite(method, callSite)),
                        resolved);
                    if (resolved.Count == count) return;
                }
            }
            assignments.AddRange(resolved);
        }
        private static bool ConstructorTargetMatches(IOperation target, string memberPath) {
            var member = target switch {
                IFieldReferenceOperation field => (ISymbol)field.Field.OriginalDefinition,
                IPropertyReferenceOperation property => property.Property.OriginalDefinition,
                _ => null
            };
            return member != null &&
                   string.Equals(
                       GetMemberPathPart(member),
                       memberPath,
                       StringComparison.Ordinal);
        }
        private static bool TryGetPrimaryConstructorMemberInitializer(
            IObjectCreationOperation creation,
            TypeDeclarationSyntax declaration,
            string memberPath,
            Compilation compilation,
            out ImmutableArray<IOperation> values) {
            values = [];
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            var expressions = new List<ExpressionSyntax>();
            foreach (var member in declaration.Members) {
                switch (member) {
                    case FieldDeclarationSyntax field:
                        foreach (var variable in field.Declaration.Variables) {
                            if (variable.Initializer?.Value is not { } expression ||
                                !MemberNameMatches(
                                    model.GetDeclaredSymbol(variable),
                                    variable.Identifier.ValueText,
                                    memberPath))
                                continue;
                            expressions.Add(expression);
                        }
                        break;
                    case PropertyDeclarationSyntax { Initializer.Value: { } expression } property
                        when MemberNameMatches(
                            model.GetDeclaredSymbol(property),
                            property.Identifier.ValueText,
                            memberPath):
                        expressions.Add(expression);
                        break;
                }
            }
            if (expressions.Count == 0 &&
                declaration is RecordDeclarationSyntax { ParameterList: { } parameterList } record &&
                model.GetDeclaredSymbol(record) is { } recordType) {
                var matchingParameters = parameterList.Parameters.Where(parameter => {
                    var name = parameter.Identifier.ValueText;
                    var property = recordType.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault();
                    return property != null && MemberNameMatches(property, name, memberPath);
                }).ToArray();
                if (matchingParameters.Length != 1) return false;
                var parameterName = matchingParameters[0].Identifier.ValueText;
                var argument = creation.Arguments.FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.Parameter?.Name,
                        parameterName,
                        StringComparison.Ordinal))?.Value;
                if (argument == null) return false;
                values = [argument];
                return true;
            }
            if (expressions.Count != 1) return false;
            var initializer = expressions[0];
            if (model.GetOperation(initializer) is not { } operation ||
                !TryCollectStableInitializerValues(
                    operation,
                    compilation,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                    out var initializerValues))
                return false;
            var mappedValues = ImmutableArray.CreateBuilder<IOperation>();
            foreach (var initializerValue in initializerValues) {
                var value = initializerValue;
                while (value is IConversionOperation conversion) value = conversion.Operand;
                if (value is IParameterReferenceOperation parameter) {
                    value = creation.Arguments.FirstOrDefault(argument =>
                        string.Equals(
                            argument.Parameter?.Name,
                            parameter.Parameter.Name,
                            StringComparison.Ordinal))?.Value!;
                    if (value == null) return false;
                }
                mappedValues.Add(value);
            }
            values = mappedValues.ToImmutable();
            return !values.IsDefaultOrEmpty;
        }
        private static bool MemberNameMatches(ISymbol? symbol, string name, string memberPath) =>
            symbol != null &&
            string.Equals(
                GetMemberPathPart(symbol.OriginalDefinition),
                memberPath,
                StringComparison.Ordinal) ||
            memberPath.EndsWith("." + name, StringComparison.Ordinal);
        private static bool TryGetConstantPathPart(
            IOperation operation,
            string prefix,
            out string path) {
            path = string.Empty;
            if (operation.ConstantValue is not { HasValue: true, Value: { } value }) return false;
            path = prefix + value.GetType().FullName + ":" +
                   Convert.ToString(value, CultureInfo.InvariantCulture);
            return true;
        }
        private static bool TryGetStaticCollectionElements(
            ICollectionExpressionOperation collection,
            out ImmutableArray<IOperation> elements) {
            elements = [];
            if (collection.SemanticModel?.Compilation is not { } compilation) return false;
            var builder = ImmutableArray.CreateBuilder<IOperation>();
            foreach (var element in collection.Elements) {
                if (element is not ISpreadOperation spread) {
                    builder.Add(element);
                    continue;
                }
                if (!TryAppendStaticCollectionValue(
                        spread.Operand,
                        compilation,
                        new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default),
                        builder))
                    return false;
            }
            elements = builder.ToImmutable();
            return true;
        }
        private static bool TryAppendStaticCollectionValue(
            IOperation value,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            ImmutableArray<IOperation>.Builder elements) {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is IParenthesizedOperation parenthesized)
                return TryAppendStaticCollectionValue(
                    parenthesized.Operand, compilation, visited, elements);
            if (value is ILocalReferenceOperation local) {
                if (!TryGetStableLocalInitializers(
                        local.Local, compilation, visited, out var initializers) ||
                    initializers.Length != 1)
                    return false;
                return TryAppendStaticCollectionValue(
                    initializers[0], compilation, visited, elements);
            }
            if (value is IArrayCreationOperation { Initializer: { } arrayInitializer }) {
                elements.AddRange(arrayInitializer.ElementValues);
                return true;
            }
            if (value is ICollectionExpressionOperation collection) {
                foreach (var element in collection.Elements) {
                    if (element is ISpreadOperation spread) {
                        if (!TryAppendStaticCollectionValue(
                                spread.Operand, compilation, visited, elements))
                            return false;
                    }
                    else {
                        elements.Add(element);
                    }
                }
                return true;
            }
            return false;
        }
        private bool TryGetReturnedLambdaCapturedEffects(
            IAnonymousFunctionOperation function,
            IOperation callSite,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            var operations = function.Body.DescendantsAndSelf()
                .Where(operation => BelongsDirectlyTo(function, operation))
                .ToArray();
            var hasReceiver = operations.Any(operation => operation is IInstanceReferenceOperation);
            var hasArgument = operations.Any(operation =>
                operation is IParameterReferenceOperation parameter &&
                !SymbolEqualityComparer.Default.Equals(
                    parameter.Parameter.ContainingSymbol,
                    function.Symbol));
            var hasLocal = operations.Any(operation =>
                operation is ILocalReferenceOperation local &&
                !SymbolEqualityComparer.Default.Equals(local.Local.ContainingSymbol, function.Symbol));
            if (!hasReceiver && !hasArgument && !hasLocal) return false;
            if (hasReceiver) {
                if (!TryGetReturnedLambdaReceiverEffects(
                        function, callSite, out var receiverRead, out var receiverWrite))
                    return false;
                readEffect |= receiverRead;
                writeEffect |= receiverWrite;
            }
            if (hasArgument) {
                if (!TryGetReturnedLambdaArgumentEffects(
                        function, callSite, out var argumentRead, out var argumentWrite))
                    return false;
                readEffect |= argumentRead;
                writeEffect |= argumentWrite;
            }
            if (hasLocal) {
                if (!TryGetReturnedLambdaLocalEffects(
                        function, callSite, out var localRead, out var localWrite))
                    return false;
                readEffect |= localRead;
                writeEffect |= localWrite;
            }
            return true;
        }
        private bool TryGetCapturedLocalEffects(
            ILocalSymbol local,
            IOperation callSite,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            if (!visited.Add(local)) return false;
            try {
                if (local.DeclaringSyntaxReferences.Length != 1) return false;
                var syntaxReference = local.DeclaringSyntaxReferences[0];
                var syntax = syntaxReference.GetSyntax();
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                if (model.GetOperation(syntax) is not IVariableDeclaratorOperation declarator ||
                    declarator.Initializer?.Value is not { } value)
                    return false;
                var root = (IOperation)declarator;
                while (root.Parent != null) root = root.Parent;
                if (root.DescendantsAndSelf()
                    .OfType<ILocalReferenceOperation>()
                    .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Local, local) &&
                                      IsDirectLocalWrite(reference)))
                    return false;
                return TryGetCapturedValueEffects(
                    value,
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            }
            finally {
                visited.Remove(local);
            }
        }
        private bool TryGetCapturedValueEffects(
            IOperation value,
            IOperation callSite,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is IParenthesizedOperation parenthesized)
                return TryGetCapturedValueEffects(
                    parenthesized.Operand,
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is ILocalReferenceOperation source)
                return TryGetCapturedLocalEffects(
                    source.Local,
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            IOperation? mapped = null;
            if (value is IParameterReferenceOperation parameter &&
                callSite is IInvocationOperation invocation) {
                mapped = invocation.Arguments.FirstOrDefault(argument =>
                    string.Equals(
                        argument.Parameter?.Name,
                        parameter.Parameter.Name,
                        StringComparison.Ordinal))?.Value;
                if (mapped == null &&
                    parameter.Parameter.Ordinal == 0 &&
                    invocation.TargetMethod.ReducedFrom != null)
                    mapped = invocation.Instance;
            }
            else if (value is IInstanceReferenceOperation) {
                mapped = callSite switch {
                    IInvocationOperation receiverInvocation => receiverInvocation.Instance,
                    IPropertyReferenceOperation property => property.Instance,
                    _ => null
                };
            }
            if (mapped != null) {
                readEffect = GetInstanceReadEffect(mapped, this);
                writeEffect = GetInstanceWriteEffect(mapped, this);
                return true;
            }
            if (value is IConditionalOperation { WhenFalse: { } whenFalse } conditional)
                return TryGetCompositeCapturedValueEffects(
                    conditional.WhenTrue,
                    whenFalse,
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is ICoalesceOperation coalesce)
                return TryGetCompositeCapturedValueEffects(
                    coalesce.Value,
                    coalesce.WhenNull,
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is ISwitchExpressionOperation switchExpression) {
                foreach (var arm in switchExpression.Arms) {
                    if (!TryGetCapturedValueEffects(
                            arm.Value,
                            callSite,
                            compilation,
                            visited,
                            out var armReadEffect,
                            out var armWriteEffect))
                        return false;
                    readEffect |= armReadEffect;
                    writeEffect |= armWriteEffect;
                }
                return switchExpression.Arms.Length != 0;
            }
            if (value is IInvocationOperation returnedInvocation)
                return TryGetCapturedCallResultEffects(
                    returnedInvocation.TargetMethod,
                    returnedInvocation,
                    [],
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is IFieldReferenceOperation { Field.IsStatic: true }) {
                readEffect = GetInstanceReadEffect(value, this);
                writeEffect = GetInstanceWriteEffect(value, this);
                return true;
            }
            if (value is IPropertyReferenceOperation { Property.GetMethod: { } getter } returnedProperty)
                return TryGetCapturedCallResultEffects(
                    getter,
                    returnedProperty,
                    [],
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is ITupleOperation) return true;
            if (value is not (IObjectCreationOperation or IArrayCreationOperation or
                IAnonymousObjectCreationOperation or IDelegateCreationOperation or
                ICollectionExpressionOperation))
                return false;
            readEffect = GetInstanceReadEffect(value, this);
            writeEffect = GetInstanceWriteEffect(value, this);
            return true;
        }
        private bool TryGetCapturedCallResultEffects(
            IMethodSymbol targetMethod,
            IOperation nestedCallSite,
            ImmutableArray<IOperation> outerNestedCallSites,
            IOperation outerCallSite,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            var method = targetMethod.ReducedFrom ?? targetMethod;
            method = (method.PartialImplementationPart ?? method).OriginalDefinition;
            if (outerNestedCallSites.Any(site =>
                    GetNestedCallSiteMethod(site) is { } outerMethod &&
                    SymbolEqualityComparer.Default.Equals(
                        (outerMethod.PartialImplementationPart ?? outerMethod).OriginalDefinition,
                        method)))
                return false;
            var declaration = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
            var expressions = GetDirectReturnExpressions(declaration);
            if (expressions.IsDefaultOrEmpty) return false;
            var nestedCallSites = outerNestedCallSites.Insert(0, nestedCallSite);
            foreach (var expression in expressions) {
                var model = compilation.GetSemanticModel(expression.SyntaxTree);
                if (model.GetOperation(expression) is not { } returnedValue ||
                    !TryGetNestedReturnedValueEffects(
                        returnedValue,
                        nestedCallSites,
                        outerCallSite,
                        compilation,
                        visited,
                        out var returnedReadEffect,
                        out var returnedWriteEffect))
                    return false;
                readEffect |= returnedReadEffect;
                writeEffect |= returnedWriteEffect;
            }
            return true;
        }
        private bool TryGetNestedReturnedValueEffects(
            IOperation value,
            ImmutableArray<IOperation> nestedCallSites,
            IOperation outerCallSite,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is IParenthesizedOperation parenthesized)
                return TryGetNestedReturnedValueEffects(
                    parenthesized.Operand,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is ILocalReferenceOperation local)
                return TryGetNestedCapturedLocalEffects(
                    local.Local,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            IOperation? mapped = null;
            var nestedCallSite = nestedCallSites[0];
            if (value is IParameterReferenceOperation parameter) {
                mapped = nestedCallSite switch {
                    IInvocationOperation invocation =>
                        invocation.Arguments.FirstOrDefault(argument =>
                            string.Equals(
                                argument.Parameter?.Name,
                                parameter.Parameter.Name,
                                StringComparison.Ordinal))?.Value ??
                        (parameter.Parameter.Ordinal == 0 && invocation.TargetMethod.ReducedFrom != null
                            ? invocation.Instance
                            : null),
                    IPropertyReferenceOperation property =>
                        property.Arguments.FirstOrDefault(argument =>
                            string.Equals(
                                argument.Parameter?.Name,
                                parameter.Parameter.Name,
                                StringComparison.Ordinal))?.Value,
                    _ => null
                };
            }
            else if (value is IInstanceReferenceOperation) {
                mapped = nestedCallSite switch {
                    IInvocationOperation invocation => invocation.Instance,
                    IPropertyReferenceOperation property => property.Instance,
                    _ => null
                };
            }
            if (mapped != null) {
                if (nestedCallSites.Length == 1)
                    return TryGetCapturedValueEffects(
                        mapped,
                        outerCallSite,
                        compilation,
                        visited,
                        out readEffect,
                        out writeEffect);
                return TryGetNestedReturnedValueEffects(
                    mapped,
                    nestedCallSites.RemoveAt(0),
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            }
            if (value is IConditionalOperation { WhenFalse: { } whenFalse } conditional)
                return TryGetNestedCompositeReturnedValueEffects(
                    conditional.WhenTrue,
                    whenFalse,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is ICoalesceOperation coalesce)
                return TryGetNestedCompositeReturnedValueEffects(
                    coalesce.Value,
                    coalesce.WhenNull,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is ISwitchExpressionOperation switchExpression) {
                foreach (var arm in switchExpression.Arms) {
                    if (!TryGetNestedReturnedValueEffects(
                            arm.Value,
                            nestedCallSites,
                            outerCallSite,
                            compilation,
                            visited,
                            out var armReadEffect,
                            out var armWriteEffect))
                        return false;
                    readEffect |= armReadEffect;
                    writeEffect |= armWriteEffect;
                }
                return switchExpression.Arms.Length != 0;
            }
            if (value is IFieldReferenceOperation { Field.IsStatic: true }) {
                readEffect = GetInstanceReadEffect(value, this);
                writeEffect = GetInstanceWriteEffect(value, this);
                return true;
            }
            if (value is IInvocationOperation nestedInvocation)
                return TryGetCapturedCallResultEffects(
                    nestedInvocation.TargetMethod,
                    nestedInvocation,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is IPropertyReferenceOperation { Property.GetMethod: { } getter } nestedProperty)
                return TryGetCapturedCallResultEffects(
                    getter,
                    nestedProperty,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            if (value is not (IObjectCreationOperation or IArrayCreationOperation or
                IAnonymousObjectCreationOperation or IDelegateCreationOperation or
                ICollectionExpressionOperation))
                return false;
            readEffect = GetInstanceReadEffect(value, this);
            writeEffect = GetInstanceWriteEffect(value, this);
            return true;
        }
        private bool TryGetNestedCapturedLocalEffects(
            ILocalSymbol local,
            ImmutableArray<IOperation> nestedCallSites,
            IOperation outerCallSite,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            if (!visited.Add(local)) return false;
            try {
                if (local.DeclaringSyntaxReferences.Length != 1) return false;
                var syntax = local.DeclaringSyntaxReferences[0].GetSyntax();
                var model = compilation.GetSemanticModel(syntax.SyntaxTree);
                if (model.GetOperation(syntax) is not IVariableDeclaratorOperation declarator ||
                    declarator.Initializer?.Value is not { } value)
                    return false;
                var root = (IOperation)declarator;
                while (root.Parent != null) root = root.Parent;
                if (root.DescendantsAndSelf()
                    .OfType<ILocalReferenceOperation>()
                    .Any(reference => SymbolEqualityComparer.Default.Equals(reference.Local, local) &&
                                      IsDirectLocalWrite(reference)))
                    return false;
                return TryGetNestedReturnedValueEffects(
                    value,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect);
            }
            finally {
                visited.Remove(local);
            }
        }
        private bool TryGetNestedCompositeReturnedValueEffects(
            IOperation left,
            IOperation right,
            ImmutableArray<IOperation> nestedCallSites,
            IOperation outerCallSite,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            if (!TryGetNestedReturnedValueEffects(
                    left,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect) ||
                !TryGetNestedReturnedValueEffects(
                    right,
                    nestedCallSites,
                    outerCallSite,
                    compilation,
                    visited,
                    out var rightReadEffect,
                    out var rightWriteEffect))
                return false;
            readEffect |= rightReadEffect;
            writeEffect |= rightWriteEffect;
            return true;
        }
        private static IMethodSymbol? GetNestedCallSiteMethod(IOperation site) => site switch {
            IInvocationOperation invocation => invocation.TargetMethod.ReducedFrom ?? invocation.TargetMethod,
            IPropertyReferenceOperation property => property.Property.GetMethod,
            _ => null
        };
        private bool TryGetCompositeCapturedValueEffects(
            IOperation left,
            IOperation right,
            IOperation callSite,
            Compilation compilation,
            HashSet<ILocalSymbol> visited,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            if (!TryGetCapturedValueEffects(
                    left,
                    callSite,
                    compilation,
                    visited,
                    out readEffect,
                    out writeEffect) ||
                !TryGetCapturedValueEffects(
                    right,
                    callSite,
                    compilation,
                    visited,
                    out var rightReadEffect,
                    out var rightWriteEffect))
                return false;
            readEffect |= rightReadEffect;
            writeEffect |= rightWriteEffect;
            return true;
        }
        private static bool IsDirectLocalWrite(ILocalReferenceOperation reference) {
            IOperation current = reference;
            while (current.Parent is IConversionOperation or IParenthesizedOperation)
                current = current.Parent;
            return current.Parent switch {
                ISimpleAssignmentOperation assignment => ReferenceEquals(assignment.Target, current),
                ICompoundAssignmentOperation assignment => ReferenceEquals(assignment.Target, current),
                ICoalesceAssignmentOperation assignment => ReferenceEquals(assignment.Target, current),
                IIncrementOrDecrementOperation increment => ReferenceEquals(increment.Target, current),
                IArgumentOperation { Parameter.RefKind: not RefKind.None } argument =>
                    ReferenceEquals(argument.Value, current),
                _ => false
            };
        }
        private DelegateTarget? CreateDelegateTarget(IOperation value, IOperation? receiverOverride = null) {
            if (value is IMethodReferenceOperation reference) {
                var receiver = receiverOverride ?? reference.Instance;
                return new DelegateTarget(
                    reference.Method.OriginalDefinition,
                    receiver,
                    receiver == null ? null : GetInstanceReadEffect(receiver, this),
                    receiver == null ? null : GetInstanceWriteEffect(receiver, this),
                    null,
                    null,
                    null,
                    null);
            }
            return value is IAnonymousFunctionOperation function
                ? CreateAnonymousFunctionTarget(function)
                : null;
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
                hasCapture ? capturedWriteEffect : null,
                null,
                null);
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
            _refLocalEffects.Remove(local);
            if (_flowUncertainLocals.Contains(local) || IsFlowDependent(value.Syntax)) return;
            if (local.RefKind != RefKind.None || local.Type.TypeKind == TypeKind.Pointer) {
                var referencedValue = UnwrapAliasSource(value);
                _refLocalEffects[local] = (
                    GetStorageReadEffect(referencedValue, this),
                    GetStorageWriteEffect(referencedValue, this));
            }
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
        private static IOperation UnwrapAliasSource(IOperation value) {
            while (true) {
                switch (value) {
                    case IConversionOperation conversion:
                        value = conversion.Operand;
                        continue;
                    case IAddressOfOperation address:
                        value = address.Reference;
                        continue;
                    case { Kind: OperationKind.None } transparent:
                        var children = transparent.ChildOperations.Take(2).ToArray();
                        if (children.Length != 1) return value;
                        value = children[0];
                        continue;
                    default:
                        return value;
                }
            }
        }
        internal void MarkFlowUncertain(ILocalSymbol local) {
            local = (ILocalSymbol)local.OriginalDefinition;
            _flowUncertainLocals.Add(local);
            _freshLocals.Remove(local);
            _memberOrigins.Remove(local);
            _exactTypes.Remove(local);
            _delegateTargets.Remove(local);
            _refLocalEffects.Remove(local);
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
                    _refLocalEffects.Remove(local.Local);
                }
            }
        }
        internal bool TryGetRefLocalEffects(
            IOperation? value,
            out SharpProofEffect readEffect,
            out SharpProofEffect writeEffect) {
            while (value is IConversionOperation conversion) value = conversion.Operand;
            if (value is ILocalReferenceOperation local &&
                _refLocalEffects.TryGetValue((ILocalSymbol)local.Local.OriginalDefinition, out var effects)) {
                readEffect = effects.Read;
                writeEffect = effects.Write;
                return true;
            }
            readEffect = SharpProofEffect.None;
            writeEffect = SharpProofEffect.None;
            return false;
        }
        internal void SetRefLocalEffects(
            ILocalSymbol local,
            SharpProofEffect readEffect,
            SharpProofEffect writeEffect) =>
            _refLocalEffects[(ILocalSymbol)local.OriginalDefinition] = (readEffect, writeEffect);
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
