namespace SharpProof.Symbolic.Ir;

internal static class SymbolicSourcePredicateLowerer {
    internal static bool TryLowerSourceBooleanInvocation(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (context.InlineDepth >= SymbolicLoweringContext.MaxSourcePredicateInlineDepth ||
            context.SemanticModel.GetOperation(invocation, context.CancellationToken) is not
                IInvocationOperation operation)
            return false;

        if (operation.TargetMethod.MethodKind == MethodKind.DelegateInvoke)
            return TryLowerLocalDelegateBooleanInvocation(invocation, operation, context, out condition);

        return TryLowerSourceMethodBooleanInvocation(operation, context, out condition);
    }
    private static bool TryLowerSourceMethodBooleanInvocation(
        IInvocationOperation invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var method = invocation.TargetMethod;
        if (!CanInlineSourceBooleanPredicate(method) ||
            !TryCreateParameterSubstitutions(method.Parameters, invocation, context, out var substitutions))
            return false;

        var implicitThis = context.ImplicitThis;
        if (!method.IsStatic) {
            if (invocation.Instance?.Syntax is not ExpressionSyntax receiverExpression ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(receiverExpression, context), out var loweredImplicitThis) ||
                loweredImplicitThis.Kind != SharpProof.ProofCore.Smt.SmtValueKind.Reference)
                return false;

            implicitThis = loweredImplicitThis;
        }
        return TryLowerReturnedBoolean(method, context, substitutions, implicitThis, out condition);
    }
    private static bool CanInlineSourceBooleanPredicate(IMethodSymbol method) => method is {
        ReturnsVoid: false,
        ReturnsByRef: false,
        ReturnsByRefReadonly: false,
        ReturnType.SpecialType: SpecialType.System_Boolean,
        DeclaringSyntaxReferences.Length: > 0
    } &&
               method.Parameters.All(static parameter => parameter.RefKind == RefKind.None);

    internal static bool TryLowerReturnedBoolean(
        ISymbol symbol,
        SymbolicLoweringContext callerContext,
        Dictionary<ISymbol, SymbolicTerm> substitutions,
        SymbolicTerm implicitThis,
        out SymbolicCondition condition) {
        condition = null!;
        var callable = symbol.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(callerContext.CancellationToken))
            .FirstOrDefault();
        if (callable == null) return false;

        var semanticModel = callerContext.Compilation.GetSemanticModel(callable.SyntaxTree);
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
        return TryLowerReturnedBooleanSyntax(callable, nestedContext, substitutions, out condition);
    }
    private static bool TryLowerReturnedBooleanSyntax(
        SyntaxNode callable,
        SymbolicLoweringContext context,
        Dictionary<ISymbol, SymbolicTerm> substitutions,
        out SymbolicCondition condition) {
        switch (callable) {
            case MethodDeclarationSyntax { ExpressionBody.Expression: { } expression }:
                return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression, context), out condition);
            case MethodDeclarationSyntax { Body: { } body }:
                return TryLowerReturnedBooleanBlock(body, context, substitutions, out condition);
            case LocalFunctionStatementSyntax { ExpressionBody.Expression: { } expression }:
                return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression, context), out condition);
            case LocalFunctionStatementSyntax { Body: { } body }:
                return TryLowerReturnedBooleanBlock(body, context, substitutions, out condition);
            case AccessorDeclarationSyntax { ExpressionBody.Expression: { } expression }:
                return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression, context), out condition);
            case AccessorDeclarationSyntax { Body: { } body }:
                return TryLowerReturnedBooleanBlock(body, context, substitutions, out condition);
            case PropertyDeclarationSyntax { ExpressionBody.Expression: { } expression }:
                return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression, context), out condition);
            case PropertyDeclarationSyntax { AccessorList: { } accessorList }: {
                    var getter = accessorList.Accessors
                        .FirstOrDefault(static accessor => accessor.IsKind(SyntaxKind.GetAccessorDeclaration));
                    if (getter != null)
                        return TryLowerReturnedBooleanSyntax(getter, context, substitutions, out condition);

                    condition = null!;
                    return false;
                }
            default:
                condition = null!;
                return false;
        }
    }
    private static bool TryLowerReturnedBooleanBlock(
        BlockSyntax body,
        SymbolicLoweringContext context,
        Dictionary<ISymbol, SymbolicTerm> substitutions,
        out SymbolicCondition condition) {
        condition = null!;
        var statementIndex = 0;
        while (statementIndex < body.Statements.Count) {
            if (body.Statements[statementIndex] is LocalDeclarationStatementSyntax declaration) {
                if (!TryApplyLocalDeclarationSubstitutions(declaration, context, substitutions)) return false;
                statementIndex++;
                continue;
            }
            if (body.Statements[statementIndex] is ExpressionStatementSyntax {
                Expression: AssignmentExpressionSyntax assignment
            } && assignment.IsKind(SyntaxKind.SimpleAssignmentExpression)) {
                if (!TryApplyLocalAssignmentSubstitution(assignment, context, substitutions)) return false;
                statementIndex++;
                continue;
            }
            break;
        }
        return TryLowerReturnedBooleanStatements(body.Statements, statementIndex, context, out condition);
    }
    private static bool TryApplyLocalDeclarationSubstitutions(
        LocalDeclarationStatementSyntax declaration,
        SymbolicLoweringContext context,
        IDictionary<ISymbol, SymbolicTerm> substitutions) {
        foreach (var variable in declaration.Declaration.Variables) {
            if (context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is not ILocalSymbol local)
                return false;

            if (variable.Initializer == null) continue;
            if (!TryLowerBooleanValueTerm(variable.Initializer.Value, context, out var value)) return false;
            substitutions[local.OriginalDefinition] = value;
        }
        return true;
    }
    private static bool TryApplyLocalAssignmentSubstitution(
        AssignmentExpressionSyntax assignment,
        SymbolicLoweringContext context,
        IDictionary<ISymbol, SymbolicTerm> substitutions) {
        if (context.SemanticModel.GetSymbolInfo(assignment.Left, context.CancellationToken).Symbol is not
                ILocalSymbol local ||
            !TryLowerBooleanValueTerm(assignment.Right, context, out var value))
            return false;

        substitutions[local.OriginalDefinition] = value;
        return true;
    }
    internal static bool TryLowerBooleanValueTerm(ExpressionSyntax expression, SymbolicLoweringContext context, out SymbolicTerm term) {
        if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(expression, context), out term)) return true;

        if (SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression, context), out var condition)) {
            term = new SymbolicConditionalTerm(condition, new SymbolicBooleanConstantTerm(true), new SymbolicBooleanConstantTerm(false));
            return true;
        }
        term = null!;
        return false;
    }
    private static bool TryLowerReturnedBooleanStatements(
        SyntaxList<StatementSyntax> statements,
        int statementIndex,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        var remaining = statements.Count - statementIndex;
        if (remaining == 1) {
            if (statements[statementIndex] is ReturnStatementSyntax { Expression: { } returned })
                return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(returned, context), out condition);

            if (statements[statementIndex] is IfStatementSyntax {
                Else.Statement: { } whenFalseStatement
            } ifStatement &&
                TryGetSingleReturnExpression(ifStatement.Statement, out var whenTrue) &&
                TryGetSingleReturnExpression(whenFalseStatement, out var whenFalse))
                return TryLowerBooleanConditional(ifStatement.Condition, whenTrue, whenFalse, context, out condition);

            if (statements[statementIndex] is SwitchStatementSyntax switchStatement)
                return TryLowerReturnedBooleanSwitch(switchStatement, null, context, out condition);
        }
        if (remaining >= 2 &&
            TryLowerGuardReturnChain(statements, statementIndex, context, out condition))
            return true;

        return remaining == 2 &&
               statements[statementIndex] is SwitchStatementSyntax switchWithFallback &&
               statements[statementIndex + 1] is ReturnStatementSyntax { Expression: { } fallback } &&
               TryLowerReturnedBooleanSwitch(switchWithFallback, fallback, context, out condition);
    }
    internal static bool TryLowerBooleanConditional(
        ExpressionSyntax test,
        ExpressionSyntax whenTrueExpression,
        ExpressionSyntax whenFalseExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(test, context), out var testCondition) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(whenTrueExpression, context), out var whenTrue) ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(whenFalseExpression, context), out var whenFalse))
            return false;

        condition = CreateBooleanConditional(testCondition, whenTrue, whenFalse);
        return true;
    }
    private static SymbolicCondition CreateBooleanConditional(
        SymbolicCondition test,
        SymbolicCondition whenTrue,
        SymbolicCondition whenFalse) => new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, test, whenTrue),
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, new SymbolicNotCondition(test), whenFalse));

    private static bool TryLowerGuardReturnChain(
        SyntaxList<StatementSyntax> statements,
        int statementIndex,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (statements[statements.Count - 1] is not ReturnStatementSyntax { Expression: { } finalReturn } ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(finalReturn, context), out condition))
            return false;

        var guards = new List<(ExpressionSyntax Test, ExpressionSyntax Result)>();
        for (var index = statementIndex; index < statements.Count - 1; index++) {
            if (statements[index] is not IfStatementSyntax { Else: null } guard ||
                !TryGetSingleReturnExpression(guard.Statement, out var guardReturn))
                return false;

            guards.Add((guard.Condition, guardReturn));
        }
        if (guards.Count == 0) return false;
        for (var index = guards.Count - 1; index >= 0; index--) {
            if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(guards[index].Test, context), out var test) ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(guards[index].Result, context), out var result))
                return false;

            condition = CreateBooleanConditional(test, result, condition);
        }
        return true;
    }
    private static bool TryLowerReturnedBooleanSwitch(
        SwitchStatementSyntax switchStatement,
        ExpressionSyntax? fallbackExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = new SymbolicConstantCondition(false);
        var selections = new List<SymbolicCondition>();
        foreach (var section in switchStatement.Sections) {
            if (!TryGetSwitchSectionReturnExpression(section, out var returned) ||
                !SwitchPathConditionBuilder.TryCreateSwitchStatementSectionSymbolicCondition(
                    switchStatement.Expression,
                    section,
                    context,
                    out var selected) ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(returned, context), out var result))
                return false;

            selections.Add(selected);
            condition = new SymbolicBinaryCondition(
                SymbolicConditionOperator.Or,
                condition,
                new SymbolicBinaryCondition(SymbolicConditionOperator.And, selected, result));
        }
        if (fallbackExpression == null) return selections.Count != 0;
        if (!SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(fallbackExpression, context), out var fallback)) return false;
        var anySelected = CreateConditionDisjunction(selections);
        condition = new SymbolicBinaryCondition(
            SymbolicConditionOperator.Or,
            condition,
            new SymbolicBinaryCondition(SymbolicConditionOperator.And, new SymbolicNotCondition(anySelected), fallback));
        return true;
    }
    private static SymbolicCondition CreateConditionDisjunction(IReadOnlyList<SymbolicCondition> conditions) {
        if (conditions.Count == 0) return new SymbolicConstantCondition(false);
        var result = conditions[0];
        for (var index = 1; index < conditions.Count; index++)
            result = new SymbolicBinaryCondition(SymbolicConditionOperator.Or, result, conditions[index]);
        return result;
    }
    private static bool TryGetSwitchSectionReturnExpression(SwitchSectionSyntax section, out ExpressionSyntax expression) {
        expression = null!;
        return section.Statements.Count == 1 &&
               TryGetSingleReturnExpression(section.Statements[0], out expression);
    }
    private static bool TryGetSingleReturnExpression(StatementSyntax statement, out ExpressionSyntax expression) {
        expression = null!;
        if (statement is ReturnStatementSyntax { Expression: { } direct }) {
            expression = direct;
            return true;
        }
        if (statement is BlockSyntax { Statements.Count: 1 } block &&
            block.Statements[0] is ReturnStatementSyntax { Expression: { } nested }) {
            expression = nested;
            return true;
        }
        return false;
    }
    private static bool TryCreateParameterSubstitutions(
        IReadOnlyList<IParameterSymbol> parameters,
        IInvocationOperation invocation,
        SymbolicLoweringContext context,
        out Dictionary<ISymbol, SymbolicTerm> substitutions) {
        substitutions = new Dictionary<ISymbol, SymbolicTerm>(SymbolEqualityComparer.Default);
        foreach (var parameter in parameters) {
            if (!SymbolicValueFacts.TryGetInvocationArgumentExpression(invocation, parameter.Ordinal, out var argumentExpression) ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerTerm(argumentExpression, context), out var argument))
                return false;

            substitutions[parameter.OriginalDefinition] = argument;
        }
        return true;
    }
    private static bool TryLowerLocalDelegateBooleanInvocation(
        InvocationExpressionSyntax invocationSyntax,
        IInvocationOperation invocation,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = null!;
        if (invocation.TargetMethod.ReturnType.SpecialType != SpecialType.System_Boolean ||
            invocation.TargetMethod.Parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            !TryGetLocalDelegateReceiver(invocationSyntax, context, out var delegateLocal) ||
            !TryGetLocalDelegateInitializer(delegateLocal, invocationSyntax, context, out var initializer))
            return false;

        if (initializer is AnonymousFunctionExpressionSyntax lambda)
            return LambdaBodyReferencesOnlyParameters(lambda, context) &&
                   TryCreateLambdaParameterSubstitutions(lambda, invocation, context, out var substitutions) &&
                   TryLowerLambdaBooleanBody(lambda, context, substitutions, out condition);

        var methodInfo = context.SemanticModel.GetSymbolInfo(initializer, context.CancellationToken);
        var method = methodInfo.Symbol ??
                     (methodInfo.CandidateSymbols.Length == 1 ? methodInfo.CandidateSymbols[0] : null);
        if (method is not IMethodSymbol { IsStatic: true } sourceMethod ||
            !CanInlineSourceBooleanPredicate(sourceMethod) ||
            !TryCreateParameterSubstitutions(sourceMethod.Parameters, invocation, context, out var methodSubstitutions))
            return false;

        return TryLowerReturnedBoolean(sourceMethod, context, methodSubstitutions, context.ImplicitThis, out condition);
    }
    private static bool TryGetLocalDelegateReceiver(
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out ILocalSymbol local) {
        local = null!;
        ExpressionSyntax? receiver = invocation.Expression switch {
            IdentifierNameSyntax identifier => identifier,
            MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Invoke" } member => member.Expression,
            _ => null
        };
        if (receiver == null ||
            context.SemanticModel.GetSymbolInfo(receiver, context.CancellationToken).Symbol is not ILocalSymbol symbol)
            return false;

        local = (ILocalSymbol)symbol.OriginalDefinition;
        return true;
    }
    private static bool TryGetLocalDelegateInitializer(
        ILocalSymbol local,
        InvocationExpressionSyntax invocation,
        SymbolicLoweringContext context,
        out ExpressionSyntax initializer) {
        initializer = null!;
        if (local.DeclaringSyntaxReferences.Length != 1 ||
            local.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken) is not VariableDeclaratorSyntax {
                Initializer.Value: { } value
            } declarator ||
            declarator.Parent?.Parent is not LocalDeclarationStatementSyntax declaration ||
            invocation.FirstAncestorOrSelf<StatementSyntax>() is not { } invocationStatement ||
            declaration.Parent is not BlockSyntax block ||
            !ReferenceEquals(block, invocationStatement.Parent))
            return false;

        var declarationIndex = block.Statements.IndexOf(declaration);
        var invocationIndex = block.Statements.IndexOf(invocationStatement);
        if (declarationIndex < 0 || invocationIndex <= declarationIndex ||
            CountLocalSymbolReferences(invocationStatement, local, context) != 1)
            return false;

        for (var index = declarationIndex + 1; index < invocationIndex; index++)
            if (CountLocalSymbolReferences(block.Statements[index], local, context) != 0)
                return false;

        initializer = value;
        return true;
    }
    internal static int CountLocalSymbolReferences(SyntaxNode node, ILocalSymbol local, SymbolicLoweringContext context)
        => node.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Count(identifier => SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol,
                local));

    private static bool LambdaBodyReferencesOnlyParameters(AnonymousFunctionExpressionSyntax lambda, SymbolicLoweringContext context) {
        var parameters = GetLambdaParameterSymbols(lambda, context)
            .ToImmutableHashSet<ISymbol>(SymbolEqualityComparer.Default);
        if (parameters.Count == 0) return false;

        foreach (var identifier in GetLambdaBody(lambda)?.DescendantNodesAndSelf()
                     .OfType<IdentifierNameSyntax>() ?? []) {
            var symbol = context.SemanticModel.GetSymbolInfo(identifier, context.CancellationToken).Symbol;
            if (symbol == null ||
                (!parameters.Contains(symbol) &&
                 !IsAllowedLambdaParameterMember(identifier, parameters, context)))
                return false;
        }
        return true;
    }
    private static bool IsAllowedLambdaParameterMember(
        IdentifierNameSyntax identifier,
        ImmutableHashSet<ISymbol> parameters,
        SymbolicLoweringContext context) => identifier.Parent is MemberAccessExpressionSyntax member &&
               ReferenceEquals(member.Name, identifier) &&
               context.SemanticModel.GetSymbolInfo(member.Expression, context.CancellationToken).Symbol is { } receiver &&
               parameters.Contains(receiver);

    private static bool TryCreateLambdaParameterSubstitutions(
        AnonymousFunctionExpressionSyntax lambda,
        IInvocationOperation invocation,
        SymbolicLoweringContext context,
        out Dictionary<ISymbol, SymbolicTerm> substitutions) => TryCreateParameterSubstitutions(
            GetLambdaParameterSymbols(lambda, context).ToArray(),
            invocation,
            context,
            out substitutions);

    private static IEnumerable<IParameterSymbol> GetLambdaParameterSymbols(
        AnonymousFunctionExpressionSyntax lambda,
        SymbolicLoweringContext context) {
        foreach (var parameter in GetLambdaParameters(lambda))
            if (context.SemanticModel.GetDeclaredSymbol(parameter, context.CancellationToken) is
                IParameterSymbol symbol)
                yield return symbol.OriginalDefinition;
    }
    private static IEnumerable<ParameterSyntax> GetLambdaParameters(AnonymousFunctionExpressionSyntax lambda) => lambda switch {
        SimpleLambdaExpressionSyntax simple => new[] { simple.Parameter },
        ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.ParameterList.Parameters,
        AnonymousMethodExpressionSyntax { ParameterList: { } list } => list.Parameters,
        _ => []
    };

    private static SyntaxNode? GetLambdaBody(AnonymousFunctionExpressionSyntax lambda) => lambda switch {
        SimpleLambdaExpressionSyntax { ExpressionBody: { } expression } => expression,
        ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } expression } => expression,
        SimpleLambdaExpressionSyntax { Block: { } block } => block,
        ParenthesizedLambdaExpressionSyntax { Block: { } block } => block,
        AnonymousMethodExpressionSyntax { Block: { } block } => block,
        _ => null
    };

    private static bool TryLowerLambdaBooleanBody(
        AnonymousFunctionExpressionSyntax lambda,
        SymbolicLoweringContext callerContext,
        Dictionary<ISymbol, SymbolicTerm> substitutions,
        out SymbolicCondition condition) {
        var nestedContext = new SymbolicLoweringContext(
            callerContext.SemanticModel,
            callerContext.CancellationToken,
            callerContext.GetSymbolVersion,
            callerContext.SmtAnalysis,
            callerContext.InvocationTermLowerer,
            callerContext.ImplicitThis,
            callerContext.InlineDepth + 1,
            substitutions,
            callerContext.InvocationTermTypeResolver);
        switch (lambda) {
            case SimpleLambdaExpressionSyntax { ExpressionBody: { } simpleExpression }:
                return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(simpleExpression, nestedContext), out condition);
            case ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } parenthesizedExpression }:
                return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(parenthesizedExpression, nestedContext),
                    out condition);
            case SimpleLambdaExpressionSyntax { Block: { } simpleBlock }:
                return TryLowerReturnedBooleanBlock(simpleBlock, nestedContext, substitutions, out condition);
            case ParenthesizedLambdaExpressionSyntax { Block: { } parenthesizedBlock }:
                return TryLowerReturnedBooleanBlock(parenthesizedBlock, nestedContext, substitutions, out condition);
            case AnonymousMethodExpressionSyntax { Block: { } anonymousBlock }:
                return TryLowerReturnedBooleanBlock(anonymousBlock, nestedContext, substitutions, out condition);
            default:
                condition = null!;
                return false;
        }
    }
}
