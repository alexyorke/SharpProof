namespace SharpProof.Analyzer;

internal static class RequiresCallSiteAnalyzer {
    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        var invocations = GetReachableInvocations(
            declaration,
            semanticModel,
            cancellationToken);
        if (invocations == null)
            return AnalyzerSemanticOutcome.Unknown;

        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        foreach (var candidate in invocations) {
            cancellationToken.ThrowIfCancellationRequested();
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                AnalyzeInvocation(
                    caller,
                    candidate.Invocation,
                    candidate.IsDefinitelyExecuted,
                    candidate.HasReplayablePrefix,
                    semanticModel,
                    session,
                    reportDiagnostic,
                    cancellationToken));
        }
        return outcome;
    }

    private static ImmutableArray<InvocationCandidate>? GetReachableInvocations(
        SyntaxNode declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        ControlFlowGraph? graph;
        try {
            graph = ControlFlowGraph.Create(
                declaration,
                semanticModel,
                cancellationToken);
        }
        catch (ArgumentException) {
            return null;
        }
        catch (InvalidOperationException) {
            return null;
        }
        if (graph == null)
            return null;

        var definitelyExecuted = GetDefinitelyExecutedBlocks(graph);
        var invocations = new Dictionary<
            (SyntaxTree Tree, TextSpan Span),
            InvocationCandidate>();
        foreach (var block in graph.Blocks) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!block.IsReachable) continue;
            foreach (var root in block.Operations
                         .Concat(block.BranchValue == null
                             ? []
                             : [block.BranchValue])) {
                foreach (var invocation in root.DescendantsAndSelf()
                             .OfType<IInvocationOperation>()) {
                    var key = (
                        invocation.Syntax.SyntaxTree,
                        invocation.Syntax.Span);
                    var candidate = new InvocationCandidate(
                        invocation,
                        definitelyExecuted.Contains(block.Ordinal),
                        HasReplayablePrefix(
                            declaration,
                            invocation,
                            semanticModel,
                            cancellationToken));
                    if (!invocations.TryGetValue(key, out var existing) ||
                        !existing.CanReplay && candidate.CanReplay)
                        invocations[key] = candidate;
                }
            }
        }
        return [.. invocations.Values
            .OrderBy(static candidate =>
                candidate.Invocation.Syntax.SpanStart)];
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
        IInvocationOperation invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken) {
        if (declaration is BaseMethodDeclarationSyntax {
            ExpressionBody.Expression: { } expressionBody
        })
            return expressionBody.Span == invocation.Syntax.Span;
        if (declaration is AccessorDeclarationSyntax {
            ExpressionBody.Expression: { } accessorExpression
        })
            return accessorExpression.Span == invocation.Syntax.Span;

        var body = declaration switch {
            BaseMethodDeclarationSyntax method => method.Body,
            AccessorDeclarationSyntax accessor => accessor.Body,
            _ => null
        };
        if (body == null) return false;
        var statement = invocation.Syntax.AncestorsAndSelf()
            .OfType<StatementSyntax>()
            .FirstOrDefault(candidate =>
                ReferenceEquals(candidate.Parent, body));
        if (statement == null ||
            statement switch {
                ExpressionStatementSyntax expression =>
                    expression.Expression.Span != invocation.Syntax.Span,
                ReturnStatementSyntax { Expression: { } returned } =>
                    returned.Span != invocation.Syntax.Span,
                _ => true
            })
            return false;
        foreach (var prior in body.Statements.TakeWhile(
                     candidate => !ReferenceEquals(candidate, statement)))
            if (prior is not (
                    EmptyStatementSyntax or
                    LocalFunctionStatementSyntax) &&
                !IsDefinitelyNonThrowing(
                    semanticModel.GetOperation(prior, cancellationToken),
                    semanticModel.Compilation,
                    [],
                    cancellationToken))
                return false;
        return true;
    }

    private static bool IsDefinitelyNonThrowing(
        IOperation? operation,
        Compilation compilation,
        HashSet<IMethodSymbol> activeMethods,
        CancellationToken cancellationToken) {
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
                        IsDefinitelyNonThrowing(
                            invocation.Instance,
                            compilation,
                            activeMethods,
                            cancellationToken)) &&
                       invocation.Arguments.All(argument =>
                           IsDefinitelyNonThrowing(
                               argument.Value,
                               compilation,
                               activeMethods,
                               cancellationToken)) &&
                       IsDefinitelyNonThrowingSourceMethod(
                           invocation.TargetMethod,
                           compilation,
                           activeMethods,
                           cancellationToken);
            case ISimpleAssignmentOperation assignment:
                return assignment.Target is
                           ILocalReferenceOperation or
                           IParameterReferenceOperation or
                           IDiscardOperation &&
                       IsDefinitelyNonThrowing(
                           assignment.Value,
                           compilation,
                           activeMethods,
                           cancellationToken);
            case IBinaryOperation binary:
                return binary.OperatorMethod == null &&
                       !binary.IsChecked &&
                       binary.OperatorKind is not (
                           BinaryOperatorKind.Divide or
                           BinaryOperatorKind.Remainder) &&
                       ChildrenAreDefinitelyNonThrowing(
                           binary,
                           compilation,
                           activeMethods,
                           cancellationToken);
            case IUnaryOperation unary:
                return unary.OperatorMethod == null &&
                       !unary.IsChecked &&
                       ChildrenAreDefinitelyNonThrowing(
                           unary,
                           compilation,
                           activeMethods,
                           cancellationToken);
            case IConversionOperation conversion:
                return conversion.OperatorMethod == null &&
                       !conversion.IsChecked &&
                       !conversion.Conversion.IsUserDefined &&
                       (conversion.Conversion.IsIdentity ||
                        conversion.Conversion.IsImplicit) &&
                       IsDefinitelyNonThrowing(
                           conversion.Operand,
                           compilation,
                           activeMethods,
                           cancellationToken);
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
                return ChildrenAreDefinitelyNonThrowing(
                    operation,
                    compilation,
                    activeMethods,
                    cancellationToken);
            default:
                return false;
        }
    }

    private static bool ChildrenAreDefinitelyNonThrowing(
        IOperation operation,
        Compilation compilation,
        HashSet<IMethodSymbol> activeMethods,
        CancellationToken cancellationToken) =>
        operation.ChildOperations.All(child =>
            IsDefinitelyNonThrowing(
                child,
                compilation,
                activeMethods,
                cancellationToken));

    private static bool IsDefinitelyNonThrowingSourceMethod(
        IMethodSymbol method,
        Compilation compilation,
        HashSet<IMethodSymbol> activeMethods,
        CancellationToken cancellationToken) {
        var normalized = method.OriginalDefinition;
        if (normalized.DeclaringSyntaxReferences.Length != 1 ||
            !activeMethods.Add(normalized))
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
                BaseMethodDeclarationSyntax {
                    Body: { } block
                } => semanticModel.GetOperation(block, cancellationToken),
                BaseMethodDeclarationSyntax {
                    ExpressionBody.Expression: { } expression
                } => semanticModel.GetOperation(expression, cancellationToken),
                AccessorDeclarationSyntax {
                    Body: { } block
                } => semanticModel.GetOperation(block, cancellationToken),
                AccessorDeclarationSyntax {
                    ExpressionBody.Expression: { } expression
                } => semanticModel.GetOperation(expression, cancellationToken),
                LocalFunctionStatementSyntax {
                    Body: { } block
                } => semanticModel.GetOperation(block, cancellationToken),
                LocalFunctionStatementSyntax {
                    ExpressionBody.Expression: { } expression
                } => semanticModel.GetOperation(expression, cancellationToken),
                _ => null
            };
            return IsDefinitelyNonThrowing(
                body,
                compilation,
                activeMethods,
                cancellationToken);
        }
        finally {
            activeMethods.Remove(normalized);
        }
    }

    private static AnalyzerSemanticOutcome AnalyzeInvocation(
        IMethodSymbol caller,
        IInvocationOperation invocation,
        bool isDefinitelyExecuted,
        bool hasReplayablePrefix,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken) {
        if (!SymbolEqualityComparer.Default.Equals(
                semanticModel.GetEnclosingSymbol(
                    invocation.Syntax.SpanStart,
                    cancellationToken),
                caller))
            return AnalyzerSemanticOutcome.NotApplicable;

        var factory = session.IrFactory;
        var binding = new ContractBinder(session.Compilation, factory)
            .BindRequires(invocation.TargetMethod);
        if (!binding.IsSuccess || binding.Contracts == null)
            return AnalyzerSemanticOutcome.Unknown;
        var requires = binding.Contracts.Clauses
            .Where(static clause => clause.Kind == BoundContractKind.Requires)
            .ToImmutableArray();
        if (requires.IsDefaultOrEmpty)
            return AnalyzerSemanticOutcome.NotApplicable;
        if (!isDefinitelyExecuted || !hasReplayablePrefix)
            return AnalyzerSemanticOutcome.Unknown;
        if (invocation.TargetMethod.ReducedFrom != null ||
            invocation.TargetMethod.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None))
            return AnalyzerSemanticOutcome.Unknown;

        var replayPlan = CreateReplayPlan(
            invocation,
            binding.Contracts,
            factory,
            session.IsKnownPure,
            cancellationToken);
        if (replayPlan == null)
            return AnalyzerSemanticOutcome.Unknown;

        var interpreter = new IrInterpreter(factory);
        foreach (var input in replayPlan.Inputs) {
            var replayedInput = interpreter.Evaluate(input.Term);
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
            var replay = interpreter.Evaluate(instantiated);
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
                    invocation.Syntax.GetLocation(),
                    invocation.TargetMethod.Name,
                    printer.Print(instantiated)));
        }
        return outcome;
    }

    private static InvocationReplayPlan? CreateReplayPlan(
        IInvocationOperation invocation,
        BoundMethodContracts contracts,
        IrFactory factory,
        Func<IMethodSymbol, bool> isKnownPure,
        CancellationToken cancellationToken) {
        var lowerer = new RoslynOperationLowerer(
            factory,
            isKnownPure);
        var inputs = ImmutableArray.CreateBuilder<InvocationReplayInput>();
        if (invocation.Instance != null) {
            var receiver = lowerer.Lower(invocation.Instance);
            if (!receiver.IsExact) return null;
            inputs.Add(new InvocationReplayInput(receiver.Term, true));
        }
        foreach (var argument in invocation.Arguments
                     .OrderBy(static argument =>
                         argument.IsImplicit ? 1 : 0)
                     .ThenBy(static argument =>
                         argument.IsImplicit
                             ? argument.Parameter?.Ordinal ?? int.MaxValue
                             : argument.Syntax.SpanStart)) {
            cancellationToken.ThrowIfCancellationRequested();
            var loweredArgument = lowerer.Lower(argument.Value);
            if (!loweredArgument.IsExact) return null;
            inputs.Add(new InvocationReplayInput(
                loweredArgument.Term,
                false));
        }
        var substitutions = new Dictionary<IrVarId, IrTerm>();
        foreach (var variable in contracts.Variables) {
            cancellationToken.ThrowIfCancellationRequested();
            if (variable.Role is
                BoundContractVariableRole.Result or
                BoundContractVariableRole.PreState)
                continue;
            IOperation? actual = variable.Role switch {
                BoundContractVariableRole.Receiver => invocation.Instance,
                BoundContractVariableRole.Parameter =>
                    invocation.Arguments.FirstOrDefault(argument =>
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

    private readonly struct InvocationCandidate(
        IInvocationOperation invocation,
        bool isDefinitelyExecuted,
        bool hasReplayablePrefix) {
        internal IInvocationOperation Invocation { get; } = invocation;
        internal bool IsDefinitelyExecuted { get; } = isDefinitelyExecuted;
        internal bool HasReplayablePrefix { get; } = hasReplayablePrefix;
        internal bool CanReplay => IsDefinitelyExecuted && HasReplayablePrefix;
    }

    private sealed class InvocationReplayPlan(
        IReadOnlyDictionary<IrVarId, IrTerm> substitutions,
        ImmutableArray<InvocationReplayInput> inputs) {
        internal IReadOnlyDictionary<IrVarId, IrTerm> Substitutions { get; } =
            substitutions;
        internal ImmutableArray<InvocationReplayInput> Inputs { get; } =
            inputs;
    }

    private readonly struct InvocationReplayInput(
        IrTerm term,
        bool isReceiver) {
        internal IrTerm Term { get; } = term;
        internal bool IsReceiver { get; } = isReceiver;
    }
}
