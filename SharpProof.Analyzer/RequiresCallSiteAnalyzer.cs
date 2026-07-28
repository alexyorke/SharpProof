namespace SharpProof.Analyzer;

internal static class RequiresCallSiteAnalyzer {
    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        var callSites = GetReachableCallSites(
            declaration,
            semanticModel,
            cancellationToken);
        if (callSites == null)
            return AnalyzerSemanticOutcome.Unknown;

        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        foreach (var candidate in callSites) {
            cancellationToken.ThrowIfCancellationRequested();
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                AnalyzeCallSite(
                    caller,
                    candidate,
                    semanticModel,
                    session,
                    reportDiagnostic,
                    cancellationToken));
        }
        return outcome;
    }

    private static ImmutableArray<CallSiteCandidate>? GetReachableCallSites(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        ControlFlowGraph? graph;
        IOperation? operationRoot;
        try {
            var flowSyntax = GetPropertyExpression(declaration) ?? declaration; operationRoot = semanticModel.GetOperation(flowSyntax, cancellationToken);
            while (operationRoot?.Parent != null) operationRoot = operationRoot.Parent;
            graph = operationRoot switch {
                IMethodBodyOperation method => ControlFlowGraph.Create(method, cancellationToken),
                IConstructorBodyOperation constructor => ControlFlowGraph.Create(constructor, cancellationToken),
                IBlockOperation block => ControlFlowGraph.Create(block, cancellationToken),
                _ => ControlFlowGraph.Create(declaration, semanticModel, cancellationToken)
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException) {
            return null;
        }
        if (graph == null) return null;

        var definitelyExecuted = GetDefinitelyExecutedBlocks(graph);
        var callSites = new Dictionary<(SyntaxTree Tree, TextSpan Span), CallSiteCandidate>();
        var initializer = (operationRoot as IConstructorBodyOperation)?.Initializer;
        foreach (var block in graph.Blocks) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!block.IsReachable) continue;
            foreach (var root in block.Operations
                         .Concat(block.BranchValue == null
                              ? []
                              : [block.BranchValue])
                         .Concat(block.Ordinal == graph.Blocks[0].Ordinal && initializer != null ? [initializer] : [])) {
                foreach (var operation in root.DescendantsAndSelf()) {
                    var call = operation switch {
                        IInvocationOperation invocation => (
                            invocation.TargetMethod,
                            invocation.Instance,
                            invocation.Arguments),
                        IObjectCreationOperation {
                            Constructor: { } constructor
                        } creation => (
                            constructor,
                            (IOperation?)null,
                            creation.Arguments),
                        _ => ((IMethodSymbol, IOperation?,
                            ImmutableArray<IArgumentOperation>)?)null
                    };
                    if (call == null) continue;
                    var candidate = new CallSiteCandidate(
                        operation,
                        call.Value.Item1,
                        call.Value.Item2,
                        call.Value.Item3,
                        definitelyExecuted.Contains(block.Ordinal) &&
                        HasReplayablePrefix(
                            declaration,
                            operation,
                            semanticModel,
                            cancellationToken));
                    var key = (
                        operation.Syntax.SyntaxTree,
                        operation.Syntax.Span);
                    if (!callSites.TryGetValue(key, out var existing) ||
                        !existing.CanReplay && candidate.CanReplay)
                        callSites[key] = candidate;
                }
            }
        }
        return [.. callSites.Values
            .OrderBy(static candidate =>
                candidate.Operation.Syntax.SpanStart)];
    }

    private static HashSet<int> GetDefinitelyExecutedBlocks(
        ControlFlowGraph graph) {
        var result = new HashSet<int>();
        var pending = new Queue<BasicBlock>();
        pending.Enqueue(graph.Blocks[0]);
        while (pending.Count != 0) {
            var block = pending.Dequeue();
            if (!block.IsReachable || !result.Add(block.Ordinal))
                continue;
            if (block.ConditionKind == ControlFlowConditionKind.None) {
                EnqueueRegular(block.FallThroughSuccessor, pending);
                continue;
            }
            if (block.BranchValue?.ConstantValue is not
                { HasValue: true, Value: bool condition })
                continue;
            var takeConditional = block.ConditionKind switch {
                ControlFlowConditionKind.WhenTrue => condition,
                ControlFlowConditionKind.WhenFalse => !condition,
                _ => false
            };
            EnqueueRegular(
                takeConditional
                    ? block.ConditionalSuccessor
                    : block.FallThroughSuccessor,
                pending);
        }
        return result;
    }

    private static void EnqueueRegular(
        ControlFlowBranch? branch,
        Queue<BasicBlock> pending) {
        if (branch is {
            Semantics: ControlFlowBranchSemantics.Regular,
            Destination: { } destination
        })
            pending.Enqueue(destination);
    }

    private static bool HasReplayablePrefix(
        SyntaxNode declaration,
        IOperation callSite,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (declaration is BaseMethodDeclarationSyntax {
            ExpressionBody.Expression: { } expressionBody
        })
            return expressionBody.Span == callSite.Syntax.Span;
        if (declaration is AccessorDeclarationSyntax {
            ExpressionBody.Expression: { } accessorExpression
        })
            return accessorExpression.Span == callSite.Syntax.Span;
        var propertyExpression = GetPropertyExpression(declaration); if (propertyExpression != null)
            return propertyExpression.Span == callSite.Syntax.Span;
        if (declaration is ConstructorDeclarationSyntax constructor && callSite.Syntax is ConstructorInitializerSyntax initializer &&
            ReferenceEquals(initializer.Parent, constructor)) return true;

        var body = declaration switch {
            BaseMethodDeclarationSyntax method => method.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null
        };
        if (body == null) return false;
        var statement = callSite.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(candidate =>
                ReferenceEquals(candidate.Parent, body));
        if (statement == null ||
            statement switch {
                ExpressionStatementSyntax expression =>
                    expression.Expression.Span != callSite.Syntax.Span,
                ReturnStatementSyntax { Expression: { } returned } =>
                    returned.Span != callSite.Syntax.Span,
                _ => true
            })
            return false;
        foreach (var prior in body.Statements.TakeWhile(
                     candidate => !ReferenceEquals(candidate, statement)))
            if (prior is not (
                    EmptyStatementSyntax or
                    LocalFunctionStatementSyntax) &&
                !new NonThrowingAnalysis(
                        semanticModel.Compilation,
                        cancellationToken)
                    .IsDefinitelyNonThrowing(
                        semanticModel.GetOperation(prior, cancellationToken)))
                return false;
        return true;
    }

    private static ExpressionSyntax? GetPropertyExpression(SyntaxNode declaration) => declaration switch {
        PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
        IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
        _ => null
    };

    private sealed class NonThrowingAnalysis(
        Compilation compilation,
        CancellationToken cancellationToken) {
        private readonly HashSet<IMethodSymbol> _activeMethods = [];

        internal bool IsDefinitelyNonThrowing(IOperation? operation) {
            cancellationToken.ThrowIfCancellationRequested();
            if (operation == null) return false;
            switch (operation) {
                case ILiteralOperation:
                case ILocalReferenceOperation:
                case IParameterReferenceOperation:
                case IDiscardOperation:
                case IInstanceReferenceOperation:
                case IDefaultValueOperation:
                case ITypeOfOperation:
                case INameOfOperation:
                    return true;
                case IInvocationOperation invocation:
                    return !invocation.IsVirtual &&
                           (invocation.Instance == null ||
                            invocation.Instance is IInstanceReferenceOperation &&
                            IsDefinitelyNonThrowing(invocation.Instance)) &&
                           invocation.Arguments.All(argument =>
                               IsDefinitelyNonThrowing(argument.Value)) &&
                           IsDefinitelyNonThrowingSourceMethod(
                               invocation.TargetMethod);
                case ISimpleAssignmentOperation assignment:
                    return assignment.Target is
                               ILocalReferenceOperation or
                               IParameterReferenceOperation or
                               IDiscardOperation &&
                           IsDefinitelyNonThrowing(assignment.Value);
                case IBinaryOperation binary:
                    return binary.OperatorMethod == null &&
                           !binary.IsChecked &&
                           binary.OperatorKind is not (
                               BinaryOperatorKind.Divide or
                               BinaryOperatorKind.Remainder) &&
                           ChildrenAreDefinitelyNonThrowing(binary);
                case IUnaryOperation unary:
                    return unary.OperatorMethod == null &&
                           !unary.IsChecked &&
                           ChildrenAreDefinitelyNonThrowing(unary);
                case IConversionOperation conversion:
                    return conversion.OperatorMethod == null &&
                           !conversion.IsChecked &&
                           !conversion.Conversion.IsUserDefined &&
                           (conversion.Conversion.IsIdentity ||
                            conversion.Conversion.IsImplicit) &&
                           IsDefinitelyNonThrowing(conversion.Operand);
                case IBlockOperation:
                case IExpressionStatementOperation:
                case IReturnOperation:
                case IVariableDeclarationGroupOperation:
                case IVariableDeclarationOperation:
                case IVariableDeclaratorOperation:
                case IVariableInitializerOperation:
                case IArgumentOperation:
                case IParenthesizedOperation:
                case IConditionalOperation:
                    return ChildrenAreDefinitelyNonThrowing(operation);
                default:
                    return false;
            }
        }

        private bool ChildrenAreDefinitelyNonThrowing(IOperation operation) =>
            operation.ChildOperations.All(IsDefinitelyNonThrowing);

        private bool IsDefinitelyNonThrowingSourceMethod(IMethodSymbol method) {
            if (method.IsStatic && method.ContainingType.StaticConstructors.Length != 0) return false;
            var normalized = method.OriginalDefinition;
            if (normalized.DeclaringSyntaxReferences.Length != 1 ||
                !_activeMethods.Add(normalized))
                return false;
            try {
                var declaration = normalized.DeclaringSyntaxReferences[0]
                    .GetSyntax(cancellationToken);
                var semanticModel =
                    SharpProof.Frontend.Host.CompilationModelProvider
                        .GetSemanticModel(
                            compilation,
                            declaration.SyntaxTree);
                var body = declaration switch {
                    BaseMethodDeclarationSyntax methodDeclaration =>
                        (SyntaxNode?)methodDeclaration.Body ??
                        methodDeclaration.ExpressionBody?.Expression,
                    AccessorDeclarationSyntax accessor =>
                        (SyntaxNode?)accessor.Body ??
                        accessor.ExpressionBody?.Expression,
                    LocalFunctionStatementSyntax localFunction =>
                        (SyntaxNode?)localFunction.Body ??
                        localFunction.ExpressionBody?.Expression,
                    _ => null
                };
                return IsDefinitelyNonThrowing(
                    body == null
                        ? null
                        : semanticModel.GetOperation(body, cancellationToken));
            }
            finally {
                _activeMethods.Remove(normalized);
            }
        }
    }

    private static AnalyzerSemanticOutcome AnalyzeCallSite(
        IMethodSymbol caller,
        CallSiteCandidate candidate,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        if (!SymbolEqualityComparer.Default.Equals(
                semanticModel.GetEnclosingSymbol(
                    candidate.Operation.Syntax.SpanStart,
                    cancellationToken),
                caller))
            return AnalyzerSemanticOutcome.NotApplicable;
        if ((candidate.TargetMethod.IsStatic || candidate.TargetMethod.MethodKind == MethodKind.Constructor) && candidate.TargetMethod.ContainingType.StaticConstructors.Length != 0) return AnalyzerSemanticOutcome.Unknown;

        var factory = session.IrFactory;
        var binding = session.BindRequires(candidate.TargetMethod);
        if (!binding.IsSuccess || binding.Contracts == null)
            return AnalyzerSemanticOutcome.Unknown;
        var requires = binding.Contracts.Clauses
            .Where(static clause => clause.Kind == BoundContractKind.Requires)
            .ToImmutableArray();
        if (requires.IsDefaultOrEmpty)
            return AnalyzerSemanticOutcome.NotApplicable;
        if (!candidate.CanReplay)
            return AnalyzerSemanticOutcome.Unknown;
        if (candidate.TargetMethod.ReducedFrom != null ||
            candidate.TargetMethod.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None))
            return AnalyzerSemanticOutcome.Unknown;

        var replayPlan = CreateReplayPlan(
            candidate,
            binding.Contracts,
            factory,
            session.IsKnownPure,
            cancellationToken);
        if (replayPlan == null)
            return AnalyzerSemanticOutcome.Unknown;

        var interpreter = new IrInterpreter(factory);
        foreach (var input in replayPlan.Inputs) {
            var replayedInput = interpreter.Evaluate(
                input.Term,
                cancellationToken: cancellationToken);
            if (replayedInput.Status != IrEvaluationStatus.Value ||
                input.IsReceiver &&
                replayedInput.Value?.Kind == IrValueKind.Null)
                return AnalyzerSemanticOutcome.Unknown;
        }
        var printer = new IrPrinter(factory);
        var outcome = AnalyzerSemanticOutcome.Proven;
        foreach (var clause in requires) {
            cancellationToken.ThrowIfCancellationRequested();
            IrTerm instantiated;
            try {
                instantiated = IrSubstitution.Substitute(
                    factory,
                    clause.Condition,
                    replayPlan.Substitutions);
            }
            catch (ArgumentException) {
                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    AnalyzerSemanticOutcome.Unknown);
                continue;
            }
            var replay = interpreter.Evaluate(
                instantiated,
                cancellationToken: cancellationToken);
            if (replay.Status != IrEvaluationStatus.Value ||
                replay.Value?.Kind != IrValueKind.Boolean) {
                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    AnalyzerSemanticOutcome.Unknown);
                continue;
            }
            if (replay.Value.Boolean) continue;
            outcome = AnalyzerSemanticOutcome.Refuted;
            reportDiagnostic(
                Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.RequiresNotProvenRule,
                    candidate.Operation.Syntax.GetLocation(),
                    candidate.TargetMethod.Name,
                    printer.Print(instantiated)));
        }
        return outcome;
    }

    private static InvocationReplayPlan? CreateReplayPlan(
        CallSiteCandidate callSite,
        BoundMethodContracts contracts,
        IrFactory factory,
        Func<IMethodSymbol, bool> isKnownPure,
        CancellationToken cancellationToken) {
        var lowerer = new RoslynOperationLowerer(
            factory,
            isKnownPure);
        var inputs = ImmutableArray.CreateBuilder<(
            IrTerm Term,
            bool IsReceiver)>();
        if (callSite.Instance != null) {
            var receiver = lowerer.Lower(callSite.Instance);
            if (!receiver.IsExact) return null;
            if (callSite.Instance is not IInstanceReferenceOperation)
                inputs.Add((receiver.Term, true));
        }
        foreach (var argument in callSite.Arguments
                     .OrderBy(static argument =>
                         argument.IsImplicit ? 1 : 0)
                     .ThenBy(static argument =>
                         argument.IsImplicit
                             ? argument.Parameter?.Ordinal ?? int.MaxValue
                             : argument.Syntax.SpanStart)) {
            cancellationToken.ThrowIfCancellationRequested();
            var loweredArgument = lowerer.Lower(argument.Value);
            if (!loweredArgument.IsExact) return null;
            inputs.Add((loweredArgument.Term, false));
        }
        var substitutions = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in contracts.Variables) {
            cancellationToken.ThrowIfCancellationRequested();
            if (variable.Role is
                BoundContractVariableRole.Result or
                BoundContractVariableRole.PreState)
                continue;
            IOperation? actual = variable.Role switch {
                BoundContractVariableRole.Receiver => callSite.Instance,
                BoundContractVariableRole.Parameter =>
                    callSite.Arguments.FirstOrDefault(argument =>
                        argument.Parameter?.Ordinal == variable.Ordinal)?.Value,
                _ => null
            };
            if (actual == null) return null;
            var lowered = lowerer.Lower(actual);
            if (!lowered.IsExact) return null;
            var expected = factory.GetVariableInfo(variable.Variable).Type;
            if (lowered.Term.Type != expected) return null;
            substitutions.Add(variable.Variable, lowered.Term);
        }
        return new InvocationReplayPlan(
            substitutions,
            inputs.ToImmutable());
    }

    private readonly struct CallSiteCandidate(
        IOperation operation,
        IMethodSymbol targetMethod,
        IOperation? instance,
        ImmutableArray<IArgumentOperation> arguments,
        bool canReplay) {
        internal IOperation Operation { get; } = operation;
        internal IMethodSymbol TargetMethod { get; } = targetMethod;
        internal IOperation? Instance { get; } = instance;
        internal ImmutableArray<IArgumentOperation> Arguments { get; } = arguments;
        internal bool CanReplay { get; } = canReplay;
    }

    private sealed class InvocationReplayPlan(
        IReadOnlyDictionary<IrVarId, IrTerm> substitutions,
        ImmutableArray<(IrTerm Term, bool IsReceiver)> inputs) {
        internal IReadOnlyDictionary<IrVarId, IrTerm> Substitutions { get; } =
            substitutions;
        internal ImmutableArray<(IrTerm Term, bool IsReceiver)> Inputs { get; } =
            inputs;
    }
}
