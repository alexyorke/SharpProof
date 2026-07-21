namespace SharpProof.Symbolic.Ir;

internal static class SymbolicStringLengthLowerer {
    internal static bool TryLowerStringCreationResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
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
            SymbolicIndexingLowerer.TryGetObjectCreationArgumentExpression(objectCreationOperation, 1, out var countExpression) &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(countExpression, context), out term) &&
            term.Kind == SmtValueKind.Int)
            return true;

        if (constructor.Parameters.Length == 1 &&
            SymbolicTypeFacts.IsCharArrayType(constructor.Parameters[0].Type) &&
            SymbolicIndexingLowerer.TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var charArrayExpression))
            return SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(charArrayExpression, context, out term);

        if (constructor.Parameters.Length == 3 &&
            SymbolicTypeFacts.IsCharArrayType(constructor.Parameters[0].Type) &&
            constructor.Parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
            constructor.Parameters[2].Type.SpecialType == SpecialType.System_Int32 &&
            SymbolicIndexingLowerer.TryGetObjectCreationArgumentExpression(objectCreationOperation, 2, out var lengthExpression) &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(lengthExpression, context), out term) &&
            term.Kind == SmtValueKind.Int)
            return true;

        if (constructor.Parameters.Length == 1 &&
            SymbolicTypeFacts.IsReadOnlySpanOfCharType(constructor.Parameters[0].Type) &&
            SymbolicIndexingLowerer.TryGetObjectCreationArgumentExpression(objectCreationOperation, 0, out var spanExpression))
            return SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(spanExpression, context, out term);

        term = null!;
        return false;
    }

    internal static bool TryLowerStringInvocationResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!SymbolicIndexingLowerer.TryGetInvocationOperation(expression, context, out var invocationExpression, out var invocationOperation))
            return false;

        var method = invocationOperation.TargetMethod;
        if (method.IsStatic ||
            method.ContainingType?.SpecialType != SpecialType.System_String ||
            method.ReturnType.SpecialType != SpecialType.System_String ||
            invocationOperation.Instance?.Syntax is not ExpressionSyntax sourceExpression)
            return false;

        // Substring is not handled here: it lowers to a slice term in SymbolicStringLowerer,
        // and CreateStringResultLengthTerm projects that slice's requested length.

        if (string.Equals(method.Name, nameof(string.Remove), StringComparison.Ordinal)) {
            if (!SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(sourceExpression, context, out var sourceLength)) return false;

            if (method.Parameters.Length == 1 &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0,
                    out var startExpression) &&
                SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(startExpression, context), out var start) &&
                start.Kind == SmtValueKind.Int) {
                term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, start);
                return true;
            }

            if (method.Parameters.Length == 2 &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var _) &&
                SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 1,
                    out var countExpression) &&
                SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(countExpression, context), out var count) &&
                count.Kind == SmtValueKind.Int) {
                term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, count);
                return true;
            }

            return false;
        }

        if (string.Equals(method.Name, nameof(string.Insert), StringComparison.Ordinal) &&
            method.Parameters.Length == 2 &&
            method.Parameters[1].Type.SpecialType == SpecialType.System_String &&
            SymbolicStringLowerer.TryLowerStringTerm(sourceExpression, context, out var insertSource) &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var indexExpression) &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(indexExpression, context), out var index) &&
            index.Kind == SmtValueKind.Int &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 1, out var valueExpression) &&
            SymbolicStringLowerer.TryLowerStringTerm(valueExpression, context, out var value)) {
            // Insert changes content position but has the same length as a
            // successfully completed concatenation. Keeping the result as a
            // length projection also carries the CLR Int32 length domain.
            term = CreateStringResultLengthTerm(
                new SymbolicStringConcatTerm(insertSource, value),
                invocationExpression,
                "ir.known-api.string.insert.length");
            return true;
        }

        if (method.Name is nameof(string.PadLeft) or nameof(string.PadRight) &&
            (method.Parameters.Length == 1 ||
             method.Parameters.Length == 2 && method.Parameters[1].Type.SpecialType == SpecialType.System_Char) &&
            SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(sourceExpression, context, out var padSourceLength) &&
            SymbolicValueFacts.TryGetInvocationArgumentExpression(invocationOperation, 0, out var widthExpression) &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(widthExpression, context), out var width) &&
            width.Kind == SmtValueKind.Int) {
            term = new SymbolicConditionalTerm(
                SymbolicIrLowerer.CreateRelationCondition(
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

    internal static SymbolicTerm CreateStringResultLengthTerm(
        SymbolicTerm stringValue,
        SyntaxNode source,
        string provenance) {
        if (stringValue is SymbolicStringConstantTerm constant)
            return new SymbolicIntegerConstantTerm(constant.Value.Length);

        // A slice reaches here only on a path where the call completed, so its length is
        // the length that was asked for. Projecting it keeps this exact, where deferring
        // to the solver's total substring would only yield an upper bound.
        if (stringValue is SymbolicStringSliceTerm slice)
            return slice.Length;

        if (stringValue is not SymbolicStringConcatTerm concat)
            return new SymbolicLengthTerm(stringValue);

        var leftLength = CreateStringResultLengthTerm(concat.Left, source, provenance + ".left");
        var rightLength = CreateStringResultLengthTerm(concat.Right, source, provenance + ".right");
        return SymbolicIrLowerer.CreateOverflowAwareBinaryTerm(
            new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, leftLength, rightLength),
            int.MinValue,
            int.MaxValue,
            source,
            provenance + ".sum",
            false);
    }

    internal static bool TryLowerStringResultLengthIdentityCondition(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if ((!TryLowerStringConstructionLengthSum(binaryExpression.Left, context, out var constructedLength) ||
             !TryLowerNonNegativeLengthSum(binaryExpression.Right, context, out var comparedLength)) &&
            (!TryLowerStringConstructionLengthSum(binaryExpression.Right, context, out constructedLength) ||
             !TryLowerNonNegativeLengthSum(binaryExpression.Left, context, out comparedLength)))
            return false;

        if (!string.Equals(
                SymbolicState.CreateProofTermKey(constructedLength),
                SymbolicState.CreateProofTermKey(comparedLength),
                StringComparison.Ordinal))
            return false;

        condition = new SymbolicConstantCondition(binaryExpression.IsKind(SyntaxKind.EqualsExpression));
        return true;
    }

    private static bool TryLowerStringConstructionLengthSum(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is not MemberAccessExpressionSyntax memberAccess ||
            !string.Equals(memberAccess.Name.Identifier.ValueText, nameof(string.Length), StringComparison.Ordinal) ||
            !SymbolicStringLowerer.TryLowerStringTerm(memberAccess.Expression, context, out var stringValue) ||
            stringValue is not SymbolicStringConcatTerm) {
            term = null!;
            return false;
        }

        term = CreateMathematicalStringLengthSum(stringValue);
        return true;
    }

    private static SymbolicTerm CreateMathematicalStringLengthSum(SymbolicTerm stringValue) {
        if (stringValue is SymbolicStringConstantTerm constant)
            return new SymbolicIntegerConstantTerm(constant.Value.Length);

        if (stringValue is SymbolicConditionalTerm {
            WhenTrue: SymbolicStringConstantTerm { Value.Length: 0 },
            WhenFalse: { Kind: SmtValueKind.String } nonNullValue
        })
            return CreateMathematicalStringLengthSum(nonNullValue);

        if (stringValue is not SymbolicStringConcatTerm concat)
            return new SymbolicLengthTerm(stringValue);

        return new SymbolicBinaryTerm(
            SymbolicBinaryTermOperator.Add,
            CreateMathematicalStringLengthSum(concat.Left),
            CreateMathematicalStringLengthSum(concat.Right));
    }

    private static bool TryLowerNonNegativeLengthSum(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression) &&
            context.SemanticModel.GetOperation(binary, context.CancellationToken) is
                IBinaryOperation { OperatorMethod: null } &&
            TryLowerNonNegativeLengthSum(binary.Left, context, out var left) &&
            TryLowerNonNegativeLengthSum(binary.Right, context, out var right)) {
            term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, left, right);
            return true;
        }

        var constantValue = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constantValue.HasValue &&
            constantValue.Value != null &&
            SymbolicLoweringValueFacts.TryGetIntegralConstant(constantValue.Value, out var integerConstant) &&
            integerConstant >= 0) {
            term = new SymbolicIntegerConstantTerm(integerConstant);
            return true;
        }

        if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out var length) &&
            (length is SymbolicLengthTerm or SymbolicArrayDimensionLengthTerm or SymbolicCountTerm ||
             length is SymbolicIntegerConstantTerm { Value: >= 0 })) {
            term = length;
            return true;
        }

        term = null!;
        return false;
    }

    internal static bool IsMemoryExtensionsViewMethod(IMethodSymbol method) {
        var definition = method.ReducedFrom ?? method;
        return definition.Name is "AsSpan" or "AsMemory" &&
               definition.IsExtensionMethod &&
               definition.ContainingType?.ToDisplayString() == "System.MemoryExtensions";
    }

    internal static bool TryGetMemoryExtensionsViewSourceExpression(
        InvocationExpressionSyntax invocationExpression,
        SymbolicLoweringContext context,
        out ExpressionSyntax sourceExpression,
        out int firstArgumentIndex) {
        if (invocationExpression.Expression is MemberAccessExpressionSyntax memberAccess &&
            context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type != null) {
            sourceExpression = memberAccess.Expression;
            firstArgumentIndex = 0;
            return true;
        }

        if (invocationExpression.ArgumentList.Arguments.Count == 0) {
            sourceExpression = null!;
            firstArgumentIndex = 0;
            return false;
        }

        sourceExpression = invocationExpression.ArgumentList.Arguments[0].Expression;
        firstArgumentIndex = 1;
        return true;
    }

    internal static bool IsSupportedMemoryExtensionsViewSource(
        ExpressionSyntax sourceExpression,
        SymbolicLoweringContext context) {
        var sourceTypeInfo = context.SemanticModel.GetTypeInfo(sourceExpression, context.CancellationToken);
        var sourceType = PreferLengthSemanticType(sourceTypeInfo.Type, sourceTypeInfo.ConvertedType);
        return sourceType?.SpecialType == SpecialType.System_String ||
               sourceType is IArrayTypeSymbol { Rank: 1 };
    }

    internal static ITypeSymbol? GetPreferredLengthSemanticType(
        ExpressionSyntax expression,
        SymbolicLoweringContext context) {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        return PreferLengthSemanticType(typeInfo.Type, typeInfo.ConvertedType);
    }

    private static ITypeSymbol? PreferLengthSemanticType(
        ITypeSymbol? sourceType,
        ITypeSymbol? convertedType) {
        if (sourceType != null &&
            HasLengthLikeShape(sourceType) &&
            !HasLengthLikeShape(convertedType))
            return sourceType;

        return convertedType ?? sourceType;
    }

    private static bool HasLengthLikeShape(ITypeSymbol? type) => type?.SpecialType == SpecialType.System_String ||
               type is IArrayTypeSymbol { Rank: >= 1 } ||
               SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(type) ||
               SymbolicIndexingLowerer.HasCountBackedIntIndexer(type) ||
               SymbolicTypeFacts.HasInstanceInt32Member(type, "Count");

}
