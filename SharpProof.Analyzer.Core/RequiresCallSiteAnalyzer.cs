using SharpProof.Dataflow;

namespace SharpProof.Analyzer;

internal static partial class RequiresCallSiteAnalyzer
{
    internal static AnalyzerSemanticOutcome AnalyzeCallable(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        ControlFlowGraph? graph,
        IOperation? operationRoot,
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
            .Run();
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
        ImmutableArray<IArgumentOperation> arguments;
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
            if (initializerOperation is IInvocationOperation invocation)
            {
                target = invocation.TargetMethod;
                arguments = invocation.Arguments;
            }
            else
            {
                target = semanticModel.GetSymbolInfo(initializer, cancellationToken)
                    .Symbol as IMethodSymbol;
                var recoveredArguments = initializer.ArgumentList.Arguments
                    .Select(argument => semanticModel.GetOperation(
                        argument,
                        cancellationToken) as IArgumentOperation)
                    .ToImmutableArray();
                var argumentsBuilder =
                    ImmutableArray.CreateBuilder<IArgumentOperation>(
                        recoveredArguments.Length);
                foreach (var argument in recoveredArguments)
                {
                    if (argument == null)
                    {
                        session.RequiresObserver?.ObserveOwnerGap(new(constructor, declaration, "PrimaryConstructorArgumentsUnavailable"));
                        return AnalyzerSemanticOutcome.Unknown;
                    }
                    argumentsBuilder.Add(argument);
                }
                arguments = argumentsBuilder.MoveToImmutable();
            }
            origin = initializerOperation ??
                (arguments.IsDefaultOrEmpty ? null : arguments[0]);
            callSiteSyntax = initializer;
        }
        if (target == null || initializer != null && origin == null)
        {
            session.RequiresObserver?.ObserveOwnerGap(new(constructor, declaration, "PrimaryConstructorTargetUnavailable"));
            return AnalyzerSemanticOutcome.Unknown;
        }
        var baseCall = new RequiresCallSiteCandidate(
            origin,
            callSiteSyntax,
            target,
            Instance: null,
            arguments,
            ImmutableDictionary<int, IOperation>.Empty,
            ImmutableDictionary<int, long>.Empty,
            CanReplay: true,
            Flow: null,
            ManagedFlowStatus.BudgetExceeded)
        {
            OriginKind = initializer == null ? PotentialRequiresCallOrigin.ImplicitBaseConstructor :
                PotentialRequiresCallOrigin.ExplicitPrimaryBaseConstructor,
            CallRoleIndex = 0
        };

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
        foreach (var argument in arguments)
        {
            foreach (var operation in RequiresCallSiteDiscovery
                         .ExecutableUnflowedDescendantsAndSelf(
                             argument,
                             operationFacts))
            {
                foreach (var call in RequiresCallSiteDiscovery
                             .CreateUnflowedCandidates(
                                 operation,
                                 semanticModel,
                                 collectCallRoles: session.RequiresObserver != null))
                {
                    var existingIndex = nestedCalls.FindIndex(existing =>
                        existing.Syntax.SyntaxTree == call.Syntax.SyntaxTree &&
                        existing.Syntax.Span == call.Syntax.Span &&
                        SymbolEqualityComparer.Default.Equals(existing.TargetMethod, call.TargetMethod));
                    if (existingIndex < 0)
                    {
                        nestedCalls.Add(call);
                    }
                    else if (session.RequiresObserver != null)
                    {
                        var existing = nestedCalls[existingIndex];
                        nestedCalls[existingIndex] = existing with
                        {
                            MergedRoleCoverage = existing.MergedRoleCoverage || call.MergedRoleCoverage ||
                                existing.CallRoleIndex != call.CallRoleIndex || existing.OriginKind != call.OriginKind
                        };
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

        var baseType = constructor.ContainingType.BaseType;
        if (baseType == null)
        {
            return null;
        }

        IMethodSymbol? candidateMatch = null;
        foreach (var candidate in baseType.InstanceConstructors)
        {
            var isImplicit = true;
            foreach (var parameter in candidate.Parameters)
            {
                if (!parameter.IsOptional && !parameter.IsParams)
                {
                    isImplicit = false;
                    break;
                }
            }
            if (!isImplicit)
            {
                continue;
            }
            if (candidateMatch != null)
            {
                return null;
            }
            candidateMatch = candidate;
        }

        return candidateMatch;
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
        var root = operation;
        while (root.Parent != null)
        {
            root = root.Parent;
        }

        return new Analysis(
                constructor, initializer, semanticModel, session,
                reportDiagnostic, graph: null, operationRoot: root,
                cancellationToken: cancellationToken,
                suppliedInitializerOperation: operation)
            .Run(requireCallerOwnership: false);
    }

    private sealed class Analysis(
        IMethodSymbol caller,
        SyntaxNode declaration,
        SemanticModel semanticModel,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        ControlFlowGraph? graph,
        IOperation? operationRoot,
        CancellationToken cancellationToken,
        IOperation? suppliedInitializerOperation = null)
    {
        private readonly IrFactory _factory = session.IrFactory;
        private readonly RequiresCallSiteDiscovery _discovery =
            new(
                caller,
                declaration,
                semanticModel,
                cancellationToken,
                graph,
                operationRoot,
                suppliedInitializerOperation,
                collectCallRoles: session.RequiresObserver != null);

        internal AnalyzerSemanticOutcome Run(
            bool requireCallerOwnership = true)
        {
            var binding = session.BindRequires(caller);
            var callSites = _discovery.Get(
                binding.IsSuccess ? binding.Contracts : null,
                requireCallerOwnership);
            if (callSites == null)
            {
                session.RequiresObserver?.ObserveOwnerGap(new(caller, declaration, "CallDiscoveryIncomplete"));
                return AnalyzerSemanticOutcome.Unknown;
            }

            var outcome = AnalyzerSemanticOutcome.NotApplicable;
            foreach (var candidate in callSites)
            {
                cancellationToken.ThrowIfCancellationRequested();
                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    AnalyzeCallSite(candidate, requireCallerOwnership));
            }
            return outcome;
        }

        internal AnalyzerSemanticOutcome AnalyzeCallSite(
            RequiresCallSiteCandidate candidate,
            bool requireCallerOwnership = true)
        {
            var observer = session.RequiresObserver;
            if (observer == null)
            { return AnalyzeCallSiteCore(candidate, requireCallerOwnership, null); }
            var capture = new RequiresObservationCapture();
            var outcome = AnalyzeCallSiteCore(candidate, requireCallerOwnership, capture);
            cancellationToken.ThrowIfCancellationRequested();
            if (candidate.MergedRoleCoverage || candidate.OriginKind == null || candidate.CallRoleIndex < 0)
            {
                observer.ObserveGap(new(caller, candidate, candidate.MergedRoleCoverage ?
                    "MergedCallRoleCoverage" : "UnavailableCallRoleCoverage"));
            }
            capture.Publish(observer, caller, candidate);
            return outcome;
        }

        private AnalyzerSemanticOutcome AnalyzeCallSiteCore(
            RequiresCallSiteCandidate candidate,
            bool requireCallerOwnership,
            RequiresObservationCapture? capture)
        {
            if (requireCallerOwnership && !SymbolEqualityComparer.Default.Equals(
                    semanticModel.GetEnclosingSymbol(
                        candidate.Syntax.SpanStart, cancellationToken),
                    caller))
            {
                capture?.Gap("CallerOwnershipMismatch");
                return AnalyzerSemanticOutcome.NotApplicable;
            }

            var contractTarget = candidate.ResolvedTargetMethod ??
                RequiresCallSiteDispatch.ResolveExactTarget(
                    candidate.TargetMethod,
                    candidate.Instance,
                    cancellationToken);
            if ((contractTarget is
            { IsStatic: true } or
            { MethodKind: MethodKind.Constructor }) &&
                contractTarget.ContainingType.StaticConstructors is
                { Length: > 0 })
            {
                capture?.Gap("StaticInitialization");
                return AnalyzerSemanticOutcome.Unknown;
            }

            if (session.HasRejectedMetadataPrecondition(contractTarget))
            {
                capture?.Gap("RejectedMetadataPrecondition");
                SharpProofControlAttributePolicy.ReportRejectedContractApi(
                    contractTarget.Name,
                    candidate.Syntax.GetLocation(),
                    reportDiagnostic);
                return AnalyzerSemanticOutcome.Unknown;
            }

            var externalSourceTarget = IsExternalSourceTarget(contractTarget);
            var binding = session.BindRequires(contractTarget);
            if (binding is not { IsSuccess: true, Contracts: not null })
            {
                capture?.Gap("RequiresBindingFailed");
                return externalSourceTarget
                    ? AnalyzerSemanticOutcome.NotApplicable
                    : AnalyzerSemanticOutcome.Unknown;
            }

            var requires = binding.Contracts.Clauses
                .Where(clause =>
                    clause.Kind == BoundContractKind.Requires &&
                    (!externalSourceTarget ||
                     clause.Evidence == BoundContractEvidence.ClosedAttribute))
                .ToImmutableArray();
            capture?.Bind(contractTarget, requires);
            if (requires.IsDefaultOrEmpty)
            {
                if (externalSourceTarget)
                {
                    if (capture is not null && binding.Contracts.Clauses.Any(
                        clause => clause.Kind == BoundContractKind.Requires))
                    {
                        capture.Gap("ExternalSourceRequiresExcluded");
                    }

                    return AnalyzerSemanticOutcome.NotApplicable;
                }

                if (session.HasPotentialCallPreconditions(contractTarget))
                {
                    capture?.Gap("UnboundPotentialRequires");
                    return AnalyzerSemanticOutcome.Unknown;
                }

                return AnalyzerSemanticOutcome.NotApplicable;
            }

            if (!candidate.CanReplay)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var concrete = AnalyzeConcreteCall(
                candidate,
                binding.Contracts,
                requires,
                out var inputVariables,
                capture);
            if (candidate.FlowStatus != ManagedFlowStatus.Complete)
            {
                return concrete == AnalyzerSemanticOutcome.Refuted
                    ? AnalyzerSemanticOutcome.Refuted
                    : AnalyzerSemanticOutcome.Unknown;
            }

            return concrete ?? AnalyzeAbstractCallSite(
                candidate,
                binding.Contracts,
                requires,
                inputVariables,
                capture);
        }

        private bool IsExternalSourceTarget(IMethodSymbol target)
        {
            return !target.DeclaringSyntaxReferences.IsEmpty &&
                !SymbolEqualityComparer.Default.Equals(
                    target.ContainingAssembly,
                    semanticModel.Compilation.Assembly);
        }

        private AnalyzerSemanticOutcome AnalyzeAbstractCallSite(
            RequiresCallSiteCandidate candidate,
            BoundMethodContracts contracts,
            ImmutableArray<BoundContractClause> requires,
            ImmutableArray<BoundContractVariable> inputVariables,
            RequiresObservationCapture? capture)
        {
            if (candidate.Flow == null || candidate.Operation == null)
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var operationFacts = new DefiniteOperationFacts(semanticModel.Compilation, cancellationToken);
            if (!CallPrerequisitesComplete(
                    candidate,
                    operationFacts.CompletesNormally,
                    skipInstanceReference: true))
            {
                return AnalyzerSemanticOutcome.Unknown;
            }

            var variables = new Dictionary<IrVarId, ManagedAbstractValue>();
            var definitelyStrings = new HashSet<IrVarId>();
            if (inputVariables.IsDefault)
            {
                inputVariables = GetInputVariablesUsedBy(
                    contracts,
                    requires).ToImmutableArray();
            }
            foreach (var variable in inputVariables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryGetImplicitIntegerArgument(
                        candidate,
                        variable,
                        out var implicitValue))
                {
                    variables.Add(
                        variable.Variable,
                        ManagedAbstractValue.Integer(
                            IntervalValue.Constant(implicitValue)));
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
                        : TryEvaluateArgumentSnapshot(
                            candidate.Flow,
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
                if (DefiniteOperationFacts.IsDefinitelyString(actual))
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
                        clause.DiagnosticText);
                }), capture);
        }

        private static bool TryEvaluateArgumentSnapshot(
            ManagedFlowResult flow,
            IOperation origin,
            IOperation actual,
            out ManagedAbstractValue value)
        {
            if (actual is ISimpleAssignmentOperation assignment)
            {
                // The assignment result is its converted RHS value before the
                // store. Do not reevaluate it in the later call-entry state.
                return flow.TryEvaluate(assignment.Value, assignment.Value, out value);
            }
            return flow.TryEvaluate(origin, actual, out value);
        }

        private static bool CallPrerequisitesComplete(
            RequiresCallSiteCandidate candidate,
            Func<IOperation?, bool> completesNormally,
            bool skipInstanceReference)
        {
            var instance = candidate.Instance;
            if (instance != null &&
                (!skipInstanceReference || instance is not IInstanceReferenceOperation) &&
                !completesNormally(instance))
            {
                return false;
            }

            if (!candidate.TargetMethod.IsStatic &&
                instance != null &&
                DefiniteOperationFacts.IsDefinitelyNull(instance))
            {
                return false;
            }

            return candidate.Arguments.All(argument =>
                completesNormally(argument.Value));
        }

        private AnalyzerSemanticOutcome? AnalyzeConcreteCall(
            RequiresCallSiteCandidate callSite,
            BoundMethodContracts contracts,
            ImmutableArray<BoundContractClause> requires,
            out ImmutableArray<BoundContractVariable> inputVariables,
            RequiresObservationCapture? capture)
        {
            inputVariables = default;
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
            if (!CallPrerequisitesComplete(
                    callSite,
                    operationFacts.MayCompleteNormally,
                    skipInstanceReference: false))
            {
                return null;
            }

            inputVariables = GetInputVariablesUsedBy(
                contracts,
                requires).ToImmutableArray();

            var lowerer = RoslynOperationLowerer.CreateForConcreteReplay(
                _factory,
                session.IsKnownPure);
            var interpreter = new IrInterpreter(_factory);
            var substitutions = new Dictionary<IrVarId, IrTerm>();
            foreach (var variable in inputVariables)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryGetImplicitIntegerArgument(
                        callSite,
                        variable,
                        out var implicitValue))
                {
                    if (_factory.GetVariableInfo(variable.Variable).Type !=
                        _factory.IntegerType)
                    {
                        return null;
                    }
                    substitutions.Add(
                        variable.Variable,
                        _factory.Integer(implicitValue));
                    continue;
                }
                var actual = GetActual(callSite, variable);
                if (actual == null)
                {
                    return null;
                }

                var lowered = lowerer.Lower(actual);
                cancellationToken.ThrowIfCancellationRequested();
                if (!lowered.IsExact ||
                    lowered.Term.Type !=
                        _factory.GetVariableInfo(variable.Variable).Type)
                {
                    return null;
                }

                var value = interpreter.Evaluate(
                    lowered.Term, cancellationToken: cancellationToken);
                if (value.Status != IrEvaluationStatus.Value ||
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

                // Keep the declaration-side predicate for the diagnostic.
                // The substituted term may fold to `false`, which hides the
                // precondition that the caller violated.
                evaluations.Add(new ClauseEvaluation(
                    value.Value.Boolean,
                    clause.DiagnosticText));
            }
            return CompleteEvaluation(callSite, evaluations, capture);
        }

        private AnalyzerSemanticOutcome CompleteEvaluation(
            RequiresCallSiteCandidate callSite,
            IEnumerable<ClauseEvaluation> evaluations,
            RequiresObservationCapture? capture)
        {
            var outcome = AnalyzerSemanticOutcome.Proven;
            var ordinal = 0;
            foreach (var evaluation in evaluations)
            {
                capture?.Evaluation(ordinal, evaluation.Value);
                ordinal++;
                if (!evaluation.Value.HasValue)
                {
                    outcome = AnalyzerSemanticOutcomes.Combine(
                        outcome, AnalyzerSemanticOutcome.Unknown);
                }
                else if (!evaluation.Value.Value)
                {
                    outcome = AnalyzerSemanticOutcome.Refuted;
                    reportDiagnostic(Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.RequiresNotProvenRule,
                        callSite.Syntax.GetLocation(),
                        callSite.TargetMethod.Name,
                        evaluation.DiagnosticText));
                }
            }
            return outcome;
        }

        private sealed class RequiresObservationCapture
        {
            private IMethodSymbol? _target;
            private ImmutableArray<BoundContractClause> _clauses = [];
            private bool?[]? _values;
            private string _gap = "NoBoundRequires";

            internal void Gap(string reason)
            { _gap = reason; }

            internal void Bind(IMethodSymbol target, ImmutableArray<BoundContractClause> clauses)
            {
                _target = target;
                _clauses = clauses;
            }

            internal void Evaluation(int ordinal, bool? value)
            {
                _values ??= new bool?[_clauses.Length];
                _values[ordinal] = value;
            }

            internal void Publish(IRequiresCallSiteObserver observer, IMethodSymbol owner,
                RequiresCallSiteCandidate candidate)
            {
                if (_clauses.IsDefaultOrEmpty)
                {
                    observer.ObserveGap(new(owner, candidate, _gap));
                    return;
                }
                for (var ordinal = 0; ordinal < _clauses.Length; ordinal++)
                {
                    var value = _values?[ordinal];
                    var outcome = value == false ? AnalyzerSemanticOutcome.Refuted :
                        value == true && candidate.FlowStatus == ManagedFlowStatus.Complete
                            ? AnalyzerSemanticOutcome.Proven : AnalyzerSemanticOutcome.Unknown;
                    observer.ObserveClause(new(owner, candidate, _target!, _clauses[ordinal], ordinal, outcome));
                }
            }
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

    private static bool TryGetImplicitIntegerArgument(
        RequiresCallSiteCandidate callSite,
        BoundContractVariable variable,
        out long value)
    {
        value = 0;
        return variable.Role == BoundContractVariableRole.Parameter &&
            callSite.ImplicitIntegerArguments.TryGetValue(
                variable.Ordinal,
                out value);
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
        return CallArgumentEvaluationPolicy.FindArgument(
            callSite.Arguments,
            variable.Ordinal,
            isReducedExtension,
            requireReplayable: true);
    }

}
