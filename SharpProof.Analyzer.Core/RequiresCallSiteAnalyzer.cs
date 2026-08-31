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
        IMethodSymbol? target;
        ImmutableArray<IArgumentOperation?> arguments;
        IOperation? origin;
        SyntaxNode callSiteSyntax;
        if (initializer == null)
        {
            target = TryGetImplicitBaseConstructor(constructor);
            arguments = [];
            origin = null;
            callSiteSyntax = declaration;
        }
        else
        {
            var initializerOperation = semanticModel.GetOperation(
                initializer,
                cancellationToken);
            target = initializerOperation is IInvocationOperation invocation
                ? invocation.TargetMethod
                : semanticModel.GetSymbolInfo(initializer, cancellationToken)
                    .Symbol as IMethodSymbol;
            arguments = initializerOperation is IInvocationOperation baseCallOperation
                ? baseCallOperation.Arguments.Cast<IArgumentOperation?>()
                    .ToImmutableArray()
                : initializer.ArgumentList.Arguments
                    .Select(argument => semanticModel.GetOperation(
                        argument,
                        cancellationToken) as IArgumentOperation)
                    .ToImmutableArray();
            origin = initializerOperation ??
                (arguments.IsDefaultOrEmpty ? null : arguments[0]);
            callSiteSyntax = initializer;
        }
        if (target == null || initializer != null && origin == null ||
            arguments.Any(static argument => argument == null))
        {
            return AnalyzerSemanticOutcome.Unknown;
        }

        var baseCall = new RequiresCallSiteCandidate(
            origin,
            callSiteSyntax,
            target,
            Instance: null,
            arguments.OfType<IArgumentOperation>().ToImmutableArray(),
            ImmutableDictionary<int, IOperation>.Empty,
            CanReplay: true,
            Flow: null,
            ManagedFlowStatus.BudgetExceeded);

        var analysis = new Analysis(
                constructor,
                declaration,
                semanticModel,
                session,
                reportDiagnostic,
                graph: null,
            operationRoot: null,
            cancellationToken);
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        var nestedCalls = new List<RequiresCallSiteCandidate>();
        var operationFacts = new DefiniteOperationFacts(
            semanticModel.Compilation,
            cancellationToken);
        var argumentsMayComplete = true;
        foreach (var argument in arguments.OfType<IArgumentOperation>())
        {
            foreach (var operation in RequiresCallSiteDiscovery
                         .ExecutableUnflowedDescendantsAndSelf(
                             argument,
                             operationFacts))
            {
                foreach (var call in RequiresCallSiteDiscovery
                             .CreateUnflowedCandidates(
                                 operation,
                                 semanticModel))
                {
                    if (!nestedCalls.Any(existing =>
                            existing.Syntax.SyntaxTree ==
                                call.Syntax.SyntaxTree &&
                            existing.Syntax.Span ==
                                call.Syntax.Span &&
                            SymbolEqualityComparer.Default.Equals(
                                existing.TargetMethod,
                                call.TargetMethod)))
                    {
                        nestedCalls.Add(call);
                    }
                }
            }
            if (!operationFacts.MayCompleteNormally(argument.Value))
            {
                argumentsMayComplete = false;
                break;
            }
        }
        foreach (var call in nestedCalls)
        {
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                analysis.AnalyzeCallSite(
                    call,
                    requireCallerOwnership: false));
        }
        return argumentsMayComplete
            ? AnalyzerSemanticOutcomes.Combine(
                outcome,
                analysis.AnalyzeCallSite(
                    baseCall,
                    requireCallerOwnership: false))
            : outcome;
    }

    internal static IMethodSymbol? TryGetImplicitBaseConstructor(
        IMethodSymbol constructor)
    {
        if (constructor is not
            {
                MethodKind: MethodKind.Constructor,
                IsStatic: false,
                ContainingType.TypeKind: TypeKind.Class
            })
        {
            return null;
        }

        var candidates = constructor.ContainingType.BaseType?
            .InstanceConstructors
            .Where(static candidate => candidate.Parameters.All(
                static parameter => parameter.IsOptional || parameter.IsParams))
            .ToImmutableArray() ?? [];
        return candidates.Length == 1 ? candidates[0] : null;
    }

    internal static AnalyzerSemanticOutcome AnalyzeInitializerCall(
        IMethodSymbol constructor,
        EqualsValueClauseSyntax initializer,
        IOperation operation,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var calls = RequiresCallSiteDiscovery
            .CreateUnflowedCandidates(operation, semanticModel);
        if (calls.IsDefaultOrEmpty)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }
        var analysis = new Analysis(
                constructor, initializer, semanticModel, session,
                reportDiagnostic, graph: null, operationRoot: operation,
                cancellationToken);
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        foreach (var call in calls)
        {
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                analysis.AnalyzeCallSite(
                    call,
                    requireCallerOwnership: false));
        }
        return outcome;
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
            RequiresCallSiteCandidate candidate,
            bool requireCallerOwnership = true)
        {
            if (requireCallerOwnership && !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetEnclosingSymbol(
                        candidate.Syntax.SpanStart, cancellationToken),
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

            if (session.HasRejectedMetadataPrecondition(contractTarget))
            {
                SharpProofControlAttributePolicy.ReportRejectedContractApi(
                    contractTarget.Name,
                    candidate.Syntax.GetLocation(),
                    reportDiagnostic);
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
            if (candidate.Flow == null || candidate.Operation == null)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var operationFacts = new DefiniteOperationFacts(semanticModel.Compilation, cancellationToken);
            if (candidate.Instance != null &&
                candidate.Instance is not IInstanceReferenceOperation &&
                !operationFacts.CompletesNormally(candidate.Instance) ||
                !candidate.TargetMethod.IsStatic &&
                candidate.Instance != null &&
                DefiniteOperationFacts.IsDefinitelyNull(candidate.Instance) ||
                candidate.Arguments.Any(argument =>
                    !operationFacts.CompletesNormally(argument.Value)))
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var variables = new Dictionary<IrVarId, ManagedAbstractValue>();
            var definitelyStrings = new HashSet<IrVarId>();
            foreach (var variable in GetInputVariablesUsedBy(
                         contracts,
                         requires))
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
                        definitelyStrings,
                        _factory.StringType);
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

            var operationFacts = new DefiniteOperationFacts(
                semanticModel.Compilation, cancellationToken);
            if (callSite.Instance != null &&
                    !operationFacts.MayCompleteNormally(callSite.Instance) ||
                !callSite.TargetMethod.IsStatic &&
                callSite.Instance != null &&
                DefiniteOperationFacts.IsDefinitelyNull(callSite.Instance) ||
                callSite.Arguments.Any(argument =>
                    !operationFacts.MayCompleteNormally(argument.Value)))
            {
                return null;
            }

            var lowerer = RoslynOperationLowerer.CreateForConcreteReplay(
                _factory,
                session.IsKnownPure);
            var interpreter = new IrInterpreter(_factory);
            var substitutions = new Dictionary<IrVarId, IrTerm>();
            foreach (var variable in GetInputVariablesUsedBy(
                         contracts,
                         requires))
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
                        callSite.Syntax.GetLocation(),
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

    private static IEnumerable<BoundContractVariable> GetInputVariablesUsedBy(
        BoundMethodContracts contracts,
        ImmutableArray<BoundContractClause> clauses)
    {
        var used = clauses
            .SelectMany(static clause =>
                IrTermAnalysis.CollectVariables(clause.Condition))
            .ToImmutableHashSet();
        return GetInputVariables(contracts).Where(variable =>
            used.Contains(variable.Variable));
    }

    private static IOperation? GetActual(
        RequiresCallSiteCandidate callSite,
        BoundContractVariable variable)
    {
        var isReducedExtension =
            callSite.TargetMethod.ReducedFrom != null;
        if (variable.Role == BoundContractVariableRole.Parameter &&
            callSite.ExplicitArguments.TryGetValue(
                variable.Ordinal,
                out var explicitArgument))
        {
            return explicitArgument;
        }

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

        if (callSite.ExplicitArguments.ContainsKey(variable.Ordinal))
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
