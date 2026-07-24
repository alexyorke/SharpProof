namespace SharpProof.Symbolic.Ir;
internal static class SymbolicReferenceLowerer {
    internal static bool TryLowerReferenceNullCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        expression = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
        if (CSharpSyntaxFacts.IsNullLiteral(expression) ||
            context.SemanticModel.GetConstantValue(expression, context.CancellationToken) is { HasValue: true, Value: null }) {
            condition = new SymbolicConstantCondition(true);
            return true;
        }
        if (NullableFlowFacts.IsDefinitelyNotNullReferenceValue(
                expression,
                context.SemanticModel,
                context.CancellationToken)) {
            condition = new SymbolicConstantCondition(false);
            return true;
        }
        if (expression is ConditionalExpressionSyntax conditional &&
            SymbolicIrLowerer.LowerCondition(conditional.Condition, context) is { } branch &&
            TryLowerReferenceNullCondition(conditional.WhenTrue, context, out var whenTrue) &&
            TryLowerReferenceNullCondition(conditional.WhenFalse, context, out var whenFalse)) {
            condition = Select(branch, whenTrue, whenFalse);
            return true;
        }
        if (expression is BinaryExpressionSyntax coalesce &&
            coalesce.IsKind(SyntaxKind.CoalesceExpression) &&
            TryLowerReferenceNullCondition(coalesce.Left, context, out var left) &&
            TryLowerReferenceNullCondition(coalesce.Right, context, out var right)) {
            condition = new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right);
            return true;
        }
        if (SymbolicIrLowerer.TryLowerReferenceTerm(expression, context, out var reference)) {
            condition = SymbolicIrLowerer.CreateReferenceNullCondition(
                reference,
                true,
                expression,
                "ir.reference.null-state");
            return true;
        }
        condition = null!;
        return false;
    }
    internal static SymbolicCondition CreateEquivalentCondition(
        SymbolicCondition left,
        SymbolicCondition right) =>
        new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, left, right),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicNotCondition(left),
                new SymbolicNotCondition(right)));
    private static SymbolicCondition Select(
        SymbolicCondition branch,
        SymbolicCondition whenTrue,
        SymbolicCondition whenFalse) =>
        new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, branch, whenTrue),
            new SymbolicBinaryCondition(
                SymbolicConditionOperator.And,
                new SymbolicNotCondition(branch),
                whenFalse));
    internal static bool TryLowerReferenceConditionalAccessTerm(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        var resultType =
            context.SemanticModel.GetTypeInfo(conditionalAccess, context.CancellationToken).ConvertedType ??
            context.SemanticModel.GetTypeInfo(conditionalAccess, context.CancellationToken).Type;
        if (resultType is not { IsReferenceType: true } ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(conditionalAccess.Expression, context), out var receiver) ||
            receiver.Kind != SmtValueKind.Reference ||
            !TryLowerConditionalAccessWhenNotNullReferenceTerm(conditionalAccess, receiver, resultType, context, out var whenNotNull))
            return false;
        term = new SymbolicConditionalTerm(
            SymbolicIrLowerer.CreateReferenceNullCondition(
                receiver,
                false,
                conditionalAccess.Expression,
                "ir.conditional-access.receiver-not-null"),
            whenNotNull,
            new SymbolicNullTerm());
        return true;
    }
    private static bool TryLowerConditionalAccessWhenNotNullReferenceTerm(
        ConditionalAccessExpressionSyntax conditionalAccess,
        SymbolicTerm receiver,
        ITypeSymbol expectedType,
        SymbolicLoweringContext context,
        out SymbolicTerm term) {
        term = null!;
        if (conditionalAccess.WhenNotNull is MemberBindingExpressionSyntax memberBinding) {
            if (!SymbolicMemberLowerer.TryGetInstanceMemberSymbol(memberBinding, context, out var memberSymbol) ||
                !SymbolicTypeLowerer.TryGetSymbolType(memberSymbol, out var memberType) ||
                !SymbolEqualityComparer.Default.Equals(memberType, expectedType) ||
                !SymbolicTypeLowerer.TryGetValueKind(memberType, out var memberKind) ||
                memberKind != SmtValueKind.Reference)
                return false;
            term = new SymbolicMemberTerm(receiver, memberSymbol.Name, memberKind);
            return true;
        }
        if (conditionalAccess.WhenNotNull is ElementBindingExpressionSyntax elementBinding &&
            elementBinding.ArgumentList.Arguments.Count == 1 &&
            context.SemanticModel.GetTypeInfo(conditionalAccess.Expression, context.CancellationToken).Type is
                IArrayTypeSymbol { Rank: 1 } arrayType &&
            SymbolEqualityComparer.Default.Equals(arrayType.ElementType, expectedType) &&
            SymbolicTypeLowerer.TryGetValueKind(arrayType.ElementType, out var elementKind) &&
            elementKind == SmtValueKind.Reference &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(elementBinding.ArgumentList.Arguments[0].Expression, context),
                out var index) &&
            index.Kind == SmtValueKind.Int) {
            term = new SymbolicElementTerm(receiver, index, elementKind);
            return true;
        }
        return false;
    }
}
