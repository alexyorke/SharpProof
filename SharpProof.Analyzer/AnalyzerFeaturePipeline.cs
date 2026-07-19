namespace SharpProof.Analyzer;

internal static class AnalyzerFeaturePipeline
{
    internal static void AnalyzeOperationBlock(
        OperationBlockAnalysisContext context,
        AnalyzerSession session)
    {
        if (!TryCreateOperationBlockContext(context, session, out var methodContext)) return;

        AnalyzeCallable(methodContext, session);
    }

    internal static void AnalyzeSyntaxFallback(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session)
    {
        if (!RequiresSyntaxFallback(context.Node) ||
            !TryCreateSyntaxContext(context, session, out var methodContext))
            return;

        AnalyzeCallable(methodContext, session);
    }

    internal static bool RequiresSyntaxFallback(SyntaxNode node)
    {
        if (node is PropertyDeclarationSyntax { ExpressionBody: not null } or
            IndexerDeclarationSyntax { ExpressionBody: not null } or
            LocalFunctionStatementSyntax)
            return true;

        return node switch
        {
            MethodDeclarationSyntax method => method.Body == null && method.ExpressionBody == null,
            ConstructorDeclarationSyntax constructor =>
                constructor.Body == null && constructor.ExpressionBody == null,
            OperatorDeclarationSyntax operatorDeclaration =>
                operatorDeclaration.Body == null && operatorDeclaration.ExpressionBody == null,
            ConversionOperatorDeclarationSyntax conversion =>
                conversion.Body == null && conversion.ExpressionBody == null,
            AccessorDeclarationSyntax accessor => accessor.Body == null && accessor.ExpressionBody == null,
            _ => false
        };
    }

    private static void AnalyzeCallable(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        using (EffectSummaryCatalog.UseCurrent(session.EffectSummaryCatalog))
        using (ImpurityCatalog.UseConfiguredOverrides(session.Configuration))
        using (SymbolicAnalysisLimitContext.Push(session.Configuration.AnalysisLimits, context.Node))
        {
            var features = session.Features;
            TrustedBoundaryReviewAnalyzer.Analyze(context, session);

            if (features.Includes(AnalyzerFeatures.Purity))
                MethodPurityAnalyzer.AnalyzeSymbolForPurity(
                    context,
                    session.PurityService,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Allocation))
                MethodAllocationAnalyzer.AnalyzeSymbolForZeroAllocations(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Capability))
                MethodCapabilityAnalyzer.AnalyzeSymbolForCapabilities(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Requires))
                MethodRequiresAnalyzer.AnalyzeSymbolForRequires(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Ensures))
                MethodEnsuresAnalyzer.AnalyzeSymbolForEnsures(
                    context,
                    session.PurityService,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Complexity))
                MethodExpectedComplexityAnalyzer.AnalyzeSymbolForExpectedComplexity(
                    context,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Exceptions))
                ExceptionFlowAnalyzer.AnalyzeSymbolForExceptions(
                    context,
                    session.EffectSummaryCatalog,
                    session.PurityService,
                    session.Baseline,
                    session.AttributePolicy);

            if (features.Includes(AnalyzerFeatures.Suggestions))
                InferredContractSuggestionAnalyzer.Analyze(context, session);

            if (features.Includes(AnalyzerFeatures.Nullability))
                NullableContractAnalyzer.Analyze(context, session);

            if (features.Includes(AnalyzerFeatures.CommonBugs))
                CommonBugAnalyzer.AnalyzeCallable(context, session);
        }
    }

    private static bool TryCreateOperationBlockContext(
        OperationBlockAnalysisContext context,
        AnalyzerSession session,
        out MethodBodyAnalysisContext methodContext)
    {
        methodContext = null!;
        if (context.OwningSymbol is not IMethodSymbol methodSymbol || PurityAnalysisEngine.IsMetadataSymbol(methodSymbol))
            return false;

        var declaration = FindDeclaration(methodSymbol, context.OperationBlocks, context.CancellationToken);
        if (declaration == null || RequiresSyntaxFallback(declaration)) return false;

        var semanticModel = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
        var state = session.GetOrCreateMethodBodyAnalysis(
            methodSymbol,
            declaration,
            semanticModel,
            context.OperationBlocks,
            context.CancellationToken);
        Action<Diagnostic> reportDiagnostic = context.ReportDiagnostic;
        methodContext = new MethodBodyAnalysisContext(
            state,
            AnalyzerConfiguration.GetTreeConfiguration(
                context.Options,
                declaration.SyntaxTree,
                session.Configuration),
            context.CancellationToken,
            reportDiagnostic);
        return true;
    }

    private static bool TryCreateSyntaxContext(
        SyntaxNodeAnalysisContext context,
        AnalyzerSession session,
        out MethodBodyAnalysisContext methodContext)
    {
        methodContext = null!;
        var declaredSymbol = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken);
        var methodSymbol = declaredSymbol as IMethodSymbol;
        if (methodSymbol == null &&
            declaredSymbol is IPropertySymbol propertySymbol &&
            context.Node is PropertyDeclarationSyntax { ExpressionBody: not null } or
                IndexerDeclarationSyntax { ExpressionBody: not null })
            methodSymbol = propertySymbol.GetMethod;

        if (methodSymbol == null || PurityAnalysisEngine.IsMetadataSymbol(methodSymbol)) return false;

        var state = session.GetOrCreateMethodBodyAnalysis(
            methodSymbol,
            context.Node,
            context.SemanticModel,
            ImmutableArray<IOperation>.Empty,
            context.CancellationToken);
        Action<Diagnostic> reportDiagnostic = context.ReportDiagnostic;
        methodContext = new MethodBodyAnalysisContext(
            state,
            AnalyzerConfiguration.GetTreeConfiguration(
                context.Options,
                context.Node.SyntaxTree,
                session.Configuration),
            context.CancellationToken,
            reportDiagnostic);
        return true;
    }

    private static SyntaxNode? FindDeclaration(
        IMethodSymbol methodSymbol,
        ImmutableArray<IOperation> operationBlocks,
        CancellationToken cancellationToken)
    {
        var operationSyntax = operationBlocks.IsDefaultOrEmpty
            ? null
            : operationBlocks
                .OrderByDescending(static operation => operation.Syntax.Span.Length)
                .First()
                .Syntax;
        var references = methodSymbol.DeclaringSyntaxReferences;
        if (operationSyntax != null)
            foreach (var syntaxReference in references)
                if (syntaxReference.SyntaxTree == operationSyntax.SyntaxTree &&
                    syntaxReference.Span.Contains(operationSyntax.Span))
                    return NormalizeDeclaration(syntaxReference.GetSyntax(cancellationToken));

        var declaration = references.FirstOrDefault()?.GetSyntax(cancellationToken);
        return declaration == null ? null : NormalizeDeclaration(declaration);
    }

    private static SyntaxNode NormalizeDeclaration(SyntaxNode declaration)
    {
        return declaration switch
        {
            AccessorDeclarationSyntax => declaration,
            PropertyDeclarationSyntax => declaration,
            IndexerDeclarationSyntax => declaration,
            ArrowExpressionClauseSyntax { Parent: PropertyDeclarationSyntax property } => property,
            ArrowExpressionClauseSyntax { Parent: IndexerDeclarationSyntax indexer } => indexer,
            _ => declaration
        };
    }
}
