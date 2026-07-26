namespace SharpProof.Analyzer;

internal static class AnalyzerFeaturePipeline {
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
        var subset = LanguageSubsetGate.ClassifyV2Effects(
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
