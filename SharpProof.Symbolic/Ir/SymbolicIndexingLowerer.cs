using static SharpProof.Symbolic.Ir.SymbolicIrLowerer;
using static SharpProof.Symbolic.Ir.SymbolicLoweringValueFacts;
namespace SharpProof.Symbolic.Ir;
internal static class SymbolicIndexingLowerer {
    private delegate bool TryBindShape<T>(ExpressionSyntax expression, SymbolicLoweringContext context, out T shape)
        where T : struct;
    internal static bool TryLowerElementAccessTerm(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (TryLowerFiniteArrayElement(elementAccess, context) is { } finite) {
            term = finite;
            return true;
        }
        var receiverType = GetPreferredLengthSemanticType(elementAccess.Expression, context);
        if (!TryGetElementKind(receiverType, context.Compilation, out var elementKind) ||
            LowerValue(elementAccess.Expression, context, SmtValueKind.Reference) is not { } receiver) {
            term = null!;
            return false;
        }
        var arguments = elementAccess.ArgumentList.Arguments;
        if (receiverType is IArrayTypeSymbol { Rank: > 1 } array) {
            if (arguments.Count != array.Rank) {
                term = null!;
                return false;
            }
            var indices = ImmutableArray.CreateBuilder<SymbolicTerm>(array.Rank);
            foreach (var argument in arguments) {
                if (LowerInteger(UnwrapExpression(argument.Expression), context) is not { } index) {
                    term = null!;
                    return false;
                }
                indices.Add(index);
            }
            term = new SymbolicMultiElementTerm(receiver, indices.MoveToImmutable(), elementKind);
            return true;
        }
        if (arguments.Count != 1 ||
            !TryBindIndex(arguments[0].Expression, context, out var shape) ||
            LowerInteger(shape.ValueExpression, context) is not { } value) {
            term = null!;
            return false;
        }
        term = new SymbolicElementTerm(receiver, shape.FromEnd ? new SymbolicFromEndIndexTerm(value) : value, elementKind);
        return true;
    }
    private static SymbolicTerm? TryLowerFiniteArrayElement(
        ElementAccessExpressionSyntax access,
        SymbolicLoweringContext context) {
        if (access.ArgumentList.Arguments.Count != 1 ||
            !TryBindIndex(access.ArgumentList.Arguments[0].Expression, context, out var shape) ||
            !TryGetIntegralConstant(context.SemanticModel.GetConstantValue(shape.ValueExpression, context.CancellationToken),
                out var index))
            return null;
        SeparatedSyntaxList<ExpressionSyntax>? values = UnwrapExpression(access.Expression) switch {
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            _ => null
        };
        var resolved = shape.FromEnd && values is { } items ? items.Count - index : index;
        return values is { Count: > 0 } expressions && resolved >= 0 && resolved < expressions.Count
            ? LowerValue(expressions[(int)resolved], context)
            : null;
    }
    private static bool TryGetIntegralConstant(Optional<object?> constant, out long value) {
        if (constant is { HasValue: true, Value: { } raw } && SymbolicLoweringValueFacts.TryGetIntegralConstant(raw, out value))
            return true;
        value = default;
        return false;
    }
    private static bool TryGetElementKind(ITypeSymbol? receiverType, Compilation compilation, out SmtValueKind kind) {
        ITypeSymbol? elementType = receiverType switch {
            IArrayTypeSymbol array => array.ElementType,
            { SpecialType: SpecialType.System_String } => compilation.GetSpecialType(SpecialType.System_Char),
            INamedTypeSymbol { TypeArguments.Length: 1 } named when SymbolicTypeFacts.IsBuiltInSpanType(named) =>
                named.TypeArguments[0],
            _ => TryGetInt32IndexerElementType(receiverType)
        };
        if (elementType != null) return SymbolicTypeLowerer.TryGetValueKind(elementType, out kind);
        kind = default;
        return false;
    }
    private static ITypeSymbol? TryGetInt32IndexerElementType(ITypeSymbol? type) {
        if (!SymbolicTypeFacts.HasInstanceInt32Member(type, "Count")) return null;
        foreach (var candidate in SymbolicTypeFacts.EnumerateSelfBaseTypesAndInterfaces(type!))
            foreach (var property in candidate.GetMembers().OfType<IPropertySymbol>())
                if (property is { IsIndexer: true, IsStatic: false, Parameters.Length: 1 } &&
                    property.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
                    return property.Type;
        return null;
    }
    internal static bool TryLowerArrayGetLengthInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        if (!TryGetArrayDimensionInvocation(invocation, operation, context, out var receiver, out var dimension)) {
            term = null!;
            return false;
        }
        return dimension == 0 &&
               GetPreferredLengthSemanticType(receiver, context) is IArrayTypeSymbol { Rank: 1 } &&
               TryLowerBuiltInLengthTerm(receiver, context, out term) ||
               TryLowerArrayDimensionLengthTerm(receiver, dimension, context, out term);
    }
    internal static bool TryLowerArrayBoundInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!TryGetArrayDimensionInvocation(invocation, operation, context, out var receiver, out var dimension) ||
            LowerArrayDimension(receiver, dimension, context, lowerBound: true) is not { } lower)
            return false;
        if (operation.TargetMethod.Name == nameof(Array.GetLowerBound)) {
            term = lower;
            return true;
        }
        if (operation.TargetMethod.Name != nameof(Array.GetUpperBound) ||
            LowerArrayDimension(receiver, dimension, context, lowerBound: false) is not { } length)
            return false;
        term = new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Subtract,
            new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, lower, length),
            new SymbolicIntegerConstantTerm(1));
        return true;
    }
    private static bool TryGetArrayDimensionInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context,
        out ExpressionSyntax receiver,
        out int dimension) {
        receiver = null!;
        dimension = default;
        var method = operation.TargetMethod;
        if (invocation.Expression is not MemberAccessExpressionSyntax member ||
            method.ContainingType?.SpecialType != SpecialType.System_Array ||
            method.Parameters.Length != 1 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_Int32 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, 0, out var dimensionExpression) ||
            context.SemanticModel.GetConstantValue(dimensionExpression, context.CancellationToken) is not
                { HasValue: true, Value: int constant })
            return false;
        receiver = member.Expression;
        dimension = constant;
        return true;
    }
    internal static bool TryLowerArrayDimensionLengthTerm(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = LowerArrayDimension(expression, dimension, context, lowerBound: false)!;
        return term != null;
    }
    private static SymbolicTerm? LowerArrayDimension(
        ExpressionSyntax expression,
        int dimension,
        SymbolicLoweringContext context,
        bool lowerBound) {
        expression = UnwrapExpression(expression);
        if (GetPreferredLengthSemanticType(expression, context) is not IArrayTypeSymbol array ||
            dimension < 0 ||
            dimension >= array.Rank)
            return null;
        if (lowerBound) {
            if (IsKnownZeroBasedArray(expression, array)) return new SymbolicIntegerConstantTerm(0);
            return LowerValue(expression, context, SmtValueKind.Reference) is { } reference
                ? new SymbolicVariableTerm(
                    SymbolicState.CreateProofTermKey(reference) + ".GetLowerBound(" +
                    dimension.ToString(CultureInfo.InvariantCulture) + ")",
                    SmtValueKind.Int)
                : null;
        }
        if (LowerArrayCreationDimension(expression, array, dimension, context) is { } creationLength)
            return creationLength;
        if (expression is CastExpressionSyntax cast &&
            SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type,
                array)) {
            if (LowerArrayCreationDimension(cast.Expression, array, dimension, context) is { } castCreationLength)
                return castCreationLength;
            if (LowerValue(cast.Expression, context, SmtValueKind.Reference) is { } castReference)
                return new SymbolicArrayDimensionLengthTerm(castReference, dimension);
        }
        if (LowerValue(expression, context, SmtValueKind.Reference) is not { } arrayTerm) return null;
        return array.Rank == 1 && dimension == 0
            ? new SymbolicLengthTerm(arrayTerm)
            : new SymbolicArrayDimensionLengthTerm(arrayTerm, dimension);
    }
    internal static bool TryLowerArrayTotalLengthTerm(
        ExpressionSyntax expression,
        IArrayTypeSymbol array,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        expression = UnwrapExpression(expression);
        var lowered = array.Rank > 0
            ? MultiplyDimensions(array.Rank,
                dimension => LowerArrayCreationDimension(expression, array, dimension, context))
            : null;
        lowered ??= LowerValue(expression, context) is { } reference
            ? CreateArrayTotalLength(reference, array)
            : null;
        term = lowered!;
        return lowered != null;
    }
    internal static bool TryCreateBuiltInLengthReferenceTerm(
        ITypeSymbol? type,
        SymbolicTerm reference,
        out SymbolicTerm term) {
        term = null!;
        if (reference.Kind != SmtValueKind.Reference || type == null) return false;
        if (type.SpecialType == SpecialType.System_String) {
            term = new SymbolicLengthTerm(new SymbolicStringContentTerm(reference));
            return true;
        }
        if (type is IArrayTypeSymbol { Rank: > 1 } multiDimensional) {
            term = CreateArrayTotalLength(reference, multiDimensional)!;
            return term != null;
        }
        if (type is IArrayTypeSymbol { Rank: 1 } ||
            SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type)) {
            term = new SymbolicLengthTerm(reference);
            return true;
        }
        if (type is not IArrayTypeSymbol &&
            SymbolicTypeFacts.HasInstanceInt32Member(type, "Count")) {
            term = new SymbolicCountTerm(reference);
            return true;
        }
        return false;
    }
    internal static bool HasCountBackedIntIndexer(ITypeSymbol? type)
        => SymbolicTypeFacts.HasInstanceInt32Member(type, "Count") && SymbolicTypeFacts.HasInt32Indexer(type);
    private static SymbolicTerm? CreateArrayTotalLength(SymbolicTerm reference, IArrayTypeSymbol array)
        => reference.Kind == SmtValueKind.Reference
            ? MultiplyDimensions(array.Rank, dimension => new SymbolicArrayDimensionLengthTerm(reference, dimension))
            : null;
    private static SymbolicTerm? MultiplyDimensions(int rank, Func<int, SymbolicTerm?> getLength) {
        var product = rank > 0 ? getLength(0) : null;
        for (var dimension = 1; dimension < rank && product != null; dimension++)
            product = getLength(dimension) is { } length
                ? new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Multiply, product, length)
                : null;
        return product;
    }
    private static SymbolicTerm? LowerArrayCreationDimension(
        ExpressionSyntax expression,
        IArrayTypeSymbol array,
        int dimension,
        SymbolicLoweringContext context) {
        if (dimension == 0 &&
            array.Rank == 1 &&
            expression is ImplicitArrayCreationExpressionSyntax { Initializer: { } implicitInitializer })
            return new SymbolicIntegerConstantTerm(implicitInitializer.Expressions.Count);
        if (expression is not ArrayCreationExpressionSyntax creation ||
            creation.Type.RankSpecifiers.Count == 0 ||
            creation.Type.RankSpecifiers[0].Sizes.Count != array.Rank)
            return null;
        var size = creation.Type.RankSpecifiers[0].Sizes[dimension];
        return size.IsKind(SyntaxKind.OmittedArraySizeExpression)
            ? dimension == 0 && creation.Initializer != null
                ? new SymbolicIntegerConstantTerm(creation.Initializer.Expressions.Count)
                : null
            : LowerInteger(size, context);
    }
    internal static bool TryCreateArrayElementBoundsCondition(
        ExpressionSyntax expression,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition,
        out SymbolicTerm? subject) {
        condition = null!;
        subject = null;
        if (GetPreferredLengthSemanticType(expression, context) is not IArrayTypeSymbol { Rank: > 0 } array ||
            indexExpressions.Count != array.Rank)
            return false;
        for (var dimension = 0; dimension < array.Rank; dimension++) {
            if (LowerInteger(indexExpressions[dimension], context) is not { } index ||
                LowerArrayDimension(expression, dimension, context, lowerBound: true) is not { } lower ||
                LowerArrayDimension(expression, dimension, context, lowerBound: false) is not { } length)
                return false;
            subject ??= index;
            SymbolicCondition inRange = IsKnownZeroBasedArray(expression, array)
                ? new SymbolicFactCondition(SymbolicFact.Exact(
                    new SymbolicBoundsAtom(index, length, true, true), source, provenance))
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    CreateRelationCondition(SymbolicRelationOperator.LessThanOrEqual, lower, index, source,
                        provenance + ".at-or-above-lower-bound"),
                    CreateRelationCondition(
                        SymbolicRelationOperator.LessThan,
                        index,
                        new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, lower, length),
                        source,
                        provenance + ".below-upper-bound"));
            condition = condition == null
                ? inRange
                : new SymbolicBinaryCondition(SymbolicConditionOperator.And, condition, inRange);
        }
        return condition != null;
    }
    private static bool IsKnownZeroBasedArray(ExpressionSyntax expression, IArrayTypeSymbol array)
        => array.IsSZArray ||
           UnwrapExpression(expression) is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax;
    internal static bool TryCreateBuiltInElementAccessInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax argumentExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        receiverExpression = UnwrapExpression(receiverExpression);
        var type = GetPreferredLengthSemanticType(receiverExpression, context);
        if (!IsSupportedElementReceiver(type) ||
            LowerBuiltInLength(receiverExpression, context) is not { } length)
            return false;
        if (TryBindRange(argumentExpression, context, out var range))
            return TryCreateRangeBounds(range, length, source, provenance, context, out condition);
        if (!TryBindIndex(argumentExpression, context, out var index) ||
            LowerIndex(index, length, context) is not { } effective ||
            !TryGetWellFormed(index, source, provenance + ".well-formed", context, out var wellFormed))
            return false;
        var inRange = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicBoundsAtom(effective, length, true, true), source, provenance));
        condition = wellFormed == null
            ? inRange
            : new SymbolicBinaryCondition(SymbolicConditionOperator.And, wellFormed, inRange);
        return true;
    }
    internal static bool TryCreateSubsequenceInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax startExpression,
        ExpressionSyntax? lengthExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        bool oneArgumentUpperBoundIsInclusive,
        out SymbolicCondition condition) {
        condition = null!;
        if (LowerBuiltInLength(receiverExpression, context) is not { } sourceLength) return false;
        if (lengthExpression == null && IsDefinitelyPastLength(receiverExpression, startExpression, context)) {
            condition = new SymbolicConstantCondition(false);
            return true;
        }
        if (LowerInteger(startExpression, context) is not { } start) return false;
        var startNonNegative = Relation(SymbolicRelationOperator.GreaterThanOrEqual, start, 0, source,
            provenance + ".start-non-negative");
        if (lengthExpression == null) {
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                startNonNegative,
                CreateRelationCondition(
                    oneArgumentUpperBoundIsInclusive
                        ? SymbolicRelationOperator.LessThanOrEqual
                        : SymbolicRelationOperator.LessThan,
                    start,
                    sourceLength,
                    source,
                    provenance + ".start-within-length"));
            return true;
        }
        if (LowerInteger(lengthExpression, context) is not { } count) return false;
        var countNonNegative = Relation(SymbolicRelationOperator.GreaterThanOrEqual, count, 0, source,
            provenance + ".count-non-negative");
        var startWithin = CreateRelationCondition(SymbolicRelationOperator.LessThanOrEqual, start, sourceLength, source,
            provenance + ".start-within-length");
        var countWithin = CreateRelationCondition(
            SymbolicRelationOperator.LessThanOrEqual,
            count,
            new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, start),
            source,
            provenance + ".count-within-remaining-length");
        var noOverflow = count is SymbolicIntegerConstantTerm { Value: 0 }
            ? new SymbolicConstantCondition(true)
            : CreateRelationCondition(
                SymbolicRelationOperator.LessThanOrEqual,
                start,
                new SymbolicBinaryTerm(
                    SymbolicBinaryTermOperator.Subtract,
                    new SymbolicIntegerConstantTerm(int.MaxValue),
                    count),
                source,
                provenance + ".addition-does-not-overflow");
        condition = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            startNonNegative,
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                countNonNegative,
                new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    startWithin,
                    new SymbolicBinaryCondition(SymbolicConditionOperator.And, countWithin, noOverflow))));
        return true;
    }
    private static bool IsDefinitelyPastLength(
        ExpressionSyntax receiver,
        ExpressionSyntax start,
        SymbolicLoweringContext context) {
        start = UnwrapExpression(start);
        if (start is not BinaryExpressionSyntax binary ||
            !binary.IsKind(SyntaxKind.AddExpression) ||
            context.SemanticModel.GetOperation(binary, context.CancellationToken) is not
                IBinaryOperation { OperatorMethod: null, IsChecked: false })
            return false;
        return IsLengthPlusPositive(binary.Left, binary.Right) || IsLengthPlusPositive(binary.Right, binary.Left);
        bool IsLengthPlusPositive(ExpressionSyntax lengthCandidate, ExpressionSyntax constantCandidate) {
            lengthCandidate = UnwrapExpression(lengthCandidate);
            return lengthCandidate is MemberAccessExpressionSyntax member &&
                   member.Name.Identifier.ValueText == "Length" &&
                   SyntaxFactory.AreEquivalent(UnwrapExpression(member.Expression), UnwrapExpression(receiver)) &&
                   context.SemanticModel.GetConstantValue(UnwrapExpression(constantCandidate), context.CancellationToken) is { HasValue: true, Value: int value } &&
                   value > 0;
        }
    }
    internal static bool TryLowerBuiltInLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = LowerBuiltInLength(expression, context)!;
        return term != null;
    }
    private static SymbolicTerm? LowerBuiltInLength(ExpressionSyntax expression, SymbolicLoweringContext context) {
        expression = UnwrapExpression(expression);
        if (expression is CastExpressionSyntax cast) {
            var target = context.SemanticModel.GetTypeInfo(cast.Type, context.CancellationToken).Type;
            if (target?.SpecialType == SpecialType.System_String) {
                if (SymbolicStringLowerer.TryLowerStringTerm(cast, context, out var castString))
                    return new SymbolicLengthTerm(castString);
                if (LowerValue(cast.Expression, context) is { } castReference &&
                    TryCreateBuiltInLengthReferenceTerm(target, castReference, out var castLength))
                    return castLength;
            }
            if (target is IArrayTypeSymbol { Rank: 1 } castArray) {
                if (cast.Expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax &&
                    TryLowerArrayTotalLengthTerm(cast.Expression, castArray, context, out var castCreationLength))
                    return castCreationLength;
                if (LowerValue(cast.Expression, context) is { } castReference &&
                    TryCreateBuiltInLengthReferenceTerm(target, castReference, out var castLength))
                    return castLength;
            }
        }
        if (expression is CollectionExpressionSyntax collection &&
            LowerCollectionLength(collection, context) is { } collectionLength)
            return collectionLength;
        if (LowerRangeAccessLength(expression, context) is { } rangeLength) return rangeLength;
        if (LowerViewLength(expression, context) is { } viewLength) return viewLength;
        if (SymbolicStringLengthLowerer.TryLowerStringCreationResultLengthTerm(expression, context, out var creationLength))
            return creationLength;
        if (SymbolicStringLengthLowerer.TryLowerStringInvocationResultLengthTerm(expression, context, out var invocationLength))
            return invocationLength;
        var type = GetPreferredLengthSemanticType(expression, context);
        if (type?.SpecialType == SpecialType.System_String) {
            if (SymbolicStringLowerer.TryLowerStringTerm(expression, context, out var stringValue))
                return SymbolicStringLengthLowerer.CreateStringResultLengthTerm(
                    stringValue, expression, "ir.string-result.length");
            if (LowerValue(expression, context) is { } stringReference &&
                TryCreateBuiltInLengthReferenceTerm(type, stringReference, out var stringLength))
                return stringLength;
        }
        if (expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax &&
            type is IArrayTypeSymbol array &&
            TryLowerArrayTotalLengthTerm(expression, array, context, out var arrayLength))
            return arrayLength;
        if (expression is InvocationExpressionSyntax empty &&
            context.SemanticModel.GetSymbolInfo(empty, context.CancellationToken).Symbol is IMethodSymbol {
                Name: "Empty",
                IsStatic: true,
                ContainingType.SpecialType: SpecialType.System_Array
            })
            return new SymbolicIntegerConstantTerm(0);
        if (expression is BinaryExpressionSyntax { RawKind: (int)SyntaxKind.CoalesceExpression } coalesce &&
            LowerBuiltInLength(coalesce.Left, context) is { Kind: SmtValueKind.Int } leftLength &&
            LowerBuiltInLength(coalesce.Right, context) is { Kind: SmtValueKind.Int } rightLength &&
            LowerValue(coalesce.Left, context, SmtValueKind.Reference) is { } leftReceiver)
            return new SymbolicConditionalTerm(
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    leftReceiver,
                    new SymbolicNullTerm(),
                    coalesce.Left,
                    "ir.coalesce.left-not-null"),
                leftLength,
                rightLength);
        if (expression is ConditionalExpressionSyntax conditional &&
            LowerBuiltInLength(conditional.WhenTrue, context) is { Kind: SmtValueKind.Int } whenTrue &&
            LowerBuiltInLength(conditional.WhenFalse, context) is { Kind: SmtValueKind.Int } whenFalse &&
            SymbolicLoweringValue.TryGet(LowerCondition(conditional.Condition, context), out var condition))
            return new SymbolicConditionalTerm(condition, whenTrue, whenFalse);
        return LowerValue(expression, context) is { } receiver &&
               TryCreateBuiltInLengthReferenceTerm(type, receiver, out var length)
            ? length
            : null;
    }
    private static SymbolicTerm? LowerCollectionLength(
        CollectionExpressionSyntax collection,
        SymbolicLoweringContext context) {
        SymbolicTerm total = new SymbolicIntegerConstantTerm(0);
        foreach (var element in collection.Elements) {
            var length = element switch {
                ExpressionElementSyntax => new SymbolicIntegerConstantTerm(1),
                SpreadElementSyntax spread => LowerBuiltInLength(spread.Expression, context),
                _ => null
            };
            if (length is not { Kind: SmtValueKind.Int }) return null;
            total = total is SymbolicIntegerConstantTerm { Value: 0 }
                ? length
                : new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, total, length);
        }
        return total;
    }
    private static SymbolicTerm? LowerViewLength(ExpressionSyntax expression, SymbolicLoweringContext context) {
        if (!TryGetInvocationOperation(expression, context, out _, out var operation)) return null;
        ExpressionSyntax? source = null;
        var firstArgument = 0;
        var allowRange = false;
        var method = operation.TargetMethod;
        if (!method.IsStatic &&
            method.Name == "Slice" &&
            SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ContainingType) &&
            SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(method.ReturnType))
            source = operation.Instance?.Syntax as ExpressionSyntax;
        else if (SymbolicRuntimeHazardSyntaxFacts.IsMemoryExtensionsViewMethod(method) &&
                 SymbolicRuntimeHazardSyntaxFacts.TryGetMemoryExtensionsViewSourceExpression(
                     operation, out source, out firstArgument) &&
                 IsSupportedMemoryExtensionsViewSource(source, context))
            allowRange = true;
        if (source == null || LowerBuiltInLength(source, context) is not { } sourceLength) return null;
        var remaining = method.Parameters.Length - firstArgument;
        if (remaining == 0) return sourceLength;
        if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, firstArgument, out var first)) return null;
        if (remaining == 1) {
            if (allowRange && LowerRangeLength(first, source, context) is { } rangeLength) return rangeLength;
            return LowerInteger(first, context) is { } start
                ? new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, start)
                : null;
        }
        return remaining == 2 &&
               SymbolicValueFacts.TryGetInvocationArgumentExpression(operation, firstArgument + 1, out var second)
            ? LowerInteger(second, context)
            : null;
    }
    private static bool IsSupportedMemoryExtensionsViewSource(
        ExpressionSyntax source,
        SymbolicLoweringContext context) {
        var type = GetPreferredLengthSemanticType(source, context);
        return type?.SpecialType == SpecialType.System_String || type is IArrayTypeSymbol { Rank: 1 };
    }
    private static SymbolicTerm? LowerRangeAccessLength(ExpressionSyntax expression, SymbolicLoweringContext context)
        => expression is ElementAccessExpressionSyntax { ArgumentList.Arguments.Count: 1 } access &&
           IsSupportedRangeSource(GetPreferredLengthSemanticType(access.Expression, context))
            ? LowerRangeLength(access.ArgumentList.Arguments[0].Expression, access.Expression, context)
            : null;
    private static bool IsSupportedRangeSource(ITypeSymbol? type)
        => type?.SpecialType == SpecialType.System_String ||
           type is IArrayTypeSymbol { Rank: 1 } ||
           SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type);
    private static SymbolicTerm? LowerRangeLength(
        ExpressionSyntax rangeExpression,
        ExpressionSyntax sourceExpression,
        SymbolicLoweringContext context)
        => TryBindRange(rangeExpression, context, out var range) &&
           LowerBuiltInLength(sourceExpression, context) is { } length &&
           TryLowerRange(range, length, context, out var start, out var end)
            ? new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, end, start)
            : null;
    private static bool TryCreateRangeBounds(
        RangeShape range,
        SymbolicTerm length,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!TryLowerRange(range, length, context, out var start, out var end) ||
            !TryGetWellFormed(range, source, provenance + ".well-formed", context, out var wellFormed))
            return false;
        var inRange = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            Relation(SymbolicRelationOperator.GreaterThanOrEqual, start, 0, source,
                provenance + ".start-nonnegative"),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                CreateRelationCondition(
                    SymbolicRelationOperator.LessThanOrEqual,
                    start,
                    end,
                    source,
                    provenance + ".ordered-endpoints"),
                CreateRelationCondition(
                    SymbolicRelationOperator.LessThanOrEqual,
                    end,
                    length,
                    source,
                    provenance + ".end-within-length")));
        condition = wellFormed == null
            ? inRange
            : new SymbolicBinaryCondition(SymbolicConditionOperator.And, wellFormed, inRange);
        return true;
    }
    private static bool TryLowerRange(
        RangeShape range,
        SymbolicTerm length,
        SymbolicLoweringContext context,
        out SymbolicTerm start,
        out SymbolicTerm end) {
        start = range.Start is { } startShape
            ? LowerIndex(startShape, length, context)!
            : new SymbolicIntegerConstantTerm(0);
        end = range.End is { } endShape
            ? LowerIndex(endShape, length, context)!
            : length;
        return start != null && end != null;
    }
    private static SymbolicTerm? LowerIndex(
        IndexShape shape,
        SymbolicTerm length,
        SymbolicLoweringContext context) {
        if (LowerInteger(shape.ValueExpression, context) is not { } value) return null;
        return shape.FromEnd
            ? new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, length, value)
            : value;
    }
    private static bool TryGetWellFormed(
        RangeShape range,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition? condition) {
        condition = null;
        if (range.Start is { } start &&
            !TryGetWellFormed(start, source, provenance + ".start", context, out condition))
            return false;
        if (range.End is not { } end) return true;
        if (!TryGetWellFormed(end, source, provenance + ".end", context, out var endCondition)) return false;
        if (endCondition != null)
            condition = condition == null
                ? endCondition
                : new SymbolicBinaryCondition(SymbolicConditionOperator.And, condition, endCondition);
        return true;
    }
    private static bool TryGetWellFormed(
        IndexShape shape,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition? condition) {
        condition = null;
        if (!shape.RequiresNonNegative) return true;
        if (LowerInteger(shape.ValueExpression, context) is not { } value) return false;
        condition = Relation(SymbolicRelationOperator.GreaterThanOrEqual, value, 0, source, provenance);
        return true;
    }
    private static bool TryBindRange(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RangeShape range) {
        expression = UnwrapExpression(expression);
        if (TryBindDirectRange(expression, context, out range)) return true;
        if (!IsSystemShape(expression, context, SymbolicTypeFacts.IsSystemRangeType) ||
            !TryGetShapeSymbol(expression, context, SymbolicTypeFacts.IsSystemRangeType, out var symbol))
            return false;
        return TryResolveAssignedShape(expression, symbol, context, TryBindDirectRange, out range);
    }
    private static bool TryBindDirectRange(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RangeShape range) {
        expression = UnwrapExpression(expression);
        if (expression is RangeExpressionSyntax syntax) {
            if (!TryBindOptionalIndex(syntax.LeftOperand, context, out var start) ||
                !TryBindOptionalIndex(syntax.RightOperand, context, out var end)) {
                range = default;
                return false;
            }
            range = new RangeShape(start, end);
            return true;
        }
        if (TryGetInvocationOperation(expression, context, out _, out var invocation) &&
            IsBuiltInShapeMethod(invocation.TargetMethod, context, SymbolicTypeFacts.IsSystemRangeType, true) &&
            invocation.TargetMethod.Name is "StartAt" or "EndAt" &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocation, 0, out var endpointExpression) &&
            TryBindIndex(endpointExpression, context, out var endpoint)) {
            range = invocation.TargetMethod.Name == "StartAt"
                ? new RangeShape(endpoint, null)
                : new RangeShape(null, endpoint);
            return true;
        }
        if (context.SemanticModel.GetOperation(expression, context.CancellationToken) is
                IObjectCreationOperation { Constructor: { } constructor } creation &&
            IsBuiltInShapeMethod(constructor, context, SymbolicTypeFacts.IsSystemRangeType) &&
            SymbolicValueFacts.TryGetObjectCreationArgumentExpression(creation, 0, out var startExpression) &&
            SymbolicValueFacts.TryGetObjectCreationArgumentExpression(creation, 1, out var endExpression) &&
            TryBindIndex(startExpression, context, out var createdStart) &&
            TryBindIndex(endExpression, context, out var createdEnd)) {
            range = new RangeShape(createdStart, createdEnd);
            return true;
        }
        if (context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is IPropertySymbol {
            Name: "All",
            IsStatic: true
        } property &&
            SymbolicTypeFacts.IsSystemRangeType(property.ContainingType, context.Compilation) &&
            SymbolicTypeFacts.IsSystemRangeType(property.Type, context.Compilation)) {
            range = new RangeShape(null, null);
            return true;
        }
        range = default;
        return false;
    }
    private static bool TryBindOptionalIndex(
        ExpressionSyntax? expression,
        SymbolicLoweringContext context,
        out IndexShape? index) {
        if (expression == null) {
            index = null;
            return true;
        }
        if (TryBindIndex(expression, context, out var value)) {
            index = value;
            return true;
        }
        index = null;
        return false;
    }
    private static bool TryBindIndex(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out IndexShape index) {
        expression = UnwrapExpression(expression);
        if (TryBindDirectIndex(expression, context, out index)) return true;
        if (!IsSystemShape(expression, context, SymbolicTypeFacts.IsSystemIndexType) ||
            !TryGetShapeSymbol(expression, context, SymbolicTypeFacts.IsSystemIndexType, out var symbol))
            return false;
        return TryResolveAssignedShape(expression, symbol, context, TryBindDirectIndex, out index);
    }
    private static bool TryBindDirectIndex(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out IndexShape index) {
        expression = UnwrapExpression(expression);
        if (TryGetIndexConstruction(expression, context, out var construction) &&
            construction.FromEnd is { } fromEnd) {
            index = new IndexShape(construction.ValueExpression, fromEnd, true);
            return true;
        }
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type;
        if (SymbolicTypeFacts.IsBuiltInIntegralOrEnumType(type)) {
            index = new IndexShape(expression, false, false);
            return true;
        }
        index = default;
        return false;
    }
    internal static bool TryGetIndexConstructionValueExpression(
        SyntaxNode candidate,
        SymbolicLoweringContext context,
        out ExpressionSyntax valueExpression) {
        if (candidate is ExpressionSyntax expression &&
            TryGetIndexConstruction(expression, context, out var construction)) {
            valueExpression = construction.ValueExpression;
            return true;
        }
        valueExpression = null!;
        return false;
    }
    private static bool TryGetIndexConstruction(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out IndexConstruction construction) {
        expression = UnwrapExpression(expression);
        if (expression is PrefixUnaryExpressionSyntax prefix &&
            (prefix.IsKind(SyntaxKind.IndexExpression) || prefix.OperatorToken.IsKind(SyntaxKind.CaretToken))) {
            construction = new IndexConstruction(prefix.Operand, true);
            return true;
        }
        if (TryGetInvocationOperation(expression, context, out _, out var invocation) &&
            IsBuiltInShapeMethod(invocation.TargetMethod, context, SymbolicTypeFacts.IsSystemIndexType, true) &&
            invocation.TargetMethod.Name is "FromStart" or "FromEnd" &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocation, 0, out var invokedValue)) {
            construction = new IndexConstruction(invokedValue, invocation.TargetMethod.Name == "FromEnd");
            return true;
        }
        if (context.SemanticModel.GetOperation(expression, context.CancellationToken) is
                IObjectCreationOperation { Constructor: { } constructor } creation &&
            IsBuiltInShapeMethod(constructor, context, SymbolicTypeFacts.IsSystemIndexType) &&
            SymbolicValueFacts.TryGetObjectCreationArgumentExpression(creation, 0, out var createdValue)) {
            bool? fromEnd = false;
            if (SymbolicValueFacts.TryGetObjectCreationArgumentExpression(creation, 1, out var direction))
                fromEnd = context.SemanticModel.GetConstantValue(direction, context.CancellationToken) is { HasValue: true, Value: bool value }
                    ? value
                    : null;
            construction = new IndexConstruction(createdValue, fromEnd);
            return true;
        }
        construction = default;
        return false;
    }
    private static bool TryGetShapeSymbol(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        Func<ITypeSymbol?, Compilation, bool> isShapeType,
        out ISymbol symbol) {
        symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol!;
        return symbol switch {
            ILocalSymbol local => isShapeType(local.Type, context.Compilation),
            IParameterSymbol { RefKind: RefKind.None } parameter => isShapeType(parameter.Type, context.Compilation),
            _ => false
        };
    }
    private static bool TryResolveAssignedShape<T>(
        ExpressionSyntax use,
        ISymbol symbol,
        SymbolicLoweringContext context,
        TryBindShape<T> binder,
        out T shape)
        where T : struct {
        if (SymbolCurrentValueResolver.TryResolveCurrentSimpleValueExpression(
                symbol, use, context.SemanticModel, context.CancellationToken, out var value))
            return binder(value, context, out shape);
        shape = default;
        return false;
    }
    private static bool IsBuiltInShapeMethod(
        IMethodSymbol? method,
        SymbolicLoweringContext context,
        Func<ITypeSymbol?, Compilation, bool> isShapeType,
        bool validateReturn = false)
        => method != null &&
           isShapeType(method.ContainingType, context.Compilation) &&
           (!validateReturn ||
            method.MethodKind == MethodKind.Ordinary && isShapeType(method.ReturnType, context.Compilation));
    private static bool IsSystemShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        Func<ITypeSymbol?, Compilation, bool> isShapeType) {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return isShapeType(type.ConvertedType ?? type.Type, context.Compilation);
    }
    private static bool IsSupportedElementReceiver(ITypeSymbol? type)
        => type is IArrayTypeSymbol { Rank: 1 } ||
           type?.SpecialType == SpecialType.System_String ||
           SymbolicTypeFacts.IsBuiltInSpanType(type) ||
           HasCountBackedIntIndexer(type);
    internal static bool TryGetInvocationOperation(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out InvocationExpressionSyntax invocation,
        out IInvocationOperation operation) {
        if (expression is InvocationExpressionSyntax candidate &&
            context.SemanticModel.GetOperation(candidate, context.CancellationToken) is IInvocationOperation value) {
            invocation = candidate;
            operation = value;
            return true;
        }
        invocation = null!;
        operation = null!;
        return false;
    }
    private static SymbolicTerm? LowerInteger(ExpressionSyntax expression, SymbolicLoweringContext context)
        => LowerValue(expression, context, SmtValueKind.Int);
    private static SymbolicTerm? LowerValue(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        SmtValueKind? requiredKind = null) {
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(UnwrapExpression(expression), context), out var value) ||
            requiredKind is { } kind && value.Kind != kind)
            return null;
        return value;
    }
    private static SymbolicCondition Relation(
        SymbolicRelationOperator relation,
        SymbolicTerm left,
        long right,
        SyntaxNode source,
        string provenance)
        => CreateRelationCondition(
            relation, left, new SymbolicIntegerConstantTerm(right), source, provenance);
    internal static ITypeSymbol? GetPreferredLengthSemanticType(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) {
        var type = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return HasLengthShape(type.Type) && !HasLengthShape(type.ConvertedType)
            ? type.Type
            : type.ConvertedType ?? type.Type;
    }
    private static bool HasLengthShape(ITypeSymbol? type)
        => type?.SpecialType == SpecialType.System_String ||
           type is IArrayTypeSymbol { Rank: >= 1 } ||
           SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type) ||
           SymbolicTypeFacts.HasInstanceInt32Member(type, "Count");
    private readonly record struct IndexShape(
        ExpressionSyntax ValueExpression,
        bool FromEnd,
        bool RequiresNonNegative);
    private readonly record struct IndexConstruction(ExpressionSyntax ValueExpression, bool? FromEnd);
    private readonly record struct RangeShape(IndexShape? Start, IndexShape? End);
}
