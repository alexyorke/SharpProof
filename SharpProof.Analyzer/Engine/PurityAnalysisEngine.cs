using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
    private static readonly SymbolDisplayFormat _signatureFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters,
        SymbolDisplayMemberOptions.IncludeContainingType |
        SymbolDisplayMemberOptions.IncludeParameters |
        SymbolDisplayMemberOptions.IncludeModifiers,
        parameterOptions:
        SymbolDisplayParameterOptions.IncludeType |
        SymbolDisplayParameterOptions.IncludeParamsRefOut |
        SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions:
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
        SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    internal static SymbolDisplayFormat SignatureFormat => _signatureFormat;


    private static readonly ImmutableList<IPurityRule> _purityRules = RuleRegistry.GetDefaultRules();

    /// <summary>
    ///     First registry rule per <see cref="OperationKind" />; matches former <c>FirstOrDefault</c> over
    ///     <see cref="_purityRules" />.
    /// </summary>
    private static readonly ImmutableDictionary<OperationKind, IPurityRule> _firstRuleByOperationKind =
        BuildFirstRuleByOperationKind(_purityRules);

    private readonly SharpProofAttributeIdentityPolicy _attributePolicy;
    private readonly CompilationPurityService? _purityService;
    private readonly SmtAnalysisService _smtAnalysis;

    public PurityAnalysisEngine(CompilationPurityService purityService)
    {
        _purityService = purityService ?? throw new ArgumentNullException(nameof(purityService));
        _smtAnalysis = purityService.SmtAnalysis;
        _attributePolicy = purityService.AttributePolicy;
    }

    internal PurityAnalysisEngine(SmtAnalysisService smtAnalysis)
        : this(smtAnalysis, RequiresContractHelpers.OfficialAttributePolicy)
    {
    }

    internal PurityAnalysisEngine(SmtAnalysisService smtAnalysis, SharpProofAttributeIdentityPolicy attributePolicy)
    {
        _smtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
        _attributePolicy = attributePolicy ?? throw new ArgumentNullException(nameof(attributePolicy));
    }

    private static ImmutableDictionary<OperationKind, IPurityRule> BuildFirstRuleByOperationKind(
        ImmutableList<IPurityRule> rules)
    {
        var builder = ImmutableDictionary.CreateBuilder<OperationKind, IPurityRule>();
        foreach (var rule in rules)
            foreach (var kind in rule.ApplicableOperationKinds)
                if (!builder.ContainsKey(kind))
                    builder.Add(kind, rule);

        return builder.ToImmutable();
    }

    private static SyntaxNode? GetDeclaringSyntax(
        IMethodSymbol methodSymbol,
        CancellationToken cancellationToken)
    {
        return methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(cancellationToken);
    }

    private static SyntaxNode? GetBodySyntaxNode(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
    {
        var declaringSyntaxes = methodSymbol.DeclaringSyntaxReferences;
        foreach (var syntaxRef in declaringSyntaxes)
        {
            var syntaxNode = syntaxRef.GetSyntax(cancellationToken);


            if (syntaxNode is ArrowExpressionClauseSyntax arrowExpressionClauseSyntax &&
                (arrowExpressionClauseSyntax.Parent is PropertyDeclarationSyntax ||
                 arrowExpressionClauseSyntax.Parent is IndexerDeclarationSyntax))
                return syntaxNode;

            if (syntaxNode is MethodDeclarationSyntax ||
                syntaxNode is LocalFunctionStatementSyntax ||
                syntaxNode is AnonymousFunctionExpressionSyntax ||
                syntaxNode is AccessorDeclarationSyntax ||
                syntaxNode is ConstructorDeclarationSyntax ||
                syntaxNode is OperatorDeclarationSyntax ||
                syntaxNode is ConversionOperatorDeclarationSyntax)
                return syntaxNode;
        }

        return null;
    }

    internal PurityAnalysisResult IsConsideredPure(
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        INamedTypeSymbol enforcePureAttributeSymbol,
        INamedTypeSymbol? allowSynchronizationAttributeSymbol,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<IMethodSymbol, PurityAnalysisResult>? initialPurityCache = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var sourceNode = GetDeclaringSyntax(methodSymbol, cancellationToken);
        var limits = _purityService?.AnalysisLimits ?? SymbolicAnalysisLimitContext.Limits;
        using var limitScope = SymbolicAnalysisLimitContext.Push(limits, sourceNode);
        var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
        var purityCache = new Dictionary<IMethodSymbol, PurityAnalysisResult>(SymbolEqualityComparer.Default);
        if (initialPurityCache != null)
            foreach (var entry in initialPurityCache)
                if (!SymbolEqualityComparer.Default.Equals(entry.Key, methodSymbol))
                    purityCache[entry.Key] = entry.Value;


        var result = DeterminePurityRecursiveInternal(
            methodSymbol,
            semanticModel,
            enforcePureAttributeSymbol,
            allowSynchronizationAttributeSymbol,
            visited,
            purityCache,
            _smtAnalysis,
            _attributePolicy,
            cancellationToken,
            _purityService
        );


        purityCache[methodSymbol] = result;

        return result.WithAnalysisTruncation(limitScope.Snapshot());
    }


    private static string GetPuritySource(PurityAnalysisResult result)
    {
        if (result.IsPure) return "Assumed/Analyzed Pure";
        if (result.ImpureSyntaxNode != null) return "Analyzed Impure";

        return "Unknown/Default Impure";
    }

    private static PurityAnalysisState CreateInitialRequiresState(
        IMethodSymbol methodSymbol,
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        var pathState = RequiresEntryStateBuilder.Create(
            methodSymbol,
            methodNode,
            semanticModel,
            attributePolicy,
            cancellationToken);
        return PurityAnalysisState.Pure.WithPathState(pathState);
    }

    private static bool ShouldSkipPostCfgDirectPurityProbe(
        IOperation operation,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (operation.Syntax == null) return false;

        foreach (var syntax in GetOperationVisibilitySyntaxCandidates(operation.Syntax))
            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    syntax,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis))
                return true;

        return false;
    }

    private static bool IsImpurityProvenUnreachable(
        PurityAnalysisResult result,
        SemanticModel semanticModel,
        SmtAnalysisService smtAnalysis,
        CancellationToken cancellationToken)
    {
        if (result.IsPure ||
            result.ImpureSyntaxNode == null)
            return false;

        foreach (var syntax in GetOperationVisibilitySyntaxCandidates(result.ImpureSyntaxNode))
            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    syntax,
                    semanticModel,
                    cancellationToken,
                    smtAnalysis))
                return true;

        return false;
    }

    private static IEnumerable<SyntaxNode> GetOperationVisibilitySyntaxCandidates(SyntaxNode syntax)
    {
        yield return syntax;

        foreach (var ancestor in syntax.Ancestors())
        {
            if (ancestor is ConditionalAccessExpressionSyntax conditionalAccess &&
                conditionalAccess.WhenNotNull.Span.Contains(syntax.SpanStart))
            {
                yield return conditionalAccess.WhenNotNull;
                continue;
            }

            if (ancestor is BinaryExpressionSyntax binaryExpression &&
                binaryExpression.IsKind(SyntaxKind.CoalesceExpression) &&
                binaryExpression.Right.Span.Contains(syntax.SpanStart))
            {
                yield return binaryExpression.Right;
                continue;
            }

            if (CSharpSyntaxFacts.IsCallableBoundary(ancestor)) yield break;
        }
    }
}
