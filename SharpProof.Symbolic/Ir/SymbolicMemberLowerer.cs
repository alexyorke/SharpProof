namespace SharpProof.Symbolic.Ir;

internal static class SymbolicMemberLowerer
{
    internal static bool TryGetInstanceMemberSymbol(
        SyntaxNode syntax,
        SymbolicLoweringContext context,
        out ISymbol memberSymbol)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(syntax, context.CancellationToken).Symbol;
        switch (symbol)
        {
            case IPropertySymbol { IsStatic: false } property:
                memberSymbol = property;
                return true;
            case IFieldSymbol { IsStatic: false } field:
                memberSymbol = field;
                return true;
        }

        switch (context.SemanticModel.GetOperation(syntax, context.CancellationToken))
        {
            case IPropertyReferenceOperation { Property.IsStatic: false } propertyReference:
                memberSymbol = propertyReference.Property;
                return true;
            case IFieldReferenceOperation { Field.IsStatic: false } fieldReference:
                memberSymbol = fieldReference.Field;
                return true;
        }

        memberSymbol = null!;
        return false;
    }

    internal static bool TryLowerImplicitThisMemberTerm(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (expression is not IdentifierNameSyntax ||
            !TryGetInstanceMemberSymbol(expression, context, out var memberSymbol) ||
            !SymbolicTypeLowerer.TryGetSymbolType(memberSymbol, out var memberType) ||
            !SymbolicTypeLowerer.TryGetValueKind(memberType, out var memberKind))
            return false;

        term = new SymbolicMemberTerm(
            context.ImplicitThis,
            memberSymbol.Name,
            memberKind);
        return true;
    }

    internal static bool TryLowerMemberTerm(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;

        var memberName = memberAccess.Name.Identifier.ValueText;
        if (SymbolicKnownApiLowerer.TryLowerKnownStaticValueMember(memberAccess, context, out term)) return true;

        var receiverType = context.SemanticModel.GetTypeInfo(memberAccess.Expression, context.CancellationToken).Type;
        if (SymbolicTupleLowerer.TryLowerTupleElementMemberTerm(memberAccess, context, out term)) return true;

        if (context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is
                IPropertySymbol propertySymbol &&
            TryLowerSourceBooleanPropertyTerm(memberAccess, propertySymbol, context, out term))
            return true;

        if (string.Equals(memberName, nameof(Array.Rank), StringComparison.Ordinal) &&
            receiverType is IArrayTypeSymbol { Rank: > 0 } arrayType)
        {
            term = new SymbolicIntegerConstantTerm(arrayType.Rank);
            return true;
        }

        if (string.Equals(memberName, "HasValue", StringComparison.Ordinal) &&
            SymbolicNullableLowerer.TryLowerNullableHasValueTerm(memberAccess.Expression, context, out term))
            return true;

        if (string.Equals(memberName, "Value", StringComparison.Ordinal) &&
            SymbolicNullableLowerer.TryLowerNullableValueTerm(memberAccess.Expression, context, out term))
            return true;

        if (string.Equals(memberName, nameof(string.Length), StringComparison.Ordinal))
        {
            if (receiverType?.SpecialType == SpecialType.System_String ||
                receiverType is IArrayTypeSymbol ||
                SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(receiverType))
                return SymbolicIndexingLowerer.TryLowerBuiltInLengthTerm(memberAccess.Expression, context, out term);
        }

        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(memberAccess.Expression, context), out var receiver)) return false;

        if (string.Equals(memberName, "Count", StringComparison.Ordinal) &&
            receiver.Kind == SmtValueKind.Reference &&
            context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol is
                IPropertySymbol countProperty &&
            SymbolicTypeFacts.IsKnownNonNegativeCollectionCountProperty(
                countProperty,
                receiverType,
                context.Compilation))
        {
            term = new SymbolicCountTerm(receiver);
            return true;
        }

        if (TryGetInstanceMemberValueKind(memberAccess, context, out var memberKind) &&
            receiver.Kind == SmtValueKind.Reference)
        {
            term = new SymbolicMemberTerm(receiver, memberName, memberKind);
            return true;
        }

        return false;
    }

    private static bool TryLowerSourceBooleanPropertyTerm(
        MemberAccessExpressionSyntax memberAccess,
        IPropertySymbol propertySymbol,
        SymbolicLoweringContext context,
        out SymbolicTerm term)
    {
        term = null!;
        if (!TryLowerSourceBooleanPropertyCondition(
                memberAccess,
                propertySymbol,
                context,
                out var returnedCondition))
            return false;

        term = new SymbolicConditionalTerm(
            returnedCondition,
            new SymbolicBooleanConstantTerm(true),
            new SymbolicBooleanConstantTerm(false));
        return true;
    }

    internal static bool TryLowerSourceBooleanPropertyCondition(
        MemberAccessExpressionSyntax memberAccess,
        IPropertySymbol propertySymbol,
        SymbolicLoweringContext context,
        out SymbolicCondition condition)
    {
        condition = null!;
        if (context.InlineDepth >= SymbolicLoweringContext.MaxSourcePredicateInlineDepth ||
            propertySymbol is not
            {
                IsStatic: false,
                IsIndexer: false,
                Type.SpecialType: SpecialType.System_Boolean,
                GetMethod: { } getter
            } ||
            (getter.DeclaringSyntaxReferences.Length == 0 &&
             propertySymbol.DeclaringSyntaxReferences.Length == 0) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(memberAccess.Expression, context), out var receiver) ||
            receiver.Kind != SmtValueKind.Reference)
            return false;

        var substitutions = new Dictionary<ISymbol, SymbolicTerm>(SymbolEqualityComparer.Default);
        if (getter.DeclaringSyntaxReferences.Length > 0 &&
            SymbolicSourcePredicateLowerer.TryLowerReturnedBoolean(getter, context, substitutions, receiver, out condition))
            return true;

        return SymbolicSourcePredicateLowerer.TryLowerReturnedBoolean(propertySymbol, context, receiver, out condition);
    }

    private static bool TryGetInstanceMemberValueKind(
        MemberAccessExpressionSyntax memberAccess,
        SymbolicLoweringContext context,
        out SmtValueKind kind)
    {
        var symbol = context.SemanticModel.GetSymbolInfo(memberAccess, context.CancellationToken).Symbol;
        if (symbol is IPropertySymbol { IsStatic: false } property &&
            SymbolicTypeLowerer.TryGetValueKind(property.Type, out kind))
            return true;

        if (symbol is IFieldSymbol { IsStatic: false } field &&
            SymbolicTypeLowerer.TryGetValueKind(field.Type, out kind))
            return true;

        kind = default;
        return false;
    }

}
