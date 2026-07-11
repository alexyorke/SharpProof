using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Symbolic;

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
        if (node is PropertyDeclarationSyntax or IndexerDeclarationSyntax) return true;

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
            LocalFunctionStatementSyntax localFunction =>
                localFunction.Body == null && localFunction.ExpressionBody == null,
            _ => false
        };
    }

    private static void AnalyzeCallable(
        MethodBodyAnalysisContext context,
        AnalyzerSession session)
    {
        using (session.GeneratedPurityCatalog == null
                   ? null
                   : GeneratedPurityCatalog.UseCurrent(session.GeneratedPurityCatalog))
        using (ImpurityCatalog.UseConfiguredOverrides(session.Configuration))
        using (SymbolicAnalysisLimitContext.Push(session.Configuration.AnalysisLimits, context.Node))
        {
            var features = session.Features;
            if (features.Includes(AnalyzerFeatures.Purity))
                MethodPurityAnalyzer.AnalyzeSymbolForPurity(
                    context,
                    session.PurityService,
                    session.Configuration.MissingPuritySuggestions,
                    session.Configuration.EmitExplanations,
                    session.Configuration.ReportBclFallbackGuesses,
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
                    session.Configuration,
                    session.ExceptionSummaryCatalog,
                    session.PurityService,
                    session.Baseline,
                    session.AttributePolicy);
        }
    }

    private static bool TryCreateOperationBlockContext(
        OperationBlockAnalysisContext context,
        AnalyzerSession session,
        out MethodBodyAnalysisContext methodContext)
    {
        methodContext = null!;
        if (context.OwningSymbol is not IMethodSymbol methodSymbol ||
            methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true)
            return false;

        var declaration = FindDeclaration(methodSymbol, context.OperationBlocks, context.CancellationToken);
        if (declaration == null) return false;

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
            context.Options,
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

        if (methodSymbol == null || methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true) return false;

        var state = session.GetOrCreateMethodBodyAnalysis(
            methodSymbol,
            context.Node,
            context.SemanticModel,
            ImmutableArray<IOperation>.Empty,
            context.CancellationToken);
        Action<Diagnostic> reportDiagnostic = context.ReportDiagnostic;
        methodContext = new MethodBodyAnalysisContext(
            state,
            context.Options,
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
                    return syntaxReference.GetSyntax(cancellationToken);

        return references.FirstOrDefault()?.GetSyntax(cancellationToken);
    }
}
