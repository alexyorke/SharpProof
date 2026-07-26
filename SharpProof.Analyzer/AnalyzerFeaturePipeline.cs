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
        _ = SharpProofControlAttributePolicy.ValidateAndShouldSuppress(
            method, session, context.ReportDiagnostic,
            context.CancellationToken);
    }

    internal static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context,
        AnalyzerSession session) {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.OwningSymbol is not IMethodSymbol method)
            return;
        if (method.DeclaringSyntaxReferences.IsDefaultOrEmpty) {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }

        var declaration = FindDeclaration(
            method,
            context.OperationBlocks,
            context.CancellationToken);
        if (declaration == null) {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
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
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Suppressed);
            return;
        }

        var semanticModel =
            SharpProof.Frontend.Host.CompilationModelProvider.GetSemanticModel(
            context.Compilation,
            declaration.SyntaxTree);
        var subset = LanguageSubsetGate.ClassifyEffects(
                method,
                declaration,
                semanticModel,
                context.OperationBlocks,
                session.HasResolvedApiSpec,
                context.CancellationToken);
        if (!subset.IsSupported) {
            session.RecordSemanticOutcome(
                method,
                AnalyzerSemanticOutcome.Abstained);
            return;
        }

        var outcome = AnalyzerSemanticOutcome.NotApplicable;
        if (session.Configuration.Mode is
            SharpProofMode.Effects or SharpProofMode.AllExperimental)
            outcome = AnalyzerSemanticOutcomes.Combine(
                outcome,
                EffectContractDiagnostics.Analyze(
                    method,
                    declaration,
                    session,
                    context.ReportDiagnostic,
                    context.CancellationToken));

        if (session.Configuration.Mode is
            SharpProofMode.Contracts or SharpProofMode.AllExperimental)
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
        foreach (var clause in session.GetContractClauses(method).Clauses) {
            if (clause.IsValid) continue;
            var reason = clause.Placement switch {
                ContractClausePlacement.Conditional =>
                    "expected an unconditional prologue statement",
                ContractClausePlacement.NestedCallable =>
                    "expected a clause directly owned by the callable",
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
