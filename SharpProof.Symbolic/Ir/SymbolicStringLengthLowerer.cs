namespace SharpProof.Symbolic.Ir;
internal static class SymbolicStringLengthLowerer {
    internal static bool TryLowerStringCreationResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (expression is not ObjectCreationExpressionSyntax creationSyntax ||
            context.SemanticModel.GetOperation(creationSyntax, context.CancellationToken) is not
                IObjectCreationOperation { Constructor: { ContainingType.SpecialType: SpecialType.System_String } constructor }
                creation)
            return false;
        var parameters = constructor.Parameters;
        if (parameters.Length == 2 &&
            parameters[0].Type.SpecialType == SpecialType.System_Char &&
            parameters[1].Type.SpecialType == SpecialType.System_Int32)
            return TryLowerIntegerArgument(creation.Arguments, 1, context, out term);
        if (parameters.Length == 3 &&
            SymbolicTypeFacts.IsCharArrayType(parameters[0].Type) &&
            parameters[1].Type.SpecialType == SpecialType.System_Int32 &&
            parameters[2].Type.SpecialType == SpecialType.System_Int32)
            return TryLowerIntegerArgument(creation.Arguments, 2, context, out term);
        if (parameters.Length != 1 ||
            !SymbolicTypeFacts.IsCharArrayType(parameters[0].Type) &&
            !SymbolicTypeFacts.IsReadOnlySpanOfCharType(parameters[0].Type) ||
            !TryGetArgument(creation.Arguments, 0, out var source))
            return false;
        return SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(source, context, out term);
    }
    internal static bool TryLowerStringInvocationResultLengthTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (!SymbolicIndexingLowerer.TryGetInvocationOperation(expression, context, out var invocation, out var operation) ||
            operation is not {
                TargetMethod: {
                    IsStatic: false,
                    ContainingType.SpecialType: SpecialType.System_String,
                    ReturnType.SpecialType: SpecialType.System_String
                } method,
                Instance.Syntax: ExpressionSyntax sourceExpression
            })
            return false;
        if (method.Name == nameof(string.Remove) &&
            SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(sourceExpression, context, out var sourceLength)) {
            var ordinal = method.Parameters.Length == 1 ? 0 : 1;
            if (method.Parameters.Length is not (1 or 2) ||
                !TryLowerIntegerArgument(operation.Arguments, ordinal, context, out var removed))
                return false;
            term = method.Parameters.Length == 1
                ? removed
                : new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Subtract, sourceLength, removed);
            return true;
        }
        if (method.Name == nameof(string.Insert) &&
            method.Parameters.Length == 2 &&
            method.Parameters[1].Type.SpecialType == SpecialType.System_String &&
            SymbolicStringLowerer.TryLowerStringTerm(sourceExpression, context, out var insertSource) &&
            TryLowerIntegerArgument(operation.Arguments, 0, context, out _) &&
            TryGetArgument(operation.Arguments, 1, out var valueExpression) &&
            SymbolicStringLowerer.TryLowerStringTerm(valueExpression, context, out var value)) {
            term = CreateStringResultLengthTerm(
                new SymbolicStringConcatTerm(insertSource, value),
                invocation,
                "ir.known-api.string.insert.length");
            return true;
        }
        if (method.Name is nameof(string.PadLeft) or nameof(string.PadRight) &&
            method.Parameters.Length is 1 or 2 &&
            (method.Parameters.Length == 1 ||
             method.Parameters[1].Type.SpecialType == SpecialType.System_Char) &&
            SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(sourceExpression, context, out var padSourceLength) &&
            TryLowerIntegerArgument(operation.Arguments, 0, context, out var width)) {
            term = new SymbolicConditionalTerm(
                SymbolicIrLowerer.CreateRelationCondition(
                    SymbolicRelationOperator.GreaterThan,
                    width,
                    padSourceLength,
                    invocation,
                    "ir.known-api.string.pad-width"),
                width,
                padSourceLength);
            return true;
        }
        return false;
    }
    private static bool TryLowerIntegerArgument(
        ImmutableArray<IArgumentOperation> arguments,
        int ordinal,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        return TryGetArgument(arguments, ordinal, out var expression) &&
               SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out term) &&
               term.Kind == SmtValueKind.Int;
    }
    private static bool TryGetArgument(
        ImmutableArray<IArgumentOperation> arguments,
        int ordinal,
        out ExpressionSyntax expression) {
        foreach (var argument in arguments)
            if (argument.Parameter?.Ordinal == ordinal && argument.Value.Syntax is ExpressionSyntax value) {
                expression = value;
                return true;
            }
        expression = null!;
        return false;
    }
    internal static SymbolicTerm CreateStringResultLengthTerm(
        SymbolicTerm value,
        SyntaxNode source,
        string provenance) {
        if (value is SymbolicStringConstantTerm constant)
            return new SymbolicIntegerConstantTerm(constant.Value.Length);
        if (value is SymbolicStringSliceTerm slice) return slice.Length;
        if (value is not SymbolicStringConcatTerm concat) return new SymbolicLengthTerm(value);
        return SymbolicIrLowerer.CreateOverflowAwareBinaryTerm(
            new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Add,
                CreateStringResultLengthTerm(concat.Left, source, provenance + ".left"),
                CreateStringResultLengthTerm(concat.Right, source, provenance + ".right")),
            int.MinValue,
            int.MaxValue,
            source,
            provenance + ".sum",
            false);
    }
    internal static bool TryLowerStringResultLengthIdentityCondition(
        BinaryExpressionSyntax binary,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if ((!TryLowerConstructionLengthSum(binary.Left, context, out var constructed) ||
             !TryLowerNonNegativeLengthSum(binary.Right, context, out var compared)) &&
            (!TryLowerConstructionLengthSum(binary.Right, context, out constructed) ||
             !TryLowerNonNegativeLengthSum(binary.Left, context, out compared)) ||
            SymbolicState.CreateProofTermKey(constructed) != SymbolicState.CreateProofTermKey(compared))
            return false;
        condition = new SymbolicConstantCondition(binary.IsKind(SyntaxKind.EqualsExpression));
        return true;
    }
    private static bool TryLowerConstructionLengthSum(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is MemberAccessExpressionSyntax member &&
            member.Name.Identifier.ValueText == nameof(string.Length) &&
            SymbolicStringLowerer.TryLowerStringTerm(member.Expression, context, out var value) &&
            value is SymbolicStringConcatTerm) {
            term = CreateMathematicalLength(value);
            return true;
        }
        term = null!;
        return false;
    }
    private static SymbolicTerm CreateMathematicalLength(SymbolicTerm value) {
        if (value is SymbolicStringConstantTerm constant)
            return new SymbolicIntegerConstantTerm(constant.Value.Length);
        if (value is SymbolicConditionalTerm {
            WhenTrue: SymbolicStringConstantTerm { Value.Length: 0 },
            WhenFalse: { Kind: SmtValueKind.String } nonNull
        })
            return CreateMathematicalLength(nonNull);
        return value is SymbolicStringConcatTerm concat
            ? new SymbolicBinaryTerm(
                SymbolicBinaryTermOperator.Add,
                CreateMathematicalLength(concat.Left),
                CreateMathematicalLength(concat.Right))
            : new SymbolicLengthTerm(value);
    }
    private static bool TryLowerNonNegativeLengthSum(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is BinaryExpressionSyntax binary &&
            binary.IsKind(SyntaxKind.AddExpression) &&
            context.SemanticModel.GetOperation(binary, context.CancellationToken) is IBinaryOperation { OperatorMethod: null } &&
            TryLowerNonNegativeLengthSum(binary.Left, context, out var left) &&
            TryLowerNonNegativeLengthSum(binary.Right, context, out var right)) {
            term = new SymbolicBinaryTerm(SymbolicBinaryTermOperator.Add, left, right);
            return true;
        }
        var constant = context.SemanticModel.GetConstantValue(expression, context.CancellationToken);
        if (constant is { HasValue: true, Value: { } raw } &&
            SymbolicLoweringValueFacts.TryGetIntegralConstant(raw, out var integer) &&
            integer >= 0) {
            term = new SymbolicIntegerConstantTerm(integer);
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
    internal static ITypeSymbol? GetPreferredLengthSemanticType(
        ExpressionSyntax expression,
        SymbolicLoweringContext context)
        => SymbolicIndexingLowerer.GetPreferredLengthSemanticType(expression, context);
}
