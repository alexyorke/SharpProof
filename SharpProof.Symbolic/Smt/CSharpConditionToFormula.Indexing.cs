using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SearchLib.Smt;

namespace SharpProof.Symbolic.Smt;

internal static partial class CSharpConditionToFormula
{
    internal static SmtFormula CreateSubsequenceInRangeFormula(
        SmtFormula sourceLength,
        SmtFormula start,
        SmtFormula? count,
        bool oneArgumentUpperBoundIsInclusive)
    {
        return SmtFormulaFactory.CreateSubsequenceInRangeFormula(
            sourceLength,
            start,
            count,
            oneArgumentUpperBoundIsInclusive);
    }

    private static bool TryTranslateBuiltInElementAccessValue(
        ElementAccessExpressionSyntax elementAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula? formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null;
        var receiverType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                           semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
        if (!TryGetBuiltInElementAccessElementType(receiverType, semanticModel.Compilation, out var elementType) ||
            !TryGetValueKind(elementType, out var elementKind) ||
            !TryCreateBuiltInElementAccessReceiverFormula(
                elementAccess.Expression,
                semanticModel,
                cancellationToken,
                out var receiverFormula,
                getSymbolVersion,
                inlineDepth) ||
            receiverFormula is not { Kind: SmtValueKind.Reference } ||
            !TryCreateElementAccessIndexVectorText(
                elementAccess,
                receiverType,
                semanticModel,
                cancellationToken,
                out var indexText,
                getSymbolVersion,
                inlineDepth))
            return false;

        formula = new SmtVariable(GetFormulaVariableName(receiverFormula) + "[" + indexText + "]", elementKind);
        return true;
    }

    private static bool TryCreateElementAccessIndexVectorText(
        ElementAccessExpressionSyntax elementAccess,
        ITypeSymbol? receiverType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string indexText,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (receiverType is IArrayTypeSymbol { Rank: > 1 } arrayType)
        {
            if (elementAccess.ArgumentList.Arguments.Count != arrayType.Rank)
            {
                indexText = string.Empty;
                return false;
            }

            var indexTexts = new List<string>(arrayType.Rank);
            foreach (var argument in elementAccess.ArgumentList.Arguments)
            {
                if (!TryCreateOrdinaryElementAccessIndexText(
                        argument.Expression,
                        semanticModel,
                        cancellationToken,
                        out var dimensionIndexText,
                        getSymbolVersion,
                        inlineDepth))
                {
                    indexText = string.Empty;
                    return false;
                }

                indexTexts.Add(dimensionIndexText);
            }

            indexText = string.Join(",", indexTexts);
            return indexTexts.Count != 0;
        }

        if (elementAccess.ArgumentList.Arguments.Count != 1)
        {
            indexText = string.Empty;
            return false;
        }

        return TryCreateElementAccessIndexText(
            elementAccess.ArgumentList.Arguments[0].Expression,
            semanticModel,
            cancellationToken,
            out indexText,
            getSymbolVersion,
            inlineDepth);
    }

    private static bool TryCreateOrdinaryElementAccessIndexText(
        ExpressionSyntax indexExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string indexText,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        indexExpression = UnwrapElementAccessIndexExpression(indexExpression);
        if (!TryTranslateValue(
                indexExpression,
                semanticModel,
                cancellationToken,
                out var indexFormula,
                getSymbolVersion,
                inlineDepth) ||
            indexFormula is not { Kind: SmtValueKind.Int })
        {
            indexText = string.Empty;
            return false;
        }

        indexText = CreateElementAccessIndexText(indexFormula);
        return indexText.Length > 0;
    }

    private static bool TryCreateElementAccessIndexText(
        ExpressionSyntax indexExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out string indexText,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (!TryResolveBuiltInIndexAccessIndexShape(
                indexExpression,
                semanticModel,
                cancellationToken,
                out var indexShape))
        {
            indexText = string.Empty;
            return false;
        }

        if (!TryTranslateValue(
                indexShape.ValueExpression,
                semanticModel,
                cancellationToken,
                out var indexFormula,
                getSymbolVersion,
                inlineDepth) ||
            indexFormula is not { Kind: SmtValueKind.Int })
        {
            indexText = string.Empty;
            return false;
        }

        indexText = indexShape.FromEnd
            ? "^" + CreateElementAccessIndexText(indexFormula)
            : CreateElementAccessIndexText(indexFormula);
        return indexText.Length > 0;
    }

    private static string CreateElementAccessIndexText(SmtFormula indexFormula)
    {
        return indexFormula is SmtIntegerConstant integerConstant
            ? integerConstant.Value.ToString(CultureInfo.InvariantCulture)
            : indexFormula.ToString() ?? string.Empty;
    }

    private static bool TryCreateBuiltInElementAccessLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        receiverExpression = UnwrapExpression(receiverExpression);
        if (receiverExpression is ConditionalExpressionSyntax conditionalExpression &&
            TryTranslate(
                conditionalExpression.Condition,
                semanticModel,
                cancellationToken,
                out var conditionFormula,
                getSymbolVersion,
                inlineDepth) &&
            conditionFormula != null &&
            TryCreateBuiltInElementAccessLengthFormula(
                conditionalExpression.WhenTrue,
                semanticModel,
                cancellationToken,
                out var whenTrueLength,
                getSymbolVersion,
                inlineDepth) &&
            TryCreateBuiltInElementAccessLengthFormula(
                conditionalExpression.WhenFalse,
                semanticModel,
                cancellationToken,
                out var whenFalseLength,
                getSymbolVersion,
                inlineDepth))
        {
            lengthFormula =
                new SmtConditionalFormula(conditionFormula, whenTrueLength, whenFalseLength, SmtValueKind.Int);
            return true;
        }

        if (receiverExpression is BinaryExpressionSyntax coalesceExpression &&
            coalesceExpression.IsKind(SyntaxKind.CoalesceExpression) &&
            TryTranslateValue(
                coalesceExpression.Left,
                semanticModel,
                cancellationToken,
                out var coalesceLeft,
                getSymbolVersion,
                inlineDepth) &&
            coalesceLeft is { Kind: SmtValueKind.Reference } &&
            TryCreateBuiltInElementAccessLengthFormula(
                coalesceExpression.Left,
                semanticModel,
                cancellationToken,
                out var coalesceLeftLength,
                getSymbolVersion,
                inlineDepth) &&
            TryCreateBuiltInElementAccessLengthFormula(
                coalesceExpression.Right,
                semanticModel,
                cancellationToken,
                out var coalesceRightLength,
                getSymbolVersion,
                inlineDepth))
        {
            lengthFormula = new SmtConditionalFormula(
                CreateNonNullFormula(coalesceLeft),
                coalesceLeftLength,
                coalesceRightLength,
                SmtValueKind.Int);
            return true;
        }

        if (TryCreateBuiltInRangeAccessResultLengthFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (TryCreateMemoryExtensionsViewResultLengthFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (TryCreateBuiltInSliceInvocationResultLengthFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (TryCreateStringCreationResultLengthFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (TryCreateStringInvocationResultLengthFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (TryCreateReferenceCastBuiltInLengthFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        var receiverTypeInfo = semanticModel.GetTypeInfo(receiverExpression, cancellationToken);
        var receiverType = receiverTypeInfo.ConvertedType ?? receiverTypeInfo.Type;
        if ((receiverTypeInfo.Type is IArrayTypeSymbol { Rank: 1 } ||
             receiverTypeInfo.ConvertedType is IArrayTypeSymbol { Rank: 1 }) &&
            TryCreateArrayLengthFormula(receiverExpression, semanticModel, cancellationToken, out lengthFormula,
                getSymbolVersion, inlineDepth))
            return true;

        if (receiverType is IArrayTypeSymbol { Rank: 1 } &&
            TryCreateBuiltInLengthReceiverFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var arrayLengthReceiverFormula,
                getSymbolVersion,
                inlineDepth))
        {
            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (TryCreateMemberFormula(arrayLengthReceiverFormula, "Length", intType, out var arrayLength) &&
                arrayLength is { Kind: SmtValueKind.Int })
            {
                lengthFormula = arrayLength;
                return true;
            }
        }

        if (TryGetKnownStringLength(receiverExpression, semanticModel, cancellationToken, out var knownStringLength))
        {
            lengthFormula = new SmtIntegerConstant(knownStringLength);
            return true;
        }

        if (IsStringExpression(receiverExpression, semanticModel, cancellationToken) &&
            TryTranslateStringValue(receiverExpression, semanticModel, cancellationToken, out var stringValue,
                getSymbolVersion, inlineDepth) &&
            stringValue != null)
        {
            lengthFormula = new SmtStringLengthTerm(stringValue);
            return true;
        }

        if (IsBuiltInSpanOrMemoryType(receiverType) &&
            TryCreateBuiltInLengthReceiverFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var lengthReceiverFormula,
                getSymbolVersion,
                inlineDepth))
        {
            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (TryCreateMemberFormula(lengthReceiverFormula, "Length", intType, out var receiverLength) &&
                receiverLength is { Kind: SmtValueKind.Int })
            {
                lengthFormula = receiverLength;
                return true;
            }
        }

        if (HasCountBackedIntIndexer(receiverType) &&
            TryCreateBuiltInLengthReceiverFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var countReceiverFormula,
                getSymbolVersion,
                inlineDepth))
        {
            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (TryCreateMemberFormula(countReceiverFormula, "Count", intType, out var receiverCount) &&
                receiverCount is { Kind: SmtValueKind.Int })
            {
                lengthFormula = receiverCount;
                return true;
            }
        }

        if (!TryTranslateValue(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var receiverFormula,
                getSymbolVersion,
                inlineDepth) ||
            receiverFormula is not { Kind: SmtValueKind.Reference })
        {
            lengthFormula = null!;
            return false;
        }

        var fallbackIntType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        if (!TryCreateMemberFormula(receiverFormula, "Length", fallbackIntType, out var candidate) ||
            candidate is not { Kind: SmtValueKind.Int })
        {
            lengthFormula = null!;
            return false;
        }

        lengthFormula = candidate;
        return true;
    }

    private static bool TryCreateReferenceCastBuiltInLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (UnwrapExpression(receiverExpression) is not CastExpressionSyntax castExpression ||
            !TryTranslateNonUserDefinedReferenceCastOperand(
                castExpression,
                semanticModel,
                cancellationToken,
                out var operandReference,
                out var targetType,
                getSymbolVersion,
                inlineDepth))
            return false;

        if (targetType is IArrayTypeSymbol { Rank: 1 })
        {
            var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
            if (TryCreateMemberFormula(operandReference, "Length", intType, out var candidate) &&
                candidate is { Kind: SmtValueKind.Int })
            {
                lengthFormula = candidate;
                return true;
            }

            return false;
        }

        if (targetType.SpecialType == SpecialType.System_String)
        {
            lengthFormula = new SmtStringLengthTerm(CreateStringValueFormulaForReference(operandReference));
            return true;
        }

        return false;
    }

    private static bool TryCreateBuiltInSliceInvocationResultLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (receiverExpression is not InvocationExpressionSyntax invocationExpression ||
            semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation)
            return false;

        var method = invocationOperation.TargetMethod;
        if (method.IsStatic ||
            method.Name != "Slice" ||
            !IsBuiltInSpanOrMemoryType(method.ContainingType) ||
            !IsBuiltInSpanOrMemoryType(method.ReturnType) ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression)
            return false;

        return TryCreateStartOrRangeSliceLengthFormula(
            invocationOperation,
            sourceExpression,
            semanticModel,
            cancellationToken,
            out lengthFormula,
            getSymbolVersion,
            inlineDepth);
    }

    private static bool TryCreateStartOrRangeSliceLengthFormula(
        IInvocationOperation invocationOperation,
        ExpressionSyntax sourceExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        var method = invocationOperation.TargetMethod;
        if (method.Parameters.Length == 1)
        {
            if (invocationOperation.Arguments.Length != 1 ||
                !TryCreateBuiltInElementAccessLengthFormula(
                    sourceExpression,
                    semanticModel,
                    cancellationToken,
                    out var sourceLength,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryTranslateIntInvocationArgument(
                    invocationOperation,
                    0,
                    semanticModel,
                    cancellationToken,
                    out var start,
                    getSymbolVersion,
                    inlineDepth))
                return false;

            lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, start);
            return true;
        }

        if (method.Parameters.Length != 2 ||
            invocationOperation.Arguments.Length != 2 ||
            !TryCreateBuiltInElementAccessLengthFormula(
                sourceExpression,
                semanticModel,
                cancellationToken,
                out _,
                getSymbolVersion,
                inlineDepth) ||
            !TryTranslateIntInvocationArgument(
                invocationOperation,
                0,
                semanticModel,
                cancellationToken,
                out _,
                getSymbolVersion,
                inlineDepth) ||
            !TryTranslateIntInvocationArgument(
                invocationOperation,
                1,
                semanticModel,
                cancellationToken,
                out var resultLength,
                getSymbolVersion,
                inlineDepth))
            return false;

        lengthFormula = resultLength;
        return true;
    }


    private static bool TryCreateStringCreationResultLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (receiverExpression is not ObjectCreationExpressionSyntax objectCreationExpression ||
            semanticModel.GetOperation(objectCreationExpression, cancellationToken) is not IObjectCreationOperation
                objectCreationOperation)
            return false;

        var constructor = objectCreationOperation.Constructor;
        if (constructor == null ||
            constructor.ContainingType.SpecialType != SpecialType.System_String)
            return false;

        if (constructor.Parameters.Length == 2 &&
            constructor.Parameters[0].Type.SpecialType == SpecialType.System_Char &&
            constructor.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
            objectCreationOperation.Arguments.Length == 2 &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 1, out var countExpression) &&
            TryTranslateValue(
                countExpression,
                semanticModel,
                cancellationToken,
                out var countFormula,
                getSymbolVersion,
                inlineDepth) &&
            countFormula is { Kind: SmtValueKind.Int })
        {
            lengthFormula = countFormula;
            return true;
        }

        if (constructor.Parameters.Length == 1 &&
            SymbolicTypeFacts.IsCharArrayType(constructor.Parameters[0].Type) &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var charArrayExpression) &&
            TryCreateBuiltInElementAccessLengthFormula(
                charArrayExpression,
                semanticModel,
                cancellationToken,
                out var charArrayLength,
                getSymbolVersion,
                inlineDepth))
        {
            lengthFormula = charArrayLength;
            return true;
        }

        if (constructor.Parameters.Length == 3 &&
            SymbolicTypeFacts.IsCharArrayType(constructor.Parameters[0].Type) &&
            constructor.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
            constructor.Parameters[2].Type.SpecialType == SpecialType.System_Int32 &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 2, out var lengthExpression) &&
            TryTranslateValue(
                lengthExpression,
                semanticModel,
                cancellationToken,
                out var translatedLength,
                getSymbolVersion,
                inlineDepth) &&
            translatedLength is { Kind: SmtValueKind.Int })
        {
            lengthFormula = translatedLength;
            return true;
        }

        if (constructor.Parameters.Length == 1 &&
            SymbolicTypeFacts.IsReadOnlySpanOfCharType(constructor.Parameters[0].Type) &&
            TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var spanExpression) &&
            TryCreateBuiltInElementAccessLengthFormula(
                spanExpression,
                semanticModel,
                cancellationToken,
                out var spanLength,
                getSymbolVersion,
                inlineDepth))
        {
            lengthFormula = spanLength;
            return true;
        }

        return false;
    }

    private static bool TryCreateStringInvocationResultLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (receiverExpression is not InvocationExpressionSyntax invocationExpression ||
            semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation)
            return false;

        var method = invocationOperation.TargetMethod;
        if (method.IsStatic ||
            method.ContainingType?.SpecialType != SpecialType.System_String ||
            method.ReturnType.SpecialType != SpecialType.System_String ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression)
            return false;

        if (method.Name == "Substring")
            return TryCreateStartOrRangeSliceLengthFormula(
                invocationOperation,
                sourceExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth);

        if (method.Name == "Remove")
        {
            if (!TryCreateBuiltInElementAccessLengthFormula(
                    sourceExpression,
                    semanticModel,
                    cancellationToken,
                    out var sourceLength,
                    getSymbolVersion,
                    inlineDepth))
                return false;

            if (method.Parameters.Length == 1)
            {
                if (invocationOperation.Arguments.Length != 1 ||
                    !TryTranslateIntInvocationArgument(
                        invocationOperation,
                        0,
                        semanticModel,
                        cancellationToken,
                        out var start,
                        getSymbolVersion,
                        inlineDepth))
                    return false;

                lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, start);
                return true;
            }

            if (method.Parameters.Length != 2 ||
                invocationOperation.Arguments.Length != 2 ||
                !TryTranslateIntInvocationArgument(
                    invocationOperation,
                    0,
                    semanticModel,
                    cancellationToken,
                    out _,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryTranslateIntInvocationArgument(
                    invocationOperation,
                    1,
                    semanticModel,
                    cancellationToken,
                    out var count,
                    getSymbolVersion,
                    inlineDepth))
                return false;

            lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, count);
            return true;
        }

        if (method.Name == "Insert")
        {
            if (method.Parameters.Length != 2 ||
                method.Parameters[1].Type.SpecialType != SpecialType.System_String ||
                invocationOperation.Arguments.Length != 2 ||
                !TryCreateBuiltInElementAccessLengthFormula(
                    sourceExpression,
                    semanticModel,
                    cancellationToken,
                    out var sourceLength,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryTranslateIntInvocationArgument(
                    invocationOperation,
                    0,
                    semanticModel,
                    cancellationToken,
                    out _,
                    getSymbolVersion,
                    inlineDepth) ||
                !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 1,
                    out var valueExpression) ||
                !TryCreateBuiltInElementAccessLengthFormula(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var valueLength,
                    getSymbolVersion,
                    inlineDepth))
                return false;

            lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, sourceLength, valueLength);
            return true;
        }

        if (method.Name is "PadLeft" or "PadRight")
        {
            if ((method.Parameters.Length != 1 &&
                 (method.Parameters.Length != 2 ||
                  method.Parameters[1].Type.SpecialType != SpecialType.System_Char)) ||
                invocationOperation.Arguments.Length != method.Parameters.Length ||
                !TryCreateBuiltInElementAccessLengthFormula(
                    sourceExpression,
                    semanticModel,
                    cancellationToken,
                    out var sourceLength,
                    getSymbolVersion,
                    inlineDepth) ||
                !TryTranslateIntInvocationArgument(
                    invocationOperation,
                    0,
                    semanticModel,
                    cancellationToken,
                    out var totalWidth,
                    getSymbolVersion,
                    inlineDepth))
                return false;

            lengthFormula = new SmtConditionalFormula(
                new SmtBinaryFormula(SmtBinaryOperator.GreaterThan, totalWidth, sourceLength),
                totalWidth,
                sourceLength,
                SmtValueKind.Int);
            return true;
        }

        return false;
    }

    private static bool TryTranslateIntInvocationArgument(
        IInvocationOperation invocationOperation,
        int parameterIndex,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula argument,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        argument = null!;
        if (parameterIndex < 0 ||
            parameterIndex >= invocationOperation.TargetMethod.Parameters.Length ||
            invocationOperation.TargetMethod.Parameters[parameterIndex].Type.SpecialType != SpecialType.System_Int32 ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, parameterIndex,
                out var argumentExpression) ||
            !TryTranslateValue(
                argumentExpression,
                semanticModel,
                cancellationToken,
                out var candidate,
                getSymbolVersion,
                inlineDepth) ||
            candidate is not { Kind: SmtValueKind.Int })
            return false;

        argument = candidate;
        return true;
    }

    private static bool TryGetObjectCreationArgumentExpression(
        IObjectCreationOperation objectCreationOperation,
        int parameterIndex,
        out ExpressionSyntax expression)
    {
        expression = null!;
        if (objectCreationOperation.Constructor == null ||
            parameterIndex < 0 ||
            parameterIndex >= objectCreationOperation.Constructor.Parameters.Length)
            return false;

        var parameter = objectCreationOperation.Constructor.Parameters[parameterIndex];
        foreach (var argument in objectCreationOperation.Arguments)
            if (SymbolEqualityComparer.Default.Equals(argument.Parameter, parameter) &&
                argument.Value.Syntax is ExpressionSyntax argumentExpression)
            {
                expression = argumentExpression;
                return true;
            }

        if (parameterIndex < objectCreationOperation.Arguments.Length &&
            objectCreationOperation.Arguments[parameterIndex].Value.Syntax is ExpressionSyntax fallbackExpression)
        {
            expression = fallbackExpression;
            return true;
        }

        return false;
    }

    private static bool TryCreateBuiltInRangeAccessResultLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (receiverExpression is not ElementAccessExpressionSyntax elementAccess ||
            elementAccess.ArgumentList.Arguments.Count != 1)
            return false;

        var sourceType = semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).ConvertedType ??
                         semanticModel.GetTypeInfo(elementAccess.Expression, cancellationToken).Type;
        if (!IsSupportedBuiltInElementAccessReceiver(sourceType)) return false;

        if (!TryResolveBuiltInRangeAccessRangeShape(
                elementAccess.ArgumentList.Arguments[0].Expression,
                semanticModel,
                cancellationToken,
                out var rangeShape) ||
            !TryCreateBuiltInElementAccessLengthFormula(
                elementAccess.Expression,
                semanticModel,
                cancellationToken,
                out var sourceLengthFormula,
                getSymbolVersion,
                inlineDepth) ||
            !TryCreateEffectiveRangeEndpointFormula(
                rangeShape,
                true,
                sourceLengthFormula,
                new SmtIntegerConstant(0),
                semanticModel,
                cancellationToken,
                out var startFormula,
                getSymbolVersion,
                inlineDepth) ||
            !TryCreateEffectiveRangeEndpointFormula(
                rangeShape,
                false,
                sourceLengthFormula,
                sourceLengthFormula,
                semanticModel,
                cancellationToken,
                out var endFormula,
                getSymbolVersion,
                inlineDepth))
            return false;

        lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, endFormula, startFormula);
        return true;
    }

    private static bool TryCreateMemoryExtensionsViewResultLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (receiverExpression is not InvocationExpressionSyntax invocationExpression ||
            semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation)
            return false;

        var method = invocationOperation.TargetMethod;
        if (!IsMemoryExtensionsViewMethod(method) ||
            !IsBuiltInSpanOrMemoryType(method.ReturnType) ||
            !TryGetMemoryExtensionsViewSourceExpression(
                invocationExpression,
                semanticModel,
                cancellationToken,
                out var sourceExpression,
                out var firstArgumentIndex) ||
            !IsSupportedMemoryExtensionsViewSource(sourceExpression, semanticModel, cancellationToken) ||
            !TryCreateBuiltInElementAccessLengthFormula(
                sourceExpression,
                semanticModel,
                cancellationToken,
                out var sourceLength,
                getSymbolVersion,
                inlineDepth))
            return false;

        var remainingArgumentCount = invocationExpression.ArgumentList.Arguments.Count - firstArgumentIndex;
        if (remainingArgumentCount == 0)
        {
            lengthFormula = sourceLength;
            return true;
        }

        if (remainingArgumentCount == 1)
        {
            var argument = invocationExpression.ArgumentList.Arguments[firstArgumentIndex].Expression;
            if (IsSystemRangeExpression(argument, semanticModel, cancellationToken))
                return TryCreateRangeResultLengthFormula(
                    argument,
                    sourceLength,
                    semanticModel,
                    cancellationToken,
                    out lengthFormula,
                    getSymbolVersion,
                    inlineDepth);

            if (!TryTranslateValue(
                    argument,
                    semanticModel,
                    cancellationToken,
                    out var start,
                    getSymbolVersion,
                    inlineDepth) ||
                start is not { Kind: SmtValueKind.Int })
            {
                lengthFormula = null!;
                return false;
            }

            lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, sourceLength, start);
            return true;
        }

        if (remainingArgumentCount != 2 ||
            !TryTranslateValue(
                invocationExpression.ArgumentList.Arguments[firstArgumentIndex].Expression,
                semanticModel,
                cancellationToken,
                out var translatedStart,
                getSymbolVersion,
                inlineDepth) ||
            translatedStart is not { Kind: SmtValueKind.Int } ||
            !TryTranslateValue(
                invocationExpression.ArgumentList.Arguments[firstArgumentIndex + 1].Expression,
                semanticModel,
                cancellationToken,
                out var resultLength,
                getSymbolVersion,
                inlineDepth) ||
            resultLength is not { Kind: SmtValueKind.Int })
        {
            lengthFormula = null!;
            return false;
        }

        lengthFormula = resultLength;
        return true;
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
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax sourceExpression,
        out int firstArgumentIndex)
    {
        if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
            semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type != null)
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
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var sourceTypeInfo = semanticModel.GetTypeInfo(sourceExpression, cancellationToken);
        var sourceType = sourceTypeInfo.ConvertedType ?? sourceTypeInfo.Type;
        return sourceType?.SpecialType == SpecialType.System_String ||
               sourceType is IArrayTypeSymbol { Rank: 1 };
    }

    private static bool TryCreateRangeResultLengthFormula(
        ExpressionSyntax rangeExpression,
        SmtFormula sourceLengthFormula,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (!TryResolveBuiltInRangeAccessRangeShape(
                rangeExpression,
                semanticModel,
                cancellationToken,
                out var rangeShape) ||
            !TryCreateEffectiveRangeEndpointFormula(
                rangeShape,
                true,
                sourceLengthFormula,
                new SmtIntegerConstant(0),
                semanticModel,
                cancellationToken,
                out var startFormula,
                getSymbolVersion,
                inlineDepth) ||
            !TryCreateEffectiveRangeEndpointFormula(
                rangeShape,
                false,
                sourceLengthFormula,
                sourceLengthFormula,
                semanticModel,
                cancellationToken,
                out var endFormula,
                getSymbolVersion,
                inlineDepth))
        {
            lengthFormula = null!;
            return false;
        }

        lengthFormula = new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, endFormula, startFormula);
        return true;
    }

    private static bool TryResolveBuiltInRangeAccessRangeShape(
        ExpressionSyntax argumentExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RangeExpressionShape rangeShape)
    {
        argumentExpression = UnwrapElementAccessIndexExpression(argumentExpression);
        if (TryCreateDirectRangeExpressionShape(
                argumentExpression,
                semanticModel,
                cancellationToken,
                out rangeShape))
            return true;

        if (!IsSystemRangeExpression(argumentExpression, semanticModel, cancellationToken) ||
            !TryGetLocalOrParameterRangeSymbol(argumentExpression, semanticModel, cancellationToken,
                out var rangeSymbol))
        {
            rangeShape = default;
            return false;
        }

        return TryResolveAssignedRangeShape(
            argumentExpression,
            rangeSymbol,
            semanticModel,
            cancellationToken,
            out rangeShape);
    }

    private static bool TryGetLocalOrParameterRangeSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol rangeSymbol)
    {
        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is ILocalSymbol localSymbol &&
            IsSystemRangeType(localSymbol.Type, semanticModel.Compilation))
        {
            rangeSymbol = localSymbol;
            return true;
        }

        if (symbol is IParameterSymbol { RefKind: RefKind.None } parameterSymbol &&
            IsSystemRangeType(parameterSymbol.Type, semanticModel.Compilation))
        {
            rangeSymbol = parameterSymbol;
            return true;
        }

        rangeSymbol = null!;
        return false;
    }

    private static bool TryResolveAssignedRangeShape(
        ExpressionSyntax useExpression,
        ISymbol rangeSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RangeExpressionShape rangeShape)
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
                    semanticModel,
                    cancellationToken,
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

        if (!foundAssignment)
        {
            rangeShape = default;
            return false;
        }

        return true;
    }

    private static IEnumerable<(BlockSyntax Block, StatementSyntax ContainingStatement)> EnumerateContainingBlocks(
        SyntaxNode site)
    {
        for (var current = site; current != null; current = current.Parent)
            if (current is StatementSyntax statement &&
                statement.Parent is BlockSyntax block)
                yield return (block, statement);
    }

    private static void TryGetRangeAssignmentFromPrecedingStatement(
        StatementSyntax statement,
        ISymbol rangeSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool writesRangeSymbol,
        out RangeExpressionShape? rangeShape)
    {
        rangeShape = null;
        writesRangeSymbol = false;

        if (TryGetRangeAssignmentFromLocalDeclaration(
                statement,
                rangeSymbol,
                semanticModel,
                cancellationToken,
                out writesRangeSymbol,
                out rangeShape))
            return;

        if (TryGetRangeAssignmentFromExpressionStatement(
                statement,
                rangeSymbol,
                semanticModel,
                cancellationToken,
                out writesRangeSymbol,
                out rangeShape))
            return;

        writesRangeSymbol = ContainsRangeSymbolWrite(statement, rangeSymbol, semanticModel, cancellationToken);
    }

    private static bool TryGetRangeAssignmentFromLocalDeclaration(
        StatementSyntax statement,
        ISymbol rangeSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool writesRangeSymbol,
        out RangeExpressionShape? rangeShape)
    {
        rangeShape = null;
        writesRangeSymbol = false;
        if (statement is not LocalDeclarationStatementSyntax localDeclaration) return false;

        foreach (var variable in localDeclaration.Declaration.Variables)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
            if (!IsSameSymbol(declaredSymbol, rangeSymbol)) continue;

            if (variable.Initializer == null) return true;

            writesRangeSymbol = true;
            if (localDeclaration.Declaration.Variables.Count != 1 ||
                !TryCreateDirectRangeExpressionShape(
                    variable.Initializer.Value,
                    semanticModel,
                    cancellationToken,
                    out var assignedRangeShape))
                return true;

            rangeShape = assignedRangeShape;
            return true;
        }

        return false;
    }

    private static bool TryGetRangeAssignmentFromExpressionStatement(
        StatementSyntax statement,
        ISymbol rangeSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool writesRangeSymbol,
        out RangeExpressionShape? rangeShape)
    {
        rangeShape = null;
        writesRangeSymbol = false;
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            !IsRangeSymbolReference(assignment.Left, rangeSymbol, semanticModel, cancellationToken))
            return false;

        writesRangeSymbol = true;
        if (TryCreateDirectRangeExpressionShape(
                assignment.Right,
                semanticModel,
                cancellationToken,
                out var assignedRangeShape))
            rangeShape = assignedRangeShape;

        return true;
    }

    private static bool ContainsRangeSymbolWrite(
        SyntaxNode node,
        ISymbol rangeSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<AssignmentExpressionSyntax>())
            if (IsRangeSymbolReference(assignment.Left, rangeSymbol, semanticModel, cancellationToken))
                return true;

        foreach (var unary in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<PrefixUnaryExpressionSyntax>())
            if ((unary.IsKind(SyntaxKind.PreIncrementExpression) ||
                 unary.IsKind(SyntaxKind.PreDecrementExpression)) &&
                IsRangeSymbolReference(unary.Operand, rangeSymbol, semanticModel, cancellationToken))
                return true;

        foreach (var unary in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<PostfixUnaryExpressionSyntax>())
            if ((unary.IsKind(SyntaxKind.PostIncrementExpression) ||
                 unary.IsKind(SyntaxKind.PostDecrementExpression)) &&
                IsRangeSymbolReference(unary.Operand, rangeSymbol, semanticModel, cancellationToken))
                return true;

        foreach (var argument in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<ArgumentSyntax>())
            if ((argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                 argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                IsRangeSymbolReference(argument.Expression, rangeSymbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool IsRangeSymbolReference(
        ExpressionSyntax expression,
        ISymbol rangeSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsSameSymbol(
            semanticModel.GetSymbolInfo(UnwrapElementAccessIndexExpression(expression), cancellationToken).Symbol,
            rangeSymbol);
    }

    private static bool TryCreateDirectRangeExpressionShape(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RangeExpressionShape rangeShape)
    {
        expression = UnwrapElementAccessIndexExpression(expression);
        if (expression is RangeExpressionSyntax rangeExpression)
        {
            if (!TryCreateRangeEndpointShape(
                    rangeExpression.LeftOperand,
                    semanticModel,
                    cancellationToken,
                    out var hasStart,
                    out var start) ||
                !TryCreateRangeEndpointShape(
                    rangeExpression.RightOperand,
                    semanticModel,
                    cancellationToken,
                    out var hasEnd,
                    out var end))
            {
                rangeShape = default;
                return false;
            }

            rangeShape = new RangeExpressionShape(hasStart, start, hasEnd, end);
            return true;
        }

        if (TryCreateRangeInvocationShape(expression, semanticModel, cancellationToken, out rangeShape) ||
            TryCreateRangeObjectCreationShape(expression, semanticModel, cancellationToken, out rangeShape) ||
            TryCreateRangeAllPropertyShape(expression, semanticModel, cancellationToken, out rangeShape))
            return true;

        rangeShape = default;
        return false;
    }

    private static bool TryCreateRangeEndpointShape(
        ExpressionSyntax? expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool hasEndpoint,
        out IndexExpressionShape endpoint)
    {
        if (expression == null)
        {
            hasEndpoint = false;
            endpoint = default;
            return true;
        }

        if (!TryResolveBuiltInIndexAccessIndexShape(
                expression,
                semanticModel,
                cancellationToken,
                out endpoint))
        {
            hasEndpoint = false;
            return false;
        }

        hasEndpoint = true;
        return true;
    }

    private static bool TryCreateRangeInvocationShape(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RangeExpressionShape rangeShape)
    {
        rangeShape = default;
        if (expression is not InvocationExpressionSyntax invocationExpression ||
            semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation ||
            invocationOperation.TargetMethod.MethodKind != MethodKind.Ordinary ||
            invocationOperation.TargetMethod.ReturnType is not { } returnType ||
            !IsSystemRangeType(returnType, semanticModel.Compilation) ||
            invocationOperation.TargetMethod.ContainingType is not { } containingType ||
            !IsSystemRangeType(containingType, semanticModel.Compilation))
            return false;

        if (invocationOperation.TargetMethod.Name == "StartAt")
        {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0,
                    out var startExpression) ||
                !TryResolveBuiltInIndexAccessIndexShape(
                    startExpression,
                    semanticModel,
                    cancellationToken,
                    out var start))
                return false;

            rangeShape = new RangeExpressionShape(true, start, false, default);
            return true;
        }

        if (invocationOperation.TargetMethod.Name == "EndAt")
        {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var endExpression) ||
                !TryResolveBuiltInIndexAccessIndexShape(
                    endExpression,
                    semanticModel,
                    cancellationToken,
                    out var end))
                return false;

            rangeShape = new RangeExpressionShape(false, default, true, end);
            return true;
        }

        return false;
    }

    private static bool TryCreateRangeObjectCreationShape(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RangeExpressionShape rangeShape)
    {
        rangeShape = default;
        if (expression is not ObjectCreationExpressionSyntax objectCreation ||
            semanticModel.GetOperation(objectCreation, cancellationToken) is not IObjectCreationOperation
                objectCreationOperation ||
            objectCreationOperation.Constructor == null ||
            !IsSystemRangeType(objectCreationOperation.Constructor.ContainingType, semanticModel.Compilation) ||
            !TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var startExpression) ||
            !TryGetObjectCreationArgumentExpression(objectCreationOperation, 1, out var endExpression) ||
            !TryResolveBuiltInIndexAccessIndexShape(
                startExpression,
                semanticModel,
                cancellationToken,
                out var start) ||
            !TryResolveBuiltInIndexAccessIndexShape(
                endExpression,
                semanticModel,
                cancellationToken,
                out var end))
            return false;

        rangeShape = new RangeExpressionShape(true, start, true, end);
        return true;
    }

    private static bool TryCreateRangeAllPropertyShape(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out RangeExpressionShape rangeShape)
    {
        rangeShape = default;
        if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not IPropertySymbol
            {
                Name: "All",
                IsStatic: true
            } propertySymbol ||
            !IsSystemRangeType(propertySymbol.ContainingType, semanticModel.Compilation) ||
            !IsSystemRangeType(propertySymbol.Type, semanticModel.Compilation))
            return false;

        rangeShape = new RangeExpressionShape(false, default, false, default);
        return true;
    }

    private static bool IsSameSymbol(ISymbol? candidate, ISymbol target)
    {
        return candidate != null &&
               (SymbolEqualityComparer.Default.Equals(candidate, target) ||
                SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target.OriginalDefinition));
    }

    private static bool IsSystemRangeExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return IsSystemRangeType(typeInfo.ConvertedType ?? typeInfo.Type, semanticModel.Compilation);
    }

    private static bool IsSystemRangeType(ITypeSymbol? typeSymbol, Compilation compilation)
    {
        var rangeType = compilation.GetTypeByMetadataName("System.Range");
        return typeSymbol != null &&
               rangeType != null &&
               SymbolEqualityComparer.Default.Equals(typeSymbol, rangeType);
    }

    private static bool IsSupportedBuiltInElementAccessReceiver(ITypeSymbol? typeSymbol)
    {
        return typeSymbol is IArrayTypeSymbol { Rank: 1 } ||
               typeSymbol?.SpecialType == SpecialType.System_String ||
               IsBuiltInSpanType(typeSymbol) ||
               HasCountBackedIntIndexer(typeSymbol);
    }

    private static bool IsSupportedBuiltInLengthReceiver(ITypeSymbol? typeSymbol)
    {
        return IsSupportedBuiltInElementAccessReceiver(typeSymbol) ||
               IsBuiltInMemoryType(typeSymbol);
    }

    private static bool HasCountBackedIntIndexer(ITypeSymbol? typeSymbol)
    {
        return TryGetCountBackedIndexerElementType(typeSymbol, out _);
    }

    private static bool TryGetCountBackedIndexerElementType(ITypeSymbol? typeSymbol, out ITypeSymbol elementType)
    {
        elementType = null!;
        if (typeSymbol == null ||
            !SymbolicTypeFacts.HasInstanceInt32Member(typeSymbol, "Count"))
            return false;

        return TryGetIntIndexerElementType(typeSymbol, out elementType);
    }

    private static bool TryGetIntIndexerElementType(ITypeSymbol typeSymbol, out ITypeSymbol elementType)
    {
        for (var current = typeSymbol; current != null; current = (current as INamedTypeSymbol)?.BaseType)
            if (TryGetDeclaredIntIndexerElementType(current, out elementType))
                return true;

        foreach (var interfaceType in typeSymbol.AllInterfaces)
            if (TryGetDeclaredIntIndexerElementType(interfaceType, out elementType))
                return true;

        elementType = null!;
        return false;
    }

    private static bool TryGetDeclaredIntIndexerElementType(ITypeSymbol typeSymbol, out ITypeSymbol elementType)
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
            IsBuiltInSpanType(namedType) &&
            namedType.TypeArguments.Length == 1)
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        if (TryGetCountBackedIndexerElementType(receiverType, out elementType)) return true;

        elementType = null!;
        return false;
    }

    private static bool TryCreateBuiltInElementAccessReceiverFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula receiverFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (TryTranslateValue(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var translatedReceiver,
                getSymbolVersion,
                inlineDepth) &&
            translatedReceiver is { Kind: SmtValueKind.Reference })
        {
            receiverFormula = translatedReceiver;
            return true;
        }

        receiverExpression = UnwrapExpression(receiverExpression);
        var receiverType = semanticModel.GetTypeInfo(receiverExpression, cancellationToken).ConvertedType ??
                           semanticModel.GetTypeInfo(receiverExpression, cancellationToken).Type;
        if (!IsBuiltInSpanType(receiverType))
        {
            receiverFormula = null!;
            return false;
        }

        var receiverSymbol = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken).Symbol;
        if (receiverSymbol is not ILocalSymbol and not IParameterSymbol)
        {
            receiverFormula = null!;
            return false;
        }

        receiverFormula = new SmtVariable(GetVariableName(receiverSymbol, getSymbolVersion), SmtValueKind.Reference);
        return true;
    }

    private static bool TryCreateBuiltInLengthReceiverFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula receiverFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (TryTranslateValue(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var translatedReceiver,
                getSymbolVersion,
                inlineDepth) &&
            translatedReceiver is { Kind: SmtValueKind.Reference })
        {
            receiverFormula = translatedReceiver;
            return true;
        }

        receiverExpression = UnwrapExpression(receiverExpression);
        var receiverType = semanticModel.GetTypeInfo(receiverExpression, cancellationToken).ConvertedType ??
                           semanticModel.GetTypeInfo(receiverExpression, cancellationToken).Type;
        if (!IsBuiltInSpanOrMemoryType(receiverType))
        {
            receiverFormula = null!;
            return false;
        }

        var receiverSymbol = semanticModel.GetSymbolInfo(receiverExpression, cancellationToken).Symbol;
        if (receiverSymbol is not ILocalSymbol and not IParameterSymbol)
        {
            receiverFormula = null!;
            return false;
        }

        receiverFormula = new SmtVariable(GetVariableName(receiverSymbol, getSymbolVersion), SmtValueKind.Reference);
        return true;
    }

    private static bool IsBuiltInSpanType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsBuiltInSpanType(typeSymbol);
    }

    private static bool IsBuiltInMemoryType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsBuiltInMemoryType(typeSymbol);
    }

    private static bool IsBuiltInSpanOrMemoryType(ITypeSymbol? typeSymbol)
    {
        return SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(typeSymbol);
    }

    private static bool TryResolveBuiltInIndexAccessIndexShape(
        ExpressionSyntax argumentExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IndexExpressionShape indexShape)
    {
        argumentExpression = UnwrapElementAccessIndexExpression(argumentExpression);
        if (TryCreateDirectIndexExpressionShape(
                argumentExpression,
                semanticModel,
                cancellationToken,
                out indexShape))
            return true;

        if (!IsSystemIndexExpression(argumentExpression, semanticModel, cancellationToken) ||
            !TryGetLocalOrParameterIndexSymbol(argumentExpression, semanticModel, cancellationToken,
                out var indexSymbol))
        {
            indexShape = default;
            return false;
        }

        return TryResolveAssignedIndexShape(
            argumentExpression,
            indexSymbol,
            semanticModel,
            cancellationToken,
            out indexShape);
    }

    private static bool TryGetLocalOrParameterIndexSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol indexSymbol)
    {
        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is ILocalSymbol localSymbol &&
            IsSystemIndexType(localSymbol.Type, semanticModel.Compilation))
        {
            indexSymbol = localSymbol;
            return true;
        }

        if (symbol is IParameterSymbol { RefKind: RefKind.None } parameterSymbol &&
            IsSystemIndexType(parameterSymbol.Type, semanticModel.Compilation))
        {
            indexSymbol = parameterSymbol;
            return true;
        }

        indexSymbol = null!;
        return false;
    }

    private static bool TryResolveAssignedIndexShape(
        ExpressionSyntax useExpression,
        ISymbol indexSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IndexExpressionShape indexShape)
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
                    semanticModel,
                    cancellationToken,
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

        if (!foundAssignment)
        {
            indexShape = default;
            return false;
        }

        return true;
    }

    private static void TryGetIndexAssignmentFromPrecedingStatement(
        StatementSyntax statement,
        ISymbol indexSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool writesIndexSymbol,
        out IndexExpressionShape? indexShape)
    {
        indexShape = null;
        writesIndexSymbol = false;

        if (TryGetIndexAssignmentFromLocalDeclaration(
                statement,
                indexSymbol,
                semanticModel,
                cancellationToken,
                out writesIndexSymbol,
                out indexShape))
            return;

        if (TryGetIndexAssignmentFromExpressionStatement(
                statement,
                indexSymbol,
                semanticModel,
                cancellationToken,
                out writesIndexSymbol,
                out indexShape))
            return;

        writesIndexSymbol = ContainsIndexSymbolWrite(statement, indexSymbol, semanticModel, cancellationToken);
    }

    private static bool TryGetIndexAssignmentFromLocalDeclaration(
        StatementSyntax statement,
        ISymbol indexSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool writesIndexSymbol,
        out IndexExpressionShape? indexShape)
    {
        indexShape = null;
        writesIndexSymbol = false;
        if (statement is not LocalDeclarationStatementSyntax localDeclaration) return false;

        foreach (var variable in localDeclaration.Declaration.Variables)
        {
            var declaredSymbol = semanticModel.GetDeclaredSymbol(variable, cancellationToken);
            if (!IsSameSymbol(declaredSymbol, indexSymbol)) continue;

            if (variable.Initializer == null) return true;

            writesIndexSymbol = true;
            if (localDeclaration.Declaration.Variables.Count != 1 ||
                !TryCreateDirectIndexExpressionShape(
                    variable.Initializer.Value,
                    semanticModel,
                    cancellationToken,
                    out var assignedIndexShape))
                return true;

            indexShape = assignedIndexShape;
            return true;
        }

        return false;
    }

    private static bool TryGetIndexAssignmentFromExpressionStatement(
        StatementSyntax statement,
        ISymbol indexSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool writesIndexSymbol,
        out IndexExpressionShape? indexShape)
    {
        indexShape = null;
        writesIndexSymbol = false;
        if (statement is not ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax assignment
            } ||
            !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) ||
            !IsIndexSymbolReference(assignment.Left, indexSymbol, semanticModel, cancellationToken))
            return false;

        writesIndexSymbol = true;
        if (TryCreateDirectIndexExpressionShape(
                assignment.Right,
                semanticModel,
                cancellationToken,
                out var assignedIndexShape))
            indexShape = assignedIndexShape;

        return true;
    }

    private static bool TryCreateDirectIndexExpressionShape(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IndexExpressionShape indexShape)
    {
        expression = UnwrapElementAccessIndexExpression(expression);
        if (expression is PrefixUnaryExpressionSyntax fromEndIndex &&
            fromEndIndex.OperatorToken.IsKind(SyntaxKind.CaretToken))
        {
            indexShape = new IndexExpressionShape(
                fromEndIndex.Operand,
                true,
                true);
            return true;
        }

        if (TryCreateIndexInvocationShape(expression, semanticModel, cancellationToken, out indexShape) ||
            TryCreateIndexObjectCreationShape(expression, semanticModel, cancellationToken, out indexShape))
            return true;

        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        if (typeInfo.Type != null &&
            IsIntegralOrEnumType(typeInfo.Type))
        {
            indexShape = new IndexExpressionShape(
                expression,
                false,
                false);
            return true;
        }

        indexShape = default;
        return false;
    }

    private static bool TryCreateIndexInvocationShape(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IndexExpressionShape indexShape)
    {
        indexShape = default;
        if (expression is not InvocationExpressionSyntax invocationExpression ||
            semanticModel.GetOperation(invocationExpression, cancellationToken) is not IInvocationOperation
                invocationOperation ||
            invocationOperation.TargetMethod.MethodKind != MethodKind.Ordinary ||
            invocationOperation.TargetMethod.ReturnType is not { } returnType ||
            !IsSystemIndexType(returnType, semanticModel.Compilation) ||
            invocationOperation.TargetMethod.ContainingType is not { } containingType ||
            !IsSystemIndexType(containingType, semanticModel.Compilation) ||
            !SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var valueExpression))
            return false;

        if (invocationOperation.TargetMethod.Name == "FromStart")
        {
            indexShape = new IndexExpressionShape(
                valueExpression,
                false,
                true);
            return true;
        }

        if (invocationOperation.TargetMethod.Name == "FromEnd")
        {
            indexShape = new IndexExpressionShape(
                valueExpression,
                true,
                true);
            return true;
        }

        return false;
    }

    private static bool TryCreateIndexObjectCreationShape(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IndexExpressionShape indexShape)
    {
        indexShape = default;
        if (expression is not ObjectCreationExpressionSyntax objectCreation ||
            semanticModel.GetOperation(objectCreation, cancellationToken) is not IObjectCreationOperation
                objectCreationOperation ||
            objectCreationOperation.Constructor == null ||
            !IsSystemIndexType(objectCreationOperation.Constructor.ContainingType, semanticModel.Compilation) ||
            !TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var valueExpression))
            return false;

        if (!TryGetObjectCreationArgumentExpression(objectCreationOperation, 1, out var fromEndExpression))
        {
            indexShape = new IndexExpressionShape(
                valueExpression,
                false,
                true);
            return true;
        }

        if (!TryGetConstantBool(fromEndExpression, semanticModel, cancellationToken, out var fromEnd)) return false;

        indexShape = new IndexExpressionShape(
            valueExpression,
            fromEnd,
            true);
        return true;
    }

    private static bool ContainsIndexSymbolWrite(
        SyntaxNode node,
        ISymbol indexSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<AssignmentExpressionSyntax>())
            if (IsIndexSymbolReference(assignment.Left, indexSymbol, semanticModel, cancellationToken))
                return true;

        foreach (var argument in CSharpSyntaxFacts.DescendantNodesInExecution(node, includeSelf: false)
                     .OfType<ArgumentSyntax>())
            if ((argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                 argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                IsIndexSymbolReference(argument.Expression, indexSymbol, semanticModel, cancellationToken))
                return true;

        return false;
    }

    private static bool IsIndexSymbolReference(
        ExpressionSyntax expression,
        ISymbol indexSymbol,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsSameSymbol(
            semanticModel.GetSymbolInfo(UnwrapElementAccessIndexExpression(expression), cancellationToken).Symbol,
            indexSymbol);
    }

    private static bool IsSystemIndexExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var typeInfo = semanticModel.GetTypeInfo(expression, cancellationToken);
        return IsSystemIndexType(typeInfo.ConvertedType ?? typeInfo.Type, semanticModel.Compilation);
    }

    private static bool IsSystemIndexType(ITypeSymbol? typeSymbol, Compilation compilation)
    {
        var indexType = compilation.GetTypeByMetadataName("System.Index");
        return typeSymbol != null &&
               indexType != null &&
               SymbolEqualityComparer.Default.Equals(typeSymbol, indexType);
    }

    private static bool TryCreateArrayLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (receiverExpression is ArrayCreationExpressionSyntax arrayCreation)
        {
            if (arrayCreation.Type.RankSpecifiers.Count == 1 &&
                arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
                !arrayCreation.Type.RankSpecifiers[0].Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                TryTranslateValue(
                    arrayCreation.Type.RankSpecifiers[0].Sizes[0],
                    semanticModel,
                    cancellationToken,
                    out var sizeFormula,
                    getSymbolVersion,
                    inlineDepth) &&
                sizeFormula is { Kind: SmtValueKind.Int })
            {
                lengthFormula = sizeFormula;
                return true;
            }

            if (arrayCreation.Initializer != null)
            {
                lengthFormula = new SmtIntegerConstant(arrayCreation.Initializer.Expressions.Count);
                return true;
            }
        }

        if (receiverExpression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
        {
            lengthFormula = new SmtIntegerConstant(implicitArrayCreation.Initializer.Expressions.Count);
            return true;
        }

        if (TryCreateCollectionExpressionLengthFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        if (IsArrayEmptyInvocation(receiverExpression, semanticModel, cancellationToken))
        {
            lengthFormula = new SmtIntegerConstant(0);
            return true;
        }

        lengthFormula = null!;
        return false;
    }

    private static bool TryCreateArrayDimensionLengthFormula(
        ExpressionSyntax receiverExpression,
        int dimension,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (dimension < 0) return false;

        receiverExpression = UnwrapExpression(receiverExpression);
        if (receiverExpression is ArrayCreationExpressionSyntax arrayCreation &&
            arrayCreation.Type.RankSpecifiers.Count == 1 &&
            arrayCreation.Type.RankSpecifiers[0].Sizes.Count > dimension &&
            !arrayCreation.Type.RankSpecifiers[0].Sizes[dimension].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
            TryTranslateValue(
                arrayCreation.Type.RankSpecifiers[0].Sizes[dimension],
                semanticModel,
                cancellationToken,
                out var dimensionSize,
                getSymbolVersion,
                inlineDepth) &&
            dimensionSize is { Kind: SmtValueKind.Int })
        {
            lengthFormula = dimensionSize;
            return true;
        }

        if (TryCreateReferenceCastArrayDimensionLengthFormula(
                receiverExpression,
                dimension,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        var receiverType = semanticModel.GetTypeInfo(receiverExpression, cancellationToken).ConvertedType ??
                           semanticModel.GetTypeInfo(receiverExpression, cancellationToken).Type;
        if (receiverType is not IArrayTypeSymbol arrayType ||
            dimension >= arrayType.Rank ||
            !TryTranslateValue(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var receiverFormula,
                getSymbolVersion,
                inlineDepth) ||
            receiverFormula is not { Kind: SmtValueKind.Reference })
            return false;

        var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        if (!TryCreateMemberFormula(
                receiverFormula,
                "GetLength(" + dimension.ToString(CultureInfo.InvariantCulture) + ")",
                intType,
                out var candidate) ||
            candidate is not { Kind: SmtValueKind.Int })
            return false;

        lengthFormula = candidate;
        return true;
    }

    private static bool TryCreateReferenceCastArrayDimensionLengthFormula(
        ExpressionSyntax receiverExpression,
        int dimension,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (UnwrapExpression(receiverExpression) is not CastExpressionSyntax castExpression ||
            !TryTranslateNonUserDefinedReferenceCastOperand(
                castExpression,
                semanticModel,
                cancellationToken,
                out var operandReference,
                out var targetType,
                getSymbolVersion,
                inlineDepth) ||
            targetType is not IArrayTypeSymbol arrayType ||
            dimension >= arrayType.Rank)
            return false;

        var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        if (TryCreateMemberFormula(
                operandReference,
                "GetLength(" + dimension.ToString(CultureInfo.InvariantCulture) + ")",
                intType,
                out var candidate) &&
            candidate is { Kind: SmtValueKind.Int })
        {
            lengthFormula = candidate;
            return true;
        }

        return false;
    }

    private static bool TryGetConstantNonNegativeInt(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out int value)
    {
        value = 0;
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (!constantValue.HasValue ||
            constantValue.Value == null ||
            !TryGetIntegralConstant(constantValue.Value, out var integralValue) ||
            integralValue < 0 ||
            integralValue > int.MaxValue)
            return false;

        value = (int)integralValue;
        return true;
    }

    private static bool TryGetConstantBool(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out bool value)
    {
        var constantValue = semanticModel.GetConstantValue(expression, cancellationToken);
        if (constantValue.HasValue &&
            constantValue.Value is bool booleanValue)
        {
            value = booleanValue;
            return true;
        }

        value = false;
        return false;
    }

    private static bool TryCreateCollectionExpressionLengthFormula(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (receiverExpression is not CollectionExpressionSyntax collectionExpression)
        {
            lengthFormula = null!;
            return false;
        }

        SmtFormula? current = null;
        var spreadCount = 0;
        foreach (var element in collectionExpression.Elements)
        {
            if (element is ExpressionElementSyntax)
            {
                current = AddLengthTerm(current, new SmtIntegerConstant(1));
                continue;
            }

            if (element is not SpreadElementSyntax spreadElement ||
                ++spreadCount > MaxCollectionExpressionLengthSpreads ||
                !TryCreateCollectionSpreadLengthFormula(
                    spreadElement.Expression,
                    semanticModel,
                    cancellationToken,
                    out var spreadLength,
                    getSymbolVersion,
                    inlineDepth) ||
                spreadLength is not { Kind: SmtValueKind.Int })
            {
                lengthFormula = null!;
                return false;
            }

            current = AddLengthTerm(current, spreadLength);
        }

        lengthFormula = current ?? new SmtIntegerConstant(0);
        return true;
    }

    private static bool TryCreateCollectionSpreadLengthFormula(
        ExpressionSyntax spreadExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        spreadExpression = UnwrapExpression(spreadExpression);
        if (spreadExpression is CollectionExpressionSyntax)
            return TryCreateCollectionExpressionLengthFormula(
                spreadExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth);

        var typeInfo = semanticModel.GetTypeInfo(spreadExpression, cancellationToken);
        var spreadType = typeInfo.ConvertedType ?? typeInfo.Type;
        if (IsSupportedBuiltInLengthReceiver(spreadType) &&
            TryCreateBuiltInElementAccessLengthFormula(
                spreadExpression,
                semanticModel,
                cancellationToken,
                out lengthFormula,
                getSymbolVersion,
                inlineDepth))
            return true;

        return TryCreateKnownCollectionCountLengthFormula(
            spreadExpression,
            spreadType,
            semanticModel,
            cancellationToken,
            out lengthFormula,
            getSymbolVersion,
            inlineDepth);
    }

    private static bool TryCreateKnownCollectionCountLengthFormula(
        ExpressionSyntax receiverExpression,
        ITypeSymbol? receiverType,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula lengthFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        lengthFormula = null!;
        if (receiverType == null ||
            !EnumerateKnownNonNegativeCountInterfaces(receiverType, semanticModel.Compilation).Any() ||
            !TryCreateBuiltInLengthReceiverFormula(
                receiverExpression,
                semanticModel,
                cancellationToken,
                out var receiverFormula,
                getSymbolVersion,
                inlineDepth))
            return false;

        var intType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Int32);
        if (!TryCreateMemberFormula(receiverFormula, "Count", intType, out var countFormula) ||
            countFormula is not { Kind: SmtValueKind.Int })
            return false;

        lengthFormula = countFormula;
        return true;
    }

    private static SmtFormula AddLengthTerm(SmtFormula? current, SmtFormula term)
    {
        if (current == null) return term;

        if (term is SmtIntegerConstant { Value: 0 }) return current;

        if (current is SmtIntegerConstant { Value: 0 }) return term;

        return new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Add, current, term);
    }

    private static bool IsArrayEmptyInvocation(
        ExpressionSyntax receiverExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return receiverExpression is InvocationExpressionSyntax invocation &&
               semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol
               {
                   Name: "Empty",
                   IsStatic: true,
                   ContainingType.SpecialType: SpecialType.System_Array
               };
    }

    private static bool TryCreateEffectiveBuiltInIndexFormula(
        IndexExpressionShape indexShape,
        SmtFormula lengthFormula,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula indexFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        if (!TryTranslateValue(
                indexShape.ValueExpression,
                semanticModel,
                cancellationToken,
                out var rawIndex,
                getSymbolVersion,
                inlineDepth) ||
            rawIndex is not { Kind: SmtValueKind.Int })
        {
            indexFormula = null!;
            return false;
        }

        indexFormula = indexShape.FromEnd
            ? new SmtIntegerBinaryTerm(SmtIntegerBinaryOperator.Subtract, lengthFormula, rawIndex)
            : rawIndex;
        return true;
    }

    private static bool TryCreateIndexShapeWellFormedFormula(
        IndexExpressionShape indexShape,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null!;
        if (!indexShape.RequiresNonNegativeValue) return true;

        if (!TryTranslateValue(
                indexShape.ValueExpression,
                semanticModel,
                cancellationToken,
                out var rawIndex,
                getSymbolVersion,
                inlineDepth) ||
            rawIndex is not { Kind: SmtValueKind.Int })
            return false;

        formula = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            rawIndex,
            new SmtIntegerConstant(0));
        return true;
    }

    private static bool TryCreateRangeShapeWellFormedFormula(
        RangeExpressionShape rangeShape,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null!;
        SmtFormula? startWellFormed = null;
        SmtFormula? endWellFormed = null;
        if (rangeShape.HasStart &&
            !TryCreateIndexShapeWellFormedFormula(
                rangeShape.Start,
                semanticModel,
                cancellationToken,
                out startWellFormed,
                getSymbolVersion,
                inlineDepth))
            return false;

        if (rangeShape.HasEnd &&
            !TryCreateIndexShapeWellFormedFormula(
                rangeShape.End,
                semanticModel,
                cancellationToken,
                out endWellFormed,
                getSymbolVersion,
                inlineDepth))
            return false;

        if (startWellFormed != null) formula = startWellFormed;

        if (endWellFormed != null) formula = CombineConjunction(formula, endWellFormed);

        return true;
    }

    private static SmtFormula CombineConjunction(SmtFormula? left, SmtFormula? right)
    {
        if (left == null) return right ?? new SmtBooleanConstant(true);

        if (right == null) return left;

        return new SmtBinaryFormula(SmtBinaryOperator.And, left, right);
    }

    private static SmtFormula ApplyWellFormedPrecondition(SmtFormula? wellFormed, SmtFormula inRange)
    {
        if (wellFormed == null) return inRange;

        return new SmtBinaryFormula(
            SmtBinaryOperator.Or,
            new SmtUnaryFormula(SmtUnaryOperator.Not, wellFormed),
            inRange);
    }

    private static bool TryCreateBuiltInRangeAccessInRangeFormula(
        ExpressionSyntax argumentExpression,
        SmtFormula lengthFormula,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula formula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        formula = null!;
        if (!TryResolveBuiltInRangeAccessRangeShape(
                argumentExpression,
                semanticModel,
                cancellationToken,
                out var rangeShape) ||
            !TryCreateEffectiveRangeEndpointFormula(
                rangeShape,
                true,
                lengthFormula,
                new SmtIntegerConstant(0),
                semanticModel,
                cancellationToken,
                out var startFormula,
                getSymbolVersion,
                inlineDepth) ||
            !TryCreateEffectiveRangeEndpointFormula(
                rangeShape,
                false,
                lengthFormula,
                lengthFormula,
                semanticModel,
                cancellationToken,
                out var endFormula,
                getSymbolVersion,
                inlineDepth))
            return false;

        var nonNegativeStart = new SmtBinaryFormula(
            SmtBinaryOperator.GreaterThanOrEqual,
            startFormula,
            new SmtIntegerConstant(0));
        var orderedEndpoints = new SmtBinaryFormula(
            SmtBinaryOperator.LessThanOrEqual,
            startFormula,
            endFormula);
        var endWithinLength = new SmtBinaryFormula(
            SmtBinaryOperator.LessThanOrEqual,
            endFormula,
            lengthFormula);
        formula = new SmtBinaryFormula(
            SmtBinaryOperator.And,
            nonNegativeStart,
            new SmtBinaryFormula(SmtBinaryOperator.And, orderedEndpoints, endWithinLength));
        if (!TryCreateRangeShapeWellFormedFormula(
                rangeShape,
                semanticModel,
                cancellationToken,
                out var rangeWellFormed,
                getSymbolVersion,
                inlineDepth))
            return false;

        formula = ApplyWellFormedPrecondition(rangeWellFormed, formula);
        return true;
    }

    private static bool TryCreateEffectiveRangeEndpointFormula(
        RangeExpressionShape rangeShape,
        bool useStart,
        SmtFormula lengthFormula,
        SmtFormula defaultWhenOmitted,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out SmtFormula endpointFormula,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        var hasEndpoint = useStart ? rangeShape.HasStart : rangeShape.HasEnd;
        if (!hasEndpoint)
        {
            endpointFormula = defaultWhenOmitted;
            return true;
        }

        return TryCreateEffectiveBuiltInIndexFormula(
            useStart ? rangeShape.Start : rangeShape.End,
            lengthFormula,
            semanticModel,
            cancellationToken,
            out endpointFormula,
            getSymbolVersion,
            inlineDepth);
    }

    private static ExpressionSyntax UnwrapElementAccessIndexExpression(ExpressionSyntax expression)
    {
        while (true)
        {
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
                continue;
            }

            if (expression is CheckedExpressionSyntax checkedExpression &&
                (checkedExpression.IsKind(SyntaxKind.CheckedExpression) ||
                 checkedExpression.IsKind(SyntaxKind.UncheckedExpression)))
            {
                expression = checkedExpression.Expression;
                continue;
            }

            return expression;
        }
    }

    private static bool IsBuiltInNonNegativeLengthAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (memberAccess.Name.Identifier.ValueText != "Length") return false;

        var memberSymbol = semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol;
        if (memberSymbol is not IPropertySymbol and not IFieldSymbol) return false;

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).ConvertedType ??
                           semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        return IsSupportedBuiltInLengthReceiver(receiverType);
    }

    private static bool IsKnownNonNegativeIntegralMemberAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return IsBuiltInNonNegativeLengthAccess(memberAccess, semanticModel, cancellationToken) ||
               IsKnownNonNegativeCollectionCountAccess(memberAccess, semanticModel, cancellationToken);
    }

    private static bool IsKnownNonNegativeCollectionCountAccess(
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (memberAccess.Name.Identifier.ValueText != "Count" ||
            semanticModel.GetSymbolInfo(memberAccess.Name, cancellationToken).Symbol is not IPropertySymbol
            {
                IsStatic: false,
                Parameters.Length: 0,
                Type.SpecialType: SpecialType.System_Int32
            } propertySymbol)
            return false;

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).ConvertedType ??
                           semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        return IsKnownNonNegativeCollectionCountProperty(propertySymbol, receiverType, semanticModel.Compilation);
    }

    private static bool IsKnownNonNegativeCollectionCountProperty(
        IPropertySymbol propertySymbol,
        ITypeSymbol? receiverType,
        Compilation compilation)
    {
        if (receiverType == null) return false;

        foreach (var interfaceType in EnumerateKnownNonNegativeCountInterfaces(receiverType, compilation))
            foreach (var interfaceCount in interfaceType.GetMembers("Count").OfType<IPropertySymbol>())
            {
                if (interfaceCount is not
                    {
                        IsStatic: false,
                        Parameters.Length: 0,
                        Type.SpecialType: SpecialType.System_Int32
                    })
                    continue;

                if (IsSameSymbol(propertySymbol, interfaceCount)) return true;

                if (receiverType is INamedTypeSymbol namedReceiver &&
                    namedReceiver.FindImplementationForInterfaceMember(interfaceCount) is { } implementation &&
                    implementation.DeclaringSyntaxReferences.Length == 0 &&
                    IsSameSymbol(propertySymbol, implementation))
                    return true;
            }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateKnownNonNegativeCountInterfaces(
        ITypeSymbol receiverType,
        Compilation compilation)
    {
        if (receiverType is INamedTypeSymbol namedReceiver &&
            IsKnownNonNegativeCountInterface(namedReceiver, compilation))
            yield return namedReceiver;

        foreach (var interfaceType in receiverType.AllInterfaces)
            if (IsKnownNonNegativeCountInterface(interfaceType, compilation))
                yield return interfaceType;
    }

    private static bool IsKnownNonNegativeCountInterface(INamedTypeSymbol typeSymbol, Compilation compilation)
    {
        return IsSameOriginalType(typeSymbol, compilation.GetTypeByMetadataName("System.Collections.ICollection")) ||
               IsSameOriginalType(typeSymbol,
                   compilation.GetTypeByMetadataName("System.Collections.Generic.ICollection`1")) ||
               IsSameOriginalType(typeSymbol,
                   compilation.GetTypeByMetadataName("System.Collections.Generic.IReadOnlyCollection`1"));
    }

    private static bool IsSameOriginalType(INamedTypeSymbol candidate, INamedTypeSymbol? target)
    {
        return target != null &&
               SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, target);
    }

    private static bool TryTranslateComparison(
        SyntaxKind kind,
        SmtFormula left,
        SmtFormula right,
        out SmtFormula? formula)
    {
        formula = null;
        switch (kind)
        {
            case SyntaxKind.EqualsExpression:
                if (SymbolicFactFactory.CanCompareSmtValues(left, right))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.Equal, left, right);
                    return true;
                }

                return false;
            case SyntaxKind.NotEqualsExpression:
                if (SymbolicFactFactory.CanCompareSmtValues(left, right))
                {
                    formula = new SmtBinaryFormula(SmtBinaryOperator.NotEqual, left, right);
                    return true;
                }

                return false;
            case SyntaxKind.LessThanExpression:
                return TryCreateIntegralComparison(SmtBinaryOperator.LessThan, left, right, out formula);
            case SyntaxKind.LessThanOrEqualExpression:
                return TryCreateIntegralComparison(SmtBinaryOperator.LessThanOrEqual, left, right, out formula);
            case SyntaxKind.GreaterThanExpression:
                return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThan, left, right, out formula);
            case SyntaxKind.GreaterThanOrEqualExpression:
                return TryCreateIntegralComparison(SmtBinaryOperator.GreaterThanOrEqual, left, right, out formula);
            default:
                return false;
        }
    }

    private static bool TryCreateIntegralComparison(
        SmtBinaryOperator comparison,
        SmtFormula left,
        SmtFormula right,
        out SmtFormula? formula)
    {
        formula = null;
        if (left.Kind != SmtValueKind.Int || right.Kind != SmtValueKind.Int) return false;

        formula = new SmtBinaryFormula(comparison, left, right);
        return true;
    }

    private static ISet<string>? AddNonZeroDivisorFacts(
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ISet<string>? currentFacts,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        var facts = currentFacts == null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(currentFacts, StringComparer.Ordinal);
        var initialCount = facts.Count;
        CollectNonZeroDivisorFacts(
            condition,
            branchWhenTrue,
            semanticModel,
            cancellationToken,
            facts,
            getSymbolVersion,
            inlineDepth);

        return facts.Count == initialCount ? currentFacts : facts;
    }

    private static void CollectNonZeroDivisorFacts(
        ExpressionSyntax condition,
        bool branchWhenTrue,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ISet<string> facts,
        Func<ISymbol, int>? getSymbolVersion,
        int inlineDepth)
    {
        condition = UnwrapExpression(condition);

        if (condition is PrefixUnaryExpressionSyntax prefixUnary &&
            prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            CollectNonZeroDivisorFacts(
                prefixUnary.Operand,
                !branchWhenTrue,
                semanticModel,
                cancellationToken,
                facts,
                getSymbolVersion,
                inlineDepth);
            return;
        }

        if (condition is not BinaryExpressionSyntax binaryExpression) return;

        if (branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalAndExpression))
        {
            CollectNonZeroDivisorFacts(binaryExpression.Left, true, semanticModel, cancellationToken, facts,
                getSymbolVersion, inlineDepth);
            CollectNonZeroDivisorFacts(binaryExpression.Right, true, semanticModel, cancellationToken, facts,
                getSymbolVersion, inlineDepth);
            return;
        }

        if (!branchWhenTrue && binaryExpression.IsKind(SyntaxKind.LogicalOrExpression))
        {
            CollectNonZeroDivisorFacts(binaryExpression.Left, false, semanticModel, cancellationToken, facts,
                getSymbolVersion, inlineDepth);
            CollectNonZeroDivisorFacts(binaryExpression.Right, false, semanticModel, cancellationToken, facts,
                getSymbolVersion, inlineDepth);
            return;
        }

        if (!IsNonZeroComparisonKind(binaryExpression.Kind(), branchWhenTrue)) return;

        if (!TryGetZeroComparisonCandidate(
                binaryExpression.Left,
                binaryExpression.Right,
                semanticModel,
                cancellationToken,
                out var candidate))
            return;

        if (TryTranslateValue(candidate, semanticModel, cancellationToken, out var candidateFormula, getSymbolVersion,
                inlineDepth) &&
            candidateFormula is { Kind: SmtValueKind.Int } &&
            !IsZeroIntegerConstant(candidateFormula))
            facts.Add(CreateDivisorKey(candidateFormula));
    }

    private static bool IsNonZeroComparisonKind(SyntaxKind kind, bool branchWhenTrue)
    {
        return branchWhenTrue
            ? kind is SyntaxKind.NotEqualsExpression or SyntaxKind.LessThanExpression
                or SyntaxKind.GreaterThanExpression
            : kind is SyntaxKind.EqualsExpression or SyntaxKind.LessThanOrEqualExpression
                or SyntaxKind.GreaterThanOrEqualExpression;
    }

    private static bool TryGetZeroComparisonCandidate(
        ExpressionSyntax left,
        ExpressionSyntax right,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ExpressionSyntax candidate)
    {
        if (IsZeroIntegralExpression(right, semanticModel, cancellationToken))
        {
            candidate = left;
            return true;
        }

        if (IsZeroIntegralExpression(left, semanticModel, cancellationToken))
        {
            candidate = right;
            return true;
        }

        candidate = null!;
        return false;
    }

    private static bool IsZeroIntegralExpression(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        return TryTranslateValue(expression, semanticModel, cancellationToken, out var formula, null) &&
               IsZeroIntegerConstant(formula);
    }

    private static bool IsZeroIntegerConstant(SmtFormula? formula)
    {
        return formula is SmtIntegerConstant integerConstant && integerConstant.Value == 0;
    }

    private static string CreateDivisorKey(SmtFormula formula)
    {
        return formula.ToString();
    }
}
