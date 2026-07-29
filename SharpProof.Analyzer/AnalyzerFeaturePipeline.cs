namespace SharpProof.Analyzer;

internal static class AnalyzerFeaturePipeline
{
    internal static void ValidateMethodAttributes(
        SymbolAnalysisContext context,
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Symbol is not IMethodSymbol method ||
            method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            return;
        }

        EffectContractDiagnostics.ValidateArguments(method, session, context.ReportDiagnostic);
        ClosedContractDiagnostics.Validate(method, session, context.ReportDiagnostic);
        var selection = GetSelection(
            method, session, context.ReportDiagnostic, context.CancellationToken);
        if ((!method.IsAbstract && !method.IsExtern) || !selection.Any)
        {
            return;
        }

        if (selection.IsSuppressed)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed);
            return;
        }
        if (!selection.Contracts &&
            selection.Effects &&
            session.ResolveEffectContract(method).Kind == EffectContractResolutionKind.Valid)
        {
            var outcome = EffectContractDiagnostics.Analyze(
                method,
                method.DeclaringSyntaxReferences[0].GetSyntax(context.CancellationToken),
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
        AnalyzerSession session)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.OwningSymbol is not IMethodSymbol method)
        {
            return;
        }

        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }

        var declaration = FindDeclaration(
            method, context.OperationBlocks, context.CancellationToken);
        if (declaration == null)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Abstained);
            return;
        }
        var hasInvalidContractClauses =
            ValidateContractClauses(method, session, context.ReportDiagnostic);
        var selection = GetSelection(
            method, session, context.ReportDiagnostic, context.CancellationToken);
        if (selection.IsSuppressed)
        {
            session.RecordSemanticOutcome(method, AnalyzerSemanticOutcome.Suppressed);
            return;
        }

        var semanticModel = SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
            context.Compilation, declaration.SyntaxTree);
        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        var subsetIncompleteReported = false;
        var classifySubset = session.Configuration.EffectsEnabled || selection.Contracts;
        var subset = classifySubset
            ? LanguageSubsetGate.ClassifyEffects(
                method,
                declaration,
                semanticModel,
                context.OperationBlocks,
                session.HasResolvedApiSpec,
                context.CancellationToken)
            : LanguageSubsetDecision.Supported;
        if (!subset.IsSupported)
        {
            if (selection.Any)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
                    method.Name,
                    subset.OperationKind is { } operation
                        ? subset.Reason + " (" + operation + ")"
                        : subset.Reason.ToString()));
                subsetIncompleteReported = true;
            }

            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome, AnalyzerSemanticOutcome.Abstained);
        }
        else if (session.Configuration.EffectsEnabled)
        {
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                EffectContractDiagnostics.Analyze(
                    method,
                    declaration,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken));
        }

        if (session.Configuration.ContractsEnabled)
        {
            var requiresOutcome =
                RequiresCallSiteAnalyzer.Analyze(
                    method,
                    declaration,
                    semanticModel,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken);
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                requiresOutcome);
            if (selection.Contracts &&
                requiresOutcome == AnalyzerSemanticOutcome.Unknown &&
                !subsetIncompleteReported &&
                !hasInvalidContractClauses &&
                !method.IsAbstract &&
                !method.IsExtern)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    GeneratedDiagnosticDescriptors.SelectedAnalysisIncompleteRule,
                    AnalyzerSyntaxHelpers.GetCallableDeclarationLocation(declaration),
                    method.Name,
                    "RequiresCallSiteAnalysisUnknown"));
            }
        }

        session.RecordSemanticOutcome(method, outcome);
    }

    private static bool ValidateContractClauses(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        var inventory = session.GetContractClauses(method);
        var intrinsicViolations =
            session.GetContractIntrinsicViolations(inventory);
        ReportInvalidIntrinsics(intrinsicViolations, session, reportDiagnostic);
        ReportInvalidClauses(inventory.Clauses, reportDiagnostic);
        foreach (var owner in GetNestedOwners(inventory, session.Compilation))
        {
            ReportInvalidClauses(session.GetContractClauses(owner).Clauses, reportDiagnostic);
        }
        return inventory.HasPlacementErrors ||
            !intrinsicViolations.IsDefaultOrEmpty;
    }

    private static IEnumerable<IMethodSymbol> GetNestedOwners(
        ContractClauseInventory inventory,
        Compilation compilation)
    {
        return inventory.Clauses
            .Where(static clause => clause.Placement == ContractClausePlacement.NestedCallable)
            .Select(clause => SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(compilation, clause.Invocation.Syntax.SyntaxTree)
                .GetEnclosingSymbol(clause.Invocation.Syntax.SpanStart))
            .OfType<IMethodSymbol>()
            .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default);
    }

    private static void ReportInvalidIntrinsics(
        ImmutableArray<ContractIntrinsicViolation> violations,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var violation in violations)
        {
            if (!session.TryMarkContractIntrinsicValidated(violation))
            {
                continue;
            }

            var isOld = violation.Failure is
                ContractBindingFailure.OldOutsideEnsures or ContractBindingFailure.NestedOld;
            var (argument, reason) = DescribeIntrinsicViolation(violation.Failure, isOld);
            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                isOld ? "Contract.Old" : "Contract.Result",
                argument,
                reason,
                violation.Invocation.Syntax.GetLocation()));
        }
    }

    private static (string Argument, string Reason) DescribeIntrinsicViolation(
        ContractBindingFailure failure,
        bool isOld)
    {
        return failure switch
        {
            ContractBindingFailure.NestedOld => (
                "<nesting>", "Contract.Old cannot be nested inside Contract.Old"),
            ContractBindingFailure.InvalidIntrinsicSignature => (
                "<signature>",
                isOld
                    ? "expected exactly one value argument"
                    : "expected a result type matching the callable return type"),
            _ => ("<placement>", "expected use inside Contract.Ensures")
        };
    }

    private static void ReportInvalidClauses(
        ImmutableArray<ContractClauseOccurrence> clauses,
        Action<Diagnostic> reportDiagnostic)
    {
        foreach (var clause in clauses)
        {
            if (clause.IsValid ||
                clause.Placement == ContractClausePlacement.NestedCallable)
            {
                continue;
            }

            reportDiagnostic(InvalidContractArgumentDiagnostics.Create(
                "Contract." + clause.Kind,
                "<placement>",
                DescribePlacement(clause.Placement),
                clause.Location));
        }
    }

    private static string DescribePlacement(ContractClausePlacement placement)
    {
        return placement switch
        {
            ContractClausePlacement.Conditional =>
                "expected an unconditional prologue statement",
            ContractClausePlacement.Unreachable =>
                "expected a reachable prologue statement",
            ContractClausePlacement.Late =>
                "expected the clause before every non-contract statement",
            _ => "expected a direct prologue statement"
        };
    }

    private static MethodSelection GetSelection(
        IMethodSymbol method,
        AnalyzerSession session,
        Action<Diagnostic> reportDiagnostic,
        CancellationToken cancellationToken)
    {
        var features = session.Attributes.Select(
            method,
            session.Configuration.ContractsEnabled &&
            session.ResolveContractSource(method).HasSelectedContractIntent) &
            ((session.Configuration.ContractsEnabled
                  ? ContractSelectionFeatures.Contracts
                  : ContractSelectionFeatures.None) |
             (session.Configuration.EffectsEnabled
                  ? ContractSelectionFeatures.Effects
                  : ContractSelectionFeatures.None));
        var suppressed = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            method, session, reportDiagnostic, cancellationToken);
        return new(features, suppressed);
    }

    private static SyntaxNode? FindDeclaration(
        IMethodSymbol method,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken)
    {
        var operationSyntax = operationBlocks
            .OrderByDescending(static operation => operation.Syntax.Span.Length)
            .FirstOrDefault()?.Syntax;
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (operationSyntax == null ||
                reference.SyntaxTree == operationSyntax.SyntaxTree &&
                reference.Span.Contains(operationSyntax.Span))
            {
                return NormalizeDeclaration(reference.GetSyntax(cancellationToken));
            }
        }
        return null;
    }

    private static SyntaxNode NormalizeDeclaration(SyntaxNode declaration)
    {
        return declaration switch
        {
            ArrowExpressionClauseSyntax { Parent: { } parent } => parent,
            _ => declaration
        };
    }

    private readonly record struct MethodSelection(
        ContractSelectionFeatures Features,
        bool IsSuppressed)
    {
        internal bool Contracts => (Features & ContractSelectionFeatures.Contracts) != 0;
        internal bool Effects => (Features & ContractSelectionFeatures.Effects) != 0;
        internal bool Any => Contracts || Effects;
    }
}
