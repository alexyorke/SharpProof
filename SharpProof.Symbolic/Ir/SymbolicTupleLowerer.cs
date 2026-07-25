namespace SharpProof.Symbolic.Ir;
internal static class SymbolicTupleLowerer {
    internal static bool TryLowerTupleEqualityCondition(
        BinaryExpressionSyntax binaryExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (TupleComparisonUsesUserDefinedElementOperator(binaryExpression.Left, context) ||
            TupleComparisonUsesUserDefinedElementOperator(binaryExpression.Right, context) ||
            !TryLowerTupleElementTerms(binaryExpression.Left, context, out var leftElements) ||
            !TryLowerTupleElementTerms(binaryExpression.Right, context, out var rightElements) ||
            leftElements.Length == 0 ||
            leftElements.Length != rightElements.Length)
            return false;
        SymbolicCondition? equality = null;
        for (var index = 0; index < leftElements.Length; index++) {
            if (!SymbolicOperatorLowerer.CanCompareTerms(leftElements[index], rightElements[index], SymbolicRelationOperator.Equal))
                return false;
            var elementEquality = SymbolicIrLowerer.CreateRelationCondition(
                SymbolicRelationOperator.Equal,
                leftElements[index],
                rightElements[index],
                binaryExpression,
                "ir.tuple.equality.element");
            equality = equality == null
                ? elementEquality
                : new SymbolicBinaryCondition(SymbolicConditionOperator.And, equality, elementEquality);
        }
        condition = binaryExpression.IsKind(SyntaxKind.EqualsExpression)
            ? equality!
            : new SymbolicNotCondition(equality!);
        return true;
    }
    private static bool TupleComparisonUsesUserDefinedElementOperator(ExpressionSyntax expression, SymbolicLoweringContext context) {
        var typeInfo = context.SemanticModel.GetTypeInfo(expression, context.CancellationToken);
        if ((typeInfo.ConvertedType ?? typeInfo.Type) is not INamedTypeSymbol { IsTupleType: true } tupleType)
            return false;
        return tupleType.TupleElements.Any(static element => {
            var type = element.Type;
            return type.GetMembers("op_Equality").OfType<IMethodSymbol>().Any() ||
                   type.GetMembers("op_Inequality").OfType<IMethodSymbol>().Any();
        });
    }
    internal static bool TryLowerTupleElementMemberTerm(
        BoundNode node,
        out SymbolicTerm term) {
        var memberAccess = (MemberAccessExpressionSyntax)node.Syntax;
        var context = node.Context;
        term = null!;
        if (node.Symbol is not IFieldSymbol field ||
            !TryGetTupleElementStorageName(field, out var storageName) ||
            !SymbolicTypeLowerer.TryGetValueKind(field.Type, out var kind))
            return false;
        if (SymbolicLoweringValueFacts.UnwrapExpression(memberAccess.Expression) is TupleExpressionSyntax tupleExpression &&
            TryGetTupleStoragePosition(storageName, out var position) &&
            position < tupleExpression.Arguments.Count &&
            SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(tupleExpression.Arguments[position].Expression, context),
                out var tupleElement) &&
            tupleElement.Kind == kind) {
            term = tupleElement;
            return true;
        }
        if (!SymbolicLoweringValueFacts.TryGetStableVariableSymbol(memberAccess.Expression, context, out var tupleSymbol)) return false;
        term = CreateTupleStorageTerm(tupleSymbol, storageName, kind, context);
        return true;
    }
    private static bool TryGetTupleStoragePosition(string storageName, out int position) {
        position = -1;
        return storageName.StartsWith("Item", StringComparison.Ordinal) &&
               int.TryParse(
                   storageName.Substring("Item".Length),
                   System.Globalization.NumberStyles.None,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out var oneBased) &&
               oneBased > 0 &&
               (position = oneBased - 1) >= 0;
    }
    private static bool TryLowerTupleElementTerms(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out ImmutableArray<SymbolicTerm> terms) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is TupleExpressionSyntax tupleExpression) {
            var tupleBuilder = ImmutableArray.CreateBuilder<SymbolicTerm>(tupleExpression.Arguments.Count);
            foreach (var argument in tupleExpression.Arguments) {
                if (!TryAppendTupleExpressionTerms(argument.Expression, context, tupleBuilder)) {
                    terms = [];
                    return false;
                }
            }
            terms = tupleBuilder.MoveToImmutable();
            return terms.Length != 0;
        }
        terms = [];
        if (!SymbolicLoweringValueFacts.TryGetStableVariableSymbol(expression, context, out var symbol) ||
            context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is not INamedTypeSymbol {
                IsTupleType: true
            } tupleType ||
            tupleType.TupleElements.Length == 0)
            return false;
        var builder = ImmutableArray.CreateBuilder<SymbolicTerm>(tupleType.TupleElements.Length);
        if (!TryAppendTupleStorageTerms(
                context.GetVariableName(symbol),
                tupleType,
                builder))
            return false;
        terms = builder.ToImmutable();
        return true;
    }
    internal static bool TryLowerTupleReturningInvocationElements(
        IInvocationOperation invocation,
        SymbolicLoweringContext callerContext,
        out ImmutableArray<SymbolicTerm> terms) {
        terms = [];
        var method = invocation.TargetMethod;
        if (callerContext.InlineDepth >= SymbolicLoweringContext.MaxSourcePredicateInlineDepth ||
            method.ReturnsByRef ||
            method.ReturnsByRefReadonly ||
            method.ReturnType is not INamedTypeSymbol { IsTupleType: true } ||
            method.DeclaringSyntaxReferences.Length == 0 ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(method, invocation))
            return false;
        var substitutions = new Dictionary<ISymbol, SymbolicTerm>(SymbolEqualityComparer.Default);
        foreach (var parameter in method.Parameters) {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(
                    invocation,
                    parameter.Ordinal,
                    out var argumentExpression) ||
                !SymbolicLoweringValue.TryGet(
                    SymbolicIrLowerer.LowerTerm(argumentExpression, callerContext),
                    out var argument))
                return false;
            substitutions[parameter.OriginalDefinition] = argument;
        }
        var implicitThis = callerContext.ImplicitThis;
        if (!method.IsStatic) {
            if (invocation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                !SymbolicLoweringValue.TryGet(
                    SymbolicIrLowerer.LowerTerm(receiverExpression, callerContext),
                    out var loweredImplicitThis) ||
                loweredImplicitThis.Kind != SmtValueKind.Reference)
                return false;
            implicitThis = loweredImplicitThis;
        }
        var callable = method.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(callerContext.CancellationToken))
            .FirstOrDefault();
        if (!TryGetSingleReturnedExpression(callable, out var returned))
            return false;
        var semanticModel = callerContext.Compilation.GetSemanticModel(returned.SyntaxTree);
        var nestedContext = new SymbolicLoweringContext(
            semanticModel,
            callerContext.CancellationToken,
            callerContext.GetSymbolVersion,
            callerContext.SmtAnalysis,
            callerContext.InvocationTermLowerer,
            implicitThis,
            callerContext.InlineDepth + 1,
            substitutions,
            callerContext.InvocationTermTypeResolver);
        return TryLowerTupleElementTerms(returned, nestedContext, out terms);
    }
    private static bool TryGetSingleReturnedExpression(
        SyntaxNode? callable,
        out ExpressionSyntax returned) {
        returned = callable switch {
            MethodDeclarationSyntax { ExpressionBody.Expression: { } expression } => expression,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression } => expression,
            MethodDeclarationSyntax { Body.Statements.Count: 1 } method
                when method.Body.Statements[0] is ReturnStatementSyntax { Expression: { } expression } => expression,
            LocalFunctionStatementSyntax { Body.Statements.Count: 1 } localFunction
                when localFunction.Body.Statements[0] is ReturnStatementSyntax { Expression: { } expression } => expression,
            _ => null!
        };
        return returned != null;
    }
    private static bool TryAppendTupleExpressionTerms(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        ImmutableArray<SymbolicTerm>.Builder builder) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is TupleExpressionSyntax tupleExpression) {
            foreach (var argument in tupleExpression.Arguments)
                if (!TryAppendTupleExpressionTerms(argument.Expression, context, builder))
                    return false;
            return tupleExpression.Arguments.Count != 0;
        }
        if (context.SemanticModel.GetTypeInfo(expression, context.CancellationToken).Type is
                INamedTypeSymbol { IsTupleType: true } tupleType &&
            SymbolicLoweringValueFacts.TryGetStableVariableSymbol(expression, context, out var symbol))
            return TryAppendTupleStorageTerms(
                context.GetVariableName(symbol),
                tupleType,
                builder);
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out var element))
            return false;
        builder.Add(element);
        return true;
    }
    private static bool TryAppendTupleStorageTerms(
        string storagePrefix,
        INamedTypeSymbol tupleType,
        ImmutableArray<SymbolicTerm>.Builder builder) {
        foreach (var element in tupleType.TupleElements) {
            var field = element.CorrespondingTupleField ?? element;
            if (!TryGetTupleElementStorageName(field, out var storageName))
                return false;
            var elementStorage = storagePrefix + "." + storageName;
            if (field.Type is INamedTypeSymbol { IsTupleType: true } nestedTuple) {
                if (!TryAppendTupleStorageTerms(elementStorage, nestedTuple, builder))
                    return false;
                continue;
            }
            if (!SymbolicTypeLowerer.TryGetValueKind(field.Type, out var kind))
                return false;
            builder.Add(new SymbolicVariableTerm(elementStorage, kind));
        }
        return true;
    }
    internal static bool TryGetTupleElementStorageName(IFieldSymbol field, out string storageName) {
        var storageField = field.CorrespondingTupleField ?? field;
        storageName = storageField.Name;
        return storageName.StartsWith("Item", StringComparison.Ordinal);
    }
    private static SymbolicTerm CreateTupleStorageTerm(
        ISymbol tupleSymbol,
        string storageName,
        SmtValueKind kind,
        SymbolicLoweringContext context) => new SymbolicVariableTerm(context.GetVariableName(tupleSymbol) + "." + storageName, kind);
}
