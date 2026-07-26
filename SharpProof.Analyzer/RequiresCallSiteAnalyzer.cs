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
                        definitelyExecuted.Contains(block.Ordinal));
                    if (!invocations.TryGetValue(key, out var existing) ||
                        !existing.IsDefinitelyExecuted &&
                        candidate.IsDefinitelyExecuted)
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

    private static AnalyzerSemanticOutcome AnalyzeInvocation(
        IMethodSymbol caller,
        IInvocationOperation invocation,
        bool isDefinitelyExecuted,
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
        if (!isDefinitelyExecuted)
            return AnalyzerSemanticOutcome.Unknown;
        if (invocation.TargetMethod.ReducedFrom != null ||
            invocation.TargetMethod.Parameters.Any(static parameter =>
                parameter.RefKind != RefKind.None))
            return AnalyzerSemanticOutcome.Unknown;

        var substitutions = CreateSubstitutions(
            invocation,
            binding.Contracts,
            factory,
            session.IsKnownPure,
            cancellationToken);
        if (substitutions == null)
            return AnalyzerSemanticOutcome.Unknown;

        var interpreter = new IrInterpreter(factory);
        var printer = new IrPrinter(factory);
        var outcome = AnalyzerSemanticOutcome.Proven;
        foreach (var clause in requires) {
            cancellationToken.ThrowIfCancellationRequested();
            IrTerm instantiated;
            try {
                instantiated = IrSubstitution.Substitute(
                    factory,
                    clause.Condition,
                    substitutions);
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

    private static IReadOnlyDictionary<IrVarId, IrTerm>? CreateSubstitutions(
        IInvocationOperation invocation,
        BoundMethodContracts contracts,
        IrFactory factory,
        Func<IMethodSymbol, bool> isKnownPure,
        CancellationToken cancellationToken) {
        var lowerer = new RoslynOperationLowerer(
            factory,
            isKnownPure);
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
        return substitutions;
    }

    private readonly struct InvocationCandidate(
        IInvocationOperation invocation,
        bool isDefinitelyExecuted) {
        internal IInvocationOperation Invocation { get; } = invocation;
        internal bool IsDefinitelyExecuted { get; } = isDefinitelyExecuted;
    }
}
