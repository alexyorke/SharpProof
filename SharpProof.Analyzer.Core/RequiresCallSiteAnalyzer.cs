namespace SharpProof.Analyzer;

internal static partial class RequiresCallSiteAnalyzer
{
    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        return RequiresCallSiteTreeAnalyzer.Analyze(
            caller,
            declaration,
            semanticModel,
            session,
            reportDiagnostic,
            cancellationToken);
    }

    internal static AnalyzerSemanticOutcome AnalyzeCallable(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        ControlFlowGraph? graph,
        IOperation? operationRoot,
        bool screenForPotentialCalls,
        CancellationToken cancellationToken)
    {
        return new Analysis(
                caller,
                declaration,
                semanticModel,
                session,
                reportDiagnostic,
                graph,
                operationRoot,
                cancellationToken)
            .Run(screenForPotentialCalls);
    }

    internal static AnalyzerSemanticOutcome AnalyzePrimaryConstructorInitializer(
        IMethodSymbol constructor,
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var initializer = declaration.BaseList?.Types
            .OfType<PrimaryConstructorBaseTypeSyntax>()
            .SingleOrDefault();
        if (initializer == null)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }

        var target = semanticModel.GetSymbolInfo(
                initializer,
                cancellationToken)
            .Symbol as IMethodSymbol;
        var arguments = initializer.ArgumentList.Arguments
            .Select(argument => semanticModel.GetOperation(
                argument,
                cancellationToken) as IArgumentOperation)
            .ToImmutableArray();
        if (target == null || arguments.IsDefaultOrEmpty ||
            arguments.Any(static argument => argument == null))
        {
            return AnalyzerSemanticOutcome.Unknown;
        }

        var call = new RequiresCallSiteCandidate(
            arguments[0]!,
            target,
            Instance: null,
            arguments.OfType<IArgumentOperation>().ToImmutableArray(),
            CanReplay: true,
            Flow: null,
            ManagedFlowStatus.BudgetExceeded);

        return new Analysis(
                constructor,
                declaration,
                semanticModel,
                session,
                reportDiagnostic,
                graph: null,
                operationRoot: null,
                cancellationToken)
            .AnalyzeCallSite(call);
    }

    private sealed class Analysis(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        ControlFlowGraph? graph,
        IOperation? operationRoot,
        CancellationToken cancellationToken)
    {
        private readonly IrFactory _factory = session.IrFactory;
        private readonly RequiresCallSiteDiscovery _discovery =
            new(
                caller,
                declaration,
                semanticModel,
                cancellationToken,
                graph,
                operationRoot);

        internal AnalyzerSemanticOutcome Run(
            bool screenForPotentialCalls)
        {
            if (screenForPotentialCalls &&
                !_discovery.HasPotentialCallSite(
                    session.HasPotentialCallPreconditions))
            {
                return AnalyzerSemanticOutcome.NotApplicable;
            }

            var binding = session.BindRequires(caller);
            var callSites = _discovery.Get(
                binding.IsSuccess ? binding.Contracts : null);
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

        internal AnalyzerSemanticOutcome AnalyzeCallSite(
            RequiresCallSiteCandidate candidate)
        {
            if (!SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetEnclosingSymbol(
                        candidate.Operation.Syntax.SpanStart, cancellationToken),
                    caller))
            {
                return AnalyzerSemanticOutcome.NotApplicable;
            }

            var contractTarget =
                candidate.TargetMethod.ReducedFrom ??
                candidate.TargetMethod;
            if ((contractTarget is
            { IsStatic: true } or
            { MethodKind: MethodKind.Constructor }) &&
                contractTarget.ContainingType.StaticConstructors is
                { Length: > 0 })
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var binding = session.BindRequires(contractTarget);
            if (binding is not { IsSuccess: true, Contracts: not null })
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var requires = binding.Contracts.Clauses
                .Where(static clause => clause.Kind == BoundContractKind.Requires)
                .ToImmutableArray();
            if (requires.IsDefaultOrEmpty)
            {
                return session.HasPotentialCallPreconditions(contractTarget)
                    ? AnalyzerSemanticOutcome.Unknown
                    : AnalyzerSemanticOutcome.NotApplicable;
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
            RequiresCallSiteCandidate candidate,
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
            var definitelyStrings = new HashSet<IrVarId>();
            foreach (var variable in GetInputVariables(contracts))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var actual = GetActual(candidate, variable);
                if (actual == null)
                {
                    return AnalyzerSemanticOutcome.Unknown;
                }

                var alias = GetAliasEvaluation(candidate, variable, actual);
                ManagedAbstractValue value;
                if (alias == CallArgumentEvaluation.Unsupported ||
                    !(alias == CallArgumentEvaluation.CallEntry
                        ? candidate.Flow.TryEvaluateAtOrigin(
                            candidate.Operation,
                            actual,
                            out value)
                        : candidate.Flow.TryEvaluate(
                            candidate.Operation,
                            actual,
                            out value)))
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
                if (IsDefinitelyString(actual))
                {
                    definitelyStrings.Add(variable.Variable);
                }
            }

            return CompleteEvaluation(
                candidate,
                requires.Select(clause =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var value = ManagedContractFacts.Evaluate(
                        clause.Condition,
                        variables,
                        definitelyStrings);
                    return new ClauseEvaluation(
                        value.TryGetBoolean(out var proven) ? proven : null,
                        clause.Condition);
                }));
        }

        private AnalyzerSemanticOutcome? AnalyzeConcreteCall(
            RequiresCallSiteCandidate callSite,
            BoundMethodContracts contracts,
            ImmutableArray<BoundContractClause> requires)
        {
            if (contracts.Target.Parameters.Any(
                    static parameter => parameter.RefKind != RefKind.None) ||
                requires.Any(static clause =>
                    ManagedContractFacts.ContainsPotentiallyFailingCast(
                        clause.Condition)))
            {
                return null;
            }

            var lowerer = RoslynOperationLowerer.CreateForConcreteReplay(
                _factory,
                session.IsKnownPure);
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
            RequiresCallSiteCandidate callSite,
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
        RequiresCallSiteCandidate callSite,
        BoundContractVariable variable)
    {
        var isReducedExtension =
            callSite.TargetMethod.ReducedFrom != null;
        return variable.Role switch
        {
            BoundContractVariableRole.Receiver => callSite.Instance,
            BoundContractVariableRole.Parameter
                when isReducedExtension && variable.Ordinal == 0 =>
                callSite.Instance,
            BoundContractVariableRole.Parameter =>
                GetArgument(callSite, variable)?.Value,
            _ => null
        };
    }

    private static CallArgumentEvaluation GetAliasEvaluation(
        RequiresCallSiteCandidate callSite,
        BoundContractVariable variable,
        IOperation actual)
    {
        if (variable.Role != BoundContractVariableRole.Parameter)
        {
            return CallArgumentEvaluation.Snapshot;
        }

        var isReducedExtension = callSite.TargetMethod.ReducedFrom != null;
        if (isReducedExtension && variable.Ordinal == 0)
        {
            var receiverKind =
                callSite.TargetMethod.ReducedFrom!.Parameters[0].RefKind;
            return CallArgumentAliasPolicy.Classify(
                receiverKind,
                actual,
                argumentSyntax: null,
                isSyntheticReceiver: true);
        }

        var argument = GetArgument(callSite, variable);
        if (argument?.Parameter == null)
        {
            return CallArgumentEvaluation.Unsupported;
        }

        var isSyntheticReceiver =
            callSite.TargetMethod.IsExtensionMethod &&
            callSite.TargetMethod.ReducedFrom == null &&
            callSite.Instance == null &&
            variable.Ordinal == 0 &&
            argument.Syntax is not ArgumentSyntax;
        return CallArgumentAliasPolicy.Classify(
            argument.Parameter.RefKind,
            actual,
            argument.Syntax,
            isSyntheticReceiver);
    }

    private static IArgumentOperation? GetArgument(
        RequiresCallSiteCandidate callSite,
        BoundContractVariable variable)
    {
        var isReducedExtension =
            callSite.TargetMethod.ReducedFrom != null;
        var ordinal = isReducedExtension
            ? variable.Ordinal - 1
            : variable.Ordinal;
        IArgumentOperation? result = null;
        foreach (var argument in callSite.Arguments)
        {
            if (argument.Parameter?.Ordinal != ordinal)
            {
                continue;
            }

            if (argument.ArgumentKind ==
                    ArgumentKind.ParamArray ||
                result != null)
            {
                return null;
            }

            result = argument;
        }

        return result;
    }

    private static bool IsDefinitelyString(IOperation operation)
    {
        return DefiniteOperationFacts.IsDefinitelyString(operation);
    }

}
