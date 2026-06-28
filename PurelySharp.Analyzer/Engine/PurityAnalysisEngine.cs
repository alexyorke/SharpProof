using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.FlowAnalysis;
using System.Collections.Immutable;
using System;
using System.IO;
using System.Globalization;
using PurelySharp.Analyzer.Engine.Smt;
using PurelySharp.Analyzer.Engine.Rules;
using PurelySharp.Analyzer.Engine.Symbolic;
using SearchLib.Purity;
using SearchLib.Smt;
using System.Threading;

namespace PurelySharp.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {
        private readonly CompilationPurityService? _purityService;
        private readonly SmtAnalysisService _smtAnalysis;

        public PurityAnalysisEngine()
        {
            _smtAnalysis = new SmtAnalysisService(SmtAnalysisOptions.Default);
        }

        public PurityAnalysisEngine(CompilationPurityService? purityService)
        {
            _purityService = purityService;
            _smtAnalysis = purityService?.SmtAnalysis ?? new SmtAnalysisService(SmtAnalysisOptions.Default);
        }


        private static readonly SymbolDisplayFormat _signatureFormat = new SymbolDisplayFormat(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            memberOptions:
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


        private static readonly ImmutableList<IPurityRule> _purityRules = Rules.RuleRegistry.GetDefaultRules();

        /// <summary>First registry rule per <see cref="OperationKind"/>; matches former <c>FirstOrDefault</c> over <see cref="_purityRules"/>.</summary>
        private static readonly ImmutableDictionary<OperationKind, IPurityRule> _firstRuleByOperationKind = BuildFirstRuleByOperationKind(_purityRules);

        private static ImmutableDictionary<OperationKind, IPurityRule> BuildFirstRuleByOperationKind(ImmutableList<IPurityRule> rules)
        {
            var builder = ImmutableDictionary.CreateBuilder<OperationKind, IPurityRule>();
            foreach (var rule in rules)
            {
                foreach (var kind in rule.ApplicableOperationKinds)
                {
                    if (!builder.ContainsKey(kind))
                        builder.Add(kind, rule);
                }
            }
            return builder.ToImmutable();
        }






        public readonly struct PurityAnalysisResult
        {

            public bool IsPure { get; }


            public SyntaxNode? ImpureSyntaxNode { get; }

            public PurityEvidence Evidence { get; }

            private PurityAnalysisResult(bool isPure, SyntaxNode? impureSyntaxNode, PurityEvidence evidence)
            {
                IsPure = isPure;
                ImpureSyntaxNode = impureSyntaxNode;
                Evidence = evidence;
            }


            public static PurityAnalysisResult Pure => new PurityAnalysisResult(true, null, PurityEvidence.None);


            public static PurityAnalysisResult Impure(SyntaxNode impureSyntaxNode)
            {

                if (impureSyntaxNode == null)
                {
                    throw new ArgumentNullException(nameof(impureSyntaxNode), "Use ImpureUnknownLocation for impurity without a specific node.");
                }
                return new PurityAnalysisResult(false, impureSyntaxNode, PurityEvidence.Create("unsupported_operation", ruleName: "UnsupportedOperation", syntaxNode: impureSyntaxNode));
            }

            public static PurityAnalysisResult Impure(SyntaxNode impureSyntaxNode, PurityEvidence evidence)
            {
                if (impureSyntaxNode == null)
                {
                    throw new ArgumentNullException(nameof(impureSyntaxNode), "Use ImpureUnknownLocation for impurity without a specific node.");
                }

                if (evidence.IsEmpty)
                {
                    evidence = PurityEvidence.Create("unsupported_operation", ruleName: "UnsupportedOperation", syntaxNode: impureSyntaxNode);
                }

                return new PurityAnalysisResult(false, impureSyntaxNode, evidence.WithSyntax(impureSyntaxNode));
            }


            public static PurityAnalysisResult ImpureUnknownLocation => new PurityAnalysisResult(false, null, PurityEvidence.Create("unknown"));

            public PurityAnalysisResult WithEvidence(PurityEvidence evidence)
            {
                return IsPure ? this : new PurityAnalysisResult(false, ImpureSyntaxNode, evidence);
            }

            public PurityAnalysisResult WithCallee(IMethodSymbol calleeSymbol, SyntaxNode? callSite)
            {
                if (IsPure)
                {
                    return this;
                }

                var evidence = Evidence.IsEmpty
                    ? PurityEvidence.Create("impure_callee", symbol: calleeSymbol, syntaxNode: callSite)
                    : Evidence.WithCallee(calleeSymbol.ToDisplayString(_signatureFormat), callSite);
                return new PurityAnalysisResult(false, ImpureSyntaxNode ?? callSite, evidence);
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

            private PurityEvidence(
                string category,
                string ruleName,
                string operationKind,
                string symbol,
                string catalogSource,
                string calleeChain)
            {
                Category = category;
                RuleName = ruleName;
                OperationKind = operationKind;
                Symbol = symbol;
                CatalogSource = catalogSource;
                CalleeChain = calleeChain;
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
                string? calleeChain = null)
            {
                var operationKind = operation?.Kind.ToString() ?? syntaxNode?.Kind().ToString() ?? string.Empty;
                return new PurityEvidence(
                    category,
                    ruleName ?? string.Empty,
                    operationKind,
                    symbol?.ToDisplayString(_signatureFormat) ?? string.Empty,
                    catalogSource ?? string.Empty,
                    calleeChain ?? string.Empty);
            }

            public PurityEvidence WithSyntax(SyntaxNode syntaxNode)
            {
                if (!string.IsNullOrEmpty(OperationKind))
                {
                    return this;
                }

                return new PurityEvidence(Category, RuleName, syntaxNode.Kind().ToString(), Symbol, CatalogSource, CalleeChain);
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
                    chain);
            }

            public ImmutableDictionary<string, string?> ToDiagnosticProperties()
            {
                var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityCategoryProperty, Category);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityRuleProperty, RuleName);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityOperationKindProperty, OperationKind);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpuritySymbolProperty, Symbol);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityCatalogSourceProperty, CatalogSource);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityCalleeChainProperty, CalleeChain);
                return builder.ToImmutable();
            }

            public string ToSummary()
            {
                var category = string.IsNullOrEmpty(Category) ? "unknown" : Category;
                if (!string.IsNullOrEmpty(Symbol))
                {
                    return category + " at " + Symbol;
                }

                return category;
            }

            private static void AddIfPresent(ImmutableDictionary<string, string?>.Builder builder, string key, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    builder[key] = value;
                }
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
            public ImmutableHashSet<ISymbol> OwnedLocalArraySymbols { get; }
            public ImmutableHashSet<ISymbol> DefinitelyNullLocalSymbols { get; }
            public ImmutableDictionary<ISymbol, INamedTypeSymbol> LocalConcreteTypes { get; }
            public ImmutableDictionary<ISymbol, int> SmtSymbolVersions { get; }
            public ImmutableArray<SmtFormula> PathConditions { get; }


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
                ImmutableArray<SmtFormula>? pathConditions = null,
                ImmutableDictionary<CaptureId, ISymbol>? flowCaptureSymbols = null,
                ImmutableHashSet<CaptureId>? ownedArrayFlowCaptures = null)
            {
                HasPotentialImpurity = hasPotentialImpurity;
                FirstImpureSyntaxNode = firstImpureSyntaxNode;
                FirstImpurityEvidence = firstImpurityEvidence;

                DelegateTargetMap = delegateTargetMap ?? ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
                FlowCaptures = flowCaptures ?? ImmutableDictionary<CaptureId, PurityAnalysisResult>.Empty;
                FlowCaptureTargets = flowCaptureTargets ?? ImmutableDictionary<CaptureId, PotentialTargets>.Empty;
                FlowCaptureConcreteTypes = flowCaptureConcreteTypes ?? ImmutableDictionary<CaptureId, INamedTypeSymbol>.Empty;
                FlowCaptureSymbols = flowCaptureSymbols ?? ImmutableDictionary.Create<CaptureId, ISymbol>();
                OwnedArrayFlowCaptures = ownedArrayFlowCaptures ?? ImmutableHashSet<CaptureId>.Empty;
                OwnedLocalArraySymbols = ownedLocalArraySymbols ?? ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
                DefinitelyNullLocalSymbols = definitelyNullLocalSymbols ?? ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
                LocalConcreteTypes = localConcreteTypes ?? ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
                SmtSymbolVersions = smtSymbolVersions ?? ImmutableDictionary.Create<ISymbol, int>(SymbolEqualityComparer.Default);
                PathConditions = pathConditions ?? ImmutableArray<SmtFormula>.Empty;
            }


            public static PurityAnalysisState Pure => new PurityAnalysisState(false, null, null, null);


            public static PurityAnalysisState Merge(IEnumerable<PurityAnalysisState> states)
            {
                var stateList = states.ToList();
                bool mergedImpurity = false;
                SyntaxNode? firstImpureNode = null;
                PurityEvidence firstEvidence = PurityEvidence.None;
                foreach (var state in stateList)
                {

                    if (state.HasPotentialImpurity)
                    {
                        mergedImpurity = true;
                        if (firstImpureNode == null)
                        {
                            firstImpureNode = state.FirstImpureSyntaxNode;
                            firstEvidence = state.FirstImpurityEvidence;
                        }
                    }


                }

                var mergedTargets = MergeDelegateTargetMapsAcrossAll(stateList.Select(s => s.DelegateTargetMap));
                var mergedCaptures = MergeFlowCaptureMaps(stateList.Select(s => s.FlowCaptures));
                var mergedCaptureTargets = MergeFlowCaptureTargetMapsAcrossAll(stateList.Select(s => s.FlowCaptureTargets));
                var mergedCaptureConcreteTypes = IntersectFlowCaptureConcreteTypesAcrossAll(stateList.Select(s => s.FlowCaptureConcreteTypes));
                var mergedCaptureSymbols = IntersectFlowCaptureSymbolsAcrossAll(stateList.Select(s => s.FlowCaptureSymbols));
                var mergedOwnedArrayFlowCaptures = IntersectOwnedArrayFlowCapturesAcrossAll(stateList.Select(s => s.OwnedArrayFlowCaptures));
                var mergedOwnedLocalArrays = IntersectOwnedLocalArraySymbolsAcrossAll(stateList.Select(s => s.OwnedLocalArraySymbols));
                var mergedDefinitelyNullLocals = IntersectOwnedLocalArraySymbolsAcrossAll(stateList.Select(s => s.DefinitelyNullLocalSymbols));
                var mergedLocalConcreteTypes = IntersectLocalConcreteTypesAcrossAll(stateList.Select(s => s.LocalConcreteTypes));
                return new PurityAnalysisState(mergedImpurity, firstImpureNode, mergedTargets, mergedCaptures, mergedCaptureTargets, mergedOwnedLocalArrays, mergedDefinitelyNullLocals, firstEvidence, localConcreteTypes: mergedLocalConcreteTypes, smtSymbolVersions: MergeSmtSymbolVersionsAcrossAll(stateList.Select(s => s.SmtSymbolVersions)), flowCaptureConcreteTypes: mergedCaptureConcreteTypes, pathConditions: MergePathConditionsAcrossAll(stateList.Select(s => s.PathConditions)), flowCaptureSymbols: mergedCaptureSymbols, ownedArrayFlowCaptures: mergedOwnedArrayFlowCaptures);
            }


            public bool Equals(PurityAnalysisState other)
            {
                if (this.HasPotentialImpurity != other.HasPotentialImpurity ||
                    !object.Equals(this.FirstImpureSyntaxNode, other.FirstImpureSyntaxNode) ||
                    !this.FirstImpurityEvidence.Equals(other.FirstImpurityEvidence) ||
                    this.DelegateTargetMap.Count != other.DelegateTargetMap.Count ||
                    this.FlowCaptures.Count != other.FlowCaptures.Count ||
                    this.FlowCaptureTargets.Count != other.FlowCaptureTargets.Count ||
                    this.FlowCaptureConcreteTypes.Count != other.FlowCaptureConcreteTypes.Count ||
                    this.FlowCaptureSymbols.Count != other.FlowCaptureSymbols.Count ||
                    this.OwnedArrayFlowCaptures.Count != other.OwnedArrayFlowCaptures.Count ||
                    this.OwnedLocalArraySymbols.Count != other.OwnedLocalArraySymbols.Count ||
                    this.DefinitelyNullLocalSymbols.Count != other.DefinitelyNullLocalSymbols.Count ||
                    this.LocalConcreteTypes.Count != other.LocalConcreteTypes.Count ||
                    this.SmtSymbolVersions.Count != other.SmtSymbolVersions.Count ||
                    this.PathConditions.Length != other.PathConditions.Length)
                {
                    return false;
                }



                foreach (var kvp in this.DelegateTargetMap)
                {
                    if (!other.DelegateTargetMap.TryGetValue(kvp.Key, out var otherValue) || !kvp.Value.Equals(otherValue))
                    {
                        return false;
                    }
                }

                foreach (var kvp in this.FlowCaptures)
                {
                    if (!other.FlowCaptures.TryGetValue(kvp.Key, out var otherCap) || !PurityResultsEqual(kvp.Value, otherCap))
                    {
                        return false;
                    }
                }

                foreach (var kvp in this.FlowCaptureTargets)
                {
                    if (!other.FlowCaptureTargets.TryGetValue(kvp.Key, out var otherTargets) || !kvp.Value.Equals(otherTargets))
                    {
                        return false;
                    }
                }

                foreach (var kvp in this.FlowCaptureConcreteTypes)
                {
                    if (!other.FlowCaptureConcreteTypes.TryGetValue(kvp.Key, out var otherType) ||
                        !SymbolEqualityComparer.Default.Equals(kvp.Value, otherType))
                    {
                        return false;
                    }
                }

                foreach (var kvp in this.FlowCaptureSymbols)
                {
                    if (!other.FlowCaptureSymbols.TryGetValue(kvp.Key, out var otherSymbol) ||
                        !SymbolEqualityComparer.Default.Equals(kvp.Value, otherSymbol))
                    {
                        return false;
                    }
                }

                foreach (var captureId in this.OwnedArrayFlowCaptures)
                {
                    if (!other.OwnedArrayFlowCaptures.Contains(captureId))
                    {
                        return false;
                    }
                }

                foreach (var symbol in this.OwnedLocalArraySymbols)
                {
                    if (!other.OwnedLocalArraySymbols.Contains(symbol))
                    {
                        return false;
                    }
                }

                foreach (var symbol in this.DefinitelyNullLocalSymbols)
                {
                    if (!other.DefinitelyNullLocalSymbols.Contains(symbol))
                    {
                        return false;
                    }
                }

                for (var i = 0; i < this.PathConditions.Length; i++)
                {
                    if (!Equals(this.PathConditions[i], other.PathConditions[i]))
                    {
                        return false;
                    }
                }

                foreach (var kvp in this.LocalConcreteTypes)
                {
                    if (!other.LocalConcreteTypes.TryGetValue(kvp.Key, out var otherType) ||
                        !SymbolEqualityComparer.Default.Equals(kvp.Value, otherType))
                    {
                        return false;
                    }
                }

                foreach (var kvp in this.SmtSymbolVersions)
                {
                    if (!other.SmtSymbolVersions.TryGetValue(kvp.Key, out var otherVersion) ||
                        kvp.Value != otherVersion)
                    {
                        return false;
                    }
                }

                return true;
            }

            public override bool Equals(object obj)
            {
                return obj is PurityAnalysisState other && Equals(other);
            }


            public override int GetHashCode()
            {

                int hash = 17;
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
                {
                    hash = hash * 23 + captureId.GetHashCode();
                }

                foreach (var symbol in OwnedLocalArraySymbols.OrderBy(sym => sym.Name))
                {
                    hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(symbol);
                }

                foreach (var symbol in DefinitelyNullLocalSymbols.OrderBy(sym => sym.Name))
                {
                    hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(symbol);
                }

                foreach (var condition in PathConditions)
                {
                    hash = hash * 23 + condition.GetHashCode();
                }

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

            public static bool operator ==(PurityAnalysisState left, PurityAnalysisState right) => left.Equals(right);
            public static bool operator !=(PurityAnalysisState left, PurityAnalysisState right) => !(left == right);

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
                ImmutableArray<SmtFormula>? pathConditions = null,
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
                    localConcreteTypes: localConcreteTypes ?? LocalConcreteTypes,
                    smtSymbolVersions: smtSymbolVersions ?? SmtSymbolVersions,
                    flowCaptureConcreteTypes: flowCaptureConcreteTypes ?? FlowCaptureConcreteTypes,
                    pathConditions: pathConditions ?? PathConditions,
                    flowCaptureSymbols: flowCaptureSymbols ?? FlowCaptureSymbols,
                    ownedArrayFlowCaptures: ownedArrayFlowCaptures ?? OwnedArrayFlowCaptures);
            }


            public PurityAnalysisState WithImpurity(SyntaxNode node)
            {
                if (HasPotentialImpurity) return this;
                return Copy(
                    hasPotentialImpurity: true,
                    firstImpureSyntaxNode: node,
                    updateFirstImpureSyntaxNode: true,
                    firstImpurityEvidence: PurityEvidence.Create("unsupported_operation", ruleName: "UnsupportedOperation", syntaxNode: node));
            }

            public PurityAnalysisState WithImpurity(PurityAnalysisResult result, SyntaxNode fallbackNode)
            {
                if (HasPotentialImpurity) return this;
                var node = result.ImpureSyntaxNode ?? fallbackNode;
                var evidence = result.Evidence.IsEmpty
                    ? PurityEvidence.Create("unsupported_operation", ruleName: "UnsupportedOperation", syntaxNode: node)
                    : result.Evidence.WithSyntax(node);
                return Copy(
                    hasPotentialImpurity: true,
                    firstImpureSyntaxNode: node,
                    updateFirstImpureSyntaxNode: true,
                    firstImpurityEvidence: evidence);
            }

            public PurityAnalysisState WithDelegateTarget(ISymbol delegateSymbol, PotentialTargets targets)
            {

                var newMap = this.DelegateTargetMap.SetItem(delegateSymbol, targets);
                return Copy(delegateTargetMap: newMap);
            }

            public PurityAnalysisState WithoutDelegateTarget(ISymbol delegateSymbol)
            {
                if (!this.DelegateTargetMap.ContainsKey(delegateSymbol))
                {
                    return this;
                }

                var newMap = this.DelegateTargetMap.Remove(delegateSymbol);
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
                {
                    return this;
                }

                return Copy(flowCaptureConcreteTypes: FlowCaptureConcreteTypes.SetItem(id, concreteType));
            }

            public PurityAnalysisState WithFlowCaptureSymbol(CaptureId id, ISymbol symbol)
            {
                return Copy(flowCaptureSymbols: FlowCaptureSymbols.SetItem(id, symbol));
            }

            public PurityAnalysisState WithOwnedArrayFlowCapture(CaptureId id)
            {
                if (OwnedArrayFlowCaptures.Contains(id))
                {
                    return this;
                }

                return Copy(ownedArrayFlowCaptures: OwnedArrayFlowCaptures.Add(id));
            }

            public PurityAnalysisState WithoutOwnedArrayFlowCapture(CaptureId id)
            {
                if (!OwnedArrayFlowCaptures.Contains(id))
                {
                    return this;
                }

                return Copy(ownedArrayFlowCaptures: OwnedArrayFlowCaptures.Remove(id));
            }

            public bool IsOwnedArrayFlowCapture(CaptureId id)
            {
                return OwnedArrayFlowCaptures.Contains(id);
            }

            public PurityAnalysisState WithOwnedLocalArray(ISymbol localSymbol)
            {
                return Copy(
                    ownedLocalArraySymbols: OwnedLocalArraySymbols.Add(localSymbol),
                    definitelyNullLocalSymbols: DefinitelyNullLocalSymbols.Remove(localSymbol));
            }

            public PurityAnalysisState WithoutOwnedLocalArray(ISymbol localSymbol)
            {
                if (!OwnedLocalArraySymbols.Contains(localSymbol))
                {
                    return this;
                }

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
                if (!DefinitelyNullLocalSymbols.Contains(localSymbol))
                {
                    return this;
                }

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
                {
                    return this;
                }

                return Copy(
                    definitelyNullLocalSymbols: DefinitelyNullLocalSymbols.Remove(localSymbol),
                    localConcreteTypes: LocalConcreteTypes.SetItem(localSymbol, concreteType));
            }

            public PurityAnalysisState WithoutLocalConcreteType(ISymbol localSymbol)
            {
                if (!LocalConcreteTypes.ContainsKey(localSymbol))
                {
                    return this;
                }

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

            public PurityAnalysisState WithPathConditions(ImmutableArray<SmtFormula> pathConditions)
            {
                return Copy(pathConditions: pathConditions);
            }

            public int GetSmtSymbolVersion(ISymbol symbol)
            {
                return SmtSymbolVersions.TryGetValue(symbol.OriginalDefinition, out var version)
                    ? version
                    : 0;
            }

            public PurityAnalysisState WithIncrementedSmtSymbolVersion(ISymbol symbol)
            {
                var originalDefinition = symbol.OriginalDefinition;
                var nextVersion = GetSmtSymbolVersion(originalDefinition) + 1;
                return Copy(
                    smtSymbolVersions: SmtSymbolVersions.SetItem(originalDefinition, nextVersion),
                    pathConditions: RemovePathConditionsReferencingSymbol(originalDefinition));
            }

            private ImmutableArray<SmtFormula> RemovePathConditionsReferencingSymbol(ISymbol symbol)
            {
                if (PathConditions.IsDefaultOrEmpty)
                {
                    return PathConditions;
                }

                var variablePrefix = GetSmtSymbolVariablePrefix(symbol);
                var builder = ImmutableArray.CreateBuilder<SmtFormula>(PathConditions.Length);
                foreach (var condition in PathConditions)
                {
                    if (!ReferencesSmtVariable(condition, variablePrefix))
                    {
                        builder.Add(condition);
                    }
                }

                return builder.Count == PathConditions.Length
                    ? PathConditions
                    : builder.ToImmutable();
            }

            private static string GetSmtSymbolVariablePrefix(ISymbol symbol)
            {
                var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
                return symbol.Name + "#" + start.ToString(CultureInfo.InvariantCulture);
            }

            private static bool ReferencesSmtVariable(SmtFormula formula, string variablePrefix)
            {
                switch (formula)
                {
                    case SmtVariable variable:
                        return variable.Name.Contains(variablePrefix, StringComparison.Ordinal);
                    case SmtUnaryFormula unary:
                        return ReferencesSmtVariable(unary.Operand, variablePrefix);
                    case SmtBinaryFormula binary:
                        return ReferencesSmtVariable(binary.Left, variablePrefix) ||
                            ReferencesSmtVariable(binary.Right, variablePrefix);
                    case SmtIntegerUnaryTerm unary:
                        return ReferencesSmtVariable(unary.Operand, variablePrefix);
                    case SmtIntegerBinaryTerm binary:
                        return ReferencesSmtVariable(binary.Left, variablePrefix) ||
                            ReferencesSmtVariable(binary.Right, variablePrefix);
                    case SmtConditionalFormula conditional:
                        return ReferencesSmtVariable(conditional.Condition, variablePrefix) ||
                            ReferencesSmtVariable(conditional.WhenTrue, variablePrefix) ||
                            ReferencesSmtVariable(conditional.WhenFalse, variablePrefix);
                    default:
                        return false;
                }
            }

            private static bool PurityResultsEqual(PurityAnalysisResult a, PurityAnalysisResult b)
            {
                if (a.IsPure != b.IsPure) return false;
                if (a.IsPure) return true;
                return Equals(a.ImpureSyntaxNode, b.ImpureSyntaxNode);
            }

            private static ImmutableDictionary<CaptureId, PurityAnalysisResult> MergeFlowCaptureMaps(
                IEnumerable<ImmutableDictionary<CaptureId, PurityAnalysisResult>> maps)
            {
                var acc = ImmutableDictionary<CaptureId, PurityAnalysisResult>.Empty;
                foreach (var map in maps)
                {
                    foreach (var kvp in map)
                    {
                        if (acc.TryGetValue(kvp.Key, out var existing))
                            acc = acc.SetItem(kvp.Key, MergeCapturePurity(existing, kvp.Value));
                        else
                            acc = acc.SetItem(kvp.Key, kvp.Value);
                    }
                }

                return acc;
            }

            private static ImmutableDictionary<CaptureId, ISymbol> IntersectFlowCaptureSymbols(
                ImmutableDictionary<CaptureId, ISymbol> first,
                ImmutableDictionary<CaptureId, ISymbol> second)
            {
                return IntersectFlowCaptureSymbolMapsCore(first, second);
            }

            private static ImmutableDictionary<CaptureId, ISymbol> IntersectFlowCaptureSymbolsAcrossAll(
                IEnumerable<ImmutableDictionary<CaptureId, ISymbol>> maps)
            {
                using var enumerator = maps.GetEnumerator();
                if (!enumerator.MoveNext())
                {
                    return ImmutableDictionary.Create<CaptureId, ISymbol>();
                }

                var merged = enumerator.Current;
                while (enumerator.MoveNext())
                {
                    merged = IntersectFlowCaptureSymbols(merged, enumerator.Current);
                }

                return merged;
            }

            private static PurityAnalysisResult MergeCapturePurity(PurityAnalysisResult a, PurityAnalysisResult b)
            {
                if (!a.IsPure) return a;
                if (!b.IsPure) return b;
                return PurityAnalysisResult.Pure;
            }

            internal static ImmutableDictionary<CaptureId, PurityAnalysisResult> MergeFlowCaptureMapsForPair(
                ImmutableDictionary<CaptureId, PurityAnalysisResult> a,
                ImmutableDictionary<CaptureId, PurityAnalysisResult> b)
            {
                if (a.IsEmpty) return b;
                if (b.IsEmpty) return a;
                var acc = a;
                foreach (var kvp in b)
                {
                    if (acc.TryGetValue(kvp.Key, out var existing))
                        acc = acc.SetItem(kvp.Key, MergeCapturePurity(existing, kvp.Value));
                    else
                        acc = acc.SetItem(kvp.Key, kvp.Value);
                }

                return acc;
            }
        }


        internal readonly struct PotentialTargets : IEquatable<PotentialTargets>
        {


            public ImmutableHashSet<IMethodSymbol> MethodSymbols { get; }
            public bool IsUnresolved { get; }



            public PotentialTargets(ImmutableHashSet<IMethodSymbol>? methodSymbols)
                : this(methodSymbols, isUnresolved: false)
            {
            }

            private PotentialTargets(ImmutableHashSet<IMethodSymbol>? methodSymbols, bool isUnresolved)
            {
                MethodSymbols = methodSymbols ?? ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default);
                IsUnresolved = isUnresolved;
            }

            public static PotentialTargets Empty => new PotentialTargets(null);
            public static PotentialTargets Unresolved => new PotentialTargets(null, isUnresolved: true);

            public static PotentialTargets FromSingle(IMethodSymbol methodSymbol)
            {
                if (methodSymbol == null) return Empty;
                return new PotentialTargets(ImmutableHashSet.Create<IMethodSymbol>(SymbolEqualityComparer.Default, methodSymbol));
            }


            public static PotentialTargets Merge(PotentialTargets first, PotentialTargets second)
            {
                if (first.IsUnresolved || second.IsUnresolved)
                {
                    return Unresolved;
                }

                return new PotentialTargets(first.MethodSymbols.Union(second.MethodSymbols));
            }

            public bool Equals(PotentialTargets other)
            {

                return this.IsUnresolved == other.IsUnresolved &&
                       this.MethodSymbols.SetEquals(other.MethodSymbols);
            }

            public override bool Equals(object obj) => obj is PotentialTargets other && Equals(other);

            public override int GetHashCode()
            {
                int hash = IsUnresolved ? 31 : 17;
                foreach (var symbol in MethodSymbols.OrderBy(s => s.Name))
                {
                    hash = hash * 23 + SymbolEqualityComparer.Default.GetHashCode(symbol);
                }
                return hash;
            }
        }


        internal PurityAnalysisResult IsConsideredPure(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol)
        {




            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            var purityCache = new Dictionary<IMethodSymbol, PurityAnalysisResult>(SymbolEqualityComparer.Default);

            LogDebug($">> Enter DeterminePurity: {methodSymbol.ToDisplayString(_signatureFormat)}");


            var result = DeterminePurityRecursiveInternal(
                methodSymbol,
                semanticModel,
                enforcePureAttributeSymbol,
                allowSynchronizationAttributeSymbol,
                visited,
                purityCache,
                _smtAnalysis
            );

            LogDebug($"<< Exit DeterminePurity ({GetPuritySource(result)}): {methodSymbol.ToDisplayString(_signatureFormat)}, Final IsPure={result.IsPure}");
            LogDebug($"-- Removed Walker for: {methodSymbol.ToDisplayString(_signatureFormat)}");


            purityCache[methodSymbol] = result;

            return result;
        }


        private static string GetPuritySource(PurityAnalysisResult result)
        {

            if (result.IsPure) return "Assumed/Analyzed Pure";
            if (result.ImpureSyntaxNode != null) return "Analyzed Impure";

            return "Unknown/Default Impure";
        }


        internal static PurityAnalysisResult DeterminePurityRecursiveInternal(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            HashSet<IMethodSymbol> visited,
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
            SmtAnalysisService? smtAnalysis = null)
        {

            var activeSmtAnalysis = smtAnalysis ?? new SmtAnalysisService(SmtAnalysisOptions.Default);
            var indent = new string(' ', visited.Count * 2);
            LogDebug($"{indent}>> Enter DeterminePurity: {methodSymbol.ToDisplayString()}");



            if (purityCache.TryGetValue(methodSymbol, out var cachedResult))
            {
                LogDebug($"{indent}  Purity CACHED: {cachedResult.IsPure} for {methodSymbol.ToDisplayString()}");
                LogDebug($"{indent}<< Exit DeterminePurity (Cached): {methodSymbol.ToDisplayString()}");
                return cachedResult;
            }


            if (!visited.Add(methodSymbol))
            {
                LogDebug($"{indent}  Recursion DETECTED for {methodSymbol.ToDisplayString()}. Assuming impure for this path.");
                var recursiveResult = PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(
                    PurityEvidence.Create(
                        "unsupported_operation",
                        ruleName: "RecursivePurityAnalysis",
                        symbol: methodSymbol,
                        catalogSource: "recursive_call"));
                purityCache[methodSymbol] = recursiveResult;
                LogDebug($"{indent}<< Exit DeterminePurity (Recursion): {methodSymbol.ToDisplayString()}");
                return recursiveResult;
            }

            try
            {
                var declaringSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

                if (HasImpureAttribute(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is marked [Impure].");
                    var explicitlyImpureResult = ImpureResult(
                        declaringSyntax,
                        "impure_boundary_attribute",
                        symbol: methodSymbol,
                        catalogSource: "attribute");
                    purityCache[methodSymbol] = explicitlyImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity ([Impure]): {methodSymbol.ToDisplayString()}");
                    return explicitlyImpureResult;
                }

                if (HasPureExternalAttribute(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is marked [PureExternal].");
                    purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                    LogDebug($"{indent}<< Exit DeterminePurity ([PureExternal]): {methodSymbol.ToDisplayString()}");
                    return PurityAnalysisResult.Pure;
                }

                if (IsInConfiguredImpureNamespaceOrType(methodSymbol) && !IsConfiguredKnownPureMember(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is in a configured impure namespace/type.");
                    var configuredImpureResult = ImpureResult(
                        declaringSyntax,
                        "catalog_hit",
                        "KnownImpureMethod",
                        methodSymbol,
                        "known_impure_namespace_or_type");
                    purityCache[methodSymbol] = configuredImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Configured Impure Namespace/Type): {methodSymbol.ToDisplayString()}");
                    return configuredImpureResult;
                }

                var trustedMetadataPurity = GetTrustedMethodPurityMetadata(methodSymbol, semanticModel.Compilation);
                var knownImpureMemberSource = trustedMetadataPurity.KnownImpureMemberSource;
                var hasConfiguredKnownImpureMember = trustedMetadataPurity.HasConfiguredKnownImpureMember;
                var hasTrustedGeneratedPurity = trustedMetadataPurity.HasTrustedGeneratedPurity;
                var generatedPurity = trustedMetadataPurity.GeneratedPurity;

                if (hasConfiguredKnownImpureMember)
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is configured known impure.");
                    var configuredKnownImpureResult = ImpureResult(
                        declaringSyntax,
                        "catalog_hit",
                        "KnownImpureMethod",
                        methodSymbol,
                        knownImpureMemberSource);
                    purityCache[methodSymbol] = configuredKnownImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Configured Known Impure): {methodSymbol.ToDisplayString()}");
                    return configuredKnownImpureResult;
                }

				if (hasTrustedGeneratedPurity)
				{
					if (generatedPurity.IsPure)
					{
						LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is trusted pure from generated purity summary.");
						purityCache[methodSymbol] = PurityAnalysisResult.Pure;
						return PurityAnalysisResult.Pure;
					}

					if (!generatedPurity.IsPure)
					{
						LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is trusted impure from generated purity summary.");
						var generatedResult = ImpureResult(
							declaringSyntax,
                            generatedPurity.PrimaryCategory,
                            symbol: methodSymbol,
                            catalogSource: "generated_purity_summary");
                        purityCache[methodSymbol] = generatedResult;
                        return generatedResult;
                    }
                }

                if (knownImpureMemberSource != null)
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is known impure.");
                    var knownImpureResult = ImpureResult(
                        declaringSyntax,
                        "catalog_hit",
                        "KnownImpureMethod",
                        methodSymbol,
                        knownImpureMemberSource);
                    purityCache[methodSymbol] = knownImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Known Impure): {methodSymbol.ToDisplayString()}");
                    return knownImpureResult;
                }


                if (!hasTrustedGeneratedPurity && IsKnownPureBCLMember(methodSymbol, semanticModel.Compilation))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is known pure BCL member.");
                    purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                    LogDebug($"{indent}<< Exit DeterminePurity (Known Pure): {methodSymbol.ToDisplayString()}");
                    return PurityAnalysisResult.Pure;
                }


                SyntaxNode? bodySyntaxNode = GetBodySyntaxNode(methodSymbol, default);


                if (methodSymbol.ReturnsByRef)
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} returns by ref. IMPURE.");

                    SyntaxNode? locationSyntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.DescendantNodesAndSelf()
                        .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.RefTypeSyntax>()
                        .FirstOrDefault();

                    locationSyntax ??= methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()?.DescendantNodesAndSelf()
                                            .FirstOrDefault(n => n is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax ins && ins.Identifier.ValueText == methodSymbol.Name)
                                            ?.Parent;

                    purityCache[methodSymbol] = ImpureResult(locationSyntax ?? bodySyntaxNode);
                    LogDebug($"{indent}<< Exit DeterminePurity (ReturnsByRef): {methodSymbol.ToDisplayString()}");
                    return purityCache[methodSymbol];
                }



                if (methodSymbol.IsExtern)
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is extern. Assuming impure due unknown implementation.");
                    var externResult = ImpureResult(
                        declaringSyntax,
                        "unknown_external_call",
                        "MethodInvocationPurityRule",
                        methodSymbol,
                        "extern");
                    purityCache[methodSymbol] = externResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Extern): {methodSymbol.ToDisplayString()}");
                    return externResult;
                }

                if (methodSymbol.IsAbstract || bodySyntaxNode == null)
                {
                    if (methodSymbol.DeclaringSyntaxReferences.Length > 0 &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true &&
                        (methodSymbol.IsAbstract || methodSymbol.ContainingType?.TypeKind == TypeKind.Interface))
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source contract without an explicit body. Deferring validation to dispatch or implementation sites.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Contract Without Body): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    if (methodSymbol.MethodKind == MethodKind.PropertyGet &&
                        methodSymbol.DeclaringSyntaxReferences.Length > 0 &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true &&
                        !methodSymbol.IsAbstract &&
                        methodSymbol.ContainingType?.TypeKind != TypeKind.Interface)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source property getter without an explicit body. Treating as pure.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Auto Getter): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    if ((methodSymbol.MethodKind == MethodKind.Constructor ||
                         methodSymbol.MethodKind == MethodKind.StaticConstructor) &&
                        !methodSymbol.IsExtern &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source constructor without an explicit body. Treating as pure.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Constructor Without Body): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    if (hasTrustedGeneratedPurity && !generatedPurity.IsPure)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} has no body but does have trusted non-pure generated summary evidence.");
                        var generatedNoBodyResult = ImpureResult(
                            declaringSyntax,
                            generatedPurity.PrimaryCategory,
                            "MethodInvocationPurityRule",
                            methodSymbol,
                            "generated_purity_summary");
                        purityCache[methodSymbol] = generatedNoBodyResult;
                        LogDebug($"{indent}<< Exit DeterminePurity (Abstract/NoBody Generated Summary): {methodSymbol.ToDisplayString()}");
                        return generatedNoBodyResult;
                    }

                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is abstract or has no body AND lacks trusted purity evidence. Assuming impure.");
                    var noBodyResult = ImpureResult(
                        declaringSyntax,
                        "unknown_external_call",
                        "MethodInvocationPurityRule",
                        methodSymbol,
                        "no_body");
                    purityCache[methodSymbol] = noBodyResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Abstract/NoBody): {methodSymbol.ToDisplayString()}");
                    return noBodyResult;
                }


                IOperation? methodBodyIOperation = null;
                if (bodySyntaxNode != null)
                {
                    try
                    {
                        methodBodyIOperation = semanticModel.GetOperation(bodySyntaxNode, default);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"{indent}  Post-CFG: Error getting IOperation for method body: {ex.Message}");
                        methodBodyIOperation = null;
                    }
                }

                PurityAnalysisResult result = PurityAnalysisResult.Pure;
                var mergedDelegateTargetsFromCfg = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
                var mergedOwnedArrayFlowCapturesFromCfg = ImmutableHashSet<CaptureId>.Empty;
                var mergedOwnedLocalArraysFromCfg = ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
                var mergedLocalConcreteTypesFromCfg = ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
                if (bodySyntaxNode != null)
                {
                    bool requiresNestedBodyFallback = methodBodyIOperation?.Parent != null;
                    if (requiresNestedBodyFallback && methodBodyIOperation != null)
                    {
                        LogDebug($"{indent}Analyzing body of {methodSymbol.ToDisplayString()} using nested subtree fallback.");
                        result = AnalyzeOperationSubtreePurity(
                            methodBodyIOperation,
                            semanticModel,
                            enforcePureAttributeSymbol,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            methodSymbol,
                            purityCache);
                    }
                    else
                    {
                        LogDebug($"{indent}Analyzing body of {methodSymbol.ToDisplayString()} using CFG.");
                        result = AnalyzePurityUsingCFGInternal(
                            bodySyntaxNode,
                            semanticModel,
                            enforcePureAttributeSymbol,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            methodSymbol,
                            purityCache,
                            activeSmtAnalysis,
                            out mergedDelegateTargetsFromCfg,
                            out mergedOwnedArrayFlowCapturesFromCfg,
                            out mergedOwnedLocalArraysFromCfg,
                            out mergedLocalConcreteTypesFromCfg);
                    }

                    LogDebug($"{indent}  CFG Analysis Result for {methodSymbol.ToDisplayString()}: IsPure={result.IsPure}, ImpureNode={result.ImpureSyntaxNode?.Kind()}");
                }


                if (result.IsPure)
                {
                    LogDebug($"{indent}Post-CFG: CFG Result was Pure. Performing Post-CFG checks for {methodSymbol.ToDisplayString()}.");

                    if (methodBodyIOperation != null)
                    {
                        var pureAttrSymbolForContext = semanticModel.Compilation.GetTypeByMetadataName("PurelySharp.Attributes.PureAttribute");
                        var postCfgContext = new Rules.PurityAnalysisContext(
                            semanticModel,
                            enforcePureAttributeSymbol,
                            pureAttrSymbolForContext,
                            allowSynchronizationAttributeSymbol,
                            visited,
                            purityCache,
                            methodSymbol,
                            _purityRules,
                            CancellationToken.None,
                            null);


                        LogDebug($"{indent}  Post-CFG: Checking ReturnOperations (with merged delegate map from CFG)...");
                        var postCfgReturnState = new PurityAnalysisState(
                            false,
                            null,
                            mergedDelegateTargetsFromCfg,
                            null,
                            ownedLocalArraySymbols: mergedOwnedLocalArraysFromCfg,
                            localConcreteTypes: mergedLocalConcreteTypesFromCfg,
                            ownedArrayFlowCaptures: mergedOwnedArrayFlowCapturesFromCfg);
                        foreach (var returnOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IReturnOperation>())
                        {
                            if (returnOp.ReturnedValue != null)
                            {
                                var returnPurity = CheckSingleOperation(returnOp, postCfgContext, postCfgReturnState);
                                if (!returnPurity.IsPure)
                                {
                                    LogDebug($"{indent}    Post-CFG: Return value IMPURE: {returnOp.ReturnedValue.Syntax}");
                                    result = returnPurity;
                                    goto PostCfgChecksDone;
                                }
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: ReturnOperations check complete (result still pure).");

                        LogDebug($"{indent}  Post-CFG: Checking UsingOperations for implicit Dispose purity...");
                        foreach (var usingOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).Where(op => op.Kind == OperationKind.Using || op.Kind == OperationKind.UsingDeclaration))
                        {
                            var usingResult = CheckSingleOperation(usingOp, postCfgContext, postCfgReturnState);
                            if (!usingResult.IsPure)
                            {
                                LogDebug($"{indent}    Post-CFG: Using operation is IMPURE: {usingOp.Syntax}");
                                result = usingResult;
                                goto PostCfgChecksDone;
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: UsingOperations check complete (result still pure).");

                        LogDebug($"{indent}  Post-CFG: Checking ForEach enumerator runtime purity...");
                        foreach (var forEachOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IForEachLoopOperation>())
                        {
                            var forEachResult = LoopPurityRule.CheckForEachEnumeratorPurity(forEachOp.Collection, postCfgContext);
                            if (!forEachResult.IsPure)
                            {
                                LogDebug($"{indent}    Post-CFG: Foreach enumerator runtime is IMPURE: {forEachOp.Syntax}");
                                result = forEachResult;
                                goto PostCfgChecksDone;
                            }

                            var asyncForEachResult = LoopPurityRule.CheckForEachAsyncEnumeratorPurity(forEachOp.Collection, postCfgContext);
                            if (!asyncForEachResult.IsPure)
                            {
                                LogDebug($"{indent}    Post-CFG: Async foreach enumerator runtime is IMPURE: {forEachOp.Syntax}");
                                result = asyncForEachResult;
                                goto PostCfgChecksDone;
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: ForEach enumerator runtime checks complete (result still pure).");


                        LogDebug($"{indent}  Post-CFG: Checking ThrowOperations...");
                        foreach (var firstThrowOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IThrowOperation>())
                        {
                            if (IsInStaticallyUnreachableBranch(firstThrowOp.Syntax, semanticModel, activeSmtAnalysis))
                            {
                                LogDebug($"{indent}    Post-CFG: Skipping statically unreachable throw: {firstThrowOp.Syntax}");
                                continue;
                            }

                            if (firstThrowOp.Exception != null)
                            {
                                var exResult = CheckSingleOperation(firstThrowOp.Exception, postCfgContext, PurityAnalysisState.Pure);
                                if (!exResult.IsPure)
                                {
                                    LogDebug($"{indent}    Post-CFG: Throw exception expression is IMPURE: {firstThrowOp.Exception.Syntax}");
                                    result = PurityAnalysisResult.Impure(
                                        exResult.ImpureSyntaxNode ?? firstThrowOp.Syntax,
                                        exResult.Evidence);
                                    goto PostCfgChecksDone;
                                }
                            }

                            LogDebug($"{indent}    Post-CFG: Throw operation is IMPURE: {firstThrowOp.Syntax}");
                            result = PurityAnalysisResult.Impure(
                                firstThrowOp.Syntax,
                                PurityEvidence.Create(
                                    "throw",
                                    ruleName: "ThrowOperationPurityRule",
                                    operation: firstThrowOp));
                            goto PostCfgChecksDone;
                        }
                        LogDebug($"{indent}  Post-CFG: ThrowOperations check complete (result still pure).");


                        LogDebug($"{indent}  Post-CFG: Checking Unreachable Code (Try, Catch)...");
                        foreach (var tryOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<ITryOperation>())
                        {
                            foreach (var catchClause in tryOp.Catches)
                            {
                                var catchResult = AnalyzeOperationSubtreePurity(catchClause, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache);
                                if (!catchResult.IsPure)
                                {
                                    result = catchResult;
                                    goto PostCfgChecksDone;
                                }
                            }
                            if (tryOp.Finally != null)
                            {
                                var finallyResult = AnalyzeOperationSubtreePurity(tryOp.Finally, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache);
                                if (!finallyResult.IsPure)
                                {
                                    result = finallyResult;
                                    goto PostCfgChecksDone;
                                }
                            }
                        }

                        LogDebug($"{indent}  Post-CFG: Skipping local function declarations; invoked local functions are checked through callee purity.");

                        LogDebug($"{indent}  Post-CFG: Checking Known Impure Invocations...");
                        foreach (var invocationOp in ExecutionVisibility.VisibleDescendants(methodBodyIOperation).OfType<IInvocationOperation>())
                        {
                            if (IsInStaticallyUnreachableBranch(invocationOp.Syntax, semanticModel, activeSmtAnalysis))
                            {
                                continue;
                            }

                            var hasSemanticKnownImpureCatalogSource = TryGetSemanticKnownImpureCatalogSource(
                                invocationOp,
                                out var semanticKnownImpureCatalogSource);
                            if (invocationOp.TargetMethod != null &&
                                !IsArrayAsReadOnlyInvocation(invocationOp) &&
                                !IsTransientCharArrayConsumedByStringConstructor(invocationOp, semanticModel))
                            {
                                var targetMethod = invocationOp.TargetMethod.OriginalDefinition;
                                if (hasSemanticKnownImpureCatalogSource)
                                {
                                    LogDebug($"{indent}    Post-CFG: Found semantically known impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
                                    result = ImpureResult(
                                        invocationOp,
                                        "catalog_hit",
                                        "MethodInvocationPurityRule",
                                        targetMethod,
                                        semanticKnownImpureCatalogSource);
                                    goto PostCfgChecksDone;
                                }

                                if (IsInvariantCultureDeterministicParseInvocation(invocationOp))
                                {
                                    continue;
                                }

                                var invocationMetadataPurity = GetTrustedMethodPurityMetadata(
                                    targetMethod,
                                    semanticModel.Compilation);
                                var knownImpureSource = invocationMetadataPurity.KnownImpureMemberSource;
                                var hasConfiguredKnownImpure = invocationMetadataPurity.HasConfiguredKnownImpureMember;
                                var postCfgGeneratedPurity = invocationMetadataPurity.GeneratedPurity;
                                var hasTrustedGeneratedPurityForInvocation = invocationMetadataPurity.HasTrustedGeneratedPurity;

                                if (hasConfiguredKnownImpure)
                                {
                                    LogDebug($"{indent}    Post-CFG: Found configured known impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
                                    result = ImpureResult(
                                        invocationOp,
                                        "catalog_hit",
                                        "MethodInvocationPurityRule",
                                        targetMethod,
                                        knownImpureSource);
                                    goto PostCfgChecksDone;
                                }

                                if (hasTrustedGeneratedPurityForInvocation &&
                                    !Rules.MethodInvocationPurityRule.ShouldDeferToSpecializedDispatchPurity(targetMethod))
                                {
                                    if (postCfgGeneratedPurity.IsPure)
                                    {
                                        continue;
                                    }

                                    if (!postCfgGeneratedPurity.IsPure)
                                    {
                                        LogDebug($"{indent}    Post-CFG: Found generated-summary impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
                                        result = ImpureResult(
                                            invocationOp,
                                            postCfgGeneratedPurity.PrimaryCategory,
                                            "MethodInvocationPurityRule",
                                            targetMethod,
                                            "generated_purity_summary");
                                        goto PostCfgChecksDone;
                                    }
                                }

                                if (knownImpureSource != null)
                                {
                                    LogDebug($"{indent}    Post-CFG: Found known impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
                                    result = ImpureResult(
                                        invocationOp,
                                        "catalog_hit",
                                        "MethodInvocationPurityRule",
                                        targetMethod,
                                        knownImpureSource);
                                    goto PostCfgChecksDone;
                                }
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: Known Impure Invocations check complete (result still pure).");

                        var directThrowOnlySyntax = TryGetDirectThrowOnlySyntax(bodySyntaxNode);
                        if (directThrowOnlySyntax != null)
                        {
                            LogDebug($"{indent}  Post-CFG: Found direct throw-only body IMPURE: {directThrowOnlySyntax}");
                            result = PurityAnalysisResult.Impure(
                                directThrowOnlySyntax,
                                PurityEvidence.Create(
                                    "throw",
                                    ruleName: "ThrowOperationPurityRule",
                                    syntaxNode: directThrowOnlySyntax));
                            goto PostCfgChecksDone;
                        }


                        LogDebug($"{indent}  Post-CFG: Checking Checked Operations...");
                        foreach (var operation in ExecutionVisibility.VisibleDescendants(methodBodyIOperation))
                        {
                            bool isChecked = false;
                            IMethodSymbol? operatorMethod = null;

                            if (operation is IBinaryOperation binaryOp && binaryOp.IsChecked)
                            {
                                isChecked = true;
                                operatorMethod = binaryOp.OperatorMethod;
                            }
                            else if (operation is IUnaryOperation unaryOp && unaryOp.IsChecked)
                            {
                                isChecked = true;
                                operatorMethod = unaryOp.OperatorMethod;
                            }
                            else if (operation is ICompoundAssignmentOperation compoundAssignmentOp &&
                                     compoundAssignmentOp.OperatorMethod != null &&
                                     ShouldAnalyzeCompoundAssignmentOperator(compoundAssignmentOp.OperatorMethod.OriginalDefinition))
                            {
                                isChecked = true;
                                operatorMethod = compoundAssignmentOp.OperatorMethod.OriginalDefinition;
                            }

                            if (isChecked && operatorMethod != null)
                            {
                                LogDebug($"{indent}    Post-CFG: Found Checked Operation: {operation.Syntax} with operator method {operatorMethod.Name}");
                                var contextForOp = new Rules.PurityAnalysisContext(
                                    semanticModel,
                                    enforcePureAttributeSymbol,
                                    semanticModel.Compilation.GetTypeByMetadataName("PurelySharp.Attributes.PureAttribute"),
                                    allowSynchronizationAttributeSymbol,
                                    visited,
                                    purityCache,
                                    methodSymbol,
                                    _purityRules,
                                    CancellationToken.None,
                                    null);
                                var operatorPurity = GetCalleePurity(operatorMethod, contextForOp);

                                if (!operatorPurity.IsPure)
                                {
                                    LogDebug($"{indent}    Post-CFG: Checked operator method '{operatorMethod.Name}' is IMPURE. Operation is Impure.");
                                    result = PurityAnalysisResult.Impure(operation.Syntax);
                                    goto PostCfgChecksDone;
                                }
                            }
                        }
                        LogDebug($"{indent}  Post-CFG: Checked Operations check complete (result still pure).");
                    }
                    else
                    {
                        LogDebug($"{indent}Post-CFG: methodBodyIOperation was null, skipping post-CFG checks.");
                    }
                }

            PostCfgChecksDone:;


                purityCache[methodSymbol] = result;
                LogDebug($"{indent}<< Exit DeterminePurity (Analyzed): {methodSymbol.ToDisplayString()}, Final IsPure={result.IsPure}");
                return result;
            }
            finally
            {
                visited.Remove(methodSymbol);
                LogDebug($"{indent}-- Removed Walker for: {methodSymbol.ToDisplayString()}");
            }
        }


        private static PurityAnalysisResult AnalyzePurityUsingCFGInternal(
            SyntaxNode bodyNode,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            HashSet<IMethodSymbol> visited,
            IMethodSymbol containingMethodSymbol,
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
            SmtAnalysisService smtAnalysis,
            out ImmutableDictionary<ISymbol, PotentialTargets> mergedDelegateTargetsFromBlocks,
            out ImmutableHashSet<CaptureId> mergedOwnedArrayFlowCapturesFromBlocks,
            out ImmutableHashSet<ISymbol> mergedOwnedLocalArraysFromBlocks,
            out ImmutableDictionary<ISymbol, INamedTypeSymbol> mergedLocalConcreteTypesFromBlocks)
        {
            mergedDelegateTargetsFromBlocks = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            mergedOwnedArrayFlowCapturesFromBlocks = ImmutableHashSet<CaptureId>.Empty;
            mergedOwnedLocalArraysFromBlocks = ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
            mergedLocalConcreteTypesFromBlocks = ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            // Roslyn 4.x: Create(BlockSyntax|ArrowClause, model) throws ("operation has a non-null parent").
            // Create(BaseMethodDeclarationSyntax|LocalFunctionStatement|ConstructorDeclaration|... , model) is the supported root.
            ControlFlowGraph? cfg = null;
            try
            {
                cfg = ControlFlowGraph.Create(bodyNode, semanticModel);
                LogDebug($"CFG created successfully for node: {bodyNode.Kind()}");
            }
            catch (Exception ex)
            {
                LogDebug($"Error creating ControlFlowGraph for {containingMethodSymbol.ToDisplayString()}: {ex.Message}. Assuming impure.");
                return PurityAnalysisResult.Impure(bodyNode);
            }

            if (cfg == null || cfg.Blocks.IsEmpty)
            {
                LogDebug($"CFG is null or empty for {containingMethodSymbol.ToDisplayString()}. Assuming pure (no operations).");
                return PurityAnalysisResult.Pure;
            }


            LogDebug($"  [CFG] Created CFG with {cfg.Blocks.Length} blocks for {containingMethodSymbol.ToDisplayString()}.");


            var blockStates = new Dictionary<BasicBlock, PurityAnalysisState>(cfg.Blocks.Length);
            var exitBlockStates = new Dictionary<BasicBlock, PurityAnalysisState>(cfg.Blocks.Length);
            var worklist = new Queue<BasicBlock>();
            var inQueue = new HashSet<BasicBlock>();

            if (cfg.Blocks.Any())
            {
                var entryBlock = cfg.Blocks.First();

                LogDebug($"  [CFG] Adding Entry Block #{entryBlock.Ordinal} to worklist.");
                blockStates[entryBlock] = PurityAnalysisState.Pure;
                worklist.Enqueue(entryBlock);
                inQueue.Add(entryBlock);
            }
            else
            {
                LogDebug("  [CFG] CFG has no blocks. Exiting analysis.");
                return PurityAnalysisResult.Pure;
            }


            LogDebug("  [CFG] Starting CFG dataflow analysis worklist loop.");
            int loopIterations = 0;

            LogDebug($"  [CFG] BEFORE WHILE CHECK: worklist.Count = {worklist.Count}, loopIterations = {loopIterations}");
            while (worklist.Count > 0 && loopIterations < cfg.Blocks.Length * 50)
            {

                LogDebug("  [CFG] ENTERED WHILE LOOP.");
                loopIterations++;

                LogDebug($"  [CFG] Worklist count: {worklist.Count}. Iteration: {loopIterations}");
                var currentBlock = worklist.Dequeue();
                inQueue.Remove(currentBlock);
                LogDebug($"  [CFG] Processing CFG Block #{currentBlock.Ordinal}");

                if (!blockStates.TryGetValue(currentBlock, out var stateBefore))
                {
                    stateBefore = PurityAnalysisState.Pure;
                    blockStates[currentBlock] = stateBefore;
                }

                LogDebug($"  [CFG] StateBefore for Block #{currentBlock.Ordinal}: Impure={stateBefore.HasPotentialImpurity}");


                var stateAfter = ApplyTransferFunction(
                    currentBlock,
                    stateBefore,
                    semanticModel,
                    enforcePureAttributeSymbol,
                    allowSynchronizationAttributeSymbol,
                    visited,
                    containingMethodSymbol,
                    purityCache,
                    smtAnalysis);

                exitBlockStates[currentBlock] = stateAfter;
                LogDebug($"  [CFG] State after Block #{currentBlock.Ordinal}: Impure={stateAfter.HasPotentialImpurity}");



                LogDebug($"  [CFG] Propagating stateAfter (Impure={stateAfter.HasPotentialImpurity}) to successors of Block #{currentBlock.Ordinal}.");
                if (TryGetConstantBranchDecision(currentBlock.BranchValue, semanticModel, smtAnalysis, out var takeConditionalSuccessor))
                {
                    var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);
                    var takenSuccessor = takeConditionalSuccessor
                        ? (trueUsesConditionalSuccessor
                            ? currentBlock.ConditionalSuccessor?.Destination
                            : currentBlock.FallThroughSuccessor?.Destination)
                        : (trueUsesConditionalSuccessor
                            ? currentBlock.FallThroughSuccessor?.Destination
                            : currentBlock.ConditionalSuccessor?.Destination);
                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, takeConditionalSuccessor, smtAnalysis, out var takenState))
                    {
                        PropagateToSuccessor(takenSuccessor, takenState, blockStates, worklist, inQueue);
                    }
                }
                else
                {
                    var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);

                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, trueUsesConditionalSuccessor, smtAnalysis, out var conditionalState))
                    {
                        PropagateToSuccessor(currentBlock.ConditionalSuccessor?.Destination, conditionalState, blockStates, worklist, inQueue);
                    }

                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, !trueUsesConditionalSuccessor, smtAnalysis, out var fallThroughState))
                    {
                        PropagateToSuccessor(currentBlock.FallThroughSuccessor?.Destination, fallThroughState, blockStates, worklist, inQueue);
                    }
                }

            }

            if (worklist.Count == 0)
            {
                LogDebug("  [CFG] Finished CFG dataflow analysis worklist loop (worklist empty).");
            }
            else
            {
                LogDebug($"  [CFG] WARNING: Exited CFG dataflow loop due to iteration limit ({loopIterations}). Potential incomplete merge; continuing with aggregated block states.");
            }

            mergedDelegateTargetsFromBlocks = MergeDelegateTargetMapsFromBlockStates(exitBlockStates.Values);
            mergedOwnedArrayFlowCapturesFromBlocks = MergeOwnedArrayFlowCapturesFromBlockStates(exitBlockStates.Values);
            mergedOwnedLocalArraysFromBlocks = MergeOwnedLocalArraySymbolsFromBlockStates(exitBlockStates.Values);
            mergedLocalConcreteTypesFromBlocks = MergeLocalConcreteTypesFromBlockStates(exitBlockStates.Values);

            PurityAnalysisResult finalResult = PurityAnalysisResult.Pure;
            
            foreach (var exitState in exitBlockStates.Values)
            {
                if (exitState.HasPotentialImpurity)
                {
                    finalResult = exitState.FirstImpureSyntaxNode != null
                        ? PurityAnalysisResult.Impure(exitState.FirstImpureSyntaxNode, exitState.FirstImpurityEvidence)
                        : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(exitState.FirstImpurityEvidence);
                    LogDebug($"  [CFG] Final Result: IMPURE. Node={finalResult.ImpureSyntaxNode?.Kind()}");
                    return finalResult;
                }
            }

            LogDebug($"  [CFG] Final Result: PURE.");
            return finalResult;
        }


        private static PurityAnalysisState ApplyTransferFunction(
            BasicBlock block,
            PurityAnalysisState stateBefore,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            HashSet<IMethodSymbol> visited,
            IMethodSymbol containingMethodSymbol,
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
            SmtAnalysisService smtAnalysis)
        {
            LogDebug($"ApplyTransferFunction START for Block #{block.Ordinal} - Initial State: Impure={stateBefore.HasPotentialImpurity}");

            if (stateBefore.HasPotentialImpurity)
            {
                LogDebug($"ApplyTransferFunction SKIP for Block #{block.Ordinal} - Already impure.");
                return stateBefore;
            }

            if (stateBefore.PathConditions.Length > 0 &&
                ArePathConditionsUnsatisfiable(stateBefore, stateBefore.PathConditions, smtAnalysis))
            {
                LogDebug($"ApplyTransferFunction SKIP for Block #{block.Ordinal} - SMT path conditions are unsatisfiable.");
                return stateBefore;
            }


            var pureAttributeSymbol_block = semanticModel.Compilation.GetTypeByMetadataName("PurelySharp.Attributes.PureAttribute");
            var ruleContext = new Rules.PurityAnalysisContext(
                semanticModel,
                enforcePureAttributeSymbol,
                pureAttributeSymbol_block,
                allowSynchronizationAttributeSymbol,
                visited,
                purityCache,
                containingMethodSymbol,
                _purityRules,
                CancellationToken.None,
                null,
                smtAnalysis);


            var currentStateInBlock = stateBefore;
            PurityAnalysisResult? deferredRecursiveImpurity = null;
            SyntaxNode? deferredRecursiveSyntax = null;
            foreach (var op in block.Operations)
            {
                if (op == null) continue;

                LogDebug($"    [ATF Block {block.Ordinal}] Checking Op Kind: {op.Kind}, Syntax: {op.Syntax.ToString().Replace("\r\n", " ").Replace("\n", " ")}");

                if (op is IFlowCaptureOperation flowCap)
                {
                    var valResult = CheckSingleOperation(flowCap.Value, ruleContext, currentStateInBlock);
                    currentStateInBlock = currentStateInBlock.WithFlowCaptureResult(flowCap.Id, valResult);
                    if (!valResult.IsPure)
                    {
                        LogDebug($"ApplyTransferFunction IMPURE FlowCapture value in Block #{block.Ordinal}");
                        currentStateInBlock = currentStateInBlock.WithImpurity(valResult, flowCap.Syntax);
                        break;
                    }

                    currentStateInBlock = UpdateDelegateMapForOperation(flowCap, ruleContext, currentStateInBlock);
                    continue;
                }

                var opResult = CheckSingleOperation(op, ruleContext, currentStateInBlock);

                if (!opResult.IsPure)
                {
                    LogDebug($"ApplyTransferFunction IMPURE DETECTED in Block #{block.Ordinal} by Op: {op.Kind} ({op.Syntax})");

                    if (IsRecursivePlaceholderImpurity(opResult))
                    {
                        deferredRecursiveImpurity ??= opResult;
                        deferredRecursiveSyntax ??= op.Syntax;
                        continue;
                    }

                    currentStateInBlock = currentStateInBlock.WithImpurity(opResult, op.Syntax);
                    break;
                }


                LogDebug($"  [ApplyTF] Before UpdateDelegateMapForOperation: StateImpure={currentStateInBlock.HasPotentialImpurity}, MapCount={currentStateInBlock.DelegateTargetMap.Count}");
                currentStateInBlock = UpdateDelegateMapForOperation(op, ruleContext, currentStateInBlock);
                LogDebug($"  [ApplyTF] After UpdateDelegateMapForOperation: StateImpure={currentStateInBlock.HasPotentialImpurity}, MapCount={currentStateInBlock.DelegateTargetMap.Count}");

            }

            if (!currentStateInBlock.HasPotentialImpurity && deferredRecursiveImpurity.HasValue)
            {
                var fallbackSyntax = deferredRecursiveSyntax ??
                    block.Operations.FirstOrDefault()?.Syntax ??
                    containingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

                currentStateInBlock = currentStateInBlock.WithImpurity(
                    deferredRecursiveImpurity.Value,
                    fallbackSyntax!);
            }

            if (!currentStateInBlock.HasPotentialImpurity &&
                block.BranchValue != null &&
                ShouldAnalyzeExplicitConditionBranchValue(block.BranchValue.Syntax))
            {
                LogDebug($"    [ATF Block {block.Ordinal}] Checking Branch Value Kind: {block.BranchValue.Kind}, Syntax: {block.BranchValue.Syntax.ToString().Replace("\r\n", " ").Replace("\n", " ")}");

                var branchValueResult = CheckSingleOperation(block.BranchValue, ruleContext, currentStateInBlock);
                if (!branchValueResult.IsPure)
                {
                    LogDebug($"ApplyTransferFunction IMPURE DETECTED in Block #{block.Ordinal} by Branch Value: {block.BranchValue.Kind} ({block.BranchValue.Syntax})");
                    currentStateInBlock = currentStateInBlock.WithImpurity(branchValueResult, block.BranchValue.Syntax);
                }
                else
                {
                    currentStateInBlock = UpdateDelegateMapForOperation(block.BranchValue, ruleContext, currentStateInBlock);
                }
            }

            LogDebug($"ApplyTransferFunction END for Block #{block.Ordinal} - Final State: Impure={currentStateInBlock.HasPotentialImpurity}");
            return currentStateInBlock;
        }

        private static bool IsRecursivePlaceholderImpurity(PurityAnalysisResult result)
        {
            return !result.IsPure &&
                result.Evidence.RuleName == "RecursivePurityAnalysis" &&
                result.Evidence.CatalogSource == "recursive_call";
        }


        private static PurityAnalysisResult AnalyzeOperationSubtreePurity(
            IOperation rootOperation,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            HashSet<IMethodSymbol> visited,
            IMethodSymbol containingMethodSymbol,
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache)
        {
            var pureAttributeSymbol = semanticModel.Compilation.GetTypeByMetadataName("PurelySharp.Attributes.PureAttribute");
            var context = new Rules.PurityAnalysisContext(
                semanticModel,
                enforcePureAttributeSymbol,
                pureAttributeSymbol,
                allowSynchronizationAttributeSymbol,
                visited,
                purityCache,
                containingMethodSymbol,
                _purityRules,
                CancellationToken.None,
                null);

            var currentState = PurityAnalysisState.Pure;
            foreach (var operation in ExecutionVisibility.VisibleDescendants(rootOperation))
            {
                if (operation is IFlowCaptureOperation flowCaptureOperation)
                {
                    var valueResult = CheckSingleOperation(flowCaptureOperation.Value, context, currentState);
                    currentState = currentState.WithFlowCaptureResult(flowCaptureOperation.Id, valueResult);
                    if (!valueResult.IsPure)
                    {
                        return valueResult;
                    }

                    currentState = UpdateDelegateMapForOperation(flowCaptureOperation, context, currentState);
                    continue;
                }

                var operationResult = CheckSingleOperation(operation, context, currentState);
                if (!operationResult.IsPure)
                {
                    return operationResult;
                }

                currentState = UpdateDelegateMapForOperation(operation, context, currentState);
            }

            return currentState.HasPotentialImpurity
                ? ImpureResult(currentState.FirstImpureSyntaxNode, currentState.FirstImpurityEvidence)
                : PurityAnalysisResult.Pure;
        }

        private static SyntaxNode? TryGetDirectThrowOnlySyntax(SyntaxNode? bodySyntaxNode)
        {
            switch (bodySyntaxNode)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax blockSyntax
                    when blockSyntax.Statements.Count == 1:
                    return TryGetDirectThrowOnlySyntax(blockSyntax.Statements[0]);
                case Microsoft.CodeAnalysis.CSharp.Syntax.ThrowStatementSyntax throwStatementSyntax:
                    return throwStatementSyntax;
                case Microsoft.CodeAnalysis.CSharp.Syntax.ArrowExpressionClauseSyntax arrowExpressionClauseSyntax
                    when arrowExpressionClauseSyntax.Expression is Microsoft.CodeAnalysis.CSharp.Syntax.ThrowExpressionSyntax throwExpressionSyntax:
                    return throwExpressionSyntax;
                case Microsoft.CodeAnalysis.CSharp.Syntax.ThrowExpressionSyntax directThrowExpressionSyntax:
                    return directThrowExpressionSyntax;
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.ExpressionBody != null:
                    return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.ExpressionBody);
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.Body != null:
                    return TryGetDirectThrowOnlySyntax(methodDeclarationSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.ExpressionBody != null:
                    return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.ExpressionBody);
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.Body != null:
                    return TryGetDirectThrowOnlySyntax(localFunctionStatementSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax simpleLambdaExpressionSyntax:
                    return TryGetDirectThrowOnlySyntax(simpleLambdaExpressionSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax:
                    return TryGetDirectThrowOnlySyntax(parenthesizedLambdaExpressionSyntax.Body);
                case Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousMethodExpressionSyntax anonymousMethodExpressionSyntax
                    when anonymousMethodExpressionSyntax.Block != null:
                    return TryGetDirectThrowOnlySyntax(anonymousMethodExpressionSyntax.Block);
                default:
                    return null;
            }
        }

        internal static bool TryGetSingleReturnedValueFromNestedCallable(
            IMethodSymbol methodSymbol,
            SemanticModel semanticModel,
            out IOperation returnedOperation,
            out SyntaxNode returnedExpressionSyntax,
            out SemanticModel returnedSemanticModel)
        {
            returnedOperation = null!;
            returnedExpressionSyntax = null!;
            returnedSemanticModel = semanticModel;

            if (methodSymbol == null ||
                !CanExtractSingleReturnedValue(methodSymbol))
            {
                return false;
            }

            var callableSyntax = methodSymbol.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax())
                .FirstOrDefault();
            if (callableSyntax == null ||
                !TryGetSingleReturnedExpressionSyntax(callableSyntax, out returnedExpressionSyntax))
            {
                return false;
            }

            returnedSemanticModel = semanticModel.Compilation.GetSemanticModel(callableSyntax.SyntaxTree);
            returnedOperation = SkipImplicitConversions(returnedSemanticModel.GetOperation(returnedExpressionSyntax));
            return returnedOperation != null;
        }

        private static bool CanExtractSingleReturnedValue(IMethodSymbol methodSymbol)
        {
            return methodSymbol.MethodKind == MethodKind.LocalFunction ||
                methodSymbol.MethodKind == MethodKind.AnonymousFunction ||
                methodSymbol.MethodKind == MethodKind.Ordinary ||
                methodSymbol.MethodKind == MethodKind.StaticConstructor ||
                methodSymbol.MethodKind == MethodKind.Constructor;
        }

        internal static bool TryGetSingleReturnedValueFromInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel,
            out IOperation returnedOperation,
            out SyntaxNode returnedExpressionSyntax,
            out SemanticModel returnedSemanticModel,
            PurityAnalysisState? currentState = null)
        {
            if (TryGetSingleReturnedValueFromNestedCallable(
                    invocationOperation.TargetMethod,
                    semanticModel,
                    out returnedOperation,
                    out returnedExpressionSyntax,
                    out returnedSemanticModel))
            {
                return true;
            }

            if (invocationOperation.TargetMethod.Name == "Invoke" &&
                invocationOperation.TargetMethod.ContainingType?.TypeKind == TypeKind.Delegate &&
                invocationOperation.Instance != null)
            {
                var potentialTargets = ResolvePotentialTargets(
                    invocationOperation.Instance,
                    currentState ?? PurityAnalysisState.Pure,
                    semanticModel);
                if (potentialTargets is { IsUnresolved: false } resolvedTargets &&
                    resolvedTargets.MethodSymbols.Count == 1)
                {
                    return TryGetSingleReturnedValueFromNestedCallable(
                        resolvedTargets.MethodSymbols.Single(),
                        semanticModel,
                        out returnedOperation,
                        out returnedExpressionSyntax,
                        out returnedSemanticModel);
                }
            }

            returnedOperation = null!;
            returnedExpressionSyntax = null!;
            returnedSemanticModel = semanticModel;
            return false;
        }

        private static bool TryGetSingleReturnedExpressionSyntax(
            SyntaxNode callableSyntax,
            out SyntaxNode returnedExpressionSyntax)
        {
            switch (callableSyntax)
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.ExpressionBody?.Expression != null:
                    returnedExpressionSyntax = localFunctionStatementSyntax.ExpressionBody.Expression;
                    return true;
                case Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax localFunctionStatementSyntax
                    when localFunctionStatementSyntax.Body != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(localFunctionStatementSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.ExpressionBody?.Expression != null:
                    returnedExpressionSyntax = methodDeclarationSyntax.ExpressionBody.Expression;
                    return true;
                case Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax methodDeclarationSyntax
                    when methodDeclarationSyntax.Body != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(methodDeclarationSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.SimpleLambdaExpressionSyntax simpleLambdaExpressionSyntax:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(simpleLambdaExpressionSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedLambdaExpressionSyntax parenthesizedLambdaExpressionSyntax:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(parenthesizedLambdaExpressionSyntax.Body, out returnedExpressionSyntax);
                case Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousMethodExpressionSyntax anonymousMethodExpressionSyntax
                    when anonymousMethodExpressionSyntax.Block != null:
                    return TryGetSingleReturnedExpressionSyntaxFromBody(anonymousMethodExpressionSyntax.Block, out returnedExpressionSyntax);
                default:
                    returnedExpressionSyntax = null!;
                    return false;
            }
        }

        private static bool TryGetSingleReturnedExpressionSyntaxFromBody(
            SyntaxNode bodySyntax,
            out SyntaxNode returnedExpressionSyntax)
        {
            if (bodySyntax is Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expressionSyntax)
            {
                returnedExpressionSyntax = expressionSyntax;
                return true;
            }

            if (bodySyntax is not Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax blockSyntax)
            {
                returnedExpressionSyntax = null!;
                return false;
            }

            var directReturns = blockSyntax
                .DescendantNodes(static node =>
                    node is not Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax &&
                    node is not Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousFunctionExpressionSyntax)
                .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.ReturnStatementSyntax>()
                .Where(returnStatement => returnStatement.Expression != null)
                .ToArray();
            if (directReturns.Length != 1)
            {
                returnedExpressionSyntax = null!;
                return false;
            }

            returnedExpressionSyntax = directReturns[0].Expression!;
            return true;
        }

        private static bool ShouldAnalyzeExplicitConditionBranchValue(SyntaxNode branchValueSyntax)
        {
            foreach (var ancestor in branchValueSyntax.AncestorsAndSelf())
            {
                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax ||
                    ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.ConditionalExpressionSyntax ||
                    ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.WhileStatementSyntax ||
                    ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.DoStatementSyntax ||
                    ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.ForStatementSyntax ||
                    ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.WhenClauseSyntax)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetConstantBranchDecision(
            IOperation? branchValue,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis,
            out bool takeConditionalSuccessor)
        {
            takeConditionalSuccessor = false;

            if (branchValue?.ConstantValue.HasValue == true &&
                branchValue.ConstantValue.Value is bool constantBool)
            {
                takeConditionalSuccessor = constantBool;
                return true;
            }

            if (branchValue?.Syntax is ExpressionSyntax expressionSyntax)
            {
                if (ExecutionVisibility.IsConditionAlwaysTrueUsingSmt(expressionSyntax, semanticModel, CancellationToken.None, smtAnalysis))
                {
                    takeConditionalSuccessor = true;
                    return true;
                }

                if (ExecutionVisibility.IsConditionAlwaysFalseUsingSmt(expressionSyntax, semanticModel, CancellationToken.None, smtAnalysis))
                {
                    takeConditionalSuccessor = false;
                    return true;
                }
            }

            return false;
        }

        private static bool BranchTrueUsesConditionalSuccessor(IOperation? branchValue)
        {
            if (branchValue?.Syntax is not ExpressionSyntax expressionSyntax)
            {
                return false;
            }

            return !TryFindContainingCondition(expressionSyntax, out var conditionSyntax) ||
                HasOddLogicalNotAncestor(expressionSyntax, conditionSyntax);
        }

        private static bool TryFindContainingCondition(ExpressionSyntax branchValueSyntax, out ExpressionSyntax conditionSyntax)
        {
            foreach (var ancestor in branchValueSyntax.AncestorsAndSelf())
            {
                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax ifStatement)
                {
                    conditionSyntax = ifStatement.Condition;
                    return true;
                }

                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.ConditionalExpressionSyntax conditionalExpression)
                {
                    conditionSyntax = conditionalExpression.Condition;
                    return true;
                }

                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.WhileStatementSyntax whileStatement)
                {
                    conditionSyntax = whileStatement.Condition;
                    return true;
                }

                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.DoStatementSyntax doStatement)
                {
                    conditionSyntax = doStatement.Condition;
                    return true;
                }

                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.ForStatementSyntax forStatement)
                {
                    if (forStatement.Condition != null)
                    {
                        conditionSyntax = forStatement.Condition;
                        return true;
                    }

                    break;
                }

                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.WhenClauseSyntax whenClause)
                {
                    conditionSyntax = whenClause.Condition;
                    return true;
                }
            }

            conditionSyntax = null!;
            return false;
        }

        private static bool HasOddLogicalNotAncestor(ExpressionSyntax branchValueSyntax, ExpressionSyntax conditionSyntax)
        {
            var logicalNotCount = 0;
            for (SyntaxNode? current = branchValueSyntax; current != null && !ReferenceEquals(current, conditionSyntax); current = current.Parent)
            {
                if (current.Parent is Microsoft.CodeAnalysis.CSharp.Syntax.PrefixUnaryExpressionSyntax prefixUnary &&
                    prefixUnary.IsKind(SyntaxKind.LogicalNotExpression))
                {
                    logicalNotCount++;
                }
            }

            return logicalNotCount % 2 == 1;
        }

        private static bool TryCreateSuccessorState(
            PurityAnalysisState currentState,
            IOperation? branchValue,
            SemanticModel semanticModel,
            bool takeConditionalSuccessor,
            SmtAnalysisService smtAnalysis,
            out PurityAnalysisState successorState)
        {
            successorState = currentState;

            if (branchValue?.Syntax is not ExpressionSyntax expressionSyntax)
            {
                return true;
            }

            var nextPathConditionsBuilder = currentState.PathConditions.ToBuilder();
            var addedBranchAssumptions = CSharpConditionToFormula.TryCollectBranchAssumptions(
                expressionSyntax,
                takeConditionalSuccessor,
                semanticModel,
                CancellationToken.None,
                nextPathConditionsBuilder,
                currentState.GetSmtSymbolVersion);

            SmtFormula branchFormula;
            if (TryTranslateBranchValueToFormula(branchValue, currentState, out var operationFormula) &&
                operationFormula != null)
            {
                branchFormula = operationFormula;
            }
            else if (CSharpConditionToFormula.TryTranslate(expressionSyntax, semanticModel, CancellationToken.None, out var syntaxFormula, currentState.GetSmtSymbolVersion) &&
                     syntaxFormula != null)
            {
                branchFormula = syntaxFormula;
            }
            else
            {
                if (addedBranchAssumptions)
                {
                    var partialPathConditions = nextPathConditionsBuilder.ToImmutable();
                    if (ArePathConditionsUnsatisfiable(currentState, partialPathConditions, smtAnalysis))
                    {
                        return false;
                    }

                    successorState = currentState.WithPathConditions(partialPathConditions);
                }

                return true;
            }

            var edgeFormula = takeConditionalSuccessor
                ? branchFormula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, branchFormula);
            if (!addedBranchAssumptions)
            {
                nextPathConditionsBuilder.Add(edgeFormula);
            }

            var nextPathConditions = nextPathConditionsBuilder.ToImmutable();
            if (ArePathConditionsUnsatisfiable(currentState, nextPathConditions, smtAnalysis))
            {
                return false;
            }

            successorState = currentState.WithPathConditions(nextPathConditions);
            return true;
        }

        private static bool ArePathConditionsUnsatisfiable(
            PurityAnalysisState currentState,
            ImmutableArray<SmtFormula> pathConditions,
            SmtAnalysisService smtAnalysis)
        {
            var proofPathConditions = AppendDefinitelyNullFacts(currentState, pathConditions);
            var query = new PurityProofQuery(
                proofPathConditions,
                new PurityHazard(PurityHazardKind.BranchReachability, new SmtBooleanConstant(true)));

            var proofResult = smtAnalysis.Classify(query);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
        }

        private static bool TryTranslateBranchValueToFormula(
            IOperation? branchValue,
            PurityAnalysisState currentState,
            out SmtFormula? formula)
        {
            formula = null;
            branchValue = SkipImplicitConversions(branchValue);

            if (branchValue is IIsNullOperation isNullOperation &&
                TryCreateReferenceVariableFormula(isNullOperation.Operand, currentState, out var operandFormula))
            {
                formula = new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    operandFormula,
                    new SmtNullConstant());
                return true;
            }

            return false;
        }

        private static bool TryCreateReferenceVariableFormula(
            IOperation? operation,
            PurityAnalysisState currentState,
            out SmtFormula formula)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (TryResolveTrackedSymbol(operation, currentState) is ILocalSymbol localSymbol &&
                localSymbol.Type?.IsReferenceType == true)
            {
                formula = new SmtVariable(GetSmtVariableName(localSymbol, currentState.GetSmtSymbolVersion), SmtValueKind.Reference);
                return true;
            }

            if (TryResolveTrackedSymbol(operation, currentState) is IParameterSymbol parameterSymbol &&
                parameterSymbol.Type?.IsReferenceType == true)
            {
                formula = new SmtVariable(GetSmtVariableName(parameterSymbol, currentState.GetSmtSymbolVersion), SmtValueKind.Reference);
                return true;
            }

            formula = null!;
            return false;
        }

        private static ImmutableArray<SmtFormula> AppendDefinitelyNullFacts(
            PurityAnalysisState currentState,
            ImmutableArray<SmtFormula> pathConditions)
        {
            if (currentState.DefinitelyNullLocalSymbols.Count == 0)
            {
                return pathConditions;
            }

            var builder = ImmutableArray.CreateBuilder<SmtFormula>(pathConditions.Length + currentState.DefinitelyNullLocalSymbols.Count);
            builder.AddRange(pathConditions);

            foreach (var localSymbol in currentState.DefinitelyNullLocalSymbols.OfType<ILocalSymbol>())
            {
                if (localSymbol.Type?.IsReferenceType != true)
                {
                    continue;
                }

                builder.Add(new SmtBinaryFormula(
                    SmtBinaryOperator.Equal,
                    new SmtVariable(GetSmtVariableName(localSymbol, currentState.GetSmtSymbolVersion), SmtValueKind.Reference),
                    new SmtNullConstant()));
            }

            return builder.ToImmutable();
        }

        private static string GetSmtVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion = null)
        {
            var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
            var name = symbol.Name + "#" + start.ToString(CultureInfo.InvariantCulture);
            var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        private static bool IsInStaticallyUnreachableBranch(SyntaxNode syntaxNode, SemanticModel semanticModel, SmtAnalysisService smtAnalysis)
        {
            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(syntaxNode, semanticModel, CancellationToken.None, smtAnalysis))
            {
                return true;
            }

            foreach (var ancestor in syntaxNode.Ancestors())
            {
                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.SwitchExpressionSyntax switchExpressionSyntax &&
                    IsInUnmatchedConstantSwitchExpressionArm(syntaxNode, switchExpressionSyntax, semanticModel))
                {
                    return true;
                }

                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.SwitchStatementSyntax switchStatementSyntax &&
                    IsInUnmatchedConstantSwitchStatementSection(syntaxNode, switchStatementSyntax, semanticModel))
                {
                    return true;
                }
            }

            var pathConditions = SymbolicProgramPointFacts.CollectAncestorReachabilityConditions(syntaxNode, semanticModel, CancellationToken.None);
            if (pathConditions.Length == 0)
            {
                return false;
            }

            var query = new PurityProofQuery(
                pathConditions,
                new PurityHazard(PurityHazardKind.BranchReachability, new SmtBooleanConstant(true)));

            var proofResult = smtAnalysis.Classify(query);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
        }

        private static bool IsInUnmatchedConstantSwitchExpressionArm(
            SyntaxNode syntaxNode,
            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchExpressionSyntax switchExpressionSyntax,
            SemanticModel semanticModel)
        {
            var governingValue = semanticModel.GetConstantValue(switchExpressionSyntax.GoverningExpression);
            if (!governingValue.HasValue)
            {
                return false;
            }

            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchExpressionArmSyntax? matchedArm = null;
            foreach (var arm in switchExpressionSyntax.Arms)
            {
                if (!MatchesConstantSwitchPattern(arm.Pattern, governingValue.Value, semanticModel))
                {
                    continue;
                }

                if (IsUnknownWhenClause(arm.WhenClause, semanticModel))
                {
                    return false;
                }

                if (IsConstantTrueWhenClause(arm.WhenClause, semanticModel))
                {
                    matchedArm = arm;
                    break;
                }
            }

            if (matchedArm == null)
            {
                return false;
            }

            foreach (var arm in switchExpressionSyntax.Arms)
            {
                if (!ReferenceEquals(arm, matchedArm) && arm.Expression.Span.Contains(syntaxNode.Span))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInUnmatchedConstantSwitchStatementSection(
            SyntaxNode syntaxNode,
            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchStatementSyntax switchStatementSyntax,
            SemanticModel semanticModel)
        {
            var governingValue = semanticModel.GetConstantValue(switchStatementSyntax.Expression);
            if (!governingValue.HasValue)
            {
                return false;
            }

            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax? defaultSection = null;
            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax? matchedSection = null;
            foreach (var section in switchStatementSyntax.Sections)
            {
                foreach (var label in section.Labels)
                {
                    if (label is Microsoft.CodeAnalysis.CSharp.Syntax.DefaultSwitchLabelSyntax)
                    {
                        defaultSection ??= section;
                    }
                    else if (label is Microsoft.CodeAnalysis.CSharp.Syntax.CaseSwitchLabelSyntax caseLabel)
                    {
                        var labelValue = semanticModel.GetConstantValue(caseLabel.Value);
                        if (labelValue.HasValue && ConstantValuesEqual(labelValue.Value, governingValue.Value))
                        {
                            matchedSection = section;
                            break;
                        }
                    }
                    else if (label is Microsoft.CodeAnalysis.CSharp.Syntax.CasePatternSwitchLabelSyntax patternLabel &&
                             MatchesConstantSwitchPattern(patternLabel.Pattern, governingValue.Value, semanticModel))
                    {
                        if (IsUnknownWhenClause(patternLabel.WhenClause, semanticModel))
                        {
                            return false;
                        }

                        if (IsConstantTrueWhenClause(patternLabel.WhenClause, semanticModel))
                        {
                            matchedSection = section;
                            break;
                        }
                    }
                }

                if (matchedSection != null)
                {
                    break;
                }
            }

            matchedSection ??= defaultSection;
            if (matchedSection == null)
            {
                return false;
            }

            var reachableSections = GetReachableConstantSwitchStatementSections(
                matchedSection,
                switchStatementSyntax,
                semanticModel);

            foreach (var section in switchStatementSyntax.Sections)
            {
                if (!reachableSections.Any(reachableSection => ReferenceEquals(reachableSection, section)) &&
                    section.Span.Contains(syntaxNode.Span))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax> GetReachableConstantSwitchStatementSections(
            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax matchedSection,
            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchStatementSyntax switchStatementSyntax,
            SemanticModel semanticModel)
        {
            var reachableSections = new List<Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax>
            {
                matchedSection
            };

            for (var index = 0; index < reachableSections.Count; index++)
            {
                var section = reachableSections[index];
                foreach (var gotoStatement in section
                             .DescendantNodes()
                             .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.GotoStatementSyntax>())
                {
                    if (!ReferenceEquals(
                            gotoStatement.Ancestors().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.SwitchStatementSyntax>().FirstOrDefault(),
                            switchStatementSyntax))
                    {
                        continue;
                    }

                    var targetSection = ResolveConstantSwitchGotoTarget(gotoStatement, switchStatementSyntax, semanticModel);
                    if (targetSection == null ||
                        reachableSections.Any(reachableSection => ReferenceEquals(reachableSection, targetSection)))
                    {
                        continue;
                    }

                    reachableSections.Add(targetSection);
                }
            }

            return reachableSections;
        }

        private static Microsoft.CodeAnalysis.CSharp.Syntax.SwitchSectionSyntax? ResolveConstantSwitchGotoTarget(
            Microsoft.CodeAnalysis.CSharp.Syntax.GotoStatementSyntax gotoStatement,
            Microsoft.CodeAnalysis.CSharp.Syntax.SwitchStatementSyntax switchStatementSyntax,
            SemanticModel semanticModel)
        {
            if (gotoStatement.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoDefaultStatement))
            {
                return switchStatementSyntax.Sections.FirstOrDefault(section =>
                    section.Labels.Any(label => label is Microsoft.CodeAnalysis.CSharp.Syntax.DefaultSwitchLabelSyntax));
            }

            if (!gotoStatement.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoCaseStatement) ||
                gotoStatement.Expression == null)
            {
                return null;
            }

            var gotoValue = semanticModel.GetConstantValue(gotoStatement.Expression);
            if (!gotoValue.HasValue)
            {
                return null;
            }

            foreach (var section in switchStatementSyntax.Sections)
            {
                foreach (var label in section.Labels.OfType<Microsoft.CodeAnalysis.CSharp.Syntax.CaseSwitchLabelSyntax>())
                {
                    var labelValue = semanticModel.GetConstantValue(label.Value);
                    if (labelValue.HasValue && ConstantValuesEqual(labelValue.Value, gotoValue.Value))
                    {
                        return section;
                    }
                }
            }

            return null;
        }

        private static bool MatchesConstantSwitchPattern(
            Microsoft.CodeAnalysis.CSharp.Syntax.PatternSyntax pattern,
            object? governingValue,
            SemanticModel semanticModel)
        {
            if (pattern is Microsoft.CodeAnalysis.CSharp.Syntax.DiscardPatternSyntax)
            {
                return true;
            }

            if (pattern is Microsoft.CodeAnalysis.CSharp.Syntax.ConstantPatternSyntax constantPattern)
            {
                var patternValue = semanticModel.GetConstantValue(constantPattern.Expression);
                return patternValue.HasValue && ConstantValuesEqual(patternValue.Value, governingValue);
            }

            return false;
        }

        private static bool IsConstantTrueWhenClause(
            Microsoft.CodeAnalysis.CSharp.Syntax.WhenClauseSyntax? whenClause,
            SemanticModel semanticModel)
        {
            if (whenClause == null)
            {
                return true;
            }

            var whenValue = semanticModel.GetConstantValue(whenClause.Condition);
            return whenValue.HasValue && whenValue.Value is bool boolValue && boolValue;
        }

        private static bool IsUnknownWhenClause(
            Microsoft.CodeAnalysis.CSharp.Syntax.WhenClauseSyntax? whenClause,
            SemanticModel semanticModel)
        {
            if (whenClause == null)
            {
                return false;
            }

            var whenValue = semanticModel.GetConstantValue(whenClause.Condition);
            return !whenValue.HasValue;
        }

        private static bool ConstantValuesEqual(object? left, object? right)
        {
            return Equals(left, right);
        }

        internal static PurityAnalysisResult CheckSingleOperation(IOperation operation, Rules.PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            LogDebug($"    [CSO] Enter CheckSingleOperation for Kind: {operation.Kind}, Syntax: '{operation.Syntax.ToString().Trim()}'");
            LogDebug($"    [CSO] Current DFA State: Impure={currentState.HasPotentialImpurity}, MapCount={currentState.DelegateTargetMap.Count}");

            if (currentState.PathConditions.Length > 0 &&
                ArePathConditionsUnsatisfiable(currentState, currentState.PathConditions, context.SmtAnalysis))
            {
                LogDebug($"    [CSO] Current SMT path conditions are unsatisfiable. Treating as Pure: {operation.Syntax}");
                return PurityAnalysisResult.Pure;
            }

            var canUseSyntaxOnlyReachability = currentState.SmtSymbolVersions.Count == 0;
            if (canUseSyntaxOnlyReachability &&
                IsInStaticallyUnreachableBranch(operation.Syntax, context.SemanticModel, context.SmtAnalysis))
            {
                LogDebug($"    [CSO] Operation is in a statically unreachable branch. Treating as Pure: {operation.Syntax}");
                return PurityAnalysisResult.Pure;
            }

            if (operation is IFlowCaptureReferenceOperation flowRef)
            {
                if (currentState.FlowCaptures.TryGetValue(flowRef.Id, out var capturedPurity))
                {
                    LogDebug($"    [CSO] FlowCaptureReference resolved from CFG state: IsPure={capturedPurity.IsPure}");
                    return capturedPurity;
                }

                LogDebug($"    [CSO] FlowCaptureReference without CFG capture entry. Treating as Pure.");
                return PurityAnalysisResult.Pure;
            }

            if (operation is IFlowCaptureOperation flowCap)
            {
                LogDebug($"    [CSO] FlowCapture: analyzing captured value subtree");
                return CheckSingleOperation(flowCap.Value, context, currentState);
            }


            bool isChecked = false;
            IMethodSymbol? operatorMethod = null;

            if (operation is IBinaryOperation binaryOp && binaryOp.IsChecked)
            {
                LogDebug($"    [CSO] Found Checked Binary Operation: {operation.Syntax}");
                isChecked = true;
                operatorMethod = binaryOp.OperatorMethod;


                var leftResult = CheckSingleOperation(binaryOp.LeftOperand, context, currentState);
                if (!leftResult.IsPure)
                {
                    LogDebug($"    [CSO] Left operand of checked operation is Impure: {binaryOp.LeftOperand.Syntax}");
                    return leftResult;
                }

                var rightResult = CheckSingleOperation(binaryOp.RightOperand, context, currentState);
                if (!rightResult.IsPure)
                {
                    LogDebug($"    [CSO] Right operand of checked operation is Impure: {binaryOp.RightOperand.Syntax}");
                    return rightResult;
                }
            }
            else if (operation is IUnaryOperation unaryOp && unaryOp.IsChecked)
            {
                LogDebug($"    [CSO] Found Checked Unary Operation: {operation.Syntax}");
                isChecked = true;
                operatorMethod = unaryOp.OperatorMethod;


                var operandResult = CheckSingleOperation(unaryOp.Operand, context, currentState);
                if (!operandResult.IsPure)
                {
                    LogDebug($"    [CSO] Operand of checked operation is Impure: {unaryOp.Operand.Syntax}");
                    return operandResult;
                }
            }

            if (isChecked)
            {
                LogDebug($"    [CSO] Processing checked operation: {operation.Syntax}");


                if (operatorMethod != null)
                {

                    if (context.PurityCache.TryGetValue(operatorMethod.OriginalDefinition, out var cachedResult))
                    {
                        if (!cachedResult.IsPure)
                        {
                            LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is IMPURE (cached). Operation is Impure.");
                            return PurityAnalysisResult.Impure(operation.Syntax);
                        }
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is Pure (cached).");
                        return PurityAnalysisResult.Pure;
                    }


                    var hasTrustedGeneratedPurity = TryGetTrustedGeneratedPurityCoverage(
                        operatorMethod,
                        context.SemanticModel.Compilation,
                        out var generatedPurity);

                    if (hasTrustedGeneratedPurity)
				{
					if (generatedPurity.IsPure)
					{
						LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is trusted pure from generated purity summary.");
						return PurityAnalysisResult.Pure;
					}

					if (!generatedPurity.IsPure)
					{
						LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is trusted impure from generated purity summary.");
						return PurityAnalysisResult.Impure(
							operation.Syntax,
							PurityEvidence.Create(
                                    generatedPurity.PrimaryCategory,
                                    syntaxNode: operation.Syntax,
                                    symbol: operatorMethod.OriginalDefinition,
                                    catalogSource: "generated_purity_summary"));
                        }
                    }

                if (!hasTrustedGeneratedPurity && IsKnownPureBCLMember(operatorMethod, context.SemanticModel.Compilation))
                    {
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is known pure BCL member.");
                        return PurityAnalysisResult.Pure;
                    }

                    if (IsKnownImpure(operatorMethod))
                    {
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is known impure. Operation is Impure.");
                        return PurityAnalysisResult.Impure(operation.Syntax);
                    }


                    var operatorPurity = GetCalleePurity(operatorMethod, context);

                    if (!operatorPurity.IsPure)
                    {
                        LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is IMPURE. Operation is Impure.");
                        return PurityAnalysisResult.Impure(operation.Syntax);
                    }

                    LogDebug($"    [CSO] Checked operator method '{operatorMethod.Name}' is Pure.");
                }

                if (context.ContainingMethodSymbol != null &&
                    operatorMethod != null &&
                    IsPureEnforced(
                        context.ContainingMethodSymbol,
                        context.EnforcePureAttributeSymbol,
                        context.PureAttributeSymbol))
                {
                    var checkedOperatorIsPure = operatorMethod != null &&
                        IsPureEnforced(
                            operatorMethod,
                            context.EnforcePureAttributeSymbol,
                            context.PureAttributeSymbol);

                    if (!checkedOperatorIsPure)
                    {
                        LogDebug($"    [CSO] Checked operation is part of a method marked with [EnforcePure] and no [Pure]-enforced checked operator was found. Checking containing method purity.");

                        var containingMethodPurity = GetCalleePurity(context.ContainingMethodSymbol, context);
                        if (!containingMethodPurity.IsPure)
                        {
                            LogDebug($"    [CSO] Containing method is IMPURE. Operation is Impure.");
                            return PurityAnalysisResult.Impure(operation.Syntax);
                        }
                    }
                    else
                    {
                        LogDebug($"    [CSO] Checked operation uses a checked operator explicitly marked [Pure]; skipping containing method purity re-check.");
                    }
                }


                LogDebug($"    [CSO] Checked operation is Pure.");
                return PurityAnalysisResult.Pure;
            }


            if (operation.Kind == OperationKind.InterpolatedStringText ||
                operation.Kind == OperationKind.Interpolation)
            {
                LogDebug($"    [CSO] {operation.Kind} is parent-handled by InterpolatedStringPurityRule.");
                return PurityAnalysisResult.Pure;
            }

            if (operation.Kind == OperationKind.Discard)
            {
                LogDebug("    [CSO] Discard is parent-handled by assignment, deconstruction, or argument analysis.");
                return PurityAnalysisResult.Pure;
            }

            _firstRuleByOperationKind.TryGetValue(operation.Kind, out var applicableRule);

            if (applicableRule != null)
            {

                LogDebug($"    [CSO] Applying Rule '{applicableRule.GetType().Name}' to Kind: {operation.Kind}, Syntax: '{operation.Syntax.ToString().Trim()}'");

                var ruleResult = applicableRule.CheckPurity(operation, context, currentState);

                LogDebug($"    [CSO] Rule '{applicableRule.GetType().Name}' Result: IsPure={ruleResult.IsPure}");
                if (!ruleResult.IsPure)
                {

                    if (ruleResult.ImpureSyntaxNode == null)
                    {
                        LogDebug($"    [CSO] Rule '{applicableRule.GetType().Name}' returned impure result without syntax node. Using current operation syntax: {operation.Syntax}");

                        return operation.Syntax != null
                               ? PurityAnalysisResult.Impure(operation.Syntax)
                               : PurityAnalysisResult.ImpureUnknownLocation;
                    }
                    LogDebug($"    [CSO] Exit CheckSingleOperation (Impure from rule)");
                    return ruleResult;
                }

                LogDebug($"    [CSO] Exit CheckSingleOperation (Pure from rule)");
                return PurityAnalysisResult.Pure;
            }
            else
            {

                LogDebug($"    [CSO] No rule found for operation kind {operation.Kind}. Defaulting to impure. Syntax: '{operation.Syntax.ToString().Trim()}'");
                LogDebug($"    [CSO] Exit CheckSingleOperation (Impure default)");
                return ImpureResult(operation.Syntax, CreateUnsupportedOperationEvidence(operation));
            }
        }






        internal static bool IsKnownPureBCLMember(ISymbol symbol, Compilation? compilation) =>
            IsTriviallyPureObjectConstructor(symbol) ||
            ImpurityCatalog.IsKnownPureBCLMember(symbol, compilation);

        private static bool IsTriviallyPureObjectConstructor(ISymbol symbol)
        {
            return symbol is IMethodSymbol methodSymbol &&
                methodSymbol.MethodKind == MethodKind.Constructor &&
                methodSymbol.Parameters.Length == 0 &&
                methodSymbol.ContainingType?.SpecialType == SpecialType.System_Object;
        }
        internal static bool IsStrictPurityProfile => ImpurityCatalog.IsStrictPurityProfile;

        internal static bool IsTrustedFreshArrayFactoryOperation(
            IOperation? operation,
            Compilation compilation,
            out IMethodSymbol factoryMethod)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            if (unwrappedOperation is IInvocationOperation invocation &&
                invocation.Type is IArrayTypeSymbol &&
                IsTrustedGeneratedFreshOwnedArrayReturningMember(
                    invocation.TargetMethod.OriginalDefinition,
                    compilation))
            {
                factoryMethod = invocation.TargetMethod;
                return true;
            }

            factoryMethod = null!;
            return false;
        }

        internal static bool IsTrustedNonEscapingArrayFactoryOperation(
            IOperation? operation,
            Compilation compilation,
            out IMethodSymbol factoryMethod)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            if (unwrappedOperation is IInvocationOperation invocation &&
                invocation.Type is IArrayTypeSymbol &&
                IsTrustedGeneratedNonEscapingArrayReturningMember(
                    invocation.TargetMethod.OriginalDefinition,
                    compilation))
            {
                factoryMethod = invocation.TargetMethod;
                return true;
            }

            factoryMethod = null!;
            return false;
        }

        internal static bool IsArrayCollectionExpressionOperation(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation is ICollectionExpressionOperation collectionExpression &&
                collectionExpression.Type is IArrayTypeSymbol;
        }

        internal static bool TryResolveKnownConcreteType(
            IOperation? operation,
            PurityAnalysisState currentState,
            Compilation? compilation,
            out INamedTypeSymbol concreteType)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (operation is IConversionOperation conversionOperation)
            {
                return TryResolveKnownConcreteType(conversionOperation.Operand, currentState, compilation, out concreteType);
            }

            if (operation != null &&
                TryResolveKnownSystemTypeRuntimeReceiver(operation, compilation, out concreteType))
            {
                return true;
            }

            if (operation is IObjectCreationOperation objectCreationOperation &&
                objectCreationOperation.Type is INamedTypeSymbol createdType &&
                createdType.TypeKind is TypeKind.Class or TypeKind.Struct)
            {
                concreteType = createdType;
                return true;
            }

            if (operation is ILocalReferenceOperation localReference &&
                currentState.TryGetLocalConcreteType(localReference.Local, out concreteType))
            {
                return true;
            }

            if (operation is IFlowCaptureReferenceOperation flowCaptureReference &&
                currentState.TryGetFlowCaptureConcreteType(flowCaptureReference.Id, out concreteType))
            {
                return true;
            }

            if (TryResolveTrackedSymbol(operation, currentState) is ILocalSymbol capturedLocalSymbol &&
                currentState.TryGetLocalConcreteType(capturedLocalSymbol, out concreteType))
            {
                return true;
            }

            if (operation is IConditionalOperation conditionalOperation &&
                TryResolveKnownConcreteType(conditionalOperation.WhenTrue, currentState, compilation, out var whenTrueType) &&
                TryResolveKnownConcreteType(conditionalOperation.WhenFalse, currentState, compilation, out var whenFalseType) &&
                SymbolEqualityComparer.Default.Equals(whenTrueType, whenFalseType))
            {
                concreteType = whenTrueType;
                return true;
            }

            if (operation is ICoalesceOperation coalesceOperation &&
                TryResolveKnownConcreteType(coalesceOperation.Value, currentState, compilation, out var coalesceValueType) &&
                TryResolveKnownConcreteType(coalesceOperation.WhenNull, currentState, compilation, out var coalesceWhenNullType) &&
                SymbolEqualityComparer.Default.Equals(coalesceValueType, coalesceWhenNullType))
            {
                concreteType = coalesceValueType;
                return true;
            }

            concreteType = null!;
            return false;
        }

        internal static bool TryResolveKnownSystemTypeRuntimeReceiver(
            IOperation operation,
            Compilation? compilation,
            out INamedTypeSymbol concreteType)
        {
            concreteType = null!;

            if (operation is ITypeOfOperation)
            {
                return TryGetRuntimeTypeSymbol(operation.Type, compilation, out concreteType);
            }

            if (operation is not IInvocationOperation invocationOperation ||
                invocationOperation.TargetMethod is not { } targetMethod)
            {
                return false;
            }

            if (IsObjectGetTypeMethod(targetMethod) || IsTypeGetTypeFromHandleMethod(targetMethod))
            {
                return TryGetRuntimeTypeSymbol(invocationOperation.Type, compilation, out concreteType);
            }

            return false;
        }

        internal static bool IsKnownSystemTypeRuntimeReceiver(IOperation? operation)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (operation is IConversionOperation conversionOperation)
            {
                return IsKnownSystemTypeRuntimeReceiver(conversionOperation.Operand);
            }

            if (operation == null)
            {
                return false;
            }

            return operation is ITypeOfOperation ||
                (operation is IInvocationOperation invocationOperation &&
                 invocationOperation.TargetMethod is { } targetMethod &&
                 (IsObjectGetTypeMethod(targetMethod) || IsTypeGetTypeFromHandleMethod(targetMethod)));
        }

        internal static bool TryGetRuntimeTypeSymbol(
            ITypeSymbol? typeSymbol,
            Compilation? compilation,
            out INamedTypeSymbol concreteType)
        {
            concreteType = null!;

            if (!IsSystemTypeSymbol(typeSymbol))
            {
                return false;
            }

            if (compilation?.GetTypeByMetadataName("System.RuntimeType") is INamedTypeSymbol runtimeTypeFromCompilation)
            {
                concreteType = runtimeTypeFromCompilation;
                return true;
            }

            var containingAssembly = typeSymbol.ContainingAssembly;
            if (containingAssembly?.GetTypeByMetadataName("System.RuntimeType") is not INamedTypeSymbol runtimeType)
            {
                return false;
            }

            concreteType = runtimeType;
            return true;
        }

        internal static IMethodSymbol? ResolveMethodTargetForConcreteReceiver(
            IMethodSymbol targetMethod,
            INamedTypeSymbol exactReceiverType)
        {
            var originalTarget = targetMethod.OriginalDefinition;
            if (targetMethod.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var interfaceImplementation = exactReceiverType.FindImplementationForInterfaceMember(targetMethod) as IMethodSymbol
                    ?? exactReceiverType.FindImplementationForInterfaceMember(originalTarget) as IMethodSymbol;
                if (interfaceImplementation != null)
                {
                    return interfaceImplementation;
                }

                return !originalTarget.IsAbstract || HasMethodBody(originalTarget)
                    ? originalTarget
                    : null;
            }

            if (!(originalTarget.IsVirtual || originalTarget.IsAbstract || originalTarget.IsOverride))
            {
                return originalTarget;
            }

            for (var type = exactReceiverType; type != null; type = type.BaseType)
            {
                foreach (var member in type.GetMembers())
                {
                    if (member is IMethodSymbol method &&
                        (SymbolEqualityComparer.Default.Equals(method.OriginalDefinition, originalTarget) ||
                         OverridesTargetMethod(method, originalTarget) ||
                         ExplicitlyImplements(method, originalTarget)))
                    {
                        return method;
                    }
                }
            }

            return !originalTarget.IsAbstract
                ? originalTarget
                : null;
        }

        internal static IMethodSymbol? ResolvePropertyAccessorTargetForConcreteReceiver(
            IPropertySymbol propertySymbol,
            INamedTypeSymbol exactReceiverType,
            bool preferSetter)
        {
            if (propertySymbol.ContainingType?.TypeKind == TypeKind.Interface)
            {
                var implementation = exactReceiverType.FindImplementationForInterfaceMember(propertySymbol) ??
                    (preferSetter
                        ? propertySymbol.SetMethod == null
                            ? null
                            : exactReceiverType.FindImplementationForInterfaceMember(propertySymbol.SetMethod)
                        : propertySymbol.GetMethod == null
                            ? null
                            : exactReceiverType.FindImplementationForInterfaceMember(propertySymbol.GetMethod));
                return GetAccessorFromImplementation(implementation, preferSetter);
            }

            for (var current = exactReceiverType; current != null; current = current.BaseType)
            {
                var implementation = current
                    .GetMembers(propertySymbol.Name)
                    .OfType<IPropertySymbol>()
                    .FirstOrDefault(property =>
                        SymbolEqualityComparer.Default.Equals(property.OriginalDefinition, propertySymbol.OriginalDefinition) ||
                        OverridesProperty(property, propertySymbol));
                if (implementation == null)
                {
                    continue;
                }

                return preferSetter ? implementation.SetMethod : implementation.GetMethod;
            }

            return preferSetter ? propertySymbol.SetMethod : propertySymbol.GetMethod;
        }

        private static bool IsObjectGetTypeMethod(IMethodSymbol methodSymbol)
        {
            return !methodSymbol.IsStatic &&
                methodSymbol.Parameters.Length == 0 &&
                methodSymbol.Name == nameof(object.GetType) &&
                methodSymbol.ContainingType?.SpecialType == SpecialType.System_Object;
        }

        private static bool IsTypeGetTypeFromHandleMethod(IMethodSymbol methodSymbol)
        {
            return methodSymbol.IsStatic &&
                methodSymbol.Parameters.Length == 1 &&
                methodSymbol.Name == nameof(Type.GetTypeFromHandle) &&
                IsSystemTypeSymbol(methodSymbol.ContainingType) &&
                methodSymbol.Parameters[0].Type.SpecialType == SpecialType.System_RuntimeTypeHandle;
        }

        private static bool IsSystemTypeSymbol(ITypeSymbol? typeSymbol)
        {
            return typeSymbol != null &&
                string.Equals(typeSymbol.ToDisplayString(), "System.Type", StringComparison.Ordinal);
        }

        private static IMethodSymbol? GetAccessorFromImplementation(ISymbol? implementation, bool preferSetter)
        {
            if (implementation is IPropertySymbol propertyImplementation)
            {
                return preferSetter ? propertyImplementation.SetMethod : propertyImplementation.GetMethod;
            }

            return implementation as IMethodSymbol;
        }

        private static bool OverridesProperty(IPropertySymbol property, IPropertySymbol target)
        {
            for (var current = property; current != null; current = current.OverriddenProperty)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool OverridesTargetMethod(IMethodSymbol method, IMethodSymbol target)
        {
            for (var current = method; current != null; current = current.OverriddenMethod)
            {
                if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, target.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ExplicitlyImplements(IMethodSymbol methodSymbol, IMethodSymbol interfaceMethod)
        {
            foreach (var implemented in methodSymbol.ExplicitInterfaceImplementations)
            {
                if (SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, interfaceMethod.OriginalDefinition))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasMethodBody(IMethodSymbol methodSymbol)
        {
            return methodSymbol.DeclaringSyntaxReferences.Length > 0;
        }

        private static bool IsDefinitelyNullValue(
            IOperation? valueOperation,
            PurityAnalysisState currentState)
        {
            valueOperation = SkipImplicitConversions(valueOperation);

            while (valueOperation is IParenthesizedOperation parenthesizedOperation)
            {
                valueOperation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (valueOperation is IConversionOperation conversionOperation)
            {
                return IsDefinitelyNullValue(conversionOperation.Operand, currentState);
            }

            if (valueOperation is ILiteralOperation literalOperation &&
                literalOperation.ConstantValue.HasValue &&
                literalOperation.ConstantValue.Value == null)
            {
                return true;
            }

            if (valueOperation is IDefaultValueOperation defaultValueOperation &&
                defaultValueOperation.Type?.IsReferenceType == true)
            {
                return true;
            }

            if (valueOperation is ILocalReferenceOperation localReference)
            {
                return currentState.IsDefinitelyNullLocalSymbol(localReference.Local);
            }

            if (TryResolveTrackedSymbol(valueOperation, currentState) is ILocalSymbol capturedLocal)
            {
                return currentState.IsDefinitelyNullLocalSymbol(capturedLocal);
            }

            return false;
        }

        private static bool IsArrayEmptyFactory(IMethodSymbol methodSymbol)
        {
            return methodSymbol.Name == "Empty" &&
                methodSymbol.Parameters.Length == 0 &&
                methodSymbol.ContainingType?.SpecialType == SpecialType.System_Array;
        }


        internal static bool IsKnownImpure(ISymbol symbol) => ImpurityCatalog.IsKnownImpure(symbol);
        internal static string? GetKnownImpureMemberSource(ISymbol symbol) => ImpurityCatalog.GetKnownImpureMemberSource(symbol);


        internal static bool HasPureExternalAttribute(ISymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            if (HasDirectAttributeNamed(symbol, "PureExternalAttribute", "PurelySharp.Attributes.PureExternalAttribute"))
            {
                return true;
            }

            if (HasRecognizedExternalPureAttribute(symbol))
            {
                return true;
            }

            if (HasDirectAttributeNamed(symbol, "ImpureAttribute", "PurelySharp.Attributes.ImpureAttribute") ||
                HasAssemblyAttributeNamed(symbol, "ImpureAttribute", "PurelySharp.Attributes.ImpureAttribute"))
            {
                return false;
            }

            return HasAssemblyAttributeNamed(symbol, "PureExternalAttribute", "PurelySharp.Attributes.PureExternalAttribute");
        }

        internal static bool IsKnownMutableCollectionBoundaryType(ITypeSymbol? typeSymbol)
        {
            if (typeSymbol is not INamedTypeSymbol namedType ||
                namedType.IsValueType ||
                namedType.TypeKind == TypeKind.Delegate ||
                namedType.SpecialType == SpecialType.System_String)
            {
                return false;
            }

            return namedType.OriginalDefinition.ToDisplayString() is
                "System.Collections.Generic.List<T>" or
                "System.Collections.Generic.HashSet<T>" or
                "System.Collections.Generic.Dictionary<TKey, TValue>";
        }


        internal static bool HasImpureAttribute(ISymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            if (HasDirectAttributeNamed(symbol, "ImpureAttribute", "PurelySharp.Attributes.ImpureAttribute"))
            {
                return true;
            }

            if (HasDirectAttributeNamed(symbol, "PureExternalAttribute", "PurelySharp.Attributes.PureExternalAttribute"))
            {
                return false;
            }

            return HasAssemblyAttributeNamed(symbol, "ImpureAttribute", "PurelySharp.Attributes.ImpureAttribute");
        }


        internal static PurityAnalysisResult GetCalleePurity(
            IMethodSymbol methodSymbol,
            Rules.PurityAnalysisContext context)
        {
            if (context.PurityService != null)
            {
                return context.PurityService.GetPurity(
                    methodSymbol.OriginalDefinition,
                    context.SemanticModel,
                    context.EnforcePureAttributeSymbol,
                    context.AllowSynchronizationAttributeSymbol);
            }

            return DeterminePurityRecursiveInternal(
                methodSymbol.OriginalDefinition,
                context.SemanticModel,
                context.EnforcePureAttributeSymbol,
                context.AllowSynchronizationAttributeSymbol,
                context.VisitedMethods,
                context.PurityCache);
        }



        internal static bool IsInImpureNamespaceOrType(ISymbol symbol) => ImpurityCatalog.IsInImpureNamespaceOrType(symbol);
        internal static bool IsInConfiguredImpureNamespaceOrType(ISymbol symbol) => ImpurityCatalog.IsInConfiguredImpureNamespaceOrType(symbol);
        internal static bool IsConfiguredKnownPureMember(ISymbol symbol) => ImpurityCatalog.IsConfiguredKnownPureMember(symbol);
        internal static string GetKnownImpureCatalogHitCategory(ISymbol symbol, bool includeSynchronizationCategory = false)
        {
            var containingType = symbol.ContainingType?.ToDisplayString() ?? string.Empty;
            var containingNamespace = symbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            if (includeSynchronizationCategory &&
                (containingType == "System.Threading.Interlocked" ||
                 containingType == "System.Threading.Monitor" ||
                 containingType == "System.Threading.Mutex" ||
                 containingType == "System.Threading.Semaphore" ||
                 containingType == "System.Threading.SemaphoreSlim" ||
                 containingType == "System.Collections.Immutable.ImmutableInterlocked"))
            {
                return "synchronization";
            }

            if (containingNamespace.StartsWith("System.Reflection", StringComparison.Ordinal) ||
                containingType.StartsWith("System.Reflection.", StringComparison.Ordinal) ||
                containingType == "System.Type" ||
                containingType == "System.Runtime.Loader.AssemblyLoadContext" ||
                containingType == "System.Environment" ||
                containingType == "System.DateTime" ||
                containingType == "System.DateTimeOffset" ||
                containingType == "System.TimeProvider" ||
                containingType == "System.TimeZoneInfo" ||
                containingType == "System.Diagnostics.Stopwatch")
            {
                return "reflection_environment_source";
            }

            return "catalog_hit";
        }



        internal static bool IsPureEnforced(
            ISymbol symbol,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? pureAttributeSymbol)
        {
            if (symbol == null || enforcePureAttributeSymbol == null)
            {
                return false;
            }

            if (HasPureExternalAttribute(symbol) || HasRecognizedExternalPureAttribute(symbol))
            {
                return true;
            }

            var pureAttributeFullyQualifiedName = "global::PurelySharp.Attributes.PureAttribute";
            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad =>
                SymbolEqualityComparer.Default.Equals(ad.AttributeClass?.OriginalDefinition, enforcePureAttributeSymbol) ||
                (pureAttributeSymbol != null &&
                    SymbolEqualityComparer.Default.Equals(ad.AttributeClass?.OriginalDefinition, pureAttributeSymbol)) ||
                string.Equals(
                    ad.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    pureAttributeFullyQualifiedName,
                    StringComparison.Ordinal)
            );
        }
        private static bool HasDirectAttributeNamed(ISymbol symbol, string attributeName, string fullyQualifiedMetadataName)
        {
            if (symbol == null)
            {
                return false;
            }

            var fullyQualifiedName = "global::" + fullyQualifiedMetadataName;
            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad =>
                    IsAttributeNamed(ad, attributeName, fullyQualifiedMetadataName, fullyQualifiedName));
        }

        private static bool HasAssemblyAttributeNamed(ISymbol symbol, string attributeName, string fullyQualifiedMetadataName)
        {
            if (symbol == null)
            {
                return false;
            }

            var fullyQualifiedName = "global::" + fullyQualifiedMetadataName;
            return symbol.ContainingAssembly?.GetAttributes().Any(ad =>
                IsAttributeNamed(ad, attributeName, fullyQualifiedMetadataName, fullyQualifiedName)) == true;
        }

        private static bool HasRecognizedExternalPureAttribute(ISymbol symbol)
        {
            if (symbol == null)
            {
                return false;
            }

            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad =>
                IsAttributeMetadataName(ad, "JetBrains.Annotations.PureAttribute") ||
                IsAttributeMetadataName(ad, "System.Diagnostics.Contracts.PureAttribute"));
        }

        private static bool IsAttributeNamed(
            AttributeData attributeData,
            string attributeName,
            string fullyQualifiedMetadataName,
            string fullyQualifiedName)
        {
            return
                string.Equals(attributeData.AttributeClass?.Name, attributeName, StringComparison.Ordinal) ||
                string.Equals(attributeData.AttributeClass?.ToDisplayString(), fullyQualifiedMetadataName, StringComparison.Ordinal) ||
                string.Equals(attributeData.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), fullyQualifiedName, StringComparison.Ordinal);
        }

        private static bool IsAttributeMetadataName(AttributeData attributeData, string fullyQualifiedMetadataName)
        {
            return
                string.Equals(attributeData.AttributeClass?.ToDisplayString(), fullyQualifiedMetadataName, StringComparison.Ordinal) ||
                string.Equals(
                    attributeData.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    "global::" + fullyQualifiedMetadataName,
                    StringComparison.Ordinal);
        }

        private static IEnumerable<AttributeData> GetAttributesIncludingAssociatedSymbol(ISymbol symbol)
        {
            foreach (var attribute in symbol.GetAttributes())
            {
                yield return attribute;
            }

            if (symbol is IMethodSymbol { AssociatedSymbol: { } associatedSymbol })
            {
                foreach (var attribute in associatedSymbol.GetAttributes())
                {
                    yield return attribute;
                }
            }

            if (symbol is IPropertySymbol { GetMethod: { } getMethod } &&
                getMethod.DeclaringSyntaxReferences.Length == 0)
            {
                foreach (var attribute in getMethod.GetAttributes())
                {
                    yield return attribute;
                }
            }
        }


        private static PurityEvidence CreateUnsupportedOperationEvidence(IOperation operation)
        {
            return IsUnsafePointerOperation(operation)
                ? PurityEvidence.Create("unsafe_pointer", ruleName: "UnsupportedOperation", operation: operation)
                : PurityEvidence.Create("unsupported_operation", ruleName: "UnsupportedOperation", operation: operation);
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
            {
                return evidence.IsEmpty
                    ? PurityAnalysisResult.Impure(syntaxNode)
                    : PurityAnalysisResult.Impure(syntaxNode, evidence);
            }

            return evidence.IsEmpty
                ? PurityAnalysisResult.ImpureUnknownLocation
                : PurityAnalysisResult.ImpureUnknownLocation.WithEvidence(evidence);
        }


        internal static void LogDebug(string message)
        {
#if DEBUG
            // Intentionally no-op in Release builds; keep minimal in Debug.
#endif
        }


        private static SyntaxNode? GetBodySyntaxNode(IMethodSymbol methodSymbol, CancellationToken cancellationToken)
        {

            var declaringSyntaxes = methodSymbol.DeclaringSyntaxReferences;
            LogDebug($"  [GetBody] Checking {declaringSyntaxes.Length} declaring syntax refs for {methodSymbol.Name}");
            foreach (var syntaxRef in declaringSyntaxes)
            {
                var syntaxNode = syntaxRef.GetSyntax(cancellationToken);
                LogDebug($"  [GetBody]   SyntaxRef {syntaxRef.Span} yielded SyntaxNode of Kind: {syntaxNode?.Kind()}");


                if (syntaxNode is MethodDeclarationSyntax ||
                    syntaxNode is LocalFunctionStatementSyntax ||
                    syntaxNode is AnonymousFunctionExpressionSyntax ||
                    syntaxNode is AccessorDeclarationSyntax ||
                    syntaxNode is ConstructorDeclarationSyntax ||
                    syntaxNode is OperatorDeclarationSyntax ||
                    syntaxNode is ConversionOperatorDeclarationSyntax)
                {
                    LogDebug($"  [GetBody]   Found usable body node of Kind: {syntaxNode.Kind()}");
                    return syntaxNode;
                }
            }
            LogDebug($"  [GetBody] No usable body node found for {methodSymbol.Name}.");
            return null;
        }


        private static void PropagateToSuccessor(
            BasicBlock? successor,
            PurityAnalysisState newState,
            Dictionary<BasicBlock, PurityAnalysisState> blockStates,
            Queue<BasicBlock> worklist,
            HashSet<BasicBlock> inQueue)
        {
            if (successor == null) return;


            bool previouslyVisited = blockStates.TryGetValue(successor, out var existingState);
            if (!previouslyVisited)
            {
                existingState = PurityAnalysisState.Pure;
            }


            var mergedState = previouslyVisited ? MergeStates(existingState, newState) : newState;


            bool stateChanged = !previouslyVisited || !mergedState.Equals(existingState);











            if (stateChanged)
            {
                LogDebug($"PropagateToSuccessor: State changed for Block #{successor.Ordinal} from Impure={existingState.HasPotentialImpurity} to Impure={mergedState.HasPotentialImpurity}. Updating state.");
                blockStates[successor] = mergedState;
            }
            else
            {

                if (!previouslyVisited)
                {
                    blockStates[successor] = mergedState;
                }

                LogDebug($"PropagateToSuccessor: State unchanged for Block #{successor.Ordinal} (Impure={existingState.HasPotentialImpurity}).");
            }



            if (stateChanged || !inQueue.Contains(successor))
            {
                if (!inQueue.Contains(successor))
                {
                    LogDebug($"PropagateToSuccessor: Enqueuing Block #{successor.Ordinal} (State Changed: {stateChanged}).");
                    worklist.Enqueue(successor);
                    inQueue.Add(successor);
                }
                else
                {


                    if (stateChanged)
                    {
                        LogDebug($"PropagateToSuccessor: Block #{successor.Ordinal} already in queue, state changed. Will reprocess.");
                    }
                    else
                    {
                        LogDebug($"PropagateToSuccessor: Block #{successor.Ordinal} already in queue, state unchanged.");
                    }
                }
            }
            else
            {
                LogDebug($"PropagateToSuccessor: Block #{successor.Ordinal} already in queue and state unchanged. No enqueue needed.");
            }
        }


        internal static bool HasAttribute(ISymbol symbol, INamedTypeSymbol attributeSymbol)
        {
            if (attributeSymbol == null) return false;
            return GetAttributesIncludingAssociatedSymbol(symbol).Any(ad => SymbolEqualityComparer.Default.Equals(ad.AttributeClass?.OriginalDefinition, attributeSymbol.OriginalDefinition));
        }



        internal static PurityAnalysisResult CheckStaticConstructorPurity(ITypeSymbol? typeSymbol, Rules.PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            if (typeSymbol == null)
            {
                return PurityAnalysisResult.Pure;
            }


            IMethodSymbol? staticConstructor = typeSymbol.GetMembers(".cctor").OfType<IMethodSymbol>().FirstOrDefault();

            if (staticConstructor == null)
            {
                LogDebug($"    [CctorCheck] Type {typeSymbol.Name} has no static constructor. Pure.");
                return PurityAnalysisResult.Pure;
            }

            LogDebug($"    [CctorCheck] Found static constructor for {typeSymbol.Name}. Checking purity recursively...");




            var cctorResult = GetCalleePurity(staticConstructor, context);

            LogDebug($"    [CctorCheck] Static constructor purity result for {typeSymbol.Name}: IsPure={cctorResult.IsPure}");




            return cctorResult.IsPure
                ? PurityAnalysisResult.Pure
                : PurityAnalysisResult.Impure(
                    cctorResult.ImpureSyntaxNode ?? typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? context.ContainingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? throw new InvalidOperationException("Cannot find syntax node for static constructor impurity"),
                    cctorResult.Evidence);
        }


        private static PurityAnalysisState UpdateDelegateMapForOperation(IOperation op, Rules.PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            LogDebug($"  [UpdMap] Trying Update: OpKind={op.Kind}, CurrentImpure={currentState.HasPotentialImpurity}");

              PurityAnalysisState nextState = currentState;
              var operationToTrack = op is IExpressionStatementOperation expressionStatementOperation
                  ? expressionStatementOperation.Operation
                  : op;
  
  
                  if (operationToTrack is ICompoundAssignmentOperation compoundAssignmentOperation)
                  {
                    var targetOperation = compoundAssignmentOperation.Target;
                    var valueOperation = compoundAssignmentOperation.Value;
                    var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);

                    if (targetSymbol is ILocalSymbol compoundLocalSymbol)
                    {
                        foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(compoundLocalSymbol, context))
                        {
                            nextState = nextState.WithIncrementedSmtSymbolVersion(writtenLocalSymbol);
                        }
                    }
                    else if (targetSymbol is IParameterSymbol compoundParameterSymbol)
                    {
                        nextState = nextState.WithIncrementedSmtSymbolVersion(compoundParameterSymbol);
                    }

                    if (targetSymbol != null && targetOperation.Type?.TypeKind == TypeKind.Delegate)
                    {
                        if (compoundAssignmentOperation.OperatorKind == BinaryOperatorKind.Add)
                        {
                            PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(valueOperation, currentState);
                            if (valueTargets != null &&
                                currentState.DelegateTargetMap.TryGetValue(targetSymbol, out var currentTargets))
                            {
                                var mergedTargets = PotentialTargets.Merge(currentTargets, valueTargets.Value);
                                nextState = nextState.WithDelegateTarget(targetSymbol, mergedTargets);
                                LogDebug($"    [ATF-DEL-COMPOUND] Merged delegate targets for {targetSymbol.Name}. New Map Count: {nextState.DelegateTargetMap.Count}");
                            }
                            else
                            {
                                nextState = nextState.WithDelegateTarget(targetSymbol, PotentialTargets.Unresolved);
                                LogDebug($"    [ATF-DEL-COMPOUND] Marked map for {targetSymbol.Name} unresolved because compound add target state is incomplete.");
                            }
                        }
                        else
                        {
                            nextState = nextState.WithDelegateTarget(targetSymbol, PotentialTargets.Unresolved);
                            LogDebug($"    [ATF-DEL-COMPOUND] Marked map for {targetSymbol.Name} unresolved after delegate compound assignment.");
                        }
                    }
                }

                  else if (operationToTrack is ICoalesceAssignmentOperation coalesceAssignmentOperation)
                {
                    var targetOperation = coalesceAssignmentOperation.Target;
                    var valueOperation = coalesceAssignmentOperation.Value;
                    var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);
                    var writtenLocalSymbols = targetSymbol is ILocalSymbol targetLocalSymbol
                        ? EnumerateWrittenLocalSymbols(targetLocalSymbol, context).ToArray()
                        : Array.Empty<ILocalSymbol>();
                    if (targetSymbol is IParameterSymbol coalesceParameterSymbol)
                    {
                        nextState = nextState.WithIncrementedSmtSymbolVersion(coalesceParameterSymbol);
                    }

                    if (targetSymbol is ILocalSymbol coalesceLocalSymbol &&
                        currentState.IsDefinitelyNullLocalSymbol(coalesceLocalSymbol))
                    {
                        nextState = ApplyWrittenLocalStateUpdates(
                            nextState,
                            writtenLocalSymbols,
                            valueOperation,
                            currentState,
                            context.SemanticModel,
                            context.SemanticModel.Compilation);
                        nextState = ApplyAssignedDelegateTargets(
                            nextState,
                            targetSymbol,
                            targetOperation.Type,
                            valueOperation,
                            writtenLocalSymbols,
                            currentState,
                            "[ATF-DEL-COALESCE]",
                            "coalesce-assigned value targets are unresolved");
                    }
                }

                  else if (operationToTrack is IAssignmentOperation assignmentOperation)
                  {
                    var targetOperation = assignmentOperation.Target;
                    var valueOperation = assignmentOperation.Value;
                    var targetSymbol = TryResolveTrackedSymbol(targetOperation, currentState);
                    var writtenLocalSymbols = targetSymbol is ILocalSymbol targetLocalSymbol
                        ? EnumerateWrittenLocalSymbols(targetLocalSymbol, context).ToArray()
                        : Array.Empty<ILocalSymbol>();
                    if (targetSymbol is IParameterSymbol assignmentParameterSymbol)
                    {
                        nextState = nextState.WithIncrementedSmtSymbolVersion(assignmentParameterSymbol);
                        nextState = AddAssignedValueFact(
                            nextState,
                            assignmentParameterSymbol,
                            valueOperation,
                            currentState,
                            context.SemanticModel);
                    }

                    nextState = ApplyWrittenLocalStateUpdates(
                        nextState,
                        writtenLocalSymbols,
                        valueOperation,
                        currentState,
                        context.SemanticModel,
                        context.SemanticModel.Compilation);
                    nextState = ApplyAssignedDelegateTargets(
                        nextState,
                        targetSymbol,
                        targetOperation.Type,
                        valueOperation,
                        writtenLocalSymbols,
                        currentState,
                        "[ATF-DEL-ASSIGN]",
                        "assigned value targets are unresolved");
                }

                  else if (operationToTrack is IInvocationOperation invocationOperation)
                {
                    foreach (var argument in invocationOperation.Arguments)
                    {
                        if (argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out))
                        {
                            continue;
                        }

                        var writtenSymbol = TryResolveTrackedSymbol(SkipImplicitConversions(argument.Value), currentState);
                        if (writtenSymbol is ILocalSymbol localSymbol)
                        {
                            foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context))
                            {
                                nextState = nextState
                                    .WithoutLocalConcreteType(writtenLocalSymbol)
                                    .WithoutOwnedLocalArray(writtenLocalSymbol)
                                    .WithoutDefinitelyNullLocal(writtenLocalSymbol)
                                    .WithIncrementedSmtSymbolVersion(writtenLocalSymbol);

                                if (writtenLocalSymbol.Type?.TypeKind == TypeKind.Delegate)
                                {
                                    nextState = nextState.WithDelegateTarget(writtenLocalSymbol, PotentialTargets.Unresolved);
                                }
                            }
                        }
                        else if (writtenSymbol is IParameterSymbol parameterSymbol)
                        {
                            nextState = nextState.WithIncrementedSmtSymbolVersion(parameterSymbol);
                        }
                    }
                }

                  else if (operationToTrack is IFlowCaptureOperation flowCaptureOperation)
                {
                    if (TryResolveTrackedSymbol(flowCaptureOperation.Value, currentState) is ISymbol capturedSymbol)
                    {
                        nextState = nextState.WithFlowCaptureSymbol(flowCaptureOperation.Id, capturedSymbol);
                    }

                    PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(flowCaptureOperation.Value, currentState);
                    if (valueTargets != null)
                    {
                        nextState = nextState.WithFlowCaptureTarget(flowCaptureOperation.Id, valueTargets.Value);
                    }

                    if (TryResolveKnownConcreteType(flowCaptureOperation.Value, currentState, context.SemanticModel.Compilation, out var concreteType))
                    {
                        nextState = nextState.WithFlowCaptureConcreteType(flowCaptureOperation.Id, concreteType);
                    }

                    if (IsOwnedLocalArrayValue(flowCaptureOperation.Value, currentState, context.SemanticModel.Compilation))
                    {
                        nextState = nextState.WithOwnedArrayFlowCapture(flowCaptureOperation.Id);
                    }
                    else
                    {
                        nextState = nextState.WithoutOwnedArrayFlowCapture(flowCaptureOperation.Id);
                    }
                }

                  else if (operationToTrack is IVariableDeclarationGroupOperation groupOperation)
                {
                    foreach (var declaration in groupOperation.Declarations)
                    {
                        foreach (var declarator in declaration.Declarators)
                        {
                            if (declarator.Initializer != null)
                            {
                                var initializerValue = declarator.Initializer.Value;
                                ILocalSymbol declaredSymbol = declarator.Symbol;

                                if (TryResolveKnownConcreteType(initializerValue, nextState, context.SemanticModel.Compilation, out var concreteType))
                                {
                                    nextState = nextState.WithLocalConcreteType(declaredSymbol, concreteType);
                                }
                                else
                                {
                                    nextState = nextState.WithoutLocalConcreteType(declaredSymbol);
                                }

                                if (IsOwnedLocalArrayValue(initializerValue, nextState, context.SemanticModel.Compilation))
                                {
                                    nextState = nextState.WithOwnedLocalArray(declaredSymbol);
                                }
                                else
                                {
                                    nextState = nextState.WithoutOwnedLocalArray(declaredSymbol);
                                }

                                if (IsDefinitelyNullValue(initializerValue, nextState))
                                {
                                    nextState = nextState.WithDefinitelyNullLocal(declaredSymbol);
                                }
                                else
                                {
                                    nextState = nextState.WithoutDefinitelyNullLocal(declaredSymbol);
                                }

                                if (declaredSymbol.Type?.TypeKind == TypeKind.Delegate)
                                {
                                    PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(initializerValue, nextState);
                                    if (valueTargets != null)
                                    {
                                        nextState = nextState.WithDelegateTarget(declaredSymbol, valueTargets.Value);
                                        LogDebug($"    [ATF-DEL-VAR] Updated map for {declaredSymbol.Name} with {valueTargets.Value.MethodSymbols.Count} targets. New Map Count: {nextState.DelegateTargetMap.Count}");
                                    }
                                }

                                nextState = AddAssignedValueFact(
                                    nextState,
                                    declaredSymbol,
                                    initializerValue,
                                    nextState,
                                    context.SemanticModel);
                            }
                        }
                    }
                }


            return nextState;
        }

        private static PurityAnalysisState AddAssignedValueFact(
            PurityAnalysisState currentState,
            ISymbol targetSymbol,
            IOperation? valueOperation,
            PurityAnalysisState valueState,
            SemanticModel semanticModel)
        {
            if (valueOperation?.Syntax is not ExpressionSyntax valueExpression)
            {
                return currentState;
            }

            var nextState = currentState;
            if (TryCreateSymbolSmtValue(targetSymbol, currentState, out var targetFormula) &&
                CSharpConditionToFormula.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    CancellationToken.None,
                    out var valueFormula,
                    valueState.GetSmtSymbolVersion) &&
                valueFormula != null &&
                CanCompareSmtValues(targetFormula, valueFormula))
            {
                var assignedFact = CreateAssignedValueFact(targetFormula, valueFormula);
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(assignedFact));
            }

            if (TryCreateBuiltInLengthFormula(targetSymbol, currentState, out var targetLengthFormula) &&
                TryCreateBuiltInLengthValueFormula(
                    valueExpression,
                    semanticModel,
                    CancellationToken.None,
                    valueState.GetSmtSymbolVersion,
                    out var valueLengthFormula))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(
                    new SmtBinaryFormula(SmtBinaryOperator.Equal, targetLengthFormula, valueLengthFormula)));
            }

            return nextState;
        }

        private static SmtFormula CreateAssignedValueFact(SmtFormula targetFormula, SmtFormula valueFormula)
        {
            if (targetFormula.Kind == SmtValueKind.Bool &&
                valueFormula is SmtBooleanConstant booleanConstant)
            {
                return booleanConstant.Value
                    ? targetFormula
                    : new SmtUnaryFormula(SmtUnaryOperator.Not, targetFormula);
            }

            return new SmtBinaryFormula(SmtBinaryOperator.Equal, targetFormula, valueFormula);
        }

        private static bool TryCreateSymbolSmtValue(
            ISymbol symbol,
            PurityAnalysisState currentState,
            out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type == null)
            {
                formula = null!;
                return false;
            }

            var variableName = GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion);
            if (type.SpecialType == SpecialType.System_Boolean)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Bool);
                return true;
            }

            if (IsSmtIntegralType(type))
            {
                formula = new SmtVariable(variableName, SmtValueKind.Int);
                return true;
            }

            if (type.IsReferenceType)
            {
                formula = new SmtVariable(variableName, SmtValueKind.Reference);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateBuiltInLengthFormula(
            ISymbol symbol,
            PurityAnalysisState currentState,
            out SmtFormula formula)
        {
            var type = symbol switch
            {
                ILocalSymbol localSymbol => localSymbol.Type,
                IParameterSymbol parameterSymbol => parameterSymbol.Type,
                _ => null
            };

            if (type is IArrayTypeSymbol { Rank: 1 } ||
                type?.SpecialType == SpecialType.System_String)
            {
                var receiverFormula = new SmtVariable(GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion), SmtValueKind.Reference);
                formula = new SmtVariable(receiverFormula + ".Length", SmtValueKind.Int);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateBuiltInLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int> getSymbolVersion,
            out SmtFormula formula)
        {
            valueExpression = UnwrapSmtFactExpression(valueExpression);
            var valueTypeInfo = semanticModel.GetTypeInfo(valueExpression, cancellationToken);
            var valueType = valueTypeInfo.ConvertedType ?? valueTypeInfo.Type;
            if (valueType is IArrayTypeSymbol { Rank: 1 })
            {
                return TryCreateArrayLengthValueFormula(valueExpression, semanticModel, cancellationToken, getSymbolVersion, out formula);
            }

            if (valueType?.SpecialType == SpecialType.System_String)
            {
                return TryCreateStringLengthValueFormula(valueExpression, semanticModel, cancellationToken, getSymbolVersion, out formula);
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateArrayLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int> getSymbolVersion,
            out SmtFormula formula)
        {
            if (valueExpression is ArrayCreationExpressionSyntax arrayCreation)
            {
                if (arrayCreation.Type.RankSpecifiers.Count == 1 &&
                    arrayCreation.Type.RankSpecifiers[0].Sizes.Count == 1 &&
                    !arrayCreation.Type.RankSpecifiers[0].Sizes[0].IsKind(SyntaxKind.OmittedArraySizeExpression) &&
                    CSharpConditionToFormula.TryTranslateValue(
                        arrayCreation.Type.RankSpecifiers[0].Sizes[0],
                        semanticModel,
                        cancellationToken,
                        out var sizeFormula,
                        getSymbolVersion) &&
                    sizeFormula is { Kind: SmtValueKind.Int })
                {
                    formula = sizeFormula;
                    return true;
                }

                if (arrayCreation.Initializer != null)
                {
                    formula = new SmtIntegerConstant(arrayCreation.Initializer.Expressions.Count);
                    return true;
                }
            }

            if (valueExpression is ImplicitArrayCreationExpressionSyntax implicitArrayCreation)
            {
                formula = new SmtIntegerConstant(implicitArrayCreation.Initializer.Expressions.Count);
                return true;
            }

            if (TryCreateCollectionExpressionLengthFormula(valueExpression, out formula))
            {
                return true;
            }

            if (IsArrayEmptyInvocation(valueExpression, semanticModel, cancellationToken))
            {
                formula = new SmtIntegerConstant(0);
                return true;
            }

            return TryCreateReferenceLengthValueFormula(valueExpression, semanticModel, cancellationToken, getSymbolVersion, out formula);
        }

        private static bool TryCreateStringLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int> getSymbolVersion,
            out SmtFormula formula)
        {
            if (CSharpConditionToFormula.TryGetKnownStringLength(valueExpression, semanticModel, cancellationToken, out var stringLength))
            {
                formula = new SmtIntegerConstant(stringLength);
                return true;
            }

            return TryCreateReferenceLengthValueFormula(valueExpression, semanticModel, cancellationToken, getSymbolVersion, out formula);
        }

        private static bool TryCreateReferenceLengthValueFormula(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            Func<ISymbol, int> getSymbolVersion,
            out SmtFormula formula)
        {
            if (CSharpConditionToFormula.TryTranslateValue(
                    valueExpression,
                    semanticModel,
                    cancellationToken,
                    out var receiverFormula,
                    getSymbolVersion) &&
                receiverFormula is SmtVariable { Kind: SmtValueKind.Reference })
            {
                formula = new SmtVariable(receiverFormula + ".Length", SmtValueKind.Int);
                return true;
            }

            formula = null!;
            return false;
        }

        private static bool TryCreateCollectionExpressionLengthFormula(
            ExpressionSyntax valueExpression,
            out SmtFormula formula)
        {
            if (valueExpression is not CollectionExpressionSyntax collectionExpression ||
                collectionExpression.Elements.Any(static element => element is not ExpressionElementSyntax))
            {
                formula = null!;
                return false;
            }

            formula = new SmtIntegerConstant(collectionExpression.Elements.Count);
            return true;
        }

        private static ExpressionSyntax UnwrapSmtFactExpression(ExpressionSyntax expression)
        {
            while (true)
            {
                if (expression is ParenthesizedExpressionSyntax parenthesized)
                {
                    expression = parenthesized.Expression;
                    continue;
                }

                if (expression is PostfixUnaryExpressionSyntax postfixUnary &&
                    postfixUnary.IsKind(SyntaxKind.SuppressNullableWarningExpression))
                {
                    expression = postfixUnary.Operand;
                    continue;
                }

                return expression;
            }
        }

        private static bool IsArrayEmptyInvocation(
            ExpressionSyntax valueExpression,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            return valueExpression is InvocationExpressionSyntax invocation &&
                semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol
                {
                    Name: "Empty",
                    IsStatic: true,
                    ContainingType.SpecialType: SpecialType.System_Array
                };
        }

        private static bool CanCompareSmtValues(SmtFormula left, SmtFormula right)
        {
            return left.Kind == right.Kind ||
                left is SmtNullConstant && right.Kind == SmtValueKind.Reference ||
                right is SmtNullConstant && left.Kind == SmtValueKind.Reference;
        }

        private static bool IsSmtIntegralType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType is
                SpecialType.System_SByte or
                SpecialType.System_Byte or
                SpecialType.System_Int16 or
                SpecialType.System_UInt16 or
                SpecialType.System_Int32 or
                SpecialType.System_UInt32 or
                SpecialType.System_Int64;
        }

        private static PurityAnalysisState ApplyWrittenLocalStateUpdates(
            PurityAnalysisState currentState,
            ILocalSymbol[] writtenLocalSymbols,
            IOperation valueOperation,
            PurityAnalysisState valueState,
            SemanticModel semanticModel,
            Compilation compilation)
        {
            var nextState = currentState;

            foreach (var writtenLocalSymbol in writtenLocalSymbols)
            {
                nextState = nextState.WithIncrementedSmtSymbolVersion(writtenLocalSymbol);
                nextState = AddAssignedValueFact(
                    nextState,
                    writtenLocalSymbol,
                    valueOperation,
                    valueState,
                    semanticModel);

                if (TryResolveKnownConcreteType(valueOperation, valueState, compilation, out var concreteType))
                {
                    nextState = nextState.WithLocalConcreteType(writtenLocalSymbol, concreteType);
                }
                else
                {
                    nextState = nextState.WithoutLocalConcreteType(writtenLocalSymbol);
                }
            }

            foreach (var writtenLocalSymbol in writtenLocalSymbols)
            {
                if (IsOwnedLocalArrayValue(valueOperation, valueState, compilation))
                {
                    nextState = nextState.WithOwnedLocalArray(writtenLocalSymbol);
                }
                else
                {
                    nextState = nextState.WithoutOwnedLocalArray(writtenLocalSymbol);
                }
            }

            foreach (var writtenLocalSymbol in writtenLocalSymbols)
            {
                if (IsDefinitelyNullValue(valueOperation, valueState))
                {
                    nextState = nextState.WithDefinitelyNullLocal(writtenLocalSymbol);
                }
                else
                {
                    nextState = nextState.WithoutDefinitelyNullLocal(writtenLocalSymbol);
                }
            }

            return nextState;
        }

        private static PurityAnalysisState ApplyAssignedDelegateTargets(
            PurityAnalysisState currentState,
            ISymbol? targetSymbol,
            ITypeSymbol? targetType,
            IOperation? valueOperation,
            ILocalSymbol[] writtenLocalSymbols,
            PurityAnalysisState valueState,
            string logScope,
            string unresolvedReason)
        {
            if (valueOperation == null || targetSymbol == null || targetType?.TypeKind != TypeKind.Delegate)
            {
                return currentState;
            }

            var nextState = currentState;
            PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(valueOperation, valueState);
            if (valueTargets != null)
            {
                foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                {
                    nextState = nextState.WithDelegateTarget(writtenTargetSymbol, valueTargets.Value);
                    LogDebug($"    {logScope} Updated map for {writtenTargetSymbol.Name} with {valueTargets.Value.MethodSymbols.Count} targets. New Map Count: {nextState.DelegateTargetMap.Count}");
                }
            }
            else
            {
                foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                {
                    nextState = nextState.WithDelegateTarget(writtenTargetSymbol, PotentialTargets.Unresolved);
                    LogDebug($"    {logScope} Marked map for {writtenTargetSymbol.Name} unresolved because {unresolvedReason}. New Map Count: {nextState.DelegateTargetMap.Count}");
                }
            }

            return nextState;
        }

        private static IEnumerable<ISymbol> GetAssignmentTargetSymbols(
            ISymbol targetSymbol,
            ILocalSymbol[] writtenLocalSymbols)
        {
            if (writtenLocalSymbols.Length == 0)
            {
                yield return targetSymbol;
                yield break;
            }

            foreach (var writtenLocalSymbol in writtenLocalSymbols)
            {
                yield return writtenLocalSymbol;
            }
        }

        private static IEnumerable<ILocalSymbol> EnumerateWrittenLocalSymbols(
            ILocalSymbol localSymbol,
            Rules.PurityAnalysisContext context)
        {
            var visited = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context, visited))
            {
                yield return writtenLocalSymbol;
            }
        }

        private static IEnumerable<ILocalSymbol> EnumerateWrittenLocalSymbols(
            ILocalSymbol localSymbol,
            Rules.PurityAnalysisContext context,
            HashSet<ISymbol> visited)
        {
            if (!visited.Add(localSymbol))
            {
                yield break;
            }

            yield return localSymbol;

            if (localSymbol.RefKind == RefKind.None)
            {
                yield break;
            }

            foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is not Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax declaratorSyntax ||
                    declaratorSyntax.Initializer?.Value == null)
                {
                    continue;
                }

                var initializerSyntax = declaratorSyntax.Initializer.Value;
                if (initializerSyntax is Microsoft.CodeAnalysis.CSharp.Syntax.RefExpressionSyntax refExpressionSyntax)
                {
                    initializerSyntax = refExpressionSyntax.Expression;
                }

                if (context.SemanticModel.GetOperation(initializerSyntax) is not { } initializerOperation)
                {
                    continue;
                }

                if (TryResolveSymbol(SkipImplicitConversions(initializerOperation)) is not ILocalSymbol targetLocalSymbol)
                {
                    continue;
                }

                foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(targetLocalSymbol, context, visited))
                {
                    yield return writtenLocalSymbol;
                }
            }
        }

        internal static bool IsTrackedOwnedArrayValue(
            IOperation? valueOperation,
            PurityAnalysisState currentState)
        {
            var unwrappedValue = UnwrapArrayOwnershipPreservingConversions(valueOperation);
            if (unwrappedValue == null)
            {
                return false;
            }

            if (unwrappedValue is IArrayCreationOperation ||
                IsArrayCollectionExpressionOperation(unwrappedValue) ||
                IsArrayEmptyInvocation(unwrappedValue))
            {
                return true;
            }

            if (unwrappedValue is IFlowCaptureReferenceOperation flowCaptureReference &&
                currentState.IsOwnedArrayFlowCapture(flowCaptureReference.Id))
            {
                return true;
            }

            return unwrappedValue is ILocalReferenceOperation localReference &&
                   currentState.IsOwnedLocalArraySymbol(localReference.Local);
        }

        private static bool IsOwnedLocalArrayValue(
            IOperation? valueOperation,
            PurityAnalysisState currentState,
            Compilation compilation)
        {
            var unwrappedValue = UnwrapArrayOwnershipPreservingConversions(valueOperation);
            if (unwrappedValue == null)
            {
                return false;
            }

            if (IsTrackedOwnedArrayValue(unwrappedValue, currentState) ||
                IsTrustedFreshArrayFactoryOperation(unwrappedValue, compilation, out _))
            {
                return true;
            }

            if (unwrappedValue is IInvocationOperation invocationOperation &&
                invocationOperation.Type is IArrayTypeSymbol &&
                IsTrustedGeneratedFreshOwnedArrayReturningMember(invocationOperation.TargetMethod.OriginalDefinition, compilation))
            {
                return true;
            }

            return unwrappedValue is ILocalReferenceOperation localReference &&
                   currentState.IsOwnedLocalArraySymbol(localReference.Local);
        }

        internal static bool IsOwnedArrayValueOrTrustedFactory(
            IOperation? valueOperation,
            PurityAnalysisState currentState,
            Compilation compilation)
        {
            return IsOwnedLocalArrayValue(valueOperation, currentState, compilation);
        }

        internal static IOperation? UnwrapArrayOwnershipPreservingConversions(IOperation? operation)
        {
            while (operation is IConversionOperation conversion &&
                   (conversion.IsImplicit ||
                    (!conversion.Conversion.IsUserDefined &&
                     (conversion.Conversion.IsIdentity ||
                      conversion.Conversion.IsReference))))
            {
                operation = conversion.Operand;
            }

            return operation;
        }

        internal static bool IsArrayAsReadOnlyInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.Name != "AsReadOnly" ||
                targetMethod.ContainingType?.ToDisplayString() != "System.Array" ||
                invocationOperation.Arguments.Length != 1)
            {
                return false;
            }

            return true;
        }

        internal static bool IsArrayAsReadOnlyOwnedLocalArrayInvocation(
            IInvocationOperation invocationOperation,
            PurityAnalysisState currentState)
        {
            if (!IsArrayAsReadOnlyInvocation(invocationOperation))
            {
                return false;
            }

            var argumentValue = UnwrapArrayOwnershipPreservingConversions(invocationOperation.Arguments[0].Value);
            return IsTrackedOwnedArrayValue(argumentValue, currentState);
        }

        private static bool IsArrayEmptyInvocation(IOperation? operation)
        {
            var unwrappedOperation = UnwrapArrayOwnershipPreservingConversions(operation);
            return unwrappedOperation is IInvocationOperation invocation &&
                IsArrayEmptyFactory(invocation.TargetMethod.OriginalDefinition);
        }

        internal static bool IsTimeSpanInvariantCultureParseInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.ContainingType?.ToDisplayString() != "System.TimeSpan" ||
                invocationOperation.Arguments.Length < 2)
            {
                return false;
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 2 &&
                invocationOperation.Arguments.Length == 2)
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Name == "ParseExact" &&
                targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                IsSingleTimeSpanConstantFormat(invocationOperation.Arguments[1].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            if (targetMethod.Name == "ParseExact" &&
                targetMethod.Parameters.Length == 4 &&
                invocationOperation.Arguments.Length == 4 &&
                (targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String ||
                 IsReadOnlySpanOfChar(targetMethod.Parameters[0].Type)) &&
                IsSingleTimeSpanConstantFormat(invocationOperation.Arguments[1].Value) &&
                IsTimeSpanStylesNone(invocationOperation.Arguments[3].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            return false;
        }

        internal static bool IsInvariantCultureDeterministicParseInvocation(IInvocationOperation invocationOperation)
        {
            return IsInvariantCultureNumericParseInvocation(invocationOperation) ||
                IsTimeSpanInvariantCultureParseInvocation(invocationOperation) ||
                IsDateOnlyInvariantCultureParseInvocation(invocationOperation) ||
                IsTimeOnlyInvariantCultureParseInvocation(invocationOperation) ||
                IsDateTimeOffsetInvariantCultureParseExactInvocation(invocationOperation);
        }

        internal static bool TryGetSemanticKnownImpureCatalogSource(
            IInvocationOperation invocationOperation,
            out string catalogSource)
        {
            if (IsCurrentCultureSensitiveNumericParseOrFormatInvocation(invocationOperation) ||
                IsCurrentCultureSensitiveDateLikeParseOrFormatInvocation(invocationOperation))
            {
                catalogSource = "current_culture_semantic_rule";
                return true;
            }

            catalogSource = string.Empty;
            return false;
        }

        private static bool IsInvariantCultureNumericParseInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.Name != "Parse" ||
                !IsCultureSensitiveNumericType(targetMethod.ContainingType))
            {
                return false;
            }

            if (targetMethod.Parameters.Length == 2 &&
                invocationOperation.Arguments.Length == 2 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                targetMethod.Parameters[1].Type.ToDisplayString() == "System.Globalization.NumberStyles")
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            return false;
        }

        private static bool IsCurrentCultureSensitiveNumericParseOrFormatInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                !IsCultureSensitiveNumericType(targetMethod.ContainingType))
            {
                return IsCurrentCultureSensitiveConvertNumericInvocation(invocationOperation);
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 1 &&
                invocationOperation.Arguments.Length == 1 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return true;
            }

            if (targetMethod.Name == "TryParse" &&
                targetMethod.Parameters.Length == 2 &&
                invocationOperation.Arguments.Length == 2 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return true;
            }

            if (targetMethod.Name == "ToString" &&
                targetMethod.Parameters.Length == 0 &&
                invocationOperation.Arguments.Length == 0)
            {
                return true;
            }

            if (targetMethod.Name == "ToString" &&
                targetMethod.Parameters.Length == 1 &&
                invocationOperation.Arguments.Length == 1 &&
                targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String)
            {
                return true;
            }

            return false;
        }

        private static bool IsCurrentCultureSensitiveConvertNumericInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.ContainingType?.ToDisplayString() != "System.Convert" ||
                !IsCurrentCultureSensitiveConvertNumericMethodName(targetMethod.Name) ||
                targetMethod.Parameters.Length != 1 ||
                invocationOperation.Arguments.Length != 1)
            {
                return false;
            }

            return targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String;
        }

        private static bool IsCurrentCultureSensitiveConvertNumericMethodName(string methodName)
        {
            return methodName is
                "ToByte" or
                "ToDecimal" or
                "ToDouble" or
                "ToInt16" or
                "ToInt32" or
                "ToInt64" or
                "ToSByte" or
                "ToSingle" or
                "ToUInt16" or
                "ToUInt32" or
                "ToUInt64";
        }

        private static bool IsCurrentCultureSensitiveDateLikeParseOrFormatInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                !IsCultureSensitiveDateLikeType(targetMethod.ContainingType))
            {
                return IsCurrentCultureSensitiveConvertDateLikeInvocation(invocationOperation);
            }

            if (IsInvariantCultureDeterministicParseInvocation(invocationOperation))
            {
                return false;
            }

            if (targetMethod.Name == "Parse" &&
                invocationOperation.Arguments.Length >= 1 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return invocationOperation.Arguments.Length == 1 ||
                    HasFormatProviderParameter(targetMethod);
            }

            if (targetMethod.Name == "TryParse" &&
                invocationOperation.Arguments.Length >= 2 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return invocationOperation.Arguments.Length == 2 ||
                    HasFormatProviderParameter(targetMethod);
            }

            if ((targetMethod.Name == "ParseExact" || targetMethod.Name == "TryParseExact") &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsFormatSpecifierType(targetMethod.Parameters[1].Type))
            {
                return HasFormatProviderParameter(targetMethod) ||
                    IsDateOnlyOrTimeOnlyType(targetMethod.ContainingType) &&
                    invocationOperation.Arguments.Length == (targetMethod.Name == "ParseExact" ? 2 : 3);
            }

            if (targetMethod.Name == "ToString" &&
                invocationOperation.Arguments.Length == 0)
            {
                return true;
            }

            if (targetMethod.Name == "ToString" &&
                invocationOperation.Arguments.Length == 1 &&
                targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String)
            {
                return true;
            }

            if (invocationOperation.Arguments.Length == 0 &&
                targetMethod.Name is "ToLongDateString" or "ToShortDateString" or "ToLongTimeString" or "ToShortTimeString")
            {
                return true;
            }

            return false;
        }

        private static bool IsCurrentCultureSensitiveConvertDateLikeInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.ContainingType?.ToDisplayString() != "System.Convert" ||
                targetMethod.Name != "ToDateTime" ||
                targetMethod.Parameters.Length != 1 ||
                invocationOperation.Arguments.Length != 1)
            {
                return false;
            }

            return targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String;
        }

        private static bool IsCultureSensitiveNumericType(ITypeSymbol? containingType)
        {
            return containingType?.SpecialType is SpecialType.System_Byte or
                SpecialType.System_Decimal or
                SpecialType.System_Double or
                SpecialType.System_Int16 or
                SpecialType.System_Int32 or
                SpecialType.System_Int64 or
                SpecialType.System_SByte or
                SpecialType.System_Single or
                SpecialType.System_UInt16 or
                SpecialType.System_UInt32 or
                SpecialType.System_UInt64 ||
                containingType?.ToDisplayString() is "System.Half" or "System.Numerics.BigInteger";
        }

        private static bool IsCultureSensitiveDateLikeType(ITypeSymbol? containingType)
        {
            return containingType?.ToDisplayString() is "System.DateOnly" or
                "System.DateTime" or
                "System.DateTimeOffset" or
                "System.TimeOnly" or
                "System.TimeSpan";
        }

        private static bool IsDateOnlyOrTimeOnlyType(ITypeSymbol? containingType)
        {
            return containingType?.ToDisplayString() is "System.DateOnly" or "System.TimeOnly";
        }

        private static bool IsFormatSpecifierType(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType == SpecialType.System_String ||
                IsReadOnlySpanOfChar(typeSymbol) ||
                typeSymbol is IArrayTypeSymbol arrayType &&
                arrayType.ElementType.SpecialType == SpecialType.System_String;
        }

        private static bool HasFormatProviderParameter(IMethodSymbol methodSymbol)
        {
            foreach (var parameter in methodSymbol.Parameters)
            {
                if (parameter.Type.Name == "IFormatProvider" &&
                    parameter.Type.ContainingNamespace?.ToDisplayString() == "System")
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsTimeOnlyInvariantCultureParseInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.ContainingType?.ToDisplayString() != "System.TimeOnly" ||
                targetMethod.Name is not ("Parse" or "ParseExact"))
            {
                return false;
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 2 &&
                invocationOperation.Arguments.Length == 2 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsDateTimeStylesNone(invocationOperation.Arguments[2].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                targetMethod.Name == "ParseExact" &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsSingleTimeOnlyInvariantFormat(invocationOperation.Arguments[1].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            if (targetMethod.Parameters.Length == 4 &&
                invocationOperation.Arguments.Length == 4 &&
                targetMethod.Name == "ParseExact" &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsSingleTimeOnlyInvariantFormat(invocationOperation.Arguments[1].Value) &&
                IsDateTimeStylesNone(invocationOperation.Arguments[3].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            return false;
        }

        private static bool IsDateOnlyInvariantCultureParseInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.ContainingType?.ToDisplayString() != "System.DateOnly" ||
                targetMethod.Name is not ("Parse" or "ParseExact"))
            {
                return false;
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 2 &&
                invocationOperation.Arguments.Length == 2 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsDateTimeStylesNone(invocationOperation.Arguments[2].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                targetMethod.Name == "ParseExact" &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsSingleDateOnlyInvariantFormat(invocationOperation.Arguments[1].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            if (targetMethod.Parameters.Length == 4 &&
                invocationOperation.Arguments.Length == 4 &&
                targetMethod.Name == "ParseExact" &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsSingleDateOnlyInvariantFormat(invocationOperation.Arguments[1].Value) &&
                IsDateTimeStylesNone(invocationOperation.Arguments[3].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            return false;
        }

        private static bool IsDateTimeOffsetInvariantCultureParseExactInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.ContainingType?.ToDisplayString() != "System.DateTimeOffset" ||
                targetMethod.Name != "ParseExact")
            {
                return false;
            }

            if (targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                targetMethod.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                IsSingleDateTimeOffsetRoundtripFormat(invocationOperation.Arguments[1].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            if (targetMethod.Parameters.Length == 4 &&
                invocationOperation.Arguments.Length == 4 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsSingleDateTimeOffsetRoundtripFormat(invocationOperation.Arguments[1].Value) &&
                IsDateTimeStylesNone(invocationOperation.Arguments[3].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            return false;
        }

        private static bool IsSingleTimeSpanConstantFormat(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is string format &&
                (format == "c" || format == "g" || format == "G");
        }

        private static bool IsSingleDateOnlyInvariantFormat(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is string format &&
                format == "d";
        }

        private static bool IsSingleTimeOnlyInvariantFormat(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is string format &&
                format == "t";
        }

        private static bool IsSingleDateTimeOffsetRoundtripFormat(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is string format &&
                (format == "O" || format == "o");
        }

        private static bool IsZeroStyle(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is int styles &&
                styles == 0;
        }

        private static bool IsTimeSpanStylesNone(IOperation? operation) => IsZeroStyle(operation);

        private static bool IsDateTimeStylesNone(IOperation? operation) => IsZeroStyle(operation);

        private static bool IsReadOnlySpanOfChar(ITypeSymbol typeSymbol)
        {
            return typeSymbol is INamedTypeSymbol namedType &&
                namedType.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>" &&
                namedType.TypeArguments.Length == 1 &&
                namedType.TypeArguments[0].SpecialType == SpecialType.System_Char;
        }

        private static bool IsStringOrReadOnlySpanOfChar(ITypeSymbol typeSymbol)
        {
            return typeSymbol.SpecialType == SpecialType.System_String ||
                IsReadOnlySpanOfChar(typeSymbol);
        }

        private static bool IsCultureInfoInvariantCulture(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation is IPropertyReferenceOperation propertyReference &&
                propertyReference.Property.Name == "InvariantCulture" &&
                propertyReference.Property.ContainingType?.ToDisplayString() == "System.Globalization.CultureInfo";
        }

        internal static bool ShouldAnalyzeCompoundAssignmentOperator(IMethodSymbol operatorMethod)
        {
            return operatorMethod.DeclaringSyntaxReferences.Length > 0 ||
                   IsKnownImpure(operatorMethod) ||
                   HasImpureAttribute(operatorMethod);
        }


        internal static PurityAnalysisEngine.PotentialTargets? ResolvePotentialTargets(IOperation valueOperation, PurityAnalysisState currentState, SemanticModel? semanticModel = null)
        {
            var unwrapped = SkipImplicitConversions(valueOperation);
            if (unwrapped == null) return null;
            if (unwrapped is IFlowCaptureReferenceOperation flowCaptureReference &&
                currentState.FlowCaptureTargets.TryGetValue(flowCaptureReference.Id, out var capturedTargets))
            {
                return capturedTargets;
            }

            if (unwrapped is IConditionalOperation conditionalOperation)
            {
                if (conditionalOperation.WhenTrue == null || conditionalOperation.WhenFalse == null)
                {
                    return PurityAnalysisEngine.PotentialTargets.Unresolved;
                }

                var trueTargets = ResolvePotentialTargets(conditionalOperation.WhenTrue, currentState, semanticModel);
                var falseTargets = ResolvePotentialTargets(conditionalOperation.WhenFalse, currentState, semanticModel);
                if (trueTargets == null || falseTargets == null)
                {
                    return PurityAnalysisEngine.PotentialTargets.Unresolved;
                }

                return PurityAnalysisEngine.PotentialTargets.Merge(trueTargets.Value, falseTargets.Value);
            }

            if (unwrapped is IMethodReferenceOperation methodRef)
            {
                if (IsPotentiallyDispatchedDelegateTarget(methodRef))
                {
                    return PurityAnalysisEngine.PotentialTargets.Unresolved;
                }

                return PurityAnalysisEngine.PotentialTargets.FromSingle(methodRef.Method.OriginalDefinition);
            }

            if (unwrapped is IAnonymousFunctionOperation anonymousFunction && anonymousFunction.Symbol != null)
            {
                return PurityAnalysisEngine.PotentialTargets.FromSingle(anonymousFunction.Symbol.OriginalDefinition);
            }
            if (unwrapped is IFlowAnonymousFunctionOperation flowAnonymousFunction && flowAnonymousFunction.Symbol != null)
            {
                return PurityAnalysisEngine.PotentialTargets.FromSingle(flowAnonymousFunction.Symbol.OriginalDefinition);
            }

            if (unwrapped is IDelegateCreationOperation delegateCreation)
            {
                var target = SkipImplicitConversions(delegateCreation.Target);
                if (target is IMethodReferenceOperation lambdaRef)
                {
                    if (IsPotentiallyDispatchedDelegateTarget(lambdaRef))
                    {
                        return PurityAnalysisEngine.PotentialTargets.Unresolved;
                    }

                    return PurityAnalysisEngine.PotentialTargets.FromSingle(lambdaRef.Method.OriginalDefinition);
                }
                if (target is IAnonymousFunctionOperation anonymousTarget && anonymousTarget.Symbol != null)
                {
                    return PurityAnalysisEngine.PotentialTargets.FromSingle(anonymousTarget.Symbol.OriginalDefinition);
                }
                if (target is IFlowAnonymousFunctionOperation flowAnonymousTarget && flowAnonymousTarget.Symbol != null)
                {
                    return PurityAnalysisEngine.PotentialTargets.FromSingle(flowAnonymousTarget.Symbol.OriginalDefinition);
                }
            }

            ISymbol? valueSourceSymbol = TryResolveSymbol(unwrapped);
            if (valueSourceSymbol != null && currentState.DelegateTargetMap.TryGetValue(valueSourceSymbol, out var sourceTargets))
            {
                return sourceTargets;
            }

            if (valueSourceSymbol != null &&
                semanticModel != null &&
                CanTrustDelegateInitializerSymbol(valueSourceSymbol, semanticModel))
            {
                var initializerTargets = TryResolveDelegateInitializerTargets(valueSourceSymbol, semanticModel, currentState);
                if (initializerTargets != null)
                {
                    return initializerTargets;
                }
            }

            return null;
        }

        private static bool IsPotentiallyDispatchedDelegateTarget(IMethodReferenceOperation methodReference)
        {
            var method = methodReference.Method;
            if (method.IsSealed || method.ContainingType?.IsSealed == true)
            {
                return false;
            }

            if (method.ContainingType?.TypeKind != TypeKind.Interface &&
                !method.IsAbstract &&
                !method.IsVirtual &&
                !method.IsOverride)
            {
                return false;
            }

            if (methodReference.Instance == null)
            {
                return false;
            }

            if (SkipImplicitConversions(methodReference.Instance) is IObjectCreationOperation)
            {
                return false;
            }

            return methodReference.Instance.Type is not INamedTypeSymbol receiverType ||
                !receiverType.IsSealed;
        }

        private static bool CanTrustDelegateInitializerSymbol(ISymbol symbol, SemanticModel semanticModel)
        {
            if (symbol is ILocalSymbol)
            {
                return true;
            }

            if (symbol is IFieldSymbol fieldSymbol)
            {
                return fieldSymbol.IsReadOnly &&
                    !HasAssignmentToField(fieldSymbol, semanticModel);
            }

            return false;
        }

        private static bool HasAssignmentToField(IFieldSymbol fieldSymbol, SemanticModel semanticModel)
        {
            foreach (var syntaxReference in fieldSymbol.ContainingType.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is not TypeDeclarationSyntax typeDeclaration)
                {
                    continue;
                }

                foreach (var assignment in typeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
                {
                    var model = semanticModel.Compilation.GetSemanticModel(assignment.SyntaxTree);
                    var targetOperation = model.GetOperation(assignment.Left);
                    var targetSymbol = TryResolveSymbol(SkipImplicitConversions(targetOperation));
                    if (SymbolEqualityComparer.Default.Equals(targetSymbol, fieldSymbol))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static PurityAnalysisEngine.PotentialTargets? TryResolveDelegateInitializerTargets(ISymbol symbol, SemanticModel semanticModel, PurityAnalysisState currentState)
        {
            foreach (var syntaxReference in symbol.DeclaringSyntaxReferences)
            {
                var syntax = syntaxReference.GetSyntax();
                var model = semanticModel.Compilation.GetSemanticModel(syntax.SyntaxTree);

                SyntaxNode? initializerSyntax = syntax switch
                {
                    Microsoft.CodeAnalysis.CSharp.Syntax.VariableDeclaratorSyntax variableDeclaratorSyntax => variableDeclaratorSyntax.Initializer?.Value,
                    Microsoft.CodeAnalysis.CSharp.Syntax.PropertyDeclarationSyntax propertyDeclarationSyntax => propertyDeclarationSyntax.Initializer?.Value,
                    _ => null
                };

                if (initializerSyntax == null)
                {
                    continue;
                }

                var initializerOperation = model.GetOperation(initializerSyntax);
                if (initializerOperation == null)
                {
                    continue;
                }

                var initializerTargets = ResolvePotentialTargets(initializerOperation, currentState, model);
                if (initializerTargets != null)
                {
                    return initializerTargets;
                }
            }

            return null;
        }

        internal static IOperation? SkipImplicitConversions(IOperation? operation)
        {
            while (operation is IConversionOperation conv && conv.IsImplicit)
            {
                operation = conv.Operand;
            }
            return operation;
        }


        internal static ISymbol? TryResolveSymbol(IOperation? operation)
        {
            return operation switch
            {
                ILocalReferenceOperation localRef => localRef.Local,
                IParameterReferenceOperation paramRef => paramRef.Parameter,
                IFieldReferenceOperation fieldRef => fieldRef.Field,
                IPropertyReferenceOperation propRef => propRef.Property,
                IEventReferenceOperation eventRef => eventRef.Event,
                _ => null
            };
        }

        internal static ISymbol? TryResolveTrackedSymbol(
            IOperation? operation,
            PurityAnalysisState currentState)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            var symbol = TryResolveSymbol(operation);
            if (symbol != null)
            {
                return symbol;
            }

            return operation is IFlowCaptureReferenceOperation flowCaptureReference &&
                   currentState.TryGetFlowCaptureSymbol(flowCaptureReference.Id, out var capturedSymbol)
                ? capturedSymbol
                : null;
        }

        private static bool IsTransientCharArrayConsumedByStringConstructor(IInvocationOperation invocationOperation, SemanticModel semanticModel)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                !targetMethod.IsExtensionMethod ||
                targetMethod.Name != "ToArray" ||
                invocationOperation.Type is not IArrayTypeSymbol arrayType ||
                arrayType.ElementType.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var enumerableType = semanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            if (enumerableType == null ||
                !SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType?.OriginalDefinition, enumerableType))
            {
                return false;
            }

            IOperation? parent = invocationOperation.Parent;
            if (parent is IArgumentOperation argumentOperation)
            {
                parent = argumentOperation.Parent;
            }

            if (parent is not IObjectCreationOperation objectCreationOperation)
            {
                return false;
            }

            var constructorSymbol = objectCreationOperation.Constructor;
            return constructorSymbol?.ContainingType?.SpecialType == SpecialType.System_String &&
                   objectCreationOperation.Arguments.Length == 1;
        }
    }
}
