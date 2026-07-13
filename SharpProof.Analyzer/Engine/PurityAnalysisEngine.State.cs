using System.Collections.Immutable;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FlowAnalysis;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer.Engine;

internal partial class PurityAnalysisEngine
{
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
        public ImmutableHashSet<ISymbol> OwnedLocalArraySymbols { get; }
        public ImmutableHashSet<ISymbol> DefinitelyNullLocalSymbols { get; }
        public ImmutableDictionary<ISymbol, INamedTypeSymbol> LocalConcreteTypes { get; }
        public ImmutableDictionary<ISymbol, int> SmtSymbolVersions { get; }
        public SymbolicState PathState { get; }


        internal PurityAnalysisState(
            bool hasPotentialImpurity,
            SyntaxNode? firstImpureSyntaxNode,
            ImmutableDictionary<ISymbol, PotentialTargets>? delegateTargetMap,
            ImmutableDictionary<CaptureId, PurityAnalysisResult>? flowCaptures,
            ImmutableDictionary<CaptureId, PotentialTargets>? flowCaptureTargets = null,
            ImmutableHashSet<ISymbol>? ownedLocalArraySymbols = null,
            ImmutableHashSet<ISymbol>? definitelyNullLocalSymbols = null,
            PurityEvidence firstImpurityEvidence = default,
            ImmutableDictionary<ISymbol, INamedTypeSymbol>? localConcreteTypes = null,
            ImmutableDictionary<ISymbol, int>? smtSymbolVersions = null,
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
            OwnedLocalArraySymbols = ownedLocalArraySymbols ??
                                     ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
            DefinitelyNullLocalSymbols = definitelyNullLocalSymbols ??
                                         ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
            LocalConcreteTypes = localConcreteTypes ??
                                 ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            SmtSymbolVersions = smtSymbolVersions ??
                                ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default);
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
                   OwnedLocalArraySymbols.SetEquals(other.OwnedLocalArraySymbols) &&
                   DefinitelyNullLocalSymbols.SetEquals(other.DefinitelyNullLocalSymbols) &&
                   SymbolicStatesEqual(PathState, other.PathState) &&
                   MapsEqual(LocalConcreteTypes, other.LocalConcreteTypes,
                       static (left, right) => SymbolEqualityComparer.Default.Equals(left, right)) &&
                   MapsEqual(SmtSymbolVersions, other.SmtSymbolVersions, static (left, right) => left == right);
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

            foreach (var symbol in OwnedLocalArraySymbols.OrderBy(sym => sym.Name))
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(symbol);

            foreach (var symbol in DefinitelyNullLocalSymbols.OrderBy(sym => sym.Name))
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(symbol);

            foreach (var fact in PathState.Facts) hash = hash * 23 + fact.GetHashCode();

            foreach (var condition in PathState.PathConditions) hash = hash * 23 + condition.GetHashCode();

            foreach (var kvp in LocalConcreteTypes.OrderBy(kv => kv.Key.Name))
            {
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Key);
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Value);
            }

            foreach (var kvp in SmtSymbolVersions.OrderBy(kv => kv.Key.Name))
            {
                hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(kvp.Key);
                hash = hash * 23 + kvp.Value.GetHashCode();
            }

            return hash;
        }

        private static bool SymbolicStatesEqual(SymbolicState first, SymbolicState second)
        {
            if (first.Facts.Length != second.Facts.Length ||
                first.PathConditions.Length != second.PathConditions.Length)
                return false;

            for (var index = 0; index < first.Facts.Length; index++)
                if (!Equals(first.Facts[index], second.Facts[index]))
                    return false;

            for (var index = 0; index < first.PathConditions.Length; index++)
                if (!Equals(first.PathConditions[index], second.PathConditions[index]))
                    return false;

            return true;
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
            ImmutableHashSet<ISymbol>? ownedLocalArraySymbols = null,
            ImmutableHashSet<ISymbol>? definitelyNullLocalSymbols = null,
            PurityEvidence? firstImpurityEvidence = null,
            ImmutableDictionary<ISymbol, INamedTypeSymbol>? localConcreteTypes = null,
            ImmutableDictionary<ISymbol, int>? smtSymbolVersions = null,
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
                ownedLocalArraySymbols ?? OwnedLocalArraySymbols,
                definitelyNullLocalSymbols ?? DefinitelyNullLocalSymbols,
                firstImpurityEvidence ?? FirstImpurityEvidence,
                localConcreteTypes ?? LocalConcreteTypes,
                smtSymbolVersions ?? SmtSymbolVersions,
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
            var facts = SymbolicOwnershipFactFactory.CreateFreshOwnedValue(
                term,
                source,
                "analyzer.owned-array-flow-capture",
                evidenceKey: "evidence.owned-array-flow-capture");
            foreach (var fact in facts) pathState = pathState.AddFact(fact);

            return pathState;
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

        public PurityAnalysisState WithOwnedLocalArray(ISymbol localSymbol)
        {
            return Copy(
                ownedLocalArraySymbols: OwnedLocalArraySymbols.Add(localSymbol),
                definitelyNullLocalSymbols: DefinitelyNullLocalSymbols.Remove(localSymbol));
        }

        public PurityAnalysisState WithoutOwnedLocalArray(ISymbol localSymbol)
        {
            if (!OwnedLocalArraySymbols.Contains(localSymbol)) return this;

            return Copy(ownedLocalArraySymbols: OwnedLocalArraySymbols.Remove(localSymbol));
        }

        public bool IsOwnedLocalArraySymbol(ISymbol localSymbol)
        {
            return OwnedLocalArraySymbols.Contains(localSymbol);
        }

        public PurityAnalysisState WithDefinitelyNullLocal(ISymbol localSymbol)
        {
            return Copy(
                ownedLocalArraySymbols: OwnedLocalArraySymbols.Remove(localSymbol),
                definitelyNullLocalSymbols: DefinitelyNullLocalSymbols.Add(localSymbol),
                localConcreteTypes: LocalConcreteTypes.Remove(localSymbol));
        }

        public PurityAnalysisState WithoutDefinitelyNullLocal(ISymbol localSymbol)
        {
            if (!DefinitelyNullLocalSymbols.Contains(localSymbol)) return this;

            return Copy(definitelyNullLocalSymbols: DefinitelyNullLocalSymbols.Remove(localSymbol));
        }

        public bool IsDefinitelyNullLocalSymbol(ISymbol localSymbol)
        {
            return DefinitelyNullLocalSymbols.Contains(localSymbol);
        }

        public PurityAnalysisState WithLocalConcreteType(ISymbol localSymbol, INamedTypeSymbol concreteType)
        {
            if (LocalConcreteTypes.TryGetValue(localSymbol, out var existingType) &&
                SymbolEqualityComparer.Default.Equals(existingType, concreteType))
                return this;

            return Copy(
                definitelyNullLocalSymbols: DefinitelyNullLocalSymbols.Remove(localSymbol),
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
            return SmtSymbolVersions.TryGetValue(symbol.OriginalDefinition, out var version)
                ? version
                : 0;
        }

        public PurityAnalysisState WithSmtSymbolDefinitionVersion(ISymbol symbol, SyntaxNode definitionSyntax)
        {
            var originalDefinition = symbol.OriginalDefinition;
            var spanStart = Math.Max(0, definitionSyntax.SpanStart);
            // Definition versions are even; CFG join (phi) versions are odd. The syntax position
            // makes reprocessing the same block idempotent, including loop backedges.
            var nextVersion = spanStart <= (int.MaxValue - 2) / 2
                ? (spanStart + 1) * 2
                : 2 + spanStart % ((int.MaxValue - 2) / 2) * 2;
            return Copy(
                smtSymbolVersions: SmtSymbolVersions.SetItem(originalDefinition, nextVersion),
                pathState: SymbolicIrReferenceScanner.RemoveVariableReferences(
                    PathState,
                    SymbolicFactFactory.GetSmtVariableName(originalDefinition)));
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
}
