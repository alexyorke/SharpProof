namespace SharpProof.Analyzer;

internal static class RequiresCallSiteAnalyzer
{
    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        return new Analysis(caller, declaration, semanticModel, session, reportDiagnostic, cancellationToken).Run();
    }

    private sealed class Analysis(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        private readonly IrFactory _factory = session.IrFactory;

        internal AnalyzerSemanticOutcome Run()
        {
            var binding = session.BindRequires(caller);
            var callSites = GetReachableCallSites(binding.IsSuccess ? binding.Contracts : null);
            if (callSites == null)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var outcome = AnalyzerSemanticOutcome.NotApplicable;
            foreach (var candidate in callSites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                outcome = AnalyzerSemanticOutcomes.Combine(outcome, AnalyzeCallSite(candidate));
            }
            return outcome;
        }

        private ImmutableArray<CallSiteCandidate>? GetReachableCallSites(BoundMethodContracts? callerContracts)
        {
            if (!TryCreateGraph(out var operationRoot, out var graph))
            {
                return null;
            }

            var managedFlow = ManagedAbstractFlow.ForCompilation(semanticModel.Compilation);
            var entryState = ManagedContractFacts.ApplyRequires(
                managedFlow.CreateEntryState(caller), callerContracts);
            var flowAnalysis = managedFlow.Analyze(caller, graph, entryState, cancellationToken);
            var flowResult = flowAnalysis.Result;
            var callSites = new Dictionary<(SyntaxTree Tree, TextSpan Span), CallSiteCandidate>();
            var initializer = (operationRoot as IConstructorBodyOperation)?.Initializer;
            var operationFacts = new DefiniteOperationFacts(semanticModel.Compilation, cancellationToken);
            foreach (var block in graph.Blocks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!block.IsReachable)
                {
                    continue;
                }

                var roots = block.Operations
                    .Concat(block.BranchValue == null ? [] : [block.BranchValue])
                    .Concat(block.Ordinal == graph.Blocks[0].Ordinal && initializer != null ? [initializer] : []);
                foreach (var operation in roots.SelectMany(static root => root.DescendantsAndSelf()))
                {
                    var call = GetCall(operation);
                    if (call == null)
                    {
                        continue;
                    }

                    var hasFlowState = flowResult?.TryGetState(operation, out _) == true;
                    if (flowAnalysis.IsComplete &&
                        !hasFlowState &&
                        !IsInsideExceptionHandler(operation))
                    {
                        continue;
                    }

                    var candidate = new CallSiteCandidate(
                        operation,
                        call.Value.TargetMethod,
                        call.Value.Instance,
                        call.Value.Arguments,
                        (hasFlowState || !flowAnalysis.IsComplete) &&
                        HasReplayablePrefix(operation, operationFacts),
                        hasFlowState ? flowResult : null,
                        flowAnalysis.Status);
                    var key = (operation.Syntax.SyntaxTree, operation.Syntax.Span);
                    if (!callSites.TryGetValue(key, out var existing) ||
                        !existing.CanReplay && candidate.CanReplay)
                    {
                        callSites[key] = candidate;
                    }
                }
            }
            return [.. callSites.Values.OrderBy(static candidate => candidate.Operation.Syntax.SpanStart)];
        }

        private bool TryCreateGraph(out IOperation? operationRoot, out ControlFlowGraph graph)
        {
            try
            {
                var flowSyntax = GetPropertyExpression(declaration) ?? declaration;
                operationRoot = semanticModel.GetOperation(flowSyntax, cancellationToken);
                while (operationRoot?.Parent != null)
                {
                    operationRoot = operationRoot.Parent;
                }

                var created = operationRoot switch
                {
                    IMethodBodyOperation method =>
                        ControlFlowGraph.Create(method, cancellationToken),
                    IConstructorBodyOperation constructor =>
                        ControlFlowGraph.Create(constructor, cancellationToken),
                    IBlockOperation block =>
                        ControlFlowGraph.Create(block, cancellationToken),
                    _ => ControlFlowGraph.Create(declaration, semanticModel, cancellationToken)
                };
                if (created == null)
                {
                    graph = null!;
                    return false;
                }
                graph = created;
                return true;
            }
            catch (Exception exception) when (
                exception is ArgumentException or InvalidOperationException)
            {
                operationRoot = null;
                graph = null!;
                return false;
            }
        }

        private bool HasReplayablePrefix(
            IOperation callSite,
            DefiniteOperationFacts operationFacts)
        {
            if (declaration is BaseMethodDeclarationSyntax
                {
                    ExpressionBody.Expression: { } expressionBody
                })
            {
                return expressionBody.Span == callSite.Syntax.Span;
            }

            if (declaration is AccessorDeclarationSyntax
                {
                    ExpressionBody.Expression: { } accessorExpression
                })
            {
                return accessorExpression.Span == callSite.Syntax.Span;
            }

            var propertyExpression = GetPropertyExpression(declaration);
            if (propertyExpression != null)
            {
                return propertyExpression.Span == callSite.Syntax.Span;
            }

            if (declaration is ConstructorDeclarationSyntax constructor &&
                callSite.Syntax is ConstructorInitializerSyntax initializer &&
                ReferenceEquals(initializer.Parent, constructor))
            {
                return true;
            }

            var body = declaration switch
            {
                BaseMethodDeclarationSyntax method => method.Body,
                AccessorDeclarationSyntax accessor => accessor.Body,
                _ => null
            };
            if (body == null)
            {
                return false;
            }

            var statement = callSite.Syntax.AncestorsAndSelf().OfType<StatementSyntax>()
                .FirstOrDefault(candidate => ReferenceEquals(candidate.Parent, body));
            return statement != null &&
                   IsDirectReplayableStatement(statement, callSite, operationFacts) &&
                   body.Statements
                       .TakeWhile(candidate => !ReferenceEquals(candidate, statement))
                       .All(prior =>
                           prior is EmptyStatementSyntax or LocalFunctionStatementSyntax ||
                           operationFacts.CompletesNormally(
                               semanticModel.GetOperation(prior, cancellationToken)));
        }

        private bool IsDirectReplayableStatement(
            StatementSyntax statement,
            IOperation callSite,
            DefiniteOperationFacts operationFacts)
        {
            var span = callSite.Syntax.Span;
            return statement switch
            {
                ExpressionStatementSyntax
                {
                    Expression: AssignmentExpressionSyntax assignment
                } when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) =>
                    assignment.Right.Span == span &&
                    operationFacts.CompletesNormally(
                        semanticModel.GetOperation(assignment.Left, cancellationToken)),
                ExpressionStatementSyntax expression => expression.Expression.Span == span,
                LocalDeclarationStatementSyntax local =>
                    local.Declaration.Variables.Count == 1 &&
                    local.Declaration.Variables[0].Initializer?.Value.Span == span,
                ReturnStatementSyntax returned => returned.Expression?.Span == span,
                ThrowStatementSyntax thrown => thrown.Expression?.Span == span,
                _ => false
            };
        }

        private AnalyzerSemanticOutcome AnalyzeCallSite(CallSiteCandidate candidate)
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetEnclosingSymbol(
                        candidate.Operation.Syntax.SpanStart, cancellationToken),
                    caller))
            {
                return AnalyzerSemanticOutcome.NotApplicable;
            }

            if ((candidate.TargetMethod.IsStatic ||
                 candidate.TargetMethod.MethodKind == MethodKind.Constructor) &&
                candidate.TargetMethod.ContainingType.StaticConstructors.Length != 0)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var binding = session.BindRequires(candidate.TargetMethod);
            if (!binding.IsSuccess || binding.Contracts == null)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var requires = binding.Contracts.Clauses
                .Where(static clause => clause.Kind == BoundContractKind.Requires)
                .ToImmutableArray();
            if (requires.IsDefaultOrEmpty)
            {
                return AnalyzerSemanticOutcome.NotApplicable;
            }

            if (!candidate.CanReplay)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var concrete = AnalyzeConcreteCall(candidate, binding.Contracts, requires);
            if (candidate.FlowStatus != ManagedFlowStatus.Complete)
            {
                return concrete == AnalyzerSemanticOutcome.Refuted
                    ? AnalyzerSemanticOutcome.Refuted
                    : AnalyzerSemanticOutcome.Unknown;
            }

            return concrete ?? AnalyzeAbstractCallSite(candidate, binding.Contracts, requires);
        }

        private AnalyzerSemanticOutcome AnalyzeAbstractCallSite(
            CallSiteCandidate candidate,
            BoundMethodContracts contracts,
            ImmutableArray<BoundContractClause> requires)
        {
            if (candidate.Flow == null)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var operationFacts = new DefiniteOperationFacts(semanticModel.Compilation, cancellationToken);
            if (candidate.Instance != null &&
                candidate.Instance is not IInstanceReferenceOperation &&
                !operationFacts.CompletesNormally(candidate.Instance) ||
                candidate.Arguments.Any(argument =>
                    !operationFacts.CompletesNormally(argument.Value)))
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var variables = new Dictionary<IrVarId, ManagedAbstractValue>();
            foreach (var variable in GetInputVariables(contracts))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var actual = GetActual(candidate, variable);
                if (actual == null ||
                    !candidate.Flow.TryEvaluate(candidate.Operation, actual, out var value))
                {
                    return AnalyzerSemanticOutcome.Unknown;
                }

                if (variable.Role == BoundContractVariableRole.Receiver &&
                    actual.Type?.IsReferenceType == true &&
                    !value.IsDefinitelyNonNull)
                {
                    return AnalyzerSemanticOutcome.Unknown;
                }

                variables.Add(variable.Variable, value);
            }

            return CompleteEvaluation(
                candidate,
                requires.Select(clause =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = ManagedContractFacts.Evaluate(clause.Condition, variables);
                    return new ClauseEvaluation(
                        value.TryGetBoolean(out var proven) ? proven : null,
                        clause.Condition);
                }));
        }

        private AnalyzerSemanticOutcome? AnalyzeConcreteCall(
            CallSiteCandidate callSite,
            BoundMethodContracts contracts,
            ImmutableArray<BoundContractClause> requires)
        {
            if (callSite.TargetMethod.ReducedFrom != null ||
                callSite.TargetMethod.Parameters.Any(
                    static parameter => parameter.RefKind != RefKind.None))
            {
                return null;
            }

            var lowerer = new RoslynOperationLowerer(_factory, session.IsKnownPure);
            var interpreter = new IrInterpreter(_factory);
            var substitutions = new Dictionary<IrVarId, IrTerm>();
            foreach (var variable in GetInputVariables(contracts))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var actual = GetActual(callSite, variable);
                if (actual == null)
                {
                    return null;
                }

                var lowered = lowerer.Lower(actual);
                var value = interpreter.Evaluate(
                    lowered.Term, cancellationToken: cancellationToken);
                if (!lowered.IsExact ||
                    lowered.Term.Type != _factory.GetVariableInfo(variable.Variable).Type ||
                    value.Status != IrEvaluationStatus.Value ||
                    variable.Role == BoundContractVariableRole.Receiver &&
                    value.Value?.Kind == IrValueKind.Null)
                {
                    return null;
                }

                substitutions.Add(variable.Variable, lowered.Term);
            }

            var evaluations = ImmutableArray.CreateBuilder<ClauseEvaluation>(requires.Length);
            foreach (var clause in requires)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IrTerm condition;
                try
                {
                    condition = IrSubstitution.Substitute(
                        _factory, clause.Condition, substitutions);
                }
                catch (ArgumentException)
                {
                    return null;
                }
                var value = interpreter.Evaluate(
                    condition, cancellationToken: cancellationToken);
                if (value.Status != IrEvaluationStatus.Value ||
                    value.Value?.Kind != IrValueKind.Boolean)
                {
                    return null;
                }

                evaluations.Add(new ClauseEvaluation(value.Value.Boolean, condition));
            }
            return CompleteEvaluation(callSite, evaluations);
        }

        private AnalyzerSemanticOutcome CompleteEvaluation(
            CallSiteCandidate callSite,
            IEnumerable<ClauseEvaluation> evaluations)
        {
            var outcome = AnalyzerSemanticOutcome.Proven;
            IrPrinter? printer = null;
            foreach (var evaluation in evaluations)
            {
                if (!evaluation.Value.HasValue)
                {
                    outcome = AnalyzerSemanticOutcomes.Combine(
                        outcome, AnalyzerSemanticOutcome.Unknown);
                }
                else if (!evaluation.Value.Value)
                {
                    outcome = AnalyzerSemanticOutcome.Refuted;
                    printer ??= new IrPrinter(_factory);
                    reportDiagnostic(Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.RequiresNotProvenRule,
                        callSite.Operation.Syntax.GetLocation(),
                        callSite.TargetMethod.Name,
                        printer.Print(evaluation.Condition)));
                }
            }
            return outcome;
        }
    }

    private static IEnumerable<BoundContractVariable> GetInputVariables(
        BoundMethodContracts contracts)
    {
        return contracts.Variables.Where(static variable =>
            variable.Role is not (
                BoundContractVariableRole.Result or
                BoundContractVariableRole.PreState));
    }

    private static IOperation? GetActual(
        CallSiteCandidate callSite,
        BoundContractVariable variable)
    {
        return variable.Role switch
        {
            BoundContractVariableRole.Receiver => callSite.Instance,
            BoundContractVariableRole.Parameter =>
                callSite.Arguments.FirstOrDefault(argument =>
                    argument.Parameter?.Ordinal == variable.Ordinal)?.Value,
            _ => null
        };
    }

    private static CallTarget? GetCall(IOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => new(
                invocation.TargetMethod, invocation.Instance, invocation.Arguments),
            IObjectCreationOperation { Constructor: { } constructor } creation =>
                new(constructor, null, creation.Arguments),
            _ => null
        };
    }

    private static ExpressionSyntax? GetPropertyExpression(SyntaxNode declaration)
    {
        return declaration switch
        {
            PropertyDeclarationSyntax property => property.ExpressionBody?.Expression,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody?.Expression,
            _ => null
        };
    }

    private static bool IsInsideExceptionHandler(IOperation operation)
    {
        return operation.Syntax.AncestorsAndSelf().Any(static syntax =>
            syntax is CatchClauseSyntax or CatchFilterClauseSyntax or FinallyClauseSyntax);
    }

    private readonly record struct CallTarget(
        IMethodSymbol TargetMethod,
        IOperation? Instance,
        ImmutableArray<IArgumentOperation> Arguments);

    private readonly record struct CallSiteCandidate(
        IOperation Operation,
        IMethodSymbol TargetMethod,
        IOperation? Instance,
        ImmutableArray<IArgumentOperation> Arguments,
        bool CanReplay,
        ManagedFlowResult? Flow,
        ManagedFlowStatus FlowStatus);

    private readonly record struct ClauseEvaluation(bool? Value, IrTerm Condition);
}
