namespace SharpProof.Symbolic.Ir;
internal static class SymbolicRegexLowerer {
    private const string RegexMetadataName = "System.Text.RegularExpressions.Regex";
    private const string GeneratedRegexAttributeMetadataName =
        "System.Text.RegularExpressions.GeneratedRegexAttribute";
    private readonly record struct RegexSource(string Pattern, RegexOptions Options);
    private readonly record struct RegexInvocation(ExpressionSyntax Input, RegexSource Source);
    internal static bool TryLowerRegexMatchSuccessCondition(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is not MemberAccessExpressionSyntax {
            Name.Identifier.ValueText: "Success",
            Expression: InvocationExpressionSyntax invocation
        } ||
            context.SemanticModel.GetOperation(expression, context.CancellationToken) is not
                IPropertyReferenceOperation {
                    Property: {
                        Name: "Success",
                        Type.SpecialType: SpecialType.System_Boolean
                    }
                } ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation { TargetMethod.Name: "Match" })
            return false;
        return TryLowerRegexInvocationPredicate(invocation, context, out condition);
    }
    internal static bool TryLowerRegexMatchesCountComparison(
        BinaryExpressionSyntax comparison,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        if (TryLowerRegexMatchesCountComparisonOperand(comparison.Left, comparison.Right, comparison.Kind(), context, out condition))
            return true;
        return TryLowerRegexMatchesCountComparisonOperand(
            comparison.Right,
            comparison.Left,
            SymbolicStringLowerer.ReverseStringComparisonKind(comparison.Kind()),
            context,
            out condition);
    }
    private static bool TryLowerRegexMatchesCountComparisonOperand(
        ExpressionSyntax countExpression,
        ExpressionSyntax constantExpression,
        SyntaxKind comparisonKind,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        countExpression = SymbolicLoweringValueFacts.UnwrapExpression(countExpression);
        var constant = context.SemanticModel.GetConstantValue(constantExpression, context.CancellationToken);
        if (countExpression is not MemberAccessExpressionSyntax {
            Name.Identifier.ValueText: "Count",
            Expression: InvocationExpressionSyntax invocation
        } ||
            context.SemanticModel.GetOperation(countExpression, context.CancellationToken) is not
                IPropertyReferenceOperation { Property.Type.SpecialType: SpecialType.System_Int32 } ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation { TargetMethod.Name: "Matches" } ||
            !constant.HasValue ||
            constant.Value == null ||
            !SymbolicLoweringValueFacts.TryGetIntegralConstant(constant.Value, out var count) ||
            !SymbolicLoweringValueFacts.TryClassifyThresholdComparison(
                comparisonKind, count, 1, out var hasMatch) ||
            !TryLowerRegexInvocationParts(invocation, context, out var evaluation, out var match))
            return false;
        condition = CombineRegexEvaluationAndValue(evaluation, hasMatch ? match : new SymbolicNotCondition(match));
        return true;
    }
    internal static bool TryLowerRegexInvocationPredicate(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!TryLowerRegexInvocationParts(invocation, context, out var evaluation, out var predicate))
            return false;
        condition = CombineRegexEvaluationAndValue(evaluation, predicate);
        return true;
    }
    internal static bool TryLowerNegatedRegexInvocationPredicate(
        ExpressionSyntax expression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is not InvocationExpressionSyntax invocation ||
            !TryLowerRegexInvocationParts(invocation, context, out var evaluation, out var predicate))
            return false;
        condition = CombineRegexEvaluationAndValue(evaluation, new SymbolicNotCondition(predicate));
        return true;
    }
    private static bool TryLowerRegexInvocationParts(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition? evaluation,
        out SymbolicCondition predicate) {
        evaluation = null;
        predicate = null!;
        if (context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation operation ||
            operation.TargetMethod.Name is not ("IsMatch" or "Match" or "Matches") ||
            !IsRegexType(operation.TargetMethod.ContainingType) ||
            ResolveRegexInvocation(invocation, operation, context) is not { } resolved ||
            !SymbolicStringLowerer.TryLowerStringTerm(resolved.Input, context, out var input))
            return false;
        predicate = SymbolicIrLowerer.CreateFactCondition(
            new SymbolicStringPredicateAtom(SymbolicStringPredicateKind.RegexMatch, input,
                new SymbolicStringConstantTerm(resolved.Source.Pattern), resolved.Source.Options),
            invocation,
            "ir.regex." + operation.TargetMethod.Name.ToLowerInvariant());
        if (SymbolicIrLowerer.TryLowerReferenceTerm(resolved.Input, context, out var inputReference))
            evaluation = SymbolicIrLowerer.CreateReferenceNullCondition(
                inputReference, false, resolved.Input, "ir.regex.input-non-null");
        return true;
    }
    private static SymbolicCondition CombineRegexEvaluationAndValue(SymbolicCondition? evaluation, SymbolicCondition value)
        => evaluation == null
            ? value
            : new SymbolicBinaryCondition(SymbolicConditionOperator.And, evaluation, value);
    private static RegexInvocation? ResolveRegexInvocation(
        InvocationExpressionSyntax invocation,
        IInvocationOperation operation,
        SymbolicLoweringContext context) {
        if (operation.TargetMethod.IsStatic) {
            if (operation.Arguments.Length is not 2 and not 3 ||
                operation.Arguments[0].Value.Syntax is not ExpressionSyntax input ||
                ResolveRegexArguments(operation.Arguments, 1, context) is not { } source)
                return null;
            return new RegexInvocation(input, source);
        }
        if (operation.Instance?.Syntax is not ExpressionSyntax receiver ||
            operation.Arguments.Length is not 1 and not 2 ||
            operation.Arguments[0].Value.Syntax is not ExpressionSyntax instanceInput ||
            operation.Arguments.Length == 2 && !IsConstantZero(operation.Arguments[1].Value.Syntax, context) ||
            ResolveRegexSource(receiver, invocation, context) is not { } instanceSource)
            return null;
        return new RegexInvocation(instanceInput, instanceSource);
    }
    private static RegexSource? ResolveRegexArguments(
        ImmutableArray<IArgumentOperation> arguments,
        int patternIndex,
        SymbolicLoweringContext context) {
        var optionsIndex = patternIndex + 1;
        if (arguments.Length is var count &&
            count != optionsIndex &&
            count != optionsIndex + 1 ||
            arguments[patternIndex].Value.Syntax is not ExpressionSyntax patternExpression ||
            !SymbolicStringLowerer.TryGetConstantString(patternExpression, context, out var pattern))
            return null;
        var options = RegexOptions.None;
        if (arguments.Length > optionsIndex &&
            (arguments[optionsIndex].Value.Syntax is not ExpressionSyntax optionsExpression ||
             !SymbolicStringLowerer.TryGetRegexOptions(optionsExpression, context, out options)))
            return null;
        return new RegexSource(pattern, options);
    }
    private static RegexSource? ResolveRegexSource(
        ExpressionSyntax expression,
        SyntaxNode useSite,
        SymbolicLoweringContext context) {
        expression = SymbolicLoweringValueFacts.UnwrapExpression(expression);
        if (expression is ObjectCreationExpressionSyntax creation)
            return ResolveRegexObjectCreation(creation, context);
        if (expression is InvocationExpressionSyntax factoryInvocation &&
            context.SemanticModel.GetOperation(factoryInvocation, context.CancellationToken) is
                IInvocationOperation factoryOperation)
            return ResolveGeneratedRegexFactory(factoryOperation.TargetMethod);
        return context.SemanticModel.GetSymbolInfo(expression, context.CancellationToken).Symbol switch {
            ILocalSymbol local => ResolveLocalRegexSource(local, useSite, context),
            IFieldSymbol field => ResolveReadonlyRegexFieldSource(field, context),
            _ => null
        };
    }
    private static RegexSource? ResolveRegexObjectCreation(
        ObjectCreationExpressionSyntax creation,
        SymbolicLoweringContext context) {
        if (context.SemanticModel.GetOperation(creation, context.CancellationToken) is not
                IObjectCreationOperation operation ||
            !IsRegexType(operation.Constructor?.ContainingType) ||
            operation.Arguments.Length is not 1 and not 2)
            return null;
        return ResolveRegexArguments(operation.Arguments, 0, context);
    }
    private static RegexSource? ResolveGeneratedRegexFactory(IMethodSymbol method) {
        foreach (var attribute in method.GetAttributes()) {
            if (!string.Equals(
                    SymbolicTypeFacts.GetFullMetadataName(attribute.AttributeClass),
                    GeneratedRegexAttributeMetadataName,
                    StringComparison.Ordinal) ||
                attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string generatedPattern)
                continue;
            var options = RegexOptions.None;
            if (attribute.ConstructorArguments.Length > 1 &&
                attribute.ConstructorArguments[1].Value is int rawOptions)
                options = (RegexOptions)rawOptions;
            return SymbolicStringLowerer.CanRepresentRegexOptions(options)
                ? new RegexSource(generatedPattern, options)
                : null;
        }
        return null;
    }
    private static RegexSource? ResolveLocalRegexSource(
        ILocalSymbol local,
        SyntaxNode useSite,
        SymbolicLoweringContext context) {
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax {
                Initializer.Value: { } initializer
            } declarator ||
            declarator.Parent?.Parent is not LocalDeclarationStatementSyntax declaration ||
            useSite.FirstAncestorOrSelf<StatementSyntax>() is not { } useStatement ||
            declaration.Parent is not BlockSyntax block ||
            !ReferenceEquals(block, useStatement.Parent))
            return null;
        var declarationIndex = block.Statements.IndexOf(declaration);
        var useIndex = block.Statements.IndexOf(useStatement);
        if (declarationIndex < 0 || useIndex <= declarationIndex ||
            SymbolicSourcePredicateLowerer.CountLocalSymbolReferences(useStatement, local, context) != 1)
            return null;
        for (var index = declarationIndex + 1; index < useIndex; index++)
            if (SymbolicSourcePredicateLowerer.CountLocalSymbolReferences(block.Statements[index], local, context) != 0)
                return null;
        return ResolveRegexSource(initializer, declarator, context);
    }
    private static RegexSource? ResolveReadonlyRegexFieldSource(
        IFieldSymbol field,
        SymbolicLoweringContext callerContext) {
        if (!field.IsReadOnly ||
            !IsRegexType(field.Type) ||
            FieldHasAssignmentOutsideInitializer(field, callerContext))
            return null;
        var declarator = field.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(callerContext.CancellationToken))
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(static syntax => syntax.Initializer?.Value != null);
        if (declarator?.Initializer?.Value is not { } initializer) return null;
        var semanticModel = callerContext.Compilation.GetSemanticModel(initializer.SyntaxTree);
        var initializerContext = new SymbolicLoweringContext(
            semanticModel,
            callerContext.CancellationToken,
            callerContext.GetSymbolVersion,
            callerContext.SmtAnalysis,
            callerContext.InvocationTermLowerer,
            callerContext.ImplicitThis,
            callerContext.InlineDepth,
            callerContext.SymbolSubstitutions,
            callerContext.InvocationTermTypeResolver);
        return ResolveRegexSource(initializer, declarator, initializerContext);
    }
    private static bool FieldHasAssignmentOutsideInitializer(IFieldSymbol field, SymbolicLoweringContext context) {
        foreach (var typeReference in field.ContainingType.DeclaringSyntaxReferences) {
            var typeSyntax = typeReference.GetSyntax(context.CancellationToken);
            var semanticModel = context.Compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            foreach (var assignment in typeSyntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                if (SymbolEqualityComparer.Default.Equals(
                        semanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol,
                        field))
                    return true;
        }
        return false;
    }
    private static bool IsConstantZero(SyntaxNode expression, SymbolicLoweringContext context) =>
        expression is ExpressionSyntax expressionSyntax &&
        SymbolicLoweringValueFacts.TryGetIntegralConstant(
            expressionSyntax, context.SemanticModel, context.CancellationToken, out var value) &&
        value == 0;
    private static bool IsRegexType(ITypeSymbol? type) =>
        string.Equals(type?.ToDisplayString(), RegexMetadataName, StringComparison.Ordinal);
}
