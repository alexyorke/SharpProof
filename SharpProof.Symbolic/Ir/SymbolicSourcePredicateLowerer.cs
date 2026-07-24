namespace SharpProof.Symbolic.Ir;
internal static class SymbolicSourcePredicateLowerer {
    internal static bool IsIdentitySequenceSelector(
        ExpressionSyntax selector,
        SymbolicLoweringContext context) {
        selector = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(selector);
        if (selector is not AnonymousFunctionExpressionSyntax lambda ||
            context.SemanticModel.GetOperation(lambda, context.CancellationToken) is not
                IAnonymousFunctionOperation { Symbol: { } lambdaMethod })
            return false;
        var parameters = GetLambdaParameterSymbols(lambda, context).ToArray();
        if (parameters.Length is < 1 or > 2 ||
            parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            parameters.Length == 2 &&
            parameters[1].Type.SpecialType != SpecialType.System_Int32 ||
            !SymbolEqualityComparer.Default.Equals(lambdaMethod.ReturnType, parameters[0].Type) ||
            !TryGetSingleReturnedExpression(lambda, out var returned) ||
            !LambdaBodyReferencesOnlyParameters(lambda, context) ||
            !LambdaReadsOnlyStableParameterMembers(lambda, parameters[0], context))
            return false;
        returned = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(returned);
        return SymbolEqualityComparer.Default.Equals(
            context.SemanticModel.GetSymbolInfo(returned, context.CancellationToken).Symbol?.OriginalDefinition,
            parameters[0]);
    }
    private static bool TryGetSingleReturnedExpression(
        AnonymousFunctionExpressionSyntax lambda,
        out ExpressionSyntax returned) {
        returned = lambda switch {
            SimpleLambdaExpressionSyntax { ExpressionBody: { } expression } => expression,
            ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } expression } => expression,
            _ => null!
        };
        if (returned != null) return true;
        var block = lambda switch {
            SimpleLambdaExpressionSyntax simple => simple.Block,
            ParenthesizedLambdaExpressionSyntax parenthesized => parenthesized.Block,
            AnonymousMethodExpressionSyntax anonymous => anonymous.Block,
            _ => null
        };
        if (block?.Statements.Count != 1 ||
            block.Statements[0] is not ReturnStatementSyntax { Expression: { } blockExpression })
            return false;
        returned = blockExpression;
        return true;
    }
    internal static bool TryLowerSequencePredicate(
        ExpressionSyntax predicate,
        SymbolicTerm argument,
        SymbolicLoweringContext callerContext,
        out SymbolicCondition condition) {
        predicate = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(predicate);
        if (predicate is AnonymousFunctionExpressionSyntax lambda)
            return TryLowerSingleParameterLambdaPredicate(
                lambda,
                argument,
                callerContext,
                out condition);
        condition = null!;
        var symbolInfo = callerContext.SemanticModel.GetSymbolInfo(predicate, callerContext.CancellationToken);
        if (symbolInfo.Symbol is ILocalSymbol local &&
            predicate.FirstAncestorOrSelf<InvocationExpressionSyntax>() is { } invocation &&
            TryGetLocalDelegateInitializer(
                (ILocalSymbol)local.OriginalDefinition,
                invocation,
                callerContext,
                out var initializer)) {
            predicate = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(initializer);
            if (predicate is AnonymousFunctionExpressionSyntax initializerLambda)
                return TryLowerSingleParameterLambdaPredicate(
                    initializerLambda,
                    argument,
                    callerContext,
                    out condition);
            symbolInfo = callerContext.SemanticModel.GetSymbolInfo(predicate, callerContext.CancellationToken);
        }
        var method = symbolInfo.Symbol as IMethodSymbol ??
                     (symbolInfo.CandidateSymbols.Length == 1
                         ? symbolInfo.CandidateSymbols[0] as IMethodSymbol
                         : null);
        if (method == null) return false;
        var implicitThis = callerContext.ImplicitThis;
        if (!method.IsStatic) {
            if (callerContext.SemanticModel.GetOperation(predicate, callerContext.CancellationToken) is not
                    IMethodReferenceOperation methodReference ||
                SymbolicDispatchFacts.ResolveExactDispatchTarget(
                    method,
                    SymbolicDispatchFacts.GetReceiverOperation(methodReference)) is not { } exactMethod ||
                predicate is not MemberAccessExpressionSyntax memberAccess ||
                !SymbolicIrLowerer.TryLowerReferenceTerm(
                    memberAccess.Expression,
                    callerContext,
                    out implicitThis))
                return false;
            method = exactMethod;
        }
        method = method.OriginalDefinition;
        if (!CanInlineSourceBooleanPredicate(method) ||
            !TryCreateSequencePredicateSubstitutions(
                method.Parameters,
                argument,
                predicate.SpanStart,
                out var substitutions) ||
            !SourcePredicateParameterIsStable(method, method.Parameters[0], callerContext))
            return false;
        return TryLowerReturnedBoolean(
            method,
            callerContext,
            substitutions,
            implicitThis,
            out condition);
    }
    private static bool TryLowerSingleParameterLambdaPredicate(
        AnonymousFunctionExpressionSyntax lambda,
        SymbolicTerm argument,
        SymbolicLoweringContext callerContext,
        out SymbolicCondition condition) {
        condition = null!;
        var parameters = GetLambdaParameterSymbols(lambda, callerContext).ToArray();
        if (!TryCreateSequencePredicateSubstitutions(
                parameters,
                argument,
                lambda.SpanStart,
                out var substitutions) ||
            !LambdaBodyReferencesOnlyParameters(lambda, callerContext) ||
            !LambdaReadsOnlyStableParameterMembers(lambda, parameters[0], callerContext))
            return false;
        return TryLowerReturnedBooleanSyntax(
            lambda,
            Rebind(callerContext, substitutions, nested: true),
            substitutions,
            out condition);
    }
    private static bool TryCreateSequencePredicateSubstitutions(
        IReadOnlyList<IParameterSymbol> parameters,
        SymbolicTerm argument,
        int predicatePosition,
        out Dictionary<ISymbol, SymbolicTerm> substitutions) {
        substitutions = new Dictionary<ISymbol, SymbolicTerm>(SymbolEqualityComparer.Default);
        if (parameters.Count is < 1 or > 2 ||
            parameters.Any(static parameter => parameter.RefKind != RefKind.None) ||
            parameters.Count == 2 &&
            parameters[1].Type.SpecialType != SpecialType.System_Int32)
            return false;
        substitutions[parameters[0].OriginalDefinition] = argument;
        if (parameters.Count == 2)
            substitutions[parameters[1].OriginalDefinition] = new SymbolicVariableTerm(
                "where_index_" + predicatePosition.ToString(CultureInfo.InvariantCulture),
                SmtValueKind.Int);
        return true;
    }
    private static bool SourcePredicateParameterIsStable(
        IMethodSymbol method,
        IParameterSymbol parameter,
        SymbolicLoweringContext callerContext) {
        var callable = method.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(callerContext.CancellationToken))
            .FirstOrDefault();
        if (callable == null) return false;
        var semanticModel = callerContext.Compilation.GetSemanticModel(callable.SyntaxTree);
        if (SymbolicMutationInventory.Create(callable, semanticModel, callerContext.CancellationToken)
            .InvalidatesSymbol(parameter, true))
            return false;
        return ReadsOnlyStableParameterMembers(
            callable,
            parameter,
            semanticModel,
            callerContext.CancellationToken);
    }
    private static bool LambdaReadsOnlyStableParameterMembers(
        AnonymousFunctionExpressionSyntax lambda,
        IParameterSymbol parameter,
        SymbolicLoweringContext context) {
        var body = GetLambdaBody(lambda);
        return body != null &&
               !SymbolicMutationInventory.Create(
                   body,
                   context.SemanticModel,
                   context.CancellationToken).InvalidatesSymbol(parameter, true) &&
               ReadsOnlyStableParameterMembers(
                   body,
                   parameter,
                   context.SemanticModel,
                   context.CancellationToken);
    }
    private static bool ReadsOnlyStableParameterMembers(
        SyntaxNode? body,
        IParameterSymbol parameter,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        foreach (var member in body?.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>() ?? []) {
            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetSymbolInfo(member.Expression, cancellationToken).Symbol,
                    parameter))
                continue;
            var symbol = semanticModel.GetSymbolInfo(member, cancellationToken).Symbol;
            if (symbol is IFieldSymbol) continue;
            if (symbol is IPropertySymbol property &&
                CSharpSyntaxFacts.IsStableStorageProperty(property, cancellationToken))
                continue;
            return false;
        }
        return true;
    }
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
            SymbolicDispatchFacts.ShouldTreatAsDynamicDispatch(method, invocation) ||
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
    private static bool CanInlineSourceBooleanPredicate(IMethodSymbol method) =>
        !method.ReturnsVoid && !method.ReturnsByRef && !method.ReturnsByRefReadonly &&
        method.ReturnType.SpecialType == SpecialType.System_Boolean &&
        method.DeclaringSyntaxReferences.Length > 0 &&
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
        var nestedContext = Rebind(callerContext, substitutions, semanticModel, implicitThis, true);
        return TryLowerReturnedBooleanSyntax(callable, nestedContext, substitutions, out condition);
    }
    private static SymbolicLoweringContext Rebind(
        SymbolicLoweringContext context,
        IReadOnlyDictionary<ISymbol, SymbolicTerm> substitutions,
        SemanticModel? semanticModel = null,
        SymbolicTerm? implicitThis = null,
        bool nested = false) => new(
            semanticModel ?? context.SemanticModel, context.CancellationToken, context.GetSymbolVersion,
            context.SmtAnalysis, context.InvocationTermLowerer, implicitThis ?? context.ImplicitThis,
            context.InlineDepth + (nested ? 1 : 0), substitutions,
            context.InvocationTermTypeResolver);
    private static bool TryLowerReturnedBooleanSyntax(
        SyntaxNode callable,
        SymbolicLoweringContext context,
        Dictionary<ISymbol, SymbolicTerm> substitutions,
        out SymbolicCondition condition) {
        condition = null!;
        if (callable is PropertyDeclarationSyntax { AccessorList: { } accessors })
            callable = accessors.Accessors.FirstOrDefault(static accessor =>
                accessor.IsKind(SyntaxKind.GetAccessorDeclaration))!;
        var expression = callable switch {
            MethodDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            LocalFunctionStatementSyntax { ExpressionBody.Expression: { } value } => value,
            AccessorDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            PropertyDeclarationSyntax { ExpressionBody.Expression: { } value } => value,
            SimpleLambdaExpressionSyntax { ExpressionBody: { } value } => value,
            ParenthesizedLambdaExpressionSyntax { ExpressionBody: { } value } => value,
            _ => null
        };
        if (expression != null)
            return SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(expression, context), out condition);
        var body = callable switch {
            MethodDeclarationSyntax value => value.Body,
            LocalFunctionStatementSyntax value => value.Body,
            AccessorDeclarationSyntax value => value.Body,
            SimpleLambdaExpressionSyntax value => value.Block,
            ParenthesizedLambdaExpressionSyntax value => value.Block,
            AnonymousMethodExpressionSyntax value => value.Block,
            _ => null
        };
        if (body == null) return false;
        if (callable is AnonymousFunctionExpressionSyntax)
            return TryCompose(body.Statements, SkipBindings(body.Statements), context, out condition);
        ControlFlowGraph? graph;
        try { graph = ControlFlowGraph.Create(callable, context.SemanticModel, context.CancellationToken); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException) { return false; }
        var owner = context.SemanticModel.GetDeclaredSymbol(callable, context.CancellationToken) ??
                    (context.SemanticModel.GetOperation(callable, context.CancellationToken) as
                        IAnonymousFunctionOperation)?.Symbol ??
                    context.SemanticModel.GetEnclosingSymbol(callable.SpanStart, context.CancellationToken);
        if (graph == null || owner == null) return false;
        var domain = new InlinePredicateDomain(context, substitutions);
        var result = AnalyzerUtilitiesControlFlowAnalysis.Run(
            graph, InlinePredicateState.Bottom, domain, context.Compilation, owner, context.CancellationToken);
        return !result.Truncated && result.ExitState == InlinePredicateState.Valid &&
               TryCompose(body.Statements, SkipBindings(body.Statements), context, out condition);
    }
    private static int SkipBindings(SyntaxList<StatementSyntax> statements) {
        var index = 0;
        while (index < statements.Count && IsBindingStatement(statements[index])) index++;
        return index;
    }
    private static bool IsBindingStatement(StatementSyntax statement) =>
        statement is LocalDeclarationStatementSyntax ||
        statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment } &&
        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression);
    private enum InlinePredicateState { Bottom, Valid, Invalid }
    private sealed class InlinePredicateDomain(
        SymbolicLoweringContext context,
        IDictionary<ISymbol, SymbolicTerm> substitutions) : IControlFlowDomain<InlinePredicateState> {
        private readonly HashSet<IOperation> branchValues = [];
        private readonly HashSet<IOperation> assignments = [];
        public void SetControlFlowGraph(ControlFlowGraph graph, PointsToAnalysisResult? _) {
            foreach (var branch in graph.Blocks.Select(static block => block.BranchValue).Where(static value => value != null))
                branchValues.Add(branch!);
        }
        public InlinePredicateState Transfer(InlinePredicateState state, IOperation operation) {
            if (state != InlinePredicateState.Valid || branchValues.Contains(operation)) return state;
            operation = operation is IExpressionStatementOperation expression ? expression.Operation : operation;
            if (operation is IFlowCaptureOperation) return state;
            if (operation is not ISimpleAssignmentOperation assignment ||
                assignment.Target is not ILocalReferenceOperation local ||
                assignment.Value.Syntax is not ExpressionSyntax value)
                return InlinePredicateState.Invalid;
            if (!assignments.Add(assignment)) return state;
            if (!TryLowerBooleanValueTerm(value, context, out var term)) return InlinePredicateState.Invalid;
            substitutions[local.Local.OriginalDefinition] = term;
            return state;
        }
        public InlinePredicateState Refine(InlinePredicateState state, IOperation? _, ControlFlowConditionKind __,
            bool ___, BasicBlock ____) => state;
        public InlinePredicateState Merge(InlinePredicateState current, InlinePredicateState incoming) =>
            current == InlinePredicateState.Bottom ? incoming :
            incoming == InlinePredicateState.Bottom ? current :
            current == InlinePredicateState.Invalid || incoming == InlinePredicateState.Invalid
                ? InlinePredicateState.Invalid
                : InlinePredicateState.Valid;
        public InlinePredicateState CompleteBlock(InlinePredicateState state, BasicBlock block) =>
            block.Kind == BasicBlockKind.Entry ? InlinePredicateState.Valid : state;
        public bool Equivalent(InlinePredicateState left, InlinePredicateState right) => left == right;
        public bool IsUnreachable(InlinePredicateState state) => state == InlinePredicateState.Bottom;
        public string GetKey(InlinePredicateState state) => state.ToString();
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
    private static bool TryCompose(
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
                return TryComposeSwitch(switchStatement, null, context, out condition);
        }
        if (remaining == 2 && statements[statementIndex] is SwitchStatementSyntax switchWithFallback &&
            statements[statementIndex + 1] is ReturnStatementSyntax { Expression: { } fallback })
            return TryComposeSwitch(switchWithFallback, fallback, context, out condition);
        if (remaining < 2 ||
            statements[statements.Count - 1] is not ReturnStatementSyntax { Expression: { } finalReturn } ||
            !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(finalReturn, context), out condition))
            return false;
        for (var index = statements.Count - 2; index >= statementIndex; index--) {
            if (statements[index] is not IfStatementSyntax { Else: null } guard ||
                !TryGetSingleReturnExpression(guard.Statement, out var returned) ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(guard.Condition, context), out var test) ||
                !SymbolicLoweringValue.TryGet(SymbolicIrLowerer.LowerCondition(returned, context), out var result))
                return false;
            condition = CreateBooleanConditional(test, result, condition);
        }
        return true;
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
    private static bool TryComposeSwitch(
        SwitchStatementSyntax switchStatement,
        ExpressionSyntax? fallbackExpression,
        SymbolicLoweringContext context,
        out SymbolicCondition condition) {
        condition = new SymbolicConstantCondition(false);
        var selections = new List<SymbolicCondition>();
        foreach (var section in switchStatement.Sections) {
            if (section.Statements.Count != 1 ||
                !TryGetSingleReturnExpression(section.Statements[0], out var returned) ||
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
    private static SymbolicCondition CreateConditionDisjunction(IReadOnlyList<SymbolicCondition> conditions) =>
        conditions.Count == 0
            ? new SymbolicConstantCondition(false)
            : conditions.Skip(1).Aggregate(conditions[0], static (left, right) =>
                new SymbolicBinaryCondition(SymbolicConditionOperator.Or, left, right));
    private static bool TryGetSingleReturnExpression(StatementSyntax statement, out ExpressionSyntax expression) {
        expression = statement switch {
            ReturnStatementSyntax { Expression: { } direct } => direct,
            BlockSyntax { Statements.Count: 1 } block when block.Statements[0] is
                ReturnStatementSyntax { Expression: { } nested } => nested,
            _ => null!
        };
        return expression != null;
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
                   TryCreateParameterSubstitutions(
                       GetLambdaParameterSymbols(lambda, context).ToArray(),
                       invocation,
                       context,
                       out var substitutions) &&
                   TryLowerReturnedBooleanSyntax(
                       lambda, Rebind(context, substitutions, nested: true), substitutions, out condition);
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
}
