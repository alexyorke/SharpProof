using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.ProofCore.Smt;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
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

    internal readonly struct PurityAnalysisState : IEquatable<PurityAnalysisState>
    {
        public bool HasPotentialImpurity { get; }
        public SyntaxNode? FirstImpureSyntaxNode { get; }
        public PurityEvidence FirstImpurityEvidence { get; }


        public ImmutableDictionary<ISymbol, PotentialTargets> DelegateTargetMap { get; }

        public ImmutableDictionary<CaptureId, PurityAnalysisResult> FlowCaptures { get; }
        public ImmutableDictionary<CaptureId, PotentialTargets> FlowCaptureTargets { get; }
        public ImmutableDictionary<CaptureId, INamedTypeSymbol> FlowCaptureConcreteTypes { get; }
        public ImmutableDictionary<CaptureId, ISymbol> FlowCaptureSymbols { get; }
        public ImmutableHashSet<CaptureId> OwnedArrayFlowCaptures { get; }
        public ImmutableDictionary<ISymbol, INamedTypeSymbol> LocalConcreteTypes { get; }
        public SymbolicState PathState { get; }


        internal PurityAnalysisState(
            bool hasPotentialImpurity,
            SyntaxNode? firstImpureSyntaxNode,
            ImmutableDictionary<ISymbol, PotentialTargets>? delegateTargetMap,
            ImmutableDictionary<CaptureId, PurityAnalysisResult>? flowCaptures,
            ImmutableDictionary<CaptureId, PotentialTargets>? flowCaptureTargets = null,
            PurityEvidence firstImpurityEvidence = default,
            ImmutableDictionary<ISymbol, INamedTypeSymbol>? localConcreteTypes = null,
            ImmutableDictionary<CaptureId, INamedTypeSymbol>? flowCaptureConcreteTypes = null,
            SymbolicState? pathState = null,
            ImmutableDictionary<CaptureId, ISymbol>? flowCaptureSymbols = null,
            ImmutableHashSet<CaptureId>? ownedArrayFlowCaptures = null)
        {
            HasPotentialImpurity = hasPotentialImpurity;
            FirstImpureSyntaxNode = firstImpureSyntaxNode;
            FirstImpurityEvidence = firstImpurityEvidence;

            DelegateTargetMap = delegateTargetMap ??
                                ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            FlowCaptures = flowCaptures ?? ImmutableDictionary<CaptureId, PurityAnalysisResult>.Empty;
            FlowCaptureTargets = flowCaptureTargets ?? ImmutableDictionary<CaptureId, PotentialTargets>.Empty;
            FlowCaptureConcreteTypes =
                flowCaptureConcreteTypes ?? ImmutableDictionary<CaptureId, INamedTypeSymbol>.Empty;
            FlowCaptureSymbols = flowCaptureSymbols ?? ImmutableDictionary.Create<CaptureId, ISymbol>();
            OwnedArrayFlowCaptures = ownedArrayFlowCaptures ?? ImmutableHashSet<CaptureId>.Empty;
            LocalConcreteTypes = localConcreteTypes ??
                                 ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            PathState = pathState ?? new SymbolicState();
        }


        public static PurityAnalysisState Pure => new(false, null, null, null);


        public static PurityAnalysisState Merge(IEnumerable<PurityAnalysisState> states)
        {
            return PurityAnalysisStateMerger.MergeStatesAcrossAll(states.ToList(), 0);
        }


        public bool Equals(PurityAnalysisState other)
        {
            if (HasPotentialImpurity != other.HasPotentialImpurity ||
                !Equals(FirstImpureSyntaxNode, other.FirstImpureSyntaxNode) ||
                !FirstImpurityEvidence.Equals(other.FirstImpurityEvidence))
                return false;

            return MapsEqual(DelegateTargetMap, other.DelegateTargetMap, static (left, right) => left.Equals(right)) &&
                   MapsEqual(FlowCaptures, other.FlowCaptures,
                       static (left, right) => PurityResultsEqual(left, right)) &&
                   MapsEqual(FlowCaptureTargets, other.FlowCaptureTargets,
                       static (left, right) => left.Equals(right)) &&
                   MapsEqual(FlowCaptureConcreteTypes, other.FlowCaptureConcreteTypes,
                       static (left, right) => SymbolEqualityComparer.Default.Equals(left, right)) &&
                   MapsEqual(FlowCaptureSymbols, other.FlowCaptureSymbols,
                       static (left, right) => SymbolEqualityComparer.Default.Equals(left, right)) &&
                   OwnedArrayFlowCaptures.SetEquals(other.OwnedArrayFlowCaptures) &&
                   SymbolicStatesEqual(PathState, other.PathState) &&
                   MapsEqual(LocalConcreteTypes, other.LocalConcreteTypes,
                       static (left, right) => SymbolEqualityComparer.Default.Equals(left, right));
        }

        private static bool MapsEqual<TKey, TValue>(
            ImmutableDictionary<TKey, TValue> first,
            ImmutableDictionary<TKey, TValue> second,
            Func<TValue, TValue, bool> valuesEqual)
            where TKey : notnull
        {
            if (first.Count != second.Count) return false;

            foreach (var kvp in first)
                if (!second.TryGetValue(kvp.Key, out var otherValue) ||
                    !valuesEqual(kvp.Value, otherValue))
                    return false;

            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is PurityAnalysisState other && Equals(other);
        }


        public override int GetHashCode()
        {
            var hash = 17;
            hash = hash * 23 + HasPotentialImpurity.GetHashCode();
            hash = hash * 23 + (FirstImpureSyntaxNode?.GetHashCode() ?? 0);
            hash = hash * 23 + FirstImpurityEvidence.GetHashCode();

            foreach (var kvp in DelegateTargetMap.OrderBy(kv => kv.Key.Name))
            {
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Key);
                hash = hash * 23 + kvp.Value.GetHashCode();
            }

            foreach (var kvp in FlowCaptures.OrderBy(kv => kv.Key.GetHashCode()))
            {
                hash = hash * 23 + kvp.Key.GetHashCode();
                hash = hash * 23 + (kvp.Value.IsPure ? 1 : 0);
                hash = hash * 23 + (kvp.Value.ImpureSyntaxNode?.GetHashCode() ?? 0);
            }

            foreach (var kvp in FlowCaptureTargets.OrderBy(kv => kv.Key.GetHashCode()))
            {
                hash = hash * 23 + kvp.Key.GetHashCode();
                hash = hash * 23 + kvp.Value.GetHashCode();
            }

            foreach (var kvp in FlowCaptureConcreteTypes.OrderBy(kv => kv.Key.GetHashCode()))
            {
                hash = hash * 23 + kvp.Key.GetHashCode();
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Value);
            }

            foreach (var kvp in FlowCaptureSymbols.OrderBy(kv => kv.Key.GetHashCode()))
            {
                hash = hash * 23 + kvp.Key.GetHashCode();
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Value);
            }

            foreach (var captureId in OwnedArrayFlowCaptures.OrderBy(id => id.GetHashCode()))
                hash = hash * 23 + captureId.GetHashCode();

            foreach (var fact in PathState.Facts) hash = hash * 23 + fact.GetHashCode();

            foreach (var condition in PathState.PathConditions) hash = hash * 23 + condition.GetHashCode();

            foreach (var kvp in LocalConcreteTypes.OrderBy(kv => kv.Key.Name))
            {
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Key);
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Value);
            }

            foreach (var kvp in PathState.SymbolVersions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
                hash = hash * 23 + kvp.GetHashCode();

            return hash;
        }

        private static bool SymbolicStatesEqual(SymbolicState first, SymbolicState second)
        {
            if (first.Facts.Length != second.Facts.Length ||
                first.PathConditions.Length != second.PathConditions.Length ||
                first.IsContradictory != second.IsContradictory ||
                first.SymbolVersions.Count != second.SymbolVersions.Count)
                return false;

            for (var index = 0; index < first.Facts.Length; index++)
                if (!Equals(first.Facts[index], second.Facts[index]))
                    return false;

            for (var index = 0; index < first.PathConditions.Length; index++)
                if (!Equals(first.PathConditions[index], second.PathConditions[index]))
                    return false;

            return first.SymbolVersions.All(pair =>
                second.SymbolVersions.TryGetValue(pair.Key, out var version) && version == pair.Value);
        }

        public static bool operator ==(PurityAnalysisState left, PurityAnalysisState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PurityAnalysisState left, PurityAnalysisState right)
        {
            return !(left == right);
        }

        private PurityAnalysisState Copy(
            bool? hasPotentialImpurity = null,
            SyntaxNode? firstImpureSyntaxNode = null,
            bool updateFirstImpureSyntaxNode = false,
            ImmutableDictionary<ISymbol, PotentialTargets>? delegateTargetMap = null,
            ImmutableDictionary<CaptureId, PurityAnalysisResult>? flowCaptures = null,
            ImmutableDictionary<CaptureId, PotentialTargets>? flowCaptureTargets = null,
            PurityEvidence? firstImpurityEvidence = null,
            ImmutableDictionary<ISymbol, INamedTypeSymbol>? localConcreteTypes = null,
            ImmutableDictionary<CaptureId, INamedTypeSymbol>? flowCaptureConcreteTypes = null,
            SymbolicState? pathState = null,
            ImmutableDictionary<CaptureId, ISymbol>? flowCaptureSymbols = null,
            ImmutableHashSet<CaptureId>? ownedArrayFlowCaptures = null)
        {
            return new PurityAnalysisState(
                hasPotentialImpurity ?? HasPotentialImpurity,
                updateFirstImpureSyntaxNode ? firstImpureSyntaxNode : FirstImpureSyntaxNode,
                delegateTargetMap ?? DelegateTargetMap,
                flowCaptures ?? FlowCaptures,
                flowCaptureTargets ?? FlowCaptureTargets,
                firstImpurityEvidence ?? FirstImpurityEvidence,
                localConcreteTypes ?? LocalConcreteTypes,
                flowCaptureConcreteTypes ?? FlowCaptureConcreteTypes,
                pathState ?? PathState,
                flowCaptureSymbols ?? FlowCaptureSymbols,
                ownedArrayFlowCaptures ?? OwnedArrayFlowCaptures);
        }


        public PurityAnalysisState WithImpurity(SyntaxNode node)
        {
            if (HasPotentialImpurity) return this;
            return Copy(
                true,
                node,
                true,
                firstImpurityEvidence: PurityEvidence.Create("unsupported_operation", "UnsupportedOperation",
                    syntaxNode: node));
        }

        public PurityAnalysisState WithImpurity(PurityAnalysisResult result, SyntaxNode fallbackNode)
        {
            if (HasPotentialImpurity) return this;
            var node = result.ImpureSyntaxNode ?? fallbackNode;
            var evidence = result.Evidence.IsEmpty
                ? PurityEvidence.Create("unsupported_operation", "UnsupportedOperation", syntaxNode: node)
                : result.Evidence.WithSyntax(node);
            return Copy(
                true,
                node,
                true,
                firstImpurityEvidence: evidence);
        }

        public PurityAnalysisState WithDelegateTarget(ISymbol delegateSymbol, PotentialTargets targets)
        {
            var newMap = DelegateTargetMap.SetItem(delegateSymbol, targets);
            return Copy(delegateTargetMap: newMap);
        }

        public PurityAnalysisState WithFlowCaptureResult(CaptureId id, PurityAnalysisResult result)
        {
            return Copy(flowCaptures: FlowCaptures.SetItem(id, result));
        }

        public PurityAnalysisState WithFlowCaptureTarget(CaptureId id, PotentialTargets targets)
        {
            return Copy(flowCaptureTargets: FlowCaptureTargets.SetItem(id, targets));
        }

        public PurityAnalysisState WithFlowCaptureConcreteType(CaptureId id, INamedTypeSymbol concreteType)
        {
            if (FlowCaptureConcreteTypes.TryGetValue(id, out var existingType) &&
                SymbolEqualityComparer.Default.Equals(existingType, concreteType))
                return this;

            return Copy(flowCaptureConcreteTypes: FlowCaptureConcreteTypes.SetItem(id, concreteType));
        }

        public PurityAnalysisState WithFlowCaptureSymbol(CaptureId id, ISymbol symbol)
        {
            return Copy(flowCaptureSymbols: FlowCaptureSymbols.SetItem(id, symbol));
        }

        public PurityAnalysisState WithOwnedArrayFlowCapture(CaptureId id)
        {
            return WithOwnedArrayFlowCapture(id, null);
        }

        public PurityAnalysisState WithOwnedArrayFlowCapture(CaptureId id, SyntaxNode? source)
        {
            if (OwnedArrayFlowCaptures.Contains(id)) return this;

            return Copy(
                ownedArrayFlowCaptures: OwnedArrayFlowCaptures.Add(id),
                pathState: AddOwnedArrayFlowCaptureFacts(PathState, id, source));
        }

        public PurityAnalysisState WithoutOwnedArrayFlowCapture(CaptureId id)
        {
            if (!OwnedArrayFlowCaptures.Contains(id)) return this;

            return Copy(
                ownedArrayFlowCaptures: OwnedArrayFlowCaptures.Remove(id),
                pathState: RemoveOwnedArrayFlowCaptureFacts(PathState, id));
        }

        public bool IsOwnedArrayFlowCapture(CaptureId id)
        {
            return OwnedArrayFlowCaptures.Contains(id);
        }

        private static SymbolicState AddOwnedArrayFlowCaptureFacts(SymbolicState pathState, CaptureId id,
            SyntaxNode? source)
        {
            if (source == null) return pathState;

            var term = CreateOwnedArrayFlowCaptureTerm(id);
            return SymbolicOperationTransferKernel.TransitionLifetime(
                pathState,
                term,
                SymbolicLifetimeOperationKind.CreateOwnedValue,
                source.Span,
                "analyzer.owned-array-flow-capture",
                evidenceKey: "evidence.owned-array-flow-capture").State;
        }

        private static SymbolicState RemoveOwnedArrayFlowCaptureFacts(SymbolicState pathState, CaptureId id)
        {
            var term = CreateOwnedArrayFlowCaptureTerm(id);
            var facts = pathState.Facts
                .Where(fact => !IsOwnedArrayFlowCaptureFact(fact, term))
                .ToArray();
            return facts.Length == pathState.Facts.Length
                ? pathState
                : new SymbolicState(facts, pathState.PathConditions);
        }

        private static SymbolicTerm CreateOwnedArrayFlowCaptureTerm(CaptureId id)
        {
            return new SymbolicVariableTerm(
                "flowCapture#" + id.GetHashCode().ToString(CultureInfo.InvariantCulture),
                SmtValueKind.Reference);
        }

        private static bool IsOwnedArrayFlowCaptureFact(SymbolicFact fact, SymbolicTerm term)
        {
            return fact.Provenance.StartsWith("analyzer.owned-array-flow-capture.", StringComparison.Ordinal) &&
                   fact.Atom switch
                   {
                       SymbolicFreshnessAtom freshness => Equals(freshness.Value, term),
                       SymbolicOwnershipAtom ownership => Equals(ownership.Value, term),
                       SymbolicResourceLifetimeAtom lifetime => Equals(lifetime.Resource, term),
                       _ => false
                   };
        }

        public bool IsOwnedLocalArraySymbol(ISymbol localSymbol)
        {
            var term = PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(localSymbol, this);
            return PathState.Facts.Any(fact =>
                fact.Polarity &&
                fact.Confidence == SymbolicFactConfidence.Exact &&
                SymbolEqualityComparer.Default.Equals(fact.Symbol, localSymbol) &&
                fact.Atom is SymbolicOwnershipAtom ownership &&
                Equals(ownership.Value, term) &&
                fact.Provenance.StartsWith("analyzer.array.acquire.", StringComparison.Ordinal));
        }

        public bool IsDefinitelyNullLocalSymbol(ISymbol localSymbol)
        {
            return SymbolicStateValueFacts.IsKnownNullReference(
                PathState,
                PuritySymbolicStateFacts.CreateSymbolicReferenceTerm(localSymbol, this));
        }

        public PurityAnalysisState WithLocalConcreteType(ISymbol localSymbol, INamedTypeSymbol concreteType)
        {
            if (LocalConcreteTypes.TryGetValue(localSymbol, out var existingType) &&
                SymbolEqualityComparer.Default.Equals(existingType, concreteType))
                return this;

            return Copy(
                localConcreteTypes: LocalConcreteTypes.SetItem(localSymbol, concreteType));
        }

        public PurityAnalysisState WithoutLocalConcreteType(ISymbol localSymbol)
        {
            if (!LocalConcreteTypes.ContainsKey(localSymbol)) return this;

            return Copy(localConcreteTypes: LocalConcreteTypes.Remove(localSymbol));
        }

        public bool TryGetLocalConcreteType(ISymbol localSymbol, out INamedTypeSymbol concreteType)
        {
            return LocalConcreteTypes.TryGetValue(localSymbol, out concreteType!);
        }

        public bool TryGetFlowCaptureConcreteType(CaptureId id, out INamedTypeSymbol concreteType)
        {
            return FlowCaptureConcreteTypes.TryGetValue(id, out concreteType!);
        }

        public bool TryGetFlowCaptureSymbol(CaptureId id, out ISymbol symbol)
        {
            return FlowCaptureSymbols.TryGetValue(id, out symbol!);
        }

        public PurityAnalysisState WithPathState(SymbolicState pathState)
        {
            return Copy(pathState: pathState ?? new SymbolicState());
        }

        public int GetSmtSymbolVersion(ISymbol symbol)
        {
            var key = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
            return PathState.SymbolVersions.TryGetValue(key, out var version)
                ? version
                : 0;
        }

        public PurityAnalysisState WithSmtSymbolDefinitionVersion(ISymbol symbol, SyntaxNode definitionSyntax)
        {
            var originalDefinition = symbol.OriginalDefinition;
            var symbolKey = SymbolicFactFactory.GetSmtVariableName(originalDefinition);
            var nextVersion = SymbolicOperationTransferKernel.GetDefinitionVersion(definitionSyntax.Span);
            var pathState = SymbolicOperationTransferKernel.Invalidate(
                    PathState,
                    ImmutableArray.Create(new SymbolicInvalidationTarget(symbolKey)),
                    definitionSyntax.Span,
                    "analyzer.version-update").State.WithSymbolVersion(symbolKey, nextVersion);
            return Copy(pathState: pathState);
        }

        private static bool PurityResultsEqual(PurityAnalysisResult a, PurityAnalysisResult b)
        {
            if (a.IsPure != b.IsPure) return false;
            if (a.IsPure) return true;
            return Equals(a.ImpureSyntaxNode, b.ImpureSyntaxNode);
        }

    }


    internal readonly struct PotentialTargets : IEquatable<PotentialTargets>
    {
        public ImmutableHashSet<IMethodSymbol> MethodSymbols { get; }
        public bool IsUnresolved { get; }


        public PotentialTargets(ImmutableHashSet<IMethodSymbol>? methodSymbols)
            : this(methodSymbols, false)
        {
        }

        private PotentialTargets(ImmutableHashSet<IMethodSymbol>? methodSymbols, bool isUnresolved)
        {
            MethodSymbols = methodSymbols ?? ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default);
            IsUnresolved = isUnresolved;
        }

        public static PotentialTargets Empty => new(null);
        public static PotentialTargets Unresolved => new(null, true);

        public static PotentialTargets FromSingle(IMethodSymbol methodSymbol)
        {
            if (methodSymbol == null) return Empty;
            return new PotentialTargets(
                ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default, methodSymbol));
        }


        public static PotentialTargets Merge(PotentialTargets first, PotentialTargets second)
        {
            if (first.IsUnresolved || second.IsUnresolved) return Unresolved;

            return new PotentialTargets(first.MethodSymbols.Union(second.MethodSymbols));
        }

        public bool Equals(PotentialTargets other)
        {
            return IsUnresolved == other.IsUnresolved &&
                   MethodSymbols.SetEquals(other.MethodSymbols);
        }

        public override bool Equals(object obj)
        {
            return obj is PotentialTargets other && Equals(other);
        }

        public override int GetHashCode()
        {
            var hash = IsUnresolved ? 31 : 17;
            foreach (var symbol in MethodSymbols.OrderBy(s => s.Name))
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(symbol);
            return hash;
        }
    }

    public readonly struct PurityAnalysisResult
    {
        public bool IsPure { get; }


        public SyntaxNode? ImpureSyntaxNode { get; }

        public PurityEvidence Evidence { get; }

        public SymbolicAnalysisTruncationInfo AnalysisTruncation { get; }

        private PurityAnalysisResult(
            bool isPure,
            SyntaxNode? impureSyntaxNode,
            PurityEvidence evidence,
            SymbolicAnalysisTruncationInfo? analysisTruncation = null)
        {
            IsPure = isPure;
            ImpureSyntaxNode = impureSyntaxNode;
            Evidence = evidence;
            AnalysisTruncation = analysisTruncation ?? SymbolicAnalysisTruncationInfo.None;
        }


        public static PurityAnalysisResult Pure => new(true, null, PurityEvidence.None);


        public static PurityAnalysisResult Impure(SyntaxNode impureSyntaxNode)
        {
            if (impureSyntaxNode == null)
                throw new ArgumentNullException(nameof(impureSyntaxNode),
                    "Use ImpureUnknownLocation for impurity without a specific node.");
            return new PurityAnalysisResult(false, impureSyntaxNode,
                PurityEvidence.Create("unsupported_operation", "UnsupportedOperation", syntaxNode: impureSyntaxNode));
        }

        public static PurityAnalysisResult Impure(SyntaxNode impureSyntaxNode, PurityEvidence evidence)
        {
            if (impureSyntaxNode == null)
                throw new ArgumentNullException(nameof(impureSyntaxNode),
                    "Use ImpureUnknownLocation for impurity without a specific node.");

            if (evidence.IsEmpty)
                evidence = PurityEvidence.Create("unsupported_operation", "UnsupportedOperation",
                    syntaxNode: impureSyntaxNode);

            return new PurityAnalysisResult(false, impureSyntaxNode, evidence.WithSyntax(impureSyntaxNode));
        }


        public static PurityAnalysisResult ImpureUnknownLocation => new(false, null, PurityEvidence.Create("unknown"));

        public PurityAnalysisResult WithEvidence(PurityEvidence evidence)
        {
            return IsPure
                ? this
                : new PurityAnalysisResult(false, ImpureSyntaxNode, evidence, AnalysisTruncation);
        }

        public PurityAnalysisResult WithCallee(IMethodSymbol calleeSymbol, SyntaxNode? callSite)
        {
            if (IsPure) return this;

            var evidence = Evidence.IsEmpty
                ? PurityEvidence.Create("impure_callee", symbol: calleeSymbol, syntaxNode: callSite)
                : Evidence.WithCallee(calleeSymbol.ToDisplayString(_signatureFormat), callSite);
            return new PurityAnalysisResult(false, callSite ?? ImpureSyntaxNode, evidence, AnalysisTruncation);
        }

        public PurityAnalysisResult WithAnalysisTruncation(SymbolicAnalysisTruncationInfo truncation)
        {
            if (truncation == null) throw new ArgumentNullException(nameof(truncation));

            return new PurityAnalysisResult(
                IsPure,
                ImpureSyntaxNode,
                Evidence,
                SymbolicAnalysisTruncationInfo.Combine(new[] { AnalysisTruncation, truncation }));
        }
    }

    public readonly struct PurityEvidence
    {
        public string Category { get; }
        public string RuleName { get; }
        public string OperationKind { get; }
        public string Symbol { get; }
        public string CatalogSource { get; }
        public string CalleeChain { get; }
        public string BclFallbackGuess { get; }
        public string BclFallbackConfidence { get; }
        public string BclFallbackReason { get; }
        public SymbolicUnknownReasonInfo UnknownReasonInfo =>
            SymbolicUnknownReasonTaxonomy.ForPurity(Category, BclFallbackReason);

        private PurityEvidence(
            string category,
            string ruleName,
            string operationKind,
            string symbol,
            string catalogSource,
            string calleeChain,
            string bclFallbackGuess,
            string bclFallbackConfidence,
            string bclFallbackReason)
        {
            Category = category;
            RuleName = ruleName;
            OperationKind = operationKind;
            Symbol = symbol;
            CatalogSource = catalogSource;
            CalleeChain = calleeChain;
            BclFallbackGuess = bclFallbackGuess;
            BclFallbackConfidence = bclFallbackConfidence;
            BclFallbackReason = bclFallbackReason;
        }

        public static PurityEvidence None => default;

        public bool IsEmpty => string.IsNullOrEmpty(Category);

        public static PurityEvidence Create(
            string category,
            string? ruleName = null,
            IOperation? operation = null,
            SyntaxNode? syntaxNode = null,
            ISymbol? symbol = null,
            string? catalogSource = null,
            string? calleeChain = null,
            string? operationKindOverride = null,
            string? bclFallbackGuess = null,
            string? bclFallbackConfidence = null,
            string? bclFallbackReason = null)
        {
            var operationKind = operationKindOverride ??
                                operation?.Kind.ToString() ?? syntaxNode?.Kind().ToString() ?? string.Empty;
            return new PurityEvidence(
                category,
                ruleName ?? string.Empty,
                operationKind,
                symbol?.ToDisplayString(_signatureFormat) ?? string.Empty,
                catalogSource ?? string.Empty,
                calleeChain ?? string.Empty,
                bclFallbackGuess ?? string.Empty,
                bclFallbackConfidence ?? string.Empty,
                bclFallbackReason ?? string.Empty);
        }

        public PurityEvidence WithSyntax(SyntaxNode syntaxNode)
        {
            if (!string.IsNullOrEmpty(OperationKind)) return this;

            return new PurityEvidence(
                Category,
                RuleName,
                syntaxNode.Kind().ToString(),
                Symbol,
                CatalogSource,
                CalleeChain,
                BclFallbackGuess,
                BclFallbackConfidence,
                BclFallbackReason);
        }

        public PurityEvidence WithCallee(string calleeSymbol, SyntaxNode? callSite)
        {
            var chain = string.IsNullOrEmpty(CalleeChain)
                ? calleeSymbol
                : calleeSymbol + " -> " + CalleeChain;
            var operationKind = !string.IsNullOrEmpty(OperationKind)
                ? OperationKind
                : callSite?.Kind().ToString() ?? string.Empty;

            return new PurityEvidence(
                string.IsNullOrEmpty(Category) ? "impure_callee" : Category,
                RuleName,
                operationKind,
                string.IsNullOrEmpty(Symbol) ? calleeSymbol : Symbol,
                CatalogSource,
                chain,
                BclFallbackGuess,
                BclFallbackConfidence,
                BclFallbackReason);
        }

        public PurityEvidence WithSymbol(string symbol)
        {
            return new PurityEvidence(
                Category,
                RuleName,
                OperationKind,
                symbol,
                CatalogSource,
                CalleeChain,
                BclFallbackGuess,
                BclFallbackConfidence,
                BclFallbackReason);
        }

        public ImmutableDictionary<string, string?> ToDiagnosticProperties()
        {
            var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
            AddIfPresent(builder, SharpProofDiagnostics.ImpurityCategoryProperty, Category);
            AddIfPresent(builder, SharpProofDiagnostics.ImpurityRuleProperty, RuleName);
            AddIfPresent(builder, SharpProofDiagnostics.ImpurityOperationKindProperty, OperationKind);
            AddIfPresent(builder, SharpProofDiagnostics.ImpuritySymbolProperty, Symbol);
            AddIfPresent(builder, SharpProofDiagnostics.ImpurityCatalogSourceProperty, CatalogSource);
            AddIfPresent(builder, SharpProofDiagnostics.ImpurityCalleeChainProperty, CalleeChain);
            AddIfPresent(builder, SharpProofDiagnostics.BclFallbackGuessProperty, BclFallbackGuess);
            AddIfPresent(builder, SharpProofDiagnostics.BclFallbackConfidenceProperty, BclFallbackConfidence);
            AddIfPresent(builder, SharpProofDiagnostics.BclFallbackReasonProperty, BclFallbackReason);
            var properties = builder.ToImmutable();
            return UnknownReasonInfo.IsUnknown
                ? UnknownReasonDiagnosticProperties.Add(properties, UnknownReasonInfo)
                : properties;
        }

        public string ToSummary()
        {
            var category = GetSummaryCategoryText(Category);
            if (!string.IsNullOrEmpty(Symbol))
            {
                var summary = category + " at " + Symbol;
                return string.IsNullOrEmpty(BclFallbackGuess)
                    ? summary
                    : summary + " with non-authoritative BCL fallback guess " + BclFallbackGuess;
            }

            return string.IsNullOrEmpty(BclFallbackGuess)
                ? category
                : category + " with non-authoritative BCL fallback guess " + BclFallbackGuess;
        }

        private static string GetSummaryCategoryText(string category)
        {
            if (string.IsNullOrEmpty(category)) return "unknown";

            return category switch
            {
                "unknown_external_call" => "unverified external call",
                "bcl_fallback_probably_pure" => "unverified framework metadata member",
                "bcl_fallback_probably_impure" => "unverified framework metadata member",
                "bcl_fallback_unknown" => "unverified framework metadata member",
                _ => category
            };
        }

        private static void AddIfPresent(ImmutableDictionary<string, string?>.Builder builder, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) builder[key] = value;
        }
    }

    private static PurityEvidence CreateUnsupportedOperationEvidence(IOperation operation)
    {
        return IsUnsafePointerOperation(operation)
            ? PurityEvidence.Create("unsafe_pointer", "UnsupportedOperation", operation)
            : PurityEvidence.Create("unsupported_operation", "UnsupportedOperation", operation);
    }

    private static bool IsUnsafePointerOperation(IOperation operation)
    {
        var operationKind = operation.Kind.ToString();
        var typeKind = operation.Type?.TypeKind.ToString() ?? string.Empty;

        return operationKind.IndexOf("Pointer", StringComparison.OrdinalIgnoreCase) >= 0 ||
               operationKind.Equals("AddressOf", StringComparison.Ordinal) ||
               operationKind.Equals("Fixed", StringComparison.Ordinal) ||
               operationKind.Equals("SizeOf", StringComparison.Ordinal) ||
               operationKind.Equals("StackAlloc", StringComparison.Ordinal) ||
               typeKind.IndexOf("Pointer", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static PurityAnalysisResult ImpureResult(SyntaxNode? syntaxNode, PurityEvidence evidence = default)
    {
        if (syntaxNode != null)
            return evidence.IsEmpty
                ? PurityAnalysisResult.Impure(syntaxNode)
                : PurityAnalysisResult.Impure(syntaxNode, evidence);

        return evidence.IsEmpty
            ? PurityAnalysisResult.ImpureUnknownLocation
            : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(evidence);
    }

    internal static PurityAnalysisResult CheckStaticConstructorPurity(ITypeSymbol? typeSymbol,
        PurityAnalysisContext context, PurityAnalysisState currentState)
    {
        if (typeSymbol == null) return PurityAnalysisResult.Pure;


        var staticConstructor = typeSymbol.GetMembers(".cctor").OfType<IMethodSymbol>().FirstOrDefault();

        if (staticConstructor == null) return PurityAnalysisResult.Pure;


        var cctorResult = PurityCalleeResolver.GetCalleePurity(staticConstructor, context);


        return cctorResult.IsPure
            ? PurityAnalysisResult.Pure
            : PurityAnalysisResult.Impure(
                cctorResult.ImpureSyntaxNode ??
                typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(context.CancellationToken) ??
                context.ContainingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                    ?.GetSyntax(context.CancellationToken) ??
                throw new InvalidOperationException("Cannot find syntax node for static constructor impurity"),
                cctorResult.Evidence);
    }
}
