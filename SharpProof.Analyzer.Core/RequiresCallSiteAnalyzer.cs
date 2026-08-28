using SharpProof.Dataflow;
using SharpProof.Effects;

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
        return Analyze(
            caller,
            declaration,
            semanticModel,
            session,
            reportDiagnostic,
            cancellationToken,
            out _);
    }

    internal static AnalyzerSemanticOutcome Analyze(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken,
        out bool hasUnknown)
    {
        return RequiresCallSiteTreeAnalyzer.Analyze(
            caller,
            declaration,
            semanticModel,
            session,
            reportDiagnostic,
            cancellationToken,
            out hasUnknown);
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
        return AnalyzeCallable(
            caller,
            declaration,
            semanticModel,
            session,
            reportDiagnostic,
            graph,
            operationRoot,
            screenForPotentialCalls,
            cancellationToken,
            out _);
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
        CancellationToken cancellationToken,
        out bool hasUnknown)
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
            .Run(screenForPotentialCalls, out hasUnknown);
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
            return AnalyzeImplicitPrimaryConstructorBase(
                constructor,
                declaration,
                semanticModel,
                session,
                reportDiagnostic,
                cancellationToken);
        }

        var initializerOperation = semanticModel.GetOperation(
            initializer,
            cancellationToken);
        var target = initializerOperation is IInvocationOperation invocation
            ? invocation.TargetMethod
            : semanticModel.GetSymbolInfo(initializer, cancellationToken)
                .Symbol as IMethodSymbol;
        var arguments = initializerOperation is IInvocationOperation baseCallOperation
            ? baseCallOperation.Arguments.Cast<IArgumentOperation?>().ToImmutableArray()
            : initializer.ArgumentList.Arguments
                .Select(argument => semanticModel.GetOperation(
                    argument,
                    cancellationToken) as IArgumentOperation)
                .ToImmutableArray();
        var origin = initializerOperation ??
            (arguments.IsDefaultOrEmpty ? null : arguments[0]);
        if (target == null || origin == null ||
            arguments.Any(static argument => argument == null))
        {
            return AnalyzerSemanticOutcome.Unknown;
        }

        var baseCall = new RequiresCallSiteCandidate(
            origin,
            target,
            Instance: null,
            arguments.OfType<IArgumentOperation>().ToImmutableArray(),
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
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
                             .CreateUnflowedCandidates(operation))
                {
                    if (!nestedCalls.Any(existing =>
                            existing.Operation.Syntax.SyntaxTree ==
                                call.Operation.Syntax.SyntaxTree &&
                            existing.Operation.Syntax.Span ==
                                call.Operation.Syntax.Span &&
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

    private static AnalyzerSemanticOutcome AnalyzeImplicitPrimaryConstructorBase(
        IMethodSymbol constructor,
        TypeDeclarationSyntax declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var baseType = constructor.ContainingType.BaseType;
        if (declaration.BaseList?.Types.Any(static baseTypeSyntax =>
                baseTypeSyntax is not PrimaryConstructorBaseTypeSyntax) != true ||
            baseType == null ||
            baseType.SpecialType == SpecialType.System_Object)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }

        var candidates = baseType
            .InstanceConstructors
            .Where(static candidate =>
                candidate.Parameters.IsEmpty ||
                candidate.Parameters.All(static parameter => parameter.IsOptional))
            .ToImmutableArray();
        if (candidates.Length != 1)
        {
            return AnalyzerSemanticOutcome.NotApplicable;
        }

        var baseConstructor = candidates[0];
        var baseDeclaration = baseConstructor.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();
        if (baseDeclaration == null)
        {
            return AnalyzerSemanticOutcome.Unknown;
        }

        var baseModel = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(semanticModel.Compilation, baseDeclaration.SyntaxTree);
        var origin = (IOperation?)
            (baseDeclaration.Body == null
                ? baseDeclaration.ExpressionBody == null
                    ? null
                    : baseModel.GetOperation(
                        baseDeclaration.ExpressionBody.Expression,
                        cancellationToken)
                : baseModel.GetOperation(
                    baseDeclaration.Body,
                    cancellationToken));
        if (origin == null)
        {
            return AnalyzerSemanticOutcome.Unknown;
        }

        var call = new RequiresCallSiteCandidate(
            origin,
            baseConstructor,
            Instance: null,
            Arguments: [],
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
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
            .AnalyzeCallSite(call, requireCallerOwnership: false);
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
            .CreateUnflowedCandidates(operation);
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

    internal static AnalyzerSemanticOutcome AnalyzeSynthesizedCall(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        IMethodSymbol target,
        IOperation origin,
        CancellationToken cancellationToken)
    {
        var candidate = new RequiresCallSiteCandidate(
            origin,
            target,
            Instance: null,
            Arguments: [],
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            CanReplay: true,
            Flow: null,
            ManagedFlowStatus.Complete);
        return new Analysis(
                caller,
                declaration,
                semanticModel,
                session,
                reportDiagnostic,
                graph: null,
                operationRoot: null,
                cancellationToken)
            .AnalyzeCallSite(candidate, requireCallerOwnership: false);
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
        private readonly OperationCompletionEvaluator _completion =
            new(
                session.Compilation,
                session.ApiSpecs,
                caller,
                static (IOperation? _, IOperation __) => false,
                static (IOperation? _, IOperation __) => false,
                static (IInvocationOperation _) => false);
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
            return Run(screenForPotentialCalls, out _);
        }

        internal AnalyzerSemanticOutcome Run(
            bool screenForPotentialCalls,
            out bool hasUnknown)
        {
            hasUnknown = false;
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
                hasUnknown = true;
                return AnalyzerSemanticOutcome.Unknown;
            }

            var outcome = AnalyzerSemanticOutcome.NotApplicable;
            foreach (var candidate in callSites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidateOutcome = AnalyzeCallSite(candidate);
                hasUnknown |= candidateOutcome == AnalyzerSemanticOutcome.Unknown;
                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    candidateOutcome);
            }
            return outcome;
        }

        internal AnalyzerSemanticOutcome AnalyzeCallSite(
            RequiresCallSiteCandidate candidate,
            bool requireCallerOwnership = true)
        {
            if (requireCallerOwnership && !SymbolEqualityComparer.Default.Equals(
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
                if (!_completion.CanCompleteStaticInitialization(contractTarget))
                {
                    return AnalyzerSemanticOutcome.Unknown;
                }
            }

            if (session.HasRejectedMetadataPrecondition(contractTarget))
            {
                SharpProofControlAttributePolicy.ReportRejectedContractApi(
                    contractTarget.Name,
                    candidate.Operation.Syntax.GetLocation(),
                    reportDiagnostic);
                return AnalyzerSemanticOutcome.Unknown;
            }

            if (session.TryGetInvalidMetadataClosedPrecondition(
                    contractTarget,
                    out var attributeName,
                    out var argument,
                    out var reason))
            {
                reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                    attributeName,
                    argument,
                    reason,
                    candidate.Operation.Syntax.GetLocation()));
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
                if (TryGetSyntheticArgument(
                        candidate,
                        variable,
                        out var syntheticValue))
                {
                    variables.Add(
                        variable.Variable,
                        ManagedAbstractValue.Integer(
                            IntervalValue.Constant(syntheticValue)));
                    continue;
                }
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

            var operationFacts = new DefiniteOperationFacts(
                semanticModel.Compilation, cancellationToken);
            if (callSite.Instance != null &&
                    !operationFacts.MayCompleteNormally(callSite.Instance) ||
                !callSite.TargetMethod.IsStatic &&
                callSite.Instance != null &&
                DefiniteOperationFacts.IsDefinitelyNull(callSite.Instance) ||
                callSite.Arguments.Any(argument =>
                    !operationFacts.MayCompleteNormally(argument.Value) ||
                    HasProvenNullReceiver(
                        argument.Value,
                        callSite.Flow,
                        callSite.Operation)))
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
                if (TryGetSyntheticArgument(
                        callSite,
                        variable,
                        out var syntheticValue))
                {
                    var term = _factory.Integer(syntheticValue);
                    if (term.Type != _factory.GetVariableInfo(
                            variable.Variable).Type)
                    {
                        return null;
                    }
                    substitutions.Add(variable.Variable, term);
                    continue;
                }
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

        private static bool HasProvenNullReceiver(
            IOperation operation,
            ManagedFlowResult? flow,
            IOperation origin)
        {
            operation = DefiniteOperationFacts.UnwrapHarmlessValue(operation);
            return operation switch
            {
                IFieldReferenceOperation
                {
                    Field.IsStatic: false,
                    Instance: { } instance
                } =>
                    DefiniteOperationFacts.IsDefinitelyNull(instance) ||
                    flow?.ProvesNull(origin, instance) == true,
                IPropertyReferenceOperation
                {
                    Property.IsStatic: false,
                    Instance: { } instance
                } =>
                    DefiniteOperationFacts.IsDefinitelyNull(instance) ||
                    flow?.ProvesNull(origin, instance) == true,
                _ => false
            };
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

    private static bool TryGetSyntheticArgument(
        RequiresCallSiteCandidate callSite,
        BoundContractVariable variable,
        out long value)
    {
        value = 0;
        if (variable.Role != BoundContractVariableRole.Parameter)
        {
            return false;
        }

        var ordinal = callSite.TargetMethod.ReducedFrom != null
            ? variable.Ordinal - 1
            : variable.Ordinal;
        return callSite.SyntheticArguments.TryGetValue(ordinal, out value);
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

        if (argument.ArgumentKind == ArgumentKind.DefaultValue)
        {
            return CallArgumentEvaluation.Snapshot;
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

            if (result != null)
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
