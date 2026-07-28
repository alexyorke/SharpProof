namespace SharpProof.Analyzer;

internal static class AnalyzerFeaturePipeline {
    internal static void ValidateMethodAttributes(
        SymbolAnalysisContext context,
        AnalyzerSession session) {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Symbol is not IMethodSymbol method ||
            method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
            return;
        EffectContractDiagnostics.ValidateArguments(
            method, session, context.ReportDiagnostic);
        ClosedContractDiagnostics.Validate(
            method, session, context.ReportDiagnostic);
        var isSuppressed = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            method, session, context.ReportDiagnostic,
            context.CancellationToken);
        var contractSelected = IsContractSelected(method, session);
        var effectSelected = IsEffectSelected(method, session);
        if ((!method.IsAbstract && !method.IsExtern) ||
            !contractSelected && !effectSelected)
            return;
        if (isSuppressed) { session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed); return; }
        if (!contractSelected &&
            effectSelected &&
            session.ResolveEffectContract(method).Kind ==
                EffectContractResolutionKind.Valid) {
            var outcome = EffectContractDiagnostics.Analyze(
                method,
                method.DeclaringSyntaxReferences[0]
                    .GetSyntax(context.CancellationToken),
                session,
                context.ReportDiagnostic,
                context.CancellationToken);
            session.RecordSemanticOutcome(
                method,
                outcome == AnalyzerSemanticOutcome.NotApplicable
                    ? AnalyzerSemanticOutcome.Proven
                    : outcome);
            return;
        }
        context.ReportDiagnostic(Diagnostic.Create(
            GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
            AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(method, context.CancellationToken),
            method.Name,
            LanguageSubsetAbstentionReason.MissingOperationRoot));
        session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
    }

    internal static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context,
        AnalyzerSession session) {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.OwningSymbol is not IMethodSymbol method)
            return;
        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty) {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }

        var declaration = FindDeclaration(
            method,
            context.OperationBlocks,
            context.CancellationToken);
        if (declaration == null) {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }
        ValidateContractClauses(
            method,
            session,
            context.ReportDiagnostic);
        var isSuppressed = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            method,
            session,
            context.ReportDiagnostic,
            context.CancellationToken);
        if (isSuppressed) {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed);
            return;
        }

        var semanticModel =
            SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
            context.Compilation,
            declaration.SyntaxTree);
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        if (session.Configuration.EffectsEnabled) {
            var subset = LanguageSubsetGate.ClassifyEffects(
                method,
                declaration,
                semanticModel,
                context.OperationBlocks,
                session.HasResolvedApiSpec,
                context.CancellationToken);
            if (subset.IsSupported)
                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    EffectContractDiagnostics.Analyze(
                        method,
                        declaration,
                        session,
                        context.ReportDiagnostic,
                        context.CancellationToken));
            else {
                if (IsEffectSelected(method, session))
                    context.ReportDiagnostic(Diagnostic.Create(
                        GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                        AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
                        method.Name,
                        subset.OperationKind is { } operation
                            ? subset.Reason + " (" + operation + ")"
                            : subset.Reason.ToString()));
                outcome = AnalyzerSemanticOutcomes.Combine(
                    outcome,
                    AnalyzerSemanticOutcome.Abstained);
            }
        }

        if (session.Configuration.ContractsEnabled)
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                RequiresCallSiteAnalyzer.Analyze(
                    method,
                    declaration,
                    semanticModel,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken));
        session.RecordSemanticOutcome(method, outcome);
    }

    private static void ValidateContractClauses(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic) {
        var inventory = session.GetContractClauses(method);
        ReportInvalidIntrinsics(
            session.GetContractIntrinsicViolations(inventory),
            session,
            reportDiagnostic);
        ReportInvalidClauses(inventory.Clauses, reportDiagnostic);
        foreach (var owner in inventory.Clauses
                     .Where(static clause =>
                         clause.Placement == ContractClausePlacement.NestedCallable)
                     .Select(clause =>
                         SharpProof.Frontend.Host.CompilationModelProvider
                             .GetSemanticModel(
                                 session.Compilation,
                                 clause.Invocation.Syntax.SyntaxTree)
                             .GetEnclosingSymbol(clause.Invocation.Syntax.SpanStart))
                     .OfType<IMethodSymbol>()
                     .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default))
            ReportInvalidClauses(
                session.GetContractClauses(owner).Clauses,
                reportDiagnostic);
    }

    private static void ReportInvalidIntrinsics(
        ImmutableArray<ContractIntrinsicViolation> violations,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic) {
        foreach (var violation in violations) {
            if (!session.TryMarkContractIntrinsicValidated(violation)) continue;
            var isOld = violation.Failure is
                ContractBindingFailure.OldOutsideEnsures or
                ContractBindingFailure.NestedOld;
            var argument = violation.Failure switch {
                ContractBindingFailure.InvalidIntrinsicSignature =>
                    "<signature>",
                ContractBindingFailure.NestedOld => "<nesting>",
                _ => "<placement>"
            };
            var reason = violation.Failure switch {
                ContractBindingFailure.NestedOld =>
                    "Contract.Old cannot be nested inside Contract.Old",
                ContractBindingFailure.InvalidIntrinsicSignature =>
                    isOld
                        ? "expected exactly one value argument"
                        : "expected a result type matching the callable return type",
                _ => "expected use inside Contract.Ensures"
            };
            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                isOld ? "Contract.Old" : "Contract.Result",
                argument,
                reason,
                violation.Invocation.Syntax.GetLocation()));
        }
    }

    private static void ReportInvalidClauses(
        ImmutableArray<ContractClauseOccurrence> clauses,
        Action<Diagnostic> reportDiagnostic) {
        foreach (var clause in clauses) {
            if (clause.IsValid ||
                clause.Placement == ContractClausePlacement.NestedCallable)
                continue;
            var reason = clause.Placement switch {
                ContractClausePlacement.Conditional =>
                    "expected an unconditional prologue statement",
                ContractClausePlacement.Unreachable =>
                    "expected a reachable prologue statement",
                ContractClausePlacement.Late =>
                    "expected the clause before every non-contract statement",
                _ => "expected a direct prologue statement"
            };
            reportDiagnostic(
                InvalidContractArgumentDiagnostics.Create(
                    "Contract." + clause.Kind,
                    "<placement>",
                    reason,
                    clause.Location));
        }
    }

    private static bool IsContractSelected(
        IMethodSymbol method,
        AnalyzerSession session) =>
        session.Configuration.ContractsEnabled &&
        (!session.GetContractClauses(method).Clauses.IsEmpty ||
         method.Parameters.Any(parameter =>
             parameter.GetAttributes().Any(attribute =>
                 IsClosedContract(attribute, session.Attributes))) ||
         method.GetReturnTypeAttributes().Any(attribute =>
             IsClosedContract(attribute, session.Attributes)));

    private static bool IsEffectSelected(
        IMethodSymbol method,
        AnalyzerSession session) {
        if (!session.Configuration.EffectsEnabled) return false;
        return AnalyzerAttributeSymbols.GetCallableAttributes(method).Any(attribute =>
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.EnforcePure) ||
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.ZeroAllocations) ||
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.AllowedCapabilities) ||
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.DoesNotThrow) ||
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.AllowedExceptions) ||
            AnalyzerAttributeSymbols.Is(attribute, session.Attributes.EffectContract));
    }

    private static bool IsClosedContract(
        AttributeData attribute,
        AnalyzerAttributeSymbols symbols) =>
        AnalyzerAttributeSymbols.Is(attribute, symbols.NotNull) ||
        AnalyzerAttributeSymbols.Is(attribute, symbols.Positive) ||
        AnalyzerAttributeSymbols.Is(attribute, symbols.InRange);

    private static SyntaxNode? FindDeclaration(
        IMethodSymbol method,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken) {
        var operationSyntax = operationBlocks.IsDefaultOrEmpty
            ? null
            : operationBlocks
                .OrderByDescending(static operation => operation.Syntax.Span.Length)
                .First()
                .Syntax;
        foreach (var reference in method.DeclaringSyntaxReferences) {
            cancellationToken.ThrowIfCancellationRequested();
            if (operationSyntax == null ||
                reference.SyntaxTree == operationSyntax.SyntaxTree &&
                reference.Span.Contains(operationSyntax.Span))
                return NormalizeDeclaration(reference.GetSyntax(cancellationToken));
        }
        return null;
    }

    private static SyntaxNode NormalizeDeclaration(SyntaxNode declaration) =>
        declaration switch {
            ArrowExpressionClauseSyntax { Parent: { } parent } => parent,
            _ => declaration
        };
}
