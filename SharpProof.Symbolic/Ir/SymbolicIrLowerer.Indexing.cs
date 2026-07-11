using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Ir;

internal static partial class SymbolicIrLowerer
{
    private static bool TryLowerElementAccessTerm(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (TryLowerFiniteArrayElementAccessTerm(elementAccess, context, out term)) return true;

        var receiverTypeInfo = context.SemanticModel.GetTypeInfo(
            elementAccess.Expression,
            context.CancellationToken);
        var receiverType = receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type;
        if (!TryGetBuiltInElementAccessElementType(
                receiverType,
                context.SemanticModel.Compilation,
                out var elementType) ||
            !TryGetValueKind(elementType, out var elementKind) ||
            !TryLowerTerm(elementAccess.Expression, context, out var receiver) ||
            receiver.Kind != SmtValueKind.Reference)
            return false;

        if (receiverType is IArrayTypeSymbol { Rank: > 1 } arrayType)
        {
            if (elementAccess.ArgumentList.Arguments.Count != arrayType.Rank) return false;

            var indices = ImmutableArray.CreateBuilder<SymbolicTerm>(arrayType.Rank);
            foreach (var argument in elementAccess.ArgumentList.Arguments)
            {
                if (!TryLowerTerm(UnwrapExpression(argument.Expression), context, out var dimensionIndex) ||
                    dimensionIndex.Kind != SmtValueKind.Int)
                    return false;

                indices.Add(dimensionIndex);
            }

            term = new SymbolicMultiElementTerm(receiver, indices.MoveToImmutable(), elementKind);
            return true;
        }

        if (elementAccess.ArgumentList.Arguments.Count != 1 ||
            !TryResolveBuiltInIndexLengthShape(
                elementAccess.ArgumentList.Arguments[0].Expression,
                context,
                out var indexShape) ||
            !TryLowerTerm(indexShape.ValueExpression, context, out var index) ||
            index.Kind != SmtValueKind.Int)
            return false;

        term = new SymbolicElementTerm(
            receiver,
            indexShape.FromEnd ? new SymbolicFromEndIndexTerm(index) : index,
            elementKind);
        return true;
    }

    private static bool TryLowerFiniteArrayElementAccessTerm(
        ElementAccessExpressionSyntax elementAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (elementAccess.ArgumentList.Arguments.Count != 1 ||
            !TryResolveBuiltInIndexLengthShape(
                elementAccess.ArgumentList.Arguments[0].Expression,
                context,
                out var indexShape))
            return false;

        var constantIndex = context.SemanticModel.GetConstantValue(
            indexShape.ValueExpression,
            context.CancellationToken);
        if (!constantIndex.HasValue ||
            constantIndex.Value == null ||
            !TryGetIntegralConstant(constantIndex.Value, out var indexValue))
            return false;

        var receiver = UnwrapExpression(elementAccess.Expression);
        SeparatedSyntaxList<ExpressionSyntax>? initializerExpressions = receiver switch
        {
            ArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            ImplicitArrayCreationExpressionSyntax { Initializer: { } initializer } => initializer.Expressions,
            _ => null
        };
        if (initializerExpressions is not { } expressions || expressions.Count == 0) return false;

        var resolvedIndex = indexShape.FromEnd
            ? expressions.Count - indexValue
            : indexValue;
        if (resolvedIndex < 0 || resolvedIndex >= expressions.Count) return false;

        return TryLowerTerm(expressions[(int)resolvedIndex], context, out term);
    }

    private static bool TryGetBuiltInElementAccessElementType(
        ITypeSymbol? receiverType,
        Compilation compilation,
        out ITypeSymbol elementType)
    {
        if (receiverType is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (receiverType?.SpecialType == SpecialType.System_String)
        {
            elementType = compilation.GetSpecialType(SpecialType.System_Char);
            return true;
        }

        if (receiverType is INamedTypeSymbol namedType &&
            SymbolicTypeFacts.IsBuiltInSpanType(namedType) &&
            namedType.TypeArguments.Length == 1)
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        if (TryGetInt32IndexerElementType(receiverType, out elementType)) return true;

        elementType = null!;
        return false;
    }

    private static bool TryGetInt32IndexerElementType(
        ITypeSymbol? typeSymbol,
        out ITypeSymbol elementType)
    {
        if (typeSymbol == null || !SymbolicTypeFacts.HasInstanceInt32Member(typeSymbol, "Count"))
        {
            elementType = null!;
            return false;
        }

        for (var current = typeSymbol; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            if (TryGetDeclaredInt32IndexerElementType(current, out elementType))
                return true;

        foreach (var interfaceType in typeSymbol.AllInterfaces)
            if (TryGetDeclaredInt32IndexerElementType(interfaceType, out elementType))
                return true;

        elementType = null!;
        return false;
    }

    private static bool TryGetDeclaredInt32IndexerElementType(
        ITypeSymbol typeSymbol,
        out ITypeSymbol elementType)
    {
        foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
            if (property is { IsIndexer: true, IsStatic: false, Parameters.Length: 1 } &&
                property.Parameters[0].Type.SpecialType == SpecialType.System_Int32)
            {
                elementType = property.Type;
                return true;
            }

        elementType = null!;
        return false;
    }

    private static bool TryLowerArrayGetLengthInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            method.ContainingType?.SpecialType != SpecialType.System_Array ||
            method.Parameters.Length != 1 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_Int32)
            return false;

        var dimensionValue = context.SemanticModel.GetConstantValue(
            invocation.ArgumentList.Arguments[0].Expression,
            context.CancellationToken);
        if (dimensionValue is not { HasValue: true, Value: int dimension }) return false;

        if (dimension == 0 &&
            GetPreferredLengthSemanticType(memberAccess.Expression, context) is IArrayTypeSymbol { Rank: 1 } &&
            TryLowerBuiltInLengthTerm(memberAccess.Expression, context, out term))
            return true;

        return TryLowerArrayDimensionLengthTerm(
            memberAccess.Expression,
            dimension,
            context,
            out term);
    }

    private static bool TryLowerArrayBoundInvocation(
        InvocationExpressionSyntax invocation,
        IMethodSymbol method,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
            invocation.ArgumentList.Arguments.Count != 1 ||
            method.ContainingType?.SpecialType != SpecialType.System_Array ||
            method.Parameters.Length != 1 ||
            method.Parameters[0].Type.SpecialType != SpecialType.System_Int32)
            return false;

        var receiverType = GetPreferredLengthSemanticType(memberAccess.Expression, context);
        if (receiverType is not IArrayTypeSymbol) return false;

        var dimensionValue = context.SemanticModel.GetConstantValue(
            invocation.ArgumentList.Arguments[0].Expression,
            context.CancellationToken);
        if (dimensionValue is not { HasValue: true, Value: int dimension }) return false;

        if (string.Equals(method.Name, nameof(Array.GetLowerBound), StringComparison.Ordinal))
        {
            if (!TryLowerArrayDimensionLengthTerm(memberAccess.Expression, dimension, context, out _)) return false;

            term = new SymbolicIntegerConstantTerm(0);
            return true;
        }

        if (!string.Equals(method.Name, nameof(Array.GetUpperBound), StringComparison.Ordinal) ||
            !TryLowerArrayDimensionLengthTerm(memberAccess.Expression, dimension, context, out var length))
            return false;

        term = new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Subtract,
            length,
            new SymbolicIntegerConstantTerm(1));
        return true;
    }

    public static bool TryLowerArrayDimensionLengthTerm(
        ExpressionSyntax arrayExpression,
        int dimension,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        arrayExpression = UnwrapExpression(arrayExpression);
        var type = GetPreferredLengthSemanticType(arrayExpression, context);
        if (type is not IArrayTypeSymbol arrayType ||
            dimension < 0 ||
            dimension >= arrayType.Rank)
        {
            term = null!;
            return false;
        }

        if (TryLowerArrayCreationDimensionLengthTerm(arrayExpression, arrayType, dimension, context, out term))
            return true;

        if (TryLowerReferenceCastArrayDimensionLengthTerm(arrayExpression, arrayType, dimension, context, out term))
            return true;

        if (!TryLowerTerm(arrayExpression, context, out var arrayTerm) ||
            arrayTerm.Kind != SmtValueKind.Reference)
        {
            term = null!;
            return false;
        }

        if (arrayType.Rank == 1 &&
            dimension == 0 &&
            CreateLengthTerm(arrayTerm, out term))
            return true;

        term = new SymbolicArrayDimensionLengthTerm(arrayTerm, dimension);
        return true;
    }

    private static bool TryLowerReferenceCastArrayDimensionLengthTerm(
        ExpressionSyntax arrayExpression,
        IArrayTypeSymbol arrayType,
        int dimension,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (arrayExpression is not CastExpressionSyntax castExpression) return false;

        var targetType = context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type;
        if (!SymbolEqualityComparer.Default.Equals(targetType, arrayType)) return false;

        if (TryLowerArrayCreationDimensionLengthTerm(castExpression.Expression, arrayType, dimension, context,
                out term)) return true;

        if (!TryLowerTerm(castExpression.Expression, context, out var operand) ||
            operand.Kind != SmtValueKind.Reference)
            return false;

        term = new SymbolicArrayDimensionLengthTerm(operand, dimension);
        return true;
    }

    private static bool TryLowerArrayTotalLengthTerm(
        ExpressionSyntax arrayExpression,
        IArrayTypeSymbol arrayType,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        arrayExpression = UnwrapExpression(arrayExpression);
        if (arrayType.Rank <= 0) return false;

        if (TryLowerArrayCreationTotalLengthTerm(arrayExpression, arrayType, context, out term)) return true;

        return TryLowerTerm(arrayExpression, context, out var arrayTerm) &&
               TryCreateArrayTotalLengthReferenceTerm(arrayTerm, arrayType, out term);
    }

    internal static bool TryCreateBuiltInLengthReferenceTerm(
        ITypeSymbol? type,
        SymbolicTerm reference,
        out SymbolicTerm term)
    {
        term = null!;
        if (reference.Kind != SmtValueKind.Reference ||
            type == null)
            return false;

        if (type.SpecialType == SpecialType.System_String)
            return TryCreateStringContentReferenceTerm(reference, out var stringContent) &&
                   CreateLengthTerm(stringContent, out term);

        if (type is IArrayTypeSymbol { Rank: 1 } ||
            IsBuiltInSpanOrMemoryType(type))
            return CreateLengthTerm(reference, out term);

        if (type is not IArrayTypeSymbol &&
            HasCountBackedIntIndexer(type))
        {
            term = new SymbolicCountTerm(reference);
            return true;
        }

        if (type is not IArrayTypeSymbol &&
            SymbolicTypeFacts.HasInstanceInt32Member(type, "Count"))
        {
            term = new SymbolicCountTerm(reference);
            return true;
        }

        return type is IArrayTypeSymbol { Rank: > 1 } multiDimensionalArray &&
               TryCreateArrayTotalLengthReferenceTerm(reference, multiDimensionalArray, out term);
    }

    private static bool HasCountBackedIntIndexer(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.HasInstanceInt32Member(typeSymbol, "Count") &&
               SymbolicTypeFacts.HasInt32Indexer(typeSymbol);
    }

    private static bool TryCreateArrayTotalLengthReferenceTerm(
        SymbolicTerm arrayTerm,
        IArrayTypeSymbol arrayType,
        out SymbolicTerm term)
    {
        term = null!;
        if (arrayTerm.Kind != SmtValueKind.Reference ||
            arrayType.Rank <= 0)
            return false;

        SymbolicTerm totalLength = new SymbolicArrayDimensionLengthTerm(arrayTerm, 0);
        for (var dimension = 1; dimension < arrayType.Rank; dimension++)
            totalLength = new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Multiply,
                totalLength,
                new SymbolicArrayDimensionLengthTerm(arrayTerm, dimension));

        term = totalLength;
        return true;
    }

    private static bool CreateLengthTerm(SymbolicTerm value, out SymbolicTerm term)
    {
        term = new SymbolicLengthTerm(value);
        return true;
    }

    private static bool TryLowerArrayCreationTotalLengthTerm(
        ExpressionSyntax arrayExpression,
        IArrayTypeSymbol arrayType,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (arrayType.Rank <= 0 ||
            !TryLowerArrayCreationDimensionLengthTerm(arrayExpression, arrayType, 0, context, out var totalLength))
            return false;

        for (var dimension = 1; dimension < arrayType.Rank; dimension++)
        {
            if (!TryLowerArrayCreationDimensionLengthTerm(arrayExpression, arrayType, dimension, context,
                    out var dimensionLength)) return false;

            totalLength = new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Multiply,
                totalLength,
                dimensionLength);
        }

        term = totalLength;
        return true;
    }

    private static bool TryLowerArrayCreationDimensionLengthTerm(
        ExpressionSyntax arrayExpression,
        IArrayTypeSymbol arrayType,
        int dimension,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (dimension != 0 ||
            arrayType.Rank != 1 ||
            arrayExpression is not ImplicitArrayCreationExpressionSyntax implicitArrayCreation ||
            implicitArrayCreation.Initializer == null)
            return LowerExplicitArrayCreationDimensionLengthTerm(
                arrayExpression,
                arrayType,
                dimension,
                context,
                out term);

        term = new SymbolicIntegerConstantTerm(implicitArrayCreation.Initializer.Expressions.Count);
        return true;
    }

    private static bool LowerExplicitArrayCreationDimensionLengthTerm(
        ExpressionSyntax arrayExpression,
        IArrayTypeSymbol arrayType,
        int dimension,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (arrayExpression is not ArrayCreationExpressionSyntax arrayCreation ||
            arrayCreation.Type.RankSpecifiers.Count == 0)
            return false;

        var rankSpecifier = arrayCreation.Type.RankSpecifiers[0];
        if (rankSpecifier.Sizes.Count != arrayType.Rank ||
            rankSpecifier.Sizes[dimension].IsKind(SyntaxKind.OmittedArraySizeExpression))
        {
            if (rankSpecifier.Sizes.Count == arrayType.Rank &&
                rankSpecifier.Sizes[dimension].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                arrayCreation.Initializer != null &&
                dimension == 0)
            {
                term = new SymbolicIntegerConstantTerm(arrayCreation.Initializer.Expressions.Count);
                return true;
            }

            return false;
        }

        if (!TryLowerTerm(rankSpecifier.Sizes[dimension], context, out var sizeTerm) ||
            sizeTerm.Kind != SmtValueKind.Int)
            return false;

        term = sizeTerm;
        return true;
    }

    public static bool TryCreateArrayElementBoundsCondition(
        ExpressionSyntax arrayExpression,
        IReadOnlyList<ExpressionSyntax> indexExpressions,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition,
        out SymbolicTerm? subject)
    {
        condition = null!;
        subject = null;
        var arrayType = GetPreferredLengthSemanticType(arrayExpression, context);
        if (arrayType is not IArrayTypeSymbol { Rank: > 0 } typedArray ||
            indexExpressions.Count != typedArray.Rank)
            return false;

        SymbolicCondition? combined = null;
        for (var dimension = 0; dimension < typedArray.Rank; dimension++)
        {
            if (!TryLowerTerm(indexExpressions[dimension], context, out var index) ||
                index.Kind != SmtValueKind.Int ||
                !TryLowerArrayDimensionLengthTerm(arrayExpression, dimension, context, out var length))
                return false;

            subject ??= index;
            var dimensionInRange = new SymbolicFactCondition(SymbolicFact.Exact(
                new SymbolicBoundsAtom(
                    index,
                    length,
                    true,
                    true),
                source,
                provenance));
            combined = combined == null
                ? dimensionInRange
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    combined,
                    dimensionInRange);
        }

        if (combined == null) return false;

        condition = combined;
        return true;
    }

    public static bool TryCreateBuiltInElementAccessInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax argumentExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        receiverExpression = UnwrapExpression(receiverExpression);
        argumentExpression = UnwrapExpression(argumentExpression);
        var receiverType = GetPreferredLengthSemanticType(receiverExpression, context);
        if (!IsSupportedBuiltInElementAccessReceiver(receiverType) ||
            !TryLowerBuiltInLengthTerm(receiverExpression, context, out var sourceLength))
            return false;

        if (TryResolveBuiltInRangeLengthShape(argumentExpression, context, out var rangeShape))
            return TryCreateBuiltInRangeAccessInRangeCondition(
                rangeShape,
                sourceLength,
                source,
                provenance,
                context,
                out condition);

        if (!TryResolveBuiltInIndexLengthShape(argumentExpression, context, out var indexShape) ||
            !TryCreateEffectiveBuiltInIndexTerm(indexShape, sourceLength, context, out var effectiveIndex))
            return false;

        var inRange = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicBoundsAtom(
                effectiveIndex,
                sourceLength,
                true,
                true),
            source,
            provenance));
        if (!TryCreateIndexShapeWellFormedCondition(
                indexShape,
                source,
                provenance + ".well-formed",
                context,
                out var wellFormed))
            return false;

        condition = ApplyWellFormedPrecondition(wellFormed, inRange);
        return true;
    }

    public static bool TryCreateSubsequenceInRangeCondition(
        ExpressionSyntax receiverExpression,
        ExpressionSyntax startExpression,
        ExpressionSyntax? lengthExpression,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        bool oneArgumentUpperBoundIsInclusive,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!TryLowerBuiltInLengthTerm(receiverExpression, context, out var sourceLength) ||
            !TryLowerTerm(startExpression, context, out var start) ||
            start.Kind != SmtValueKind.Int)
            return false;

        var startNonNegative = CreateRelationCondition(
            SymbolicRelationOperator.GreaterThanOrEqual,
            start,
            new SymbolicIntegerConstantTerm(0),
            source,
            provenance + ".start-non-negative");

        if (lengthExpression == null)
        {
            var upperBound = CreateRelationCondition(
                oneArgumentUpperBoundIsInclusive
                    ? SymbolicRelationOperator.LessThanOrEqual
                    : SymbolicRelationOperator.LessThan,
                start,
                sourceLength,
                source,
                provenance + ".start-within-length");
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                startNonNegative,
                upperBound);
            return true;
        }

        if (!TryLowerTerm(lengthExpression, context, out var count) ||
            count.Kind != SmtValueKind.Int)
            return false;

        var countNonNegative = CreateRelationCondition(
            SymbolicRelationOperator.GreaterThanOrEqual,
            count,
            new SymbolicIntegerConstantTerm(0),
            source,
            provenance + ".count-non-negative");
        var startWithinLength = CreateRelationCondition(
            SymbolicRelationOperator.LessThanOrEqual,
            start,
            sourceLength,
            source,
            provenance + ".start-within-length");
        var remainingLength = new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Subtract,
            sourceLength,
            start);
        var countWithinRemainingLength = CreateRelationCondition(
            SymbolicRelationOperator.LessThanOrEqual,
            count,
            remainingLength,
            source,
            provenance + ".count-within-remaining-length");
        var additionDoesNotOverflow = count is SymbolicIntegerConstantTerm { Value: 0 }
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
                    startWithinLength,
                    new SymbolicBinaryCondition(
                        SymbolicConditionOperator.And,
                        countWithinRemainingLength,
                        additionDoesNotOverflow))));
        return true;
    }

    public static bool TryLowerBuiltInLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        expression = UnwrapExpression(expression);

        if (expression is CastExpressionSyntax castExpression)
        {
            var castTargetType = context.SemanticModel.GetTypeInfo(castExpression.Type, context.CancellationToken).Type;
            if (castTargetType?.SpecialType == SpecialType.System_String)
            {
                if (TryLowerStringTerm(castExpression, context, out var castString))
                {
                    term = new SymbolicLengthTerm(castString);
                    return true;
                }

                if (TryLowerTerm(castExpression.Expression, context, out var castReference) &&
                    TryCreateBuiltInLengthReferenceTerm(castTargetType, castReference, out term))
                    return true;
            }
            else if (castTargetType is IArrayTypeSymbol { Rank: 1 } castArrayType)
            {
                if (castExpression.Expression is ArrayCreationExpressionSyntax
                        or ImplicitArrayCreationExpressionSyntax &&
                    TryLowerArrayCreationTotalLengthTerm(castExpression.Expression, castArrayType, context, out term))
                    return true;

                if (TryLowerTerm(castExpression.Expression, context, out var castReference) &&
                    TryCreateBuiltInLengthReferenceTerm(castArrayType, castReference, out term))
                    return true;
            }
        }

        if (expression is CollectionExpressionSyntax collectionExpression &&
            TryLowerCollectionExpressionLengthTerm(collectionExpression, context, out term))
            return true;

        if (TryLowerDirectRangeAccessResultLengthTerm(expression, context, out term)) return true;

        if (TryLowerBuiltInViewResultLengthTerm(expression, context, out term)) return true;

        if (TryLowerStringCreationResultLengthTerm(expression, context, out term)) return true;

        if (TryLowerStringInvocationResultLengthTerm(expression, context, out term)) return true;

        var type = GetPreferredLengthSemanticType(expression, context);
        if (type?.SpecialType == SpecialType.System_String)
        {
            if (TryLowerStringTerm(expression, context, out var stringValue))
            {
                term = new SymbolicLengthTerm(stringValue);
                return true;
            }

            if (TryLowerTerm(expression, context, out var reference) &&
                TryCreateBuiltInLengthReferenceTerm(type, reference, out term))
                return true;
        }

        if (expression is ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax &&
            type is IArrayTypeSymbol arrayCreationType &&
            TryLowerArrayTotalLengthTerm(expression, arrayCreationType, context, out term))
            return true;

        if (expression is InvocationExpressionSyntax arrayEmptyInvocation &&
            context.SemanticModel.GetSymbolInfo(arrayEmptyInvocation, context.CancellationToken).Symbol is IMethodSymbol
            {
                Name: "Empty",
                IsStatic: true,
                ContainingType.SpecialType: SpecialType.System_Array
            })
        {
            term = new SymbolicIntegerConstantTerm(0);
            return true;
        }

        if (expression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryLowerBuiltInLengthTerm(coalesceExpression.Left, context, out var coalesceLeftLength) &&
            TryLowerBuiltInLengthTerm(coalesceExpression.Right, context, out var coalesceRightLength) &&
            coalesceLeftLength.Kind == SmtValueKind.Int &&
            coalesceRightLength.Kind == SmtValueKind.Int &&
            TryLowerTerm(coalesceExpression.Left, context, out var coalesceLeftReceiver) &&
            coalesceLeftReceiver.Kind == SmtValueKind.Reference)
        {
            term = new SymbolicConditionalTerm(
                CreateRelationCondition(
                    SymbolicRelationOperator.NotEqual,
                    coalesceLeftReceiver,
                    new SymbolicNullTerm(),
                    coalesceExpression.Left,
                    "ir.coalesce.left-not-null"),
                coalesceLeftLength,
                coalesceRightLength);
            return true;
        }

        if (expression is ConditionalExpressionSyntax conditionalLengthExpression &&
            TryLowerBuiltInLengthTerm(conditionalLengthExpression.WhenTrue, context, out var whenTrueLength) &&
            TryLowerBuiltInLengthTerm(conditionalLengthExpression.WhenFalse, context, out var whenFalseLength) &&
            whenTrueLength.Kind == SmtValueKind.Int &&
            whenFalseLength.Kind == SmtValueKind.Int &&
            TryLowerCondition(conditionalLengthExpression.Condition, context, out var lengthCondition))
        {
            term = new SymbolicConditionalTerm(lengthCondition, whenTrueLength, whenFalseLength);
            return true;
        }

        if (TryLowerTerm(expression, context, out var receiver) &&
            TryCreateBuiltInLengthReferenceTerm(type, receiver, out term))
            return true;

        term = null!;
        return false;
    }

    private static bool TryLowerCollectionExpressionLengthTerm(
        CollectionExpressionSyntax collectionExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = new SymbolicIntegerConstantTerm(0);
        foreach (var element in collectionExpression.Elements)
        {
            SymbolicTerm elementLength;
            switch (element)
            {
                case ExpressionElementSyntax:
                    elementLength = new SymbolicIntegerConstantTerm(1);
                    break;
                case SpreadElementSyntax spreadElement:
                    if (!TryLowerBuiltInLengthTerm(spreadElement.Expression, context, out elementLength) ||
                        elementLength.Kind != SmtValueKind.Int)
                    {
                        term = null!;
                        return false;
                    }

                    break;
                default:
                    term = null!;
                    return false;
            }

            term = term is SymbolicIntegerConstantTerm { Value: 0 }
                ? elementLength
                : new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, term, elementLength);
        }

        return true;
    }

    private static bool TryLowerBuiltInViewResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        return TryLowerBuiltInSliceInvocationResultLengthTerm(expression, context, out term) ||
               TryLowerMemoryExtensionsViewResultLengthTerm(expression, context, out term);
    }

    private static bool TryLowerBuiltInSliceInvocationResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (expression is not InvocationExpressionSyntax invocationExpression ||
            context.SemanticModel.GetOperation(invocationExpression, context.CancellationToken) is not
                IInvocationOperation invocationOperation)
            return false;

        var method = invocationOperation.TargetMethod;
        if (method.IsStatic ||
            method.Name != "Slice" ||
            !IsBuiltInSpanOrMemoryType(method.ContainingType) ||
            !IsBuiltInSpanOrMemoryType(method.ReturnType) ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression)
            return false;

        return TryLowerViewLengthFromInvocationArguments(
            invocationExpression,
            invocationOperation,
            sourceExpression,
            0,
            false,
            context,
            out term);
    }

    private static bool TryLowerMemoryExtensionsViewResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (expression is not InvocationExpressionSyntax invocationExpression ||
            context.SemanticModel.GetOperation(invocationExpression, context.CancellationToken) is not
                IInvocationOperation invocationOperation ||
            !IsMemoryExtensionsViewMethod(invocationOperation.TargetMethod) ||
            !IsBuiltInSpanOrMemoryType(invocationOperation.TargetMethod.ReturnType) ||
            !TryGetMemoryExtensionsViewSourceExpression(invocationExpression, context, out var sourceExpression,
                out var firstArgumentIndex) ||
            !IsSupportedMemoryExtensionsViewSource(sourceExpression, context))
            return false;

        return TryLowerViewLengthFromInvocationArguments(
            invocationExpression,
            invocationOperation,
            sourceExpression,
            firstArgumentIndex,
            true,
            context,
            out term);
    }

    private static bool TryLowerViewLengthFromInvocationArguments(
        InvocationExpressionSyntax invocationExpression,
        IInvocationOperation invocationOperation,
        ExpressionSyntax sourceExpression,
        int firstArgumentIndex,
        bool allowDirectRangeArgument,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (!TryLowerBuiltInLengthTerm(sourceExpression, context, out var sourceLength)) return false;

        var remainingArgumentCount = invocationExpression.ArgumentList.Arguments.Count - firstArgumentIndex;
        if (remainingArgumentCount == 0)
        {
            term = sourceLength;
            return true;
        }

        if (remainingArgumentCount == 1)
        {
            var argument = invocationExpression.ArgumentList.Arguments[firstArgumentIndex].Expression;
            if (allowDirectRangeArgument &&
                TryCreateRangeLengthTerm(argument, sourceExpression, context, out term))
                return true;

            if (!TryLowerTerm(argument, context, out var start) ||
                start.Kind != SmtValueKind.Int)
                return false;

            term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, start);
            return true;
        }

        if (remainingArgumentCount != 2 ||
            !TryLowerTerm(
                invocationExpression.ArgumentList.Arguments[firstArgumentIndex].Expression,
                context,
                out var translatedStart) ||
            translatedStart.Kind != SmtValueKind.Int ||
            !TryLowerTerm(
                invocationExpression.ArgumentList.Arguments[firstArgumentIndex + 1].Expression,
                context,
                out var resultLength) ||
            resultLength.Kind != SmtValueKind.Int)
            return false;

        term = resultLength;
        return true;
    }

    private static bool TryLowerDirectRangeAccessResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (expression is not ElementAccessExpressionSyntax elementAccess ||
            elementAccess.ArgumentList.Arguments.Count != 1)
            return false;

        var sourceType = GetPreferredLengthSemanticType(elementAccess.Expression, context);
        if (!IsSupportedBuiltInRangeLengthSourceType(sourceType) ||
            !TryCreateRangeLengthTerm(
                elementAccess.ArgumentList.Arguments[0].Expression,
                elementAccess.Expression,
                context,
                out term))
            return false;

        return true;
    }

    private static bool IsSupportedBuiltInRangeLengthSourceType(ITypeSymbol? type)
    {
        return type?.SpecialType == SpecialType.System_String ||
               type is IArrayTypeSymbol { Rank: 1 } ||
               IsBuiltInSpanOrMemoryType(type);
    }

    private static bool TryCreateDirectRangeLengthTerm(
        ExpressionSyntax rangeExpression,
        ExpressionSyntax sourceExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (!TryCreateDirectRangeExpressionShape(rangeExpression, context, out var rangeShape)) return false;

        return TryCreateRangeLengthTerm(rangeShape, sourceExpression, context, out term);
    }

    private static bool TryCreateRangeLengthTerm(
        ExpressionSyntax rangeExpression,
        ExpressionSyntax sourceExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (TryCreateDirectRangeLengthTerm(rangeExpression, sourceExpression, context, out term)) return true;

        if (!TryResolveBuiltInRangeLengthShape(rangeExpression, context, out var rangeShape)) return false;

        return TryCreateRangeLengthTerm(rangeShape, sourceExpression, context, out term);
    }

    private static bool TryCreateRangeLengthTerm(
        RangeLengthShape rangeShape,
        ExpressionSyntax sourceExpression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (!TryLowerBuiltInLengthTerm(sourceExpression, context, out var sourceLength) ||
            !TryLowerRangeEndpointTerm(
                rangeShape,
                true,
                sourceLength,
                new SymbolicIntegerConstantTerm(0),
                context,
                out var start) ||
            !TryLowerRangeEndpointTerm(
                rangeShape,
                false,
                sourceLength,
                sourceLength,
                context,
                out var end))
            return false;

        term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, end, start);
        return true;
    }

    private static bool TryCreateDirectRangeExpressionShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RangeLengthShape rangeShape)
    {
        expression = UnwrapExpression(expression);
        if (expression is RangeExpressionSyntax rangeExpression)
        {
            if (!TryCreateRangeEndpointShape(
                    rangeExpression.LeftOperand,
                    context,
                    out var hasStart,
                    out var start) ||
                !TryCreateRangeEndpointShape(
                    rangeExpression.RightOperand,
                    context,
                    out var hasEnd,
                    out var end))
            {
                rangeShape = default;
                return false;
            }

            rangeShape = new RangeLengthShape(hasStart, start, hasEnd, end);
            return true;
        }

        if (TryCreateRangeInvocationShape(expression, context, out rangeShape) ||
            TryCreateRangeObjectCreationShape(expression, context, out rangeShape) ||
            TryCreateRangeAllPropertyShape(expression, context, out rangeShape))
            return true;

        rangeShape = default;
        return false;
    }

    private static bool TryCreateRangeEndpointShape(
        ExpressionSyntax? expression,
        SymbolicLoweringContext context,
        out bool hasEndpoint,
        out IndexLengthShape endpoint)
    {
        if (expression == null)
        {
            hasEndpoint = false;
            endpoint = default;
            return true;
        }

        if (!TryResolveBuiltInIndexLengthShape(expression, context, out endpoint))
        {
            hasEndpoint = false;
            return false;
        }

        hasEndpoint = true;
        return true;
    }

    private static bool TryResolveBuiltInRangeLengthShape(
        ExpressionSyntax argumentExpression,
        SymbolicLoweringContext context,
        out RangeLengthShape rangeShape)
    {
        argumentExpression = UnwrapExpression(argumentExpression);
        if (TryCreateDirectRangeExpressionShape(argumentExpression, context, out rangeShape)) return true;

        if (!IsSystemRangeExpression(argumentExpression, context) ||
            !TryGetLocalOrParameterRangeSymbol(argumentExpression, context, out var rangeSymbol))
        {
            rangeShape = default;
            return false;
        }

        return TryResolveAssignedRangeLengthShape(argumentExpression, rangeSymbol, context, out rangeShape);
    }

    private static bool TryGetLocalOrParameterRangeSymbol(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ISymbol rangeSymbol)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol is ILocalSymbol localSymbol &&
            IsSystemRangeType(localSymbol.Type, context.SemanticModel.Compilation))
        {
            rangeSymbol = localSymbol;
            return true;
        }

        if (symbol is IParameterSymbol { RefKind: RefKind.None } parameterSymbol &&
            IsSystemRangeType(parameterSymbol.Type, context.SemanticModel.Compilation))
        {
            rangeSymbol = parameterSymbol;
            return true;
        }

        rangeSymbol = null!;
        return false;
    }

    private static bool TryResolveAssignedRangeLengthShape(
        ExpressionSyntax useExpression,
        ISymbol rangeSymbol,
        SymbolicLoweringContext context,
        out RangeLengthShape rangeShape)
    {
        rangeShape = default;
        var foundAssignment = false;
        foreach (var containingBlock in EnumerateContainingBlocks(useExpression).Reverse())
            foreach (var statement in containingBlock.Block.Statements)
            {
                if (statement == containingBlock.ContainingStatement) break;

                TryGetRangeAssignmentFromPrecedingStatement(
                    statement,
                    rangeSymbol,
                    context,
                    out var writesRangeSymbol,
                    out var assignedRangeShape);
                if (!writesRangeSymbol) continue;

                if (!assignedRangeShape.HasValue)
                {
                    rangeShape = default;
                    return false;
                }

                rangeShape = assignedRangeShape.GetValueOrDefault();
                foundAssignment = true;
            }

        return foundAssignment;
    }

    private static void TryGetRangeAssignmentFromPrecedingStatement(
        StatementSyntax statement,
        ISymbol rangeSymbol,
        SymbolicLoweringContext context,
        out bool writesRangeSymbol,
        out RangeLengthShape? rangeShape)
    {
        rangeShape = null;
        writesRangeSymbol = false;

        if (TryGetRangeAssignmentFromLocalDeclaration(
                statement,
                rangeSymbol,
                context,
                out writesRangeSymbol,
                out rangeShape))
            return;

        if (TryGetRangeAssignmentFromExpressionStatement(
                statement,
                rangeSymbol,
                context,
                out writesRangeSymbol,
                out rangeShape))
            return;

        writesRangeSymbol = ContainsSymbolWrite(statement, rangeSymbol, context);
    }

    private static bool TryGetRangeAssignmentFromLocalDeclaration(
        StatementSyntax statement,
        ISymbol rangeSymbol,
        SymbolicLoweringContext context,
        out bool writesRangeSymbol,
        out RangeLengthShape? rangeShape)
    {
        rangeShape = null;
        writesRangeSymbol = false;
        if (statement is not LocalDeclarationStatementSyntax localDeclaration) return false;

        foreach (var variable in localDeclaration.Declaration.Variables)
        {
            var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken);
            if (!IsSameSymbol(declaredSymbol, rangeSymbol)) continue;

            if (variable.Initializer == null) return true;

            writesRangeSymbol = true;
            if (localDeclaration.Declaration.Variables.Count != 1 ||
                !TryCreateDirectRangeExpressionShape(variable.Initializer.Value, context, out var assignedRangeShape))
                return true;

            rangeShape = assignedRangeShape;
            return true;
        }

        return false;
    }

    private static bool TryGetRangeAssignmentFromExpressionStatement(
        StatementSyntax statement,
        ISymbol rangeSymbol,
        SymbolicLoweringContext context,
        out bool writesRangeSymbol,
        out RangeLengthShape? rangeShape)
    {
        rangeShape = null;
        writesRangeSymbol = false;
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            !IsSymbolReference(assignment.Left, rangeSymbol, context))
            return false;

        writesRangeSymbol = true;
        if (TryCreateDirectRangeExpressionShape(assignment.Right, context, out var assignedRangeShape))
            rangeShape = assignedRangeShape;

        return true;
    }

    private static bool TryLowerRangeEndpointTerm(
        RangeLengthShape rangeShape,
        bool useStart,
        SymbolicTerm sourceLength,
        SymbolicTerm defaultWhenOmitted,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        if (useStart ? !rangeShape.HasStart : !rangeShape.HasEnd)
        {
            term = defaultWhenOmitted;
            return true;
        }

        return TryLowerIndexShapeTerm(
            useStart ? rangeShape.Start : rangeShape.End,
            sourceLength,
            context,
            out term);
    }

    private static bool TryLowerIndexShapeTerm(
        IndexLengthShape indexShape,
        SymbolicTerm sourceLength,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        if (!TryLowerTerm(indexShape.ValueExpression, context, out var valueTerm) ||
            valueTerm.Kind != SmtValueKind.Int)
        {
            term = null!;
            return false;
        }

        if (!indexShape.FromEnd)
        {
            term = valueTerm;
            return true;
        }

        term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, valueTerm);
        return true;
    }

    private static bool TryCreateEffectiveBuiltInIndexTerm(
        IndexLengthShape indexShape,
        SymbolicTerm sourceLength,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        return TryLowerIndexShapeTerm(indexShape, sourceLength, context, out term);
    }

    private static bool TryCreateIndexShapeWellFormedCondition(
        IndexLengthShape indexShape,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition? condition)
    {
        condition = null;
        if (!indexShape.RequiresNonNegativeValue) return true;

        if (!TryLowerTerm(indexShape.ValueExpression, context, out var rawIndex) ||
            rawIndex.Kind != SmtValueKind.Int)
            return false;

        condition = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThanOrEqual,
                rawIndex,
                new SymbolicIntegerConstantTerm(0)),
            source,
            provenance));
        return true;
    }

    private static bool TryCreateBuiltInRangeAccessInRangeCondition(
        RangeLengthShape rangeShape,
        SymbolicTerm sourceLength,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (!TryCreateEffectiveRangeEndpointTerm(
                rangeShape,
                true,
                sourceLength,
                new SymbolicIntegerConstantTerm(0),
                context,
                out var start) ||
            !TryCreateEffectiveRangeEndpointTerm(
                rangeShape,
                false,
                sourceLength,
                sourceLength,
                context,
                out var end))
            return false;

        var nonNegativeStart = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.GreaterThanOrEqual,
                start,
                new SymbolicIntegerConstantTerm(0)),
            source,
            provenance + ".start-nonnegative"));
        var orderedEndpoints = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                start,
                end),
            source,
            provenance + ".ordered-endpoints"));
        var endWithinLength = new SymbolicFactCondition(SymbolicFact.Exact(
            new SymbolicRelationAtom(
                SymbolicRelationOperator.LessThanOrEqual,
                end,
                sourceLength),
            source,
            provenance + ".end-within-length"));
        var inRange = new SymbolicBinaryCondition(
            SymbolicConditionOperator.And,
            nonNegativeStart,
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                orderedEndpoints,
                endWithinLength));
        if (!TryCreateRangeShapeWellFormedCondition(
                rangeShape,
                source,
                provenance + ".well-formed",
                context,
                out var wellFormed))
            return false;

        condition = ApplyWellFormedPrecondition(wellFormed, inRange);
        return true;
    }

    private static bool TryCreateEffectiveRangeEndpointTerm(
        RangeLengthShape rangeShape,
        bool useStart,
        SymbolicTerm sourceLength,
        SymbolicTerm defaultWhenOmitted,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        var hasEndpoint = useStart ? rangeShape.HasStart : rangeShape.HasEnd;
        if (!hasEndpoint)
        {
            term = defaultWhenOmitted;
            return true;
        }

        return TryCreateEffectiveBuiltInIndexTerm(
            useStart ? rangeShape.Start : rangeShape.End,
            sourceLength,
            context,
            out term);
    }

    private static bool TryCreateRangeShapeWellFormedCondition(
        RangeLengthShape rangeShape,
        SyntaxNode source,
        string provenance,
        SymbolicLoweringContext context,
        out SymbolicCondition? condition)
    {
        condition = null;
        SymbolicCondition? startWellFormedCondition = null;
        SymbolicCondition? endWellFormedCondition = null;
        if (rangeShape.HasStart &&
            !TryCreateIndexShapeWellFormedCondition(
                rangeShape.Start,
                source,
                provenance + ".start",
                context,
                out startWellFormedCondition))
            return false;

        if (rangeShape.HasEnd &&
            !TryCreateIndexShapeWellFormedCondition(
                rangeShape.End,
                source,
                provenance + ".end",
                context,
                out endWellFormedCondition))
            return false;

        if (startWellFormedCondition != null) condition = startWellFormedCondition;

        if (endWellFormedCondition != null)
            condition = condition == null
                ? endWellFormedCondition
                : new SymbolicBinaryCondition(
                    SymbolicConditionOperator.And,
                    condition,
                    endWellFormedCondition);

        return true;
    }

    private static SymbolicCondition ApplyWellFormedPrecondition(
        SymbolicCondition? wellFormed,
        SymbolicCondition inRange)
    {
        return wellFormed == null
            ? inRange
            : new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                new SymbolicNotCondition(wellFormed),
                inRange);
    }

    private static bool TryResolveBuiltInIndexLengthShape(
        ExpressionSyntax argumentExpression,
        SymbolicLoweringContext context,
        out IndexLengthShape indexShape)
    {
        argumentExpression = UnwrapExpression(argumentExpression);
        if (TryCreateDirectIndexExpressionShape(argumentExpression, context, out indexShape)) return true;

        if (!IsSystemIndexExpression(argumentExpression, context) ||
            !TryGetLocalOrParameterIndexSymbol(argumentExpression, context, out var indexSymbol))
        {
            indexShape = default;
            return false;
        }

        return TryResolveAssignedIndexLengthShape(argumentExpression, indexSymbol, context, out indexShape);
    }

    private static bool TryGetLocalOrParameterIndexSymbol(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ISymbol indexSymbol)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol;
        if (symbol is ILocalSymbol localSymbol &&
            IsSystemIndexType(localSymbol.Type, context.SemanticModel.Compilation))
        {
            indexSymbol = localSymbol;
            return true;
        }

        if (symbol is IParameterSymbol { RefKind: RefKind.None } parameterSymbol &&
            IsSystemIndexType(parameterSymbol.Type, context.SemanticModel.Compilation))
        {
            indexSymbol = parameterSymbol;
            return true;
        }

        indexSymbol = null!;
        return false;
    }

    private static bool TryResolveAssignedIndexLengthShape(
        ExpressionSyntax useExpression,
        ISymbol indexSymbol,
        SymbolicLoweringContext context,
        out IndexLengthShape indexShape)
    {
        indexShape = default;
        var foundAssignment = false;
        foreach (var containingBlock in EnumerateContainingBlocks(useExpression).Reverse())
            foreach (var statement in containingBlock.Block.Statements)
            {
                if (statement == containingBlock.ContainingStatement) break;

                TryGetIndexAssignmentFromPrecedingStatement(
                    statement,
                    indexSymbol,
                    context,
                    out var writesIndexSymbol,
                    out var assignedIndexShape);
                if (!writesIndexSymbol) continue;

                if (!assignedIndexShape.HasValue)
                {
                    indexShape = default;
                    return false;
                }

                indexShape = assignedIndexShape.GetValueOrDefault();
                foundAssignment = true;
            }

        return foundAssignment;
    }

    private static void TryGetIndexAssignmentFromPrecedingStatement(
        StatementSyntax statement,
        ISymbol indexSymbol,
        SymbolicLoweringContext context,
        out bool writesIndexSymbol,
        out IndexLengthShape? indexShape)
    {
        indexShape = null;
        writesIndexSymbol = false;

        if (TryGetIndexAssignmentFromLocalDeclaration(
                statement,
                indexSymbol,
                context,
                out writesIndexSymbol,
                out indexShape))
            return;

        if (TryGetIndexAssignmentFromExpressionStatement(
                statement,
                indexSymbol,
                context,
                out writesIndexSymbol,
                out indexShape))
            return;

        writesIndexSymbol = ContainsSymbolWrite(statement, indexSymbol, context);
    }

    private static bool TryGetIndexAssignmentFromLocalDeclaration(
        StatementSyntax statement,
        ISymbol indexSymbol,
        SymbolicLoweringContext context,
        out bool writesIndexSymbol,
        out IndexLengthShape? indexShape)
    {
        indexShape = null;
        writesIndexSymbol = false;
        if (statement is not LocalDeclarationStatementSyntax localDeclaration) return false;

        foreach (var variable in localDeclaration.Declaration.Variables)
        {
            var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken);
            if (!IsSameSymbol(declaredSymbol, indexSymbol)) continue;

            if (variable.Initializer == null) return true;

            writesIndexSymbol = true;
            if (localDeclaration.Declaration.Variables.Count != 1 ||
                !TryCreateDirectIndexExpressionShape(variable.Initializer.Value, context, out var assignedIndexShape))
                return true;

            indexShape = assignedIndexShape;
            return true;
        }

        return false;
    }

    private static bool TryGetIndexAssignmentFromExpressionStatement(
        StatementSyntax statement,
        ISymbol indexSymbol,
        SymbolicLoweringContext context,
        out bool writesIndexSymbol,
        out IndexLengthShape? indexShape)
    {
        indexShape = null;
        writesIndexSymbol = false;
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            !IsSymbolReference(assignment.Left, indexSymbol, context))
            return false;

        writesIndexSymbol = true;
        if (TryCreateDirectIndexExpressionShape(assignment.Right, context, out var assignedIndexShape))
            indexShape = assignedIndexShape;

        return true;
    }

    private static bool TryCreateDirectIndexExpressionShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out IndexLengthShape indexShape)
    {
        expression = UnwrapExpression(expression);
        if (expression is PrefixUnaryExpressionSyntax fromEndIndex &&
            (fromEndIndex.IsKind(SyntaxKind.IndexExpression) ||
             fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken)))
        {
            indexShape = new IndexLengthShape(
                fromEndIndex.Operand,
                true,
                true);
            return true;
        }

        if (TryCreateIndexInvocationShape(expression, context, out indexShape) ||
            TryCreateIndexObjectCreationShape(expression, context, out indexShape))
            return true;

        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        if (IsIntegralOrEnumType(typeInfo.Type))
        {
            indexShape = new IndexLengthShape(expression, false);
            return true;
        }

        indexShape = default;
        return false;
    }

    private static bool TryCreateRangeInvocationShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RangeLengthShape rangeShape)
    {
        rangeShape = default;
        if (expression is not InvocationExpressionSyntax invocationExpression ||
            context.SemanticModel.GetOperation(invocationExpression, context.CancellationToken) is not
                IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod.MethodKind != MethodKind.Ordinary ||
            invocationOperation.TargetMethod.ReturnType is not { } returnType ||
            !IsSystemRangeType(returnType, context.SemanticModel.Compilation) ||
            invocationOperation.TargetMethod.ContainingType is not { } containingType ||
            !IsSystemRangeType(containingType, context.SemanticModel.Compilation))
            return false;

        if (invocationOperation.TargetMethod.Name == "StartAt")
        {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0,
                    out var startExpression) ||
                !TryResolveBuiltInIndexLengthShape(startExpression, context, out var start))
                return false;

            rangeShape = new RangeLengthShape(true, start, false, default);
            return true;
        }

        if (invocationOperation.TargetMethod.Name == "EndAt")
        {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var endExpression) ||
                !TryResolveBuiltInIndexLengthShape(endExpression, context, out var end))
                return false;

            rangeShape = new RangeLengthShape(false, default, true, end);
            return true;
        }

        return false;
    }

    private static bool TryCreateRangeObjectCreationShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RangeLengthShape rangeShape)
    {
        rangeShape = default;
        if (expression is not ObjectCreationExpressionSyntax objectCreation ||
            context.SemanticModel.GetOperation(objectCreation, context.CancellationToken) is not
                IObjectCreationOperation objectCreationOperation ||
            objectCreationOperation.Constructor == null ||
            !IsSystemRangeType(objectCreationOperation.Constructor.ContainingType, context.SemanticModel.Compilation) ||
            !TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var startExpression) ||
            !TryGetObjectCreationArgumentExpression(objectCreationOperation, 1, out var endExpression) ||
            !TryResolveBuiltInIndexLengthShape(startExpression, context, out var start) ||
            !TryResolveBuiltInIndexLengthShape(endExpression, context, out var end))
            return false;

        rangeShape = new RangeLengthShape(true, start, true, end);
        return true;
    }

    private static bool TryCreateRangeAllPropertyShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out RangeLengthShape rangeShape)
    {
        rangeShape = default;
        if (context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol is not IPropertySymbol
            {
                Name: "All",
                IsStatic: true
            } propertySymbol ||
            !IsSystemRangeType(propertySymbol.ContainingType, context.SemanticModel.Compilation) ||
            !IsSystemRangeType(propertySymbol.Type, context.SemanticModel.Compilation))
            return false;

        rangeShape = new RangeLengthShape(false, default, false, default);
        return true;
    }

    private static bool TryCreateIndexInvocationShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out IndexLengthShape indexShape)
    {
        indexShape = default;
        if (expression is not InvocationExpressionSyntax invocationExpression ||
            context.SemanticModel.GetOperation(invocationExpression, context.CancellationToken) is not
                IInvocationOperation invocationOperation ||
            invocationOperation.TargetMethod.MethodKind != MethodKind.Ordinary ||
            invocationOperation.TargetMethod.ReturnType is not { } returnType ||
            !IsSystemIndexType(returnType, context.SemanticModel.Compilation) ||
            invocationOperation.TargetMethod.ContainingType is not { } containingType ||
            !IsSystemIndexType(containingType, context.SemanticModel.Compilation) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var valueExpression))
            return false;

        if (invocationOperation.TargetMethod.Name == "FromStart")
        {
            indexShape = new IndexLengthShape(
                valueExpression,
                false,
                true);
            return true;
        }

        if (invocationOperation.TargetMethod.Name == "FromEnd")
        {
            indexShape = new IndexLengthShape(
                valueExpression,
                true,
                true);
            return true;
        }

        return false;
    }

    private static bool TryCreateIndexObjectCreationShape(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out IndexLengthShape indexShape)
    {
        indexShape = default;
        if (expression is not ObjectCreationExpressionSyntax objectCreation ||
            context.SemanticModel.GetOperation(objectCreation, context.CancellationToken) is not
                IObjectCreationOperation objectCreationOperation ||
            objectCreationOperation.Constructor == null ||
            !IsSystemIndexType(objectCreationOperation.Constructor.ContainingType, context.SemanticModel.Compilation) ||
            !TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var valueExpression))
            return false;

        if (!TryGetObjectCreationArgumentExpression(objectCreationOperation, 1, out var fromEndExpression))
        {
            indexShape = new IndexLengthShape(
                valueExpression,
                false,
                true);
            return true;
        }

        if (!TryGetConstantBool(fromEndExpression, context, out var fromEnd)) return false;

        indexShape = new IndexLengthShape(
            valueExpression,
            fromEnd,
            true);
        return true;
    }

    private static bool IsSupportedBuiltInElementAccessReceiver(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is IArrayTypeSymbol { Rank: 1 } ||
               typeSymbol?.SpecialType == SpecialType.System_String ||
               SymbolicTypeFacts.IsBuiltInSpanType(typeSymbol) ||
               HasCountBackedIntIndexer(typeSymbol);
    }

    private static bool TryGetObjectCreationArgumentExpression(
        IObjectCreationOperation objectCreationOperation,
        int parameterIndex,
        out ExpressionSyntax argumentExpression)
    {
        argumentExpression = null!;
        if (objectCreationOperation.Constructor == null ||
            parameterIndex < 0 ||
            parameterIndex >= objectCreationOperation.Constructor.Parameters.Length)
            return false;

        var parameter = objectCreationOperation.Constructor.Parameters[parameterIndex];
        foreach (var argument in objectCreationOperation.Arguments)
            if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                argument.Value.Syntax is ExpressionSyntax expression)
            {
                argumentExpression = expression;
                return true;
            }

        if (parameterIndex < objectCreationOperation.Arguments.Length &&
            objectCreationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
        {
            argumentExpression = fallbackExpression;
            return true;
        }

        return false;
    }

    private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(
        SyntaxNode site)
    {
        for (var current = site; current != null; current = current.Parent)
            if (current is StatementSyntax statement &&
                statement.Parent is BlockSyntax block)
                yield return (block, statement);
    }

    private static bool ContainsSymbolWrite(
        SyntaxNode node,
        ISymbol symbol,
        SymbolicLoweringContext context)
    {
        foreach (var assignment in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<AssignmentExpressionSyntax>())
            if (IsSymbolReference(assignment.Left, symbol, context))
                return true;

        foreach (var argument in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<ArgumentSyntax>())
            if ((argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                 argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                IsSymbolReference(argument.Expression, symbol, context))
                return true;

        return false;
    }

    private static bool IsSymbolReference(
        ExpressionSyntax expression,
        ISymbol target,
        SymbolicLoweringContext context)
    {
        return IsSameSymbol(
            context.SemanticModel.GetSymbolInfo(UnwrapExpression(expression), context.CancellationToken).Symbol,
            target);
    }

    private static bool IsSameSymbol(ISymbol? candidate, ISymbol target)
    {
        return candidate != null &&
               (SymbolEqualityComparer.Default.Equals(candidate, target) ||
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target.OriginalDefinition));
    }

    private static bool IsSystemRangeExpression(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type, context.SemanticModel.Compilation);
    }

    private static bool IsSystemRangeType(ITypeSymbol? typeSymbol, Compilation compilation)
    {
        var rangeType = compilation.GetTypeByMetadataName("System.Range");
        return typeSymbol != null &&
               rangeType != null &&
               SymbolEqualityComparer.Default.Equals(typeSymbol, rangeType);
    }

    private static bool IsSystemIndexExpression(ExpressionSyntax expression, SymbolicLoweringContext context)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return IsSystemIndexType(typeInfo.ConvertedType ?? typeInfo.Type, context.SemanticModel.Compilation);
    }

    private static bool IsSystemIndexType(ITypeSymbol? typeSymbol, Compilation compilation)
    {
        var indexType = compilation.GetTypeByMetadataName("System.Index");
        return typeSymbol != null &&
               indexType != null &&
               SymbolEqualityComparer.Default.Equals(typeSymbol, indexType);
    }

    private static bool IsIntegralOrEnumType(ITypeSymbol? type)
    {
        return type?.TypeKind == TypeKind.Enum ||
               type?.SpecialType is SpecialType.System_Char or
                   SpecialType.System_SByte or
                   SpecialType.System_Byte or
                   SpecialType.System_Int16 or
                   SpecialType.System_UInt16 or
                   SpecialType.System_Int32 or
                   SpecialType.System_UInt32 or
                   SpecialType.System_Int64 or
                   SpecialType.System_UInt64;
    }

    private static bool TryGetConstantBool(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out bool value)
    {
        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue is { HasValue: true, Value: bool boolValue })
        {
            value = boolValue;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryLowerStringCreationResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (expression is not ObjectCreationExpressionSyntax objectCreationExpression ||
            context.SemanticModel.GetOperation(objectCreationExpression, context.CancellationToken) is not
                IObjectCreationOperation objectCreationOperation ||
            objectCreationOperation.Constructor is not { } constructor ||
            constructor.ContainingType.SpecialType != SpecialType.System_String)
            return false;

        if (constructor.Parameters.Length == 2 &&
            constructor.Parameters[0].Type.SpecialType == SpecialType.System_Char &&
            constructor.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 1, out var countExpression) &&
            TryLowerTerm(countExpression, context, out term) &&
            term.Kind == SmtValueKind.Int)
            return true;

        if (constructor.Parameters.Length == 1 &&
            SymbolicTypeFacts.IsCharArrayType(constructor.Parameters[0].Type) &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var charArrayExpression))
            return TryLowerBuiltInLengthTerm(charArrayExpression, context, out term);

        if (constructor.Parameters.Length == 3 &&
            SymbolicTypeFacts.IsCharArrayType(constructor.Parameters[0].Type) &&
            constructor.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
            constructor.Parameters[2].Type.SpecialType == SpecialType.System_Int32 &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 2, out var lengthExpression) &&
            TryLowerTerm(lengthExpression, context, out term) &&
            term.Kind == SmtValueKind.Int)
            return true;

        if (constructor.Parameters.Length == 1 &&
            SymbolicTypeFacts.IsReadOnlySpanOfCharType(constructor.Parameters[0].Type) &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var spanExpression))
            return TryLowerBuiltInLengthTerm(spanExpression, context, out term);

        term = null!;
        return false;
    }

    private static bool TryLowerStringInvocationResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (expression is not InvocationExpressionSyntax invocationExpression ||
            context.SemanticModel.GetOperation(invocationExpression, context.CancellationToken) is not
                IInvocationOperation invocationOperation)
            return false;

        var method = invocationOperation.TargetMethod;
        if (method.IsStatic ||
            method.ContainingType?.SpecialType != SpecialType.System_String ||
            method.ReturnType.SpecialType != SpecialType.System_String ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression)
            return false;

        if (string.Equals(method.Name, nameof(string.Substring), StringComparison.Ordinal) &&
            method.Parameters.Length == 1)
        {
            if (invocationOperation.Arguments.Length != 1 ||
                !TryLowerBuiltInLengthTerm(sourceExpression, context, out var sourceLength) ||
                !TryLowerTerm(
                    invocationOperation.Arguments[0].Syntax as ExpressionSyntax ??
                    invocationOperation.Arguments[0].Value.Syntax as ExpressionSyntax ??
                    invocationExpression.ArgumentList.Arguments[0].Expression, context, out var start) ||
                start.Kind != SmtValueKind.Int)
                return false;

            term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, start);
            return true;
        }

        if (string.Equals(method.Name, nameof(string.Substring), StringComparison.Ordinal) &&
            method.Parameters.Length == 2)
        {
            if (invocationOperation.Arguments.Length != 2 ||
                !TryLowerBuiltInLengthTerm(sourceExpression, context, out _) ||
                !TryLowerTerm(
                    invocationOperation.Arguments[0].Syntax as ExpressionSyntax ??
                    invocationExpression.ArgumentList.Arguments[0].Expression, context, out var startValue) ||
                startValue.Kind != SmtValueKind.Int ||
                !TryLowerTerm(
                    invocationOperation.Arguments[1].Syntax as ExpressionSyntax ??
                    invocationExpression.ArgumentList.Arguments[1].Expression, context, out var countValue) ||
                countValue.Kind != SmtValueKind.Int)
                return false;

            term = countValue;
            return true;
        }

        if (string.Equals(method.Name, nameof(string.Remove), StringComparison.Ordinal))
        {
            if (!TryLowerBuiltInLengthTerm(sourceExpression, context, out var sourceLength)) return false;

            if (method.Parameters.Length == 1 &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0,
                    out var startExpression) &&
                TryLowerTerm(startExpression, context, out var start) &&
                start.Kind == SmtValueKind.Int)
            {
                term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, start);
                return true;
            }

            if (method.Parameters.Length == 2 &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var _) &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 1,
                    out var countExpression) &&
                TryLowerTerm(countExpression, context, out var count) &&
                count.Kind == SmtValueKind.Int)
            {
                term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, count);
                return true;
            }

            return false;
        }

        if (string.Equals(method.Name, nameof(string.Insert), StringComparison.Ordinal) &&
            method.Parameters.Length == 2 &&
            method.Parameters[1].Type.SpecialType == SpecialType.System_String &&
            TryLowerBuiltInLengthTerm(sourceExpression, context, out var insertSourceLength) &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var indexExpression) &&
            TryLowerTerm(indexExpression, context, out var index) &&
            index.Kind == SmtValueKind.Int &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 1, out var valueExpression) &&
            TryLowerBuiltInLengthTerm(valueExpression, context, out var valueLength))
        {
            term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, insertSourceLength, valueLength);
            return true;
        }

        if (method.Name is nameof(string.PadLeft) or nameof(string.PadRight) &&
            (method.Parameters.Length == 1 ||
             method.Parameters.Length == 2 && method.Parameters[1].Type.SpecialType == SpecialType.System_Char) &&
            TryLowerBuiltInLengthTerm(sourceExpression, context, out var padSourceLength) &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var widthExpression) &&
            TryLowerTerm(widthExpression, context, out var width) &&
            width.Kind == SmtValueKind.Int)
        {
            term = new SymbolicConditionalTerm(
                CreateRelationCondition(
                    SymbolicRelationOperator.GreaterThan,
                    width,
                    padSourceLength,
                    invocationExpression,
                    "ir.known-api.string.pad-width"),
                width,
                padSourceLength);
            return true;
        }

        return false;
    }

    private static bool IsMemoryExtensionsViewMethod(IMethodSymbol method)
    {
        var definition = method.ReducedFrom ?? method;
        return definition.Name is "AsSpan" or "AsMemory" &&
               definition.IsExtensionMethod &&
               definition.ContainingType?.ToDisplayString() == "System.MemoryExtensions";
    }

    private static bool TryGetMemoryExtensionsViewSourceExpression(
        InvocationExpressionSyntax invocationExpression,
        SymbolicLoweringContext context,
        out ExpressionSyntax sourceExpression,
        out int firstArgumentIndex)
    {
        if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
            context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type != null)
        {
            sourceExpression = memberAccess.Expression;
            firstArgumentIndex = 0;
            return true;
        }

        if (invocationExpression.ArgumentList.Arguments.Count == 0)
        {
            sourceExpression = null!;
            firstArgumentIndex = 0;
            return false;
        }

        sourceExpression = invocationExpression.ArgumentList.Arguments[0].Expression;
        firstArgumentIndex = 1;
        return true;
    }

    private static bool IsSupportedMemoryExtensionsViewSource(
        ExpressionSyntax sourceExpression,
        SymbolicLoweringContext context)
    {
        var sourceTypeInfo = context.SemanticModel.GetTypeInfo(sourceExpression, context.CancellationToken);
        var sourceType = PreferLengthSemanticType(sourceTypeInfo.Type, sourceTypeInfo.ConvertedType);
        return sourceType?.SpecialType == SpecialType.System_String ||
               sourceType is IArrayTypeSymbol { Rank: 1 };
    }

    private static ITypeSymbol? GetPreferredLengthSemanticType(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
    {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return PreferLengthSemanticType(typeInfo.Type, typeInfo.ConvertedType);
    }

    private static ITypeSymbol? PreferLengthSemanticType(
        ITypeSymbol? sourceType,
        ITypeSymbol? convertedType)
    {
        if (sourceType != null &&
            HasLengthLikeShape(sourceType) &&
            !HasLengthLikeShape(convertedType))
            return sourceType;

        return convertedType ?? sourceType;
    }

    private static bool HasLengthLikeShape(ITypeSymbol? type)
    {
        return type?.SpecialType == SpecialType.System_String ||
               type is IArrayTypeSymbol { Rank: >= 1 } ||
               IsBuiltInSpanOrMemoryType(type) ||
               HasCountBackedIntIndexer(type) ||
               SymbolicTypeFacts.HasInstanceInt32Member(type, "Count");
    }

    private static bool TryLowerInvocationArgument(
        IInvocationOperation invocationOperation,
        int parameterIndex,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (parameterIndex < 0 ||
            parameterIndex >= invocationOperation.TargetMethod.Parameters.Length)
            return false;

        var parameter = invocationOperation.TargetMethod.Parameters[parameterIndex];
        foreach (var argument in invocationOperation.Arguments)
            if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                argument.Value.Syntax is ExpressionSyntax argumentExpression)
                return TryLowerTerm(argumentExpression, context, out term);

        if (parameterIndex < invocationOperation.Arguments.Length &&
            invocationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
            return TryLowerTerm(fallbackExpression, context, out term);

        return false;
    }

    private readonly struct IndexLengthShape
    {
        public IndexLengthShape(
            ExpressionSyntax valueExpression,
            bool fromEnd,
            bool requiresNonNegativeValue = false)
        {
            ValueExpression = valueExpression;
            FromEnd = fromEnd;
            RequiresNonNegativeValue = requiresNonNegativeValue;
        }

        public ExpressionSyntax ValueExpression { get; }

        public bool FromEnd { get; }

        public bool RequiresNonNegativeValue { get; }
    }

    private readonly struct RangeLengthShape
    {
        public RangeLengthShape(
            bool hasStart,
            IndexLengthShape start,
            bool hasEnd,
            IndexLengthShape end)
        {
            HasStart = hasStart;
            Start = start;
            HasEnd = hasEnd;
            End = end;
        }

        public bool HasStart { get; }

        public IndexLengthShape Start { get; }

        public bool HasEnd { get; }

        public IndexLengthShape End { get; }
    }
}
