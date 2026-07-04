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
using PurelySharp.Analyzer.Engine.Rules;
using PurelySharp.Symbolic;
using PurelySharp.Symbolic.Ir;
using PurelySharp.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace PurelySharp.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {
        private readonly CompilationPurityService? _purityService;
        private readonly SmtAnalysisService _smtAnalysis;

        public PurityAnalysisEngine(CompilationPurityService purityService)
        {
            _purityService = purityService ?? throw new ArgumentNullException(nameof(purityService));
            _smtAnalysis = purityService.SmtAnalysis;
        }

        internal PurityAnalysisEngine(SmtAnalysisService smtAnalysis)
        {
            _smtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
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
            public string BclFallbackGuess { get; }
            public string BclFallbackConfidence { get; }
            public string BclFallbackReason { get; }

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
                var operationKind = operationKindOverride ?? operation?.Kind.ToString() ?? syntaxNode?.Kind().ToString() ?? string.Empty;
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
                if (!string.IsNullOrEmpty(OperationKind))
                {
                    return this;
                }

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

            public ImmutableDictionary<string, string?> ToDiagnosticProperties()
            {
                var builder = ImmutableDictionary.CreateBuilder<string, string?>(StringComparer.Ordinal);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityCategoryProperty, Category);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityRuleProperty, RuleName);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityOperationKindProperty, OperationKind);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpuritySymbolProperty, Symbol);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityCatalogSourceProperty, CatalogSource);
                AddIfPresent(builder, PurelySharpDiagnostics.ImpurityCalleeChainProperty, CalleeChain);
                AddIfPresent(builder, PurelySharpDiagnostics.BclFallbackGuessProperty, BclFallbackGuess);
                AddIfPresent(builder, PurelySharpDiagnostics.BclFallbackConfidenceProperty, BclFallbackConfidence);
                AddIfPresent(builder, PurelySharpDiagnostics.BclFallbackReasonProperty, BclFallbackReason);
                return builder.ToImmutable();
            }

            public string ToSummary()
            {
                var category = string.IsNullOrEmpty(Category) ? "unknown" : Category;
                if (!string.IsNullOrEmpty(Symbol))
                {
                    var summary = category + " at " + Symbol;
                    return string.IsNullOrEmpty(BclFallbackGuess)
                        ? summary
                        : summary + " with BCL fallback " + BclFallbackGuess;
                }

                return string.IsNullOrEmpty(BclFallbackGuess)
                    ? category
                    : category + " with BCL fallback " + BclFallbackGuess;
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
                ImmutableArray<SmtFormula>? pathConditions = null,
                SymbolicState? pathState = null,
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
                PathState = pathState ?? new SymbolicState();
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
                var mergedSmtSymbolVersions = MergeSmtSymbolVersionsAcrossAll(stateList.Select(s => s.SmtSymbolVersions));
                return new PurityAnalysisState(mergedImpurity, firstImpureNode, mergedTargets, mergedCaptures, mergedCaptureTargets, mergedOwnedLocalArrays, mergedDefinitelyNullLocals, firstEvidence, localConcreteTypes: mergedLocalConcreteTypes, smtSymbolVersions: mergedSmtSymbolVersions, flowCaptureConcreteTypes: mergedCaptureConcreteTypes, pathConditions: MergePathConditionsAcrossAll(stateList, mergedSmtSymbolVersions), pathState: MergePathStatesAcrossAll(stateList), flowCaptureSymbols: mergedCaptureSymbols, ownedArrayFlowCaptures: mergedOwnedArrayFlowCaptures);
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

                if (!SymbolicStatesEqual(this.PathState, other.PathState))
                {
                    return false;
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

                foreach (var fact in PathState.Facts)
                {
                    hash = hash * 23 + fact.GetHashCode();
                }

                foreach (var condition in PathState.PathConditions)
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

            private static bool SymbolicStatesEqual(SymbolicState first, SymbolicState second)
            {
                if (first.Facts.Length != second.Facts.Length ||
                    first.PathConditions.Length != second.PathConditions.Length)
                {
                    return false;
                }

                for (var index = 0; index < first.Facts.Length; index++)
                {
                    if (!Equals(first.Facts[index], second.Facts[index]))
                    {
                        return false;
                    }
                }

                for (var index = 0; index < first.PathConditions.Length; index++)
                {
                    if (!Equals(first.PathConditions[index], second.PathConditions[index]))
                    {
                        return false;
                    }
                }

                return true;
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
                    localConcreteTypes: localConcreteTypes ?? LocalConcreteTypes,
                    smtSymbolVersions: smtSymbolVersions ?? SmtSymbolVersions,
                    flowCaptureConcreteTypes: flowCaptureConcreteTypes ?? FlowCaptureConcreteTypes,
                    pathConditions: pathConditions ?? PathConditions,
                    pathState: pathState ?? PathState,
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
                return WithOwnedArrayFlowCapture(id, source: null);
            }

            public PurityAnalysisState WithOwnedArrayFlowCapture(CaptureId id, SyntaxNode? source)
            {
                if (OwnedArrayFlowCaptures.Contains(id))
                {
                    return this;
                }

                return Copy(
                    ownedArrayFlowCaptures: OwnedArrayFlowCaptures.Add(id),
                    pathState: AddOwnedArrayFlowCaptureFacts(PathState, id, source));
            }

            public PurityAnalysisState WithoutOwnedArrayFlowCapture(CaptureId id)
            {
                if (!OwnedArrayFlowCaptures.Contains(id))
                {
                    return this;
                }

                return Copy(
                    ownedArrayFlowCaptures: OwnedArrayFlowCaptures.Remove(id),
                    pathState: RemoveOwnedArrayFlowCaptureFacts(PathState, id));
            }

            public bool IsOwnedArrayFlowCapture(CaptureId id)
            {
                return OwnedArrayFlowCaptures.Contains(id);
            }

            private static SymbolicState AddOwnedArrayFlowCaptureFacts(SymbolicState pathState, CaptureId id, SyntaxNode? source)
            {
                if (source == null)
                {
                    return pathState;
                }

                var term = CreateOwnedArrayFlowCaptureTerm(id);
                var facts = SymbolicOwnershipFactFactory.CreateFreshOwned(
                    term,
                    source,
                    "analyzer.owned-array-flow-capture",
                    evidenceKey: "evidence.owned-array-flow-capture");
                foreach (var fact in facts)
                {
                    pathState = pathState.AddFact(fact);
                }

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
                           _ => false,
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

            public PurityAnalysisState WithPathConditionsAndState(
                ImmutableArray<SmtFormula> pathConditions,
                SymbolicState pathState)
            {
                return Copy(pathConditions: pathConditions, pathState: pathState ?? new SymbolicState());
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
                    pathConditions: RemovePathConditionsReferencingSymbol(originalDefinition),
                    pathState: new SymbolicState());
            }

            private ImmutableArray<SmtFormula> RemovePathConditionsReferencingSymbol(ISymbol symbol)
            {
                if (PathConditions.IsDefaultOrEmpty)
                {
                    return PathConditions;
                }

                var variablePrefix = SymbolicFactFactory.GetSmtVariableName(symbol);
                var builder = ImmutableArray.CreateBuilder<SmtFormula>(PathConditions.Length);
                foreach (var condition in PathConditions)
                {
                    if (!SmtFormulaReferenceScanner.ContainsVariablePrefix(condition, variablePrefix))
                    {
                        builder.Add(condition);
                    }
                }

                return builder.Count == PathConditions.Length
                    ? PathConditions
                    : builder.ToImmutable();
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
            SmtAnalysisService smtAnalysis)
        {

            var activeSmtAnalysis = smtAnalysis ?? throw new ArgumentNullException(nameof(smtAnalysis));
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
                        "impure_callee",
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

                    var escapeSyntax = locationSyntax ?? bodySyntaxNode;
                    purityCache[methodSymbol] = escapeSyntax == null
                        ? ImpureResult(bodySyntaxNode)
                        : PurityAnalysisResult.Impure(
                            escapeSyntax,
                            CreateByRefReturnEscapeEvidence(methodSymbol, escapeSyntax));
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

                    if (TryCreateBclFallbackImpurity(
                            methodSymbol,
                            declaringSyntax,
                            operation: null,
                            ruleName: "MethodInvocationPurityRule",
                            out var bclFallbackNoBodyResult))
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} has no trusted purity evidence. Reporting BCL fallback guess.");
                        purityCache[methodSymbol] = bclFallbackNoBodyResult;
                        LogDebug($"{indent}<< Exit DeterminePurity (Abstract/NoBody BCL Fallback): {methodSymbol.ToDisplayString()}");
                        return bclFallbackNoBodyResult;
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
                var mergedPathStateFromCfg = new SymbolicState();
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
                            purityCache,
                            activeSmtAnalysis);
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
                            out mergedLocalConcreteTypesFromCfg,
                            out mergedPathStateFromCfg);
                    }

                    LogDebug($"{indent}  CFG Analysis Result for {methodSymbol.ToDisplayString()}: IsPure={result.IsPure}, ImpureNode={result.ImpureSyntaxNode?.Kind()}");
                }


                PurityAnalysisState? postCfgExitResourceState = null;
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
                            null,
                            activeSmtAnalysis);


                        LogDebug($"{indent}  Post-CFG: Checking ReturnOperations (with merged delegate map from CFG)...");
                        var postCfgReturnState = new PurityAnalysisState(
                            false,
                            null,
                            mergedDelegateTargetsFromCfg,
                            null,
                            ownedLocalArraySymbols: mergedOwnedLocalArraysFromCfg,
                            localConcreteTypes: mergedLocalConcreteTypesFromCfg,
                            pathState: mergedPathStateFromCfg,
                            ownedArrayFlowCaptures: mergedOwnedArrayFlowCapturesFromCfg);
                        postCfgExitResourceState = AddScopeEndResourceDisposeFacts(
                            AddStraightLineResourceActionFacts(
                                postCfgReturnState,
                                methodBodyIOperation,
                                semanticModel),
                            methodBodyIOperation);
                        var visibleReturnOperations = ExecutionVisibility.VisibleDescendants(methodBodyIOperation)
                            .OfType<IReturnOperation>()
                            .ToArray();
                        if (visibleReturnOperations.Length == 1)
                        {
                            postCfgExitResourceState = AddReturnedOwnedResourceFacts(
                                postCfgExitResourceState.Value,
                                visibleReturnOperations[0],
                                postCfgExitResourceState.Value);
                        }

                        foreach (var returnOp in visibleReturnOperations)
                        {
                            if (returnOp.ReturnedValue != null)
                            {
                                var returnState = AddCompletedStraightLineUsingDisposeFacts(
                                    postCfgReturnState,
                                    methodBodyIOperation,
                                    returnOp);
                                var returnPurity = CheckSingleOperation(returnOp, postCfgContext, returnState);
                                if (!returnPurity.IsPure)
                                {
                                    if (IsImpurityProvenUnreachable(returnPurity, semanticModel, activeSmtAnalysis))
                                    {
                                        continue;
                                    }

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
                            if (ShouldSkipPostCfgDirectPurityProbe(forEachOp, semanticModel, activeSmtAnalysis))
                            {
                                LogDebug($"{indent}    Post-CFG: Skipping statically unreachable foreach enumerator runtime check: {forEachOp.Syntax}");
                                continue;
                            }

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
                            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                                    firstThrowOp.Syntax,
                                    semanticModel,
                                    CancellationToken.None,
                                    activeSmtAnalysis))
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
                                var catchResult = AnalyzeOperationSubtreePurity(catchClause, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache, activeSmtAnalysis);
                                if (!catchResult.IsPure)
                                {
                                    result = catchResult;
                                    goto PostCfgChecksDone;
                                }
                            }
                            if (tryOp.Finally != null)
                            {
                                var finallyResult = AnalyzeOperationSubtreePurity(tryOp.Finally, semanticModel, enforcePureAttributeSymbol, allowSynchronizationAttributeSymbol, visited, methodSymbol, purityCache, activeSmtAnalysis);
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
                            if (ShouldSkipPostCfgDirectPurityProbe(invocationOp, semanticModel, activeSmtAnalysis))
                            {
                                continue;
                            }

                            var hasSemanticKnownImpureCatalogSource = TryGetSemanticKnownImpureCatalogSource(
                                invocationOp,
                                out var semanticKnownImpureCatalogSource);
                            if (invocationOp.TargetMethod != null &&
                                !IsArrayAsReadOnlyInvocation(invocationOp) &&
                                !IsArrayInterfaceGetEnumeratorInvocation(invocationOp, semanticModel) &&
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
                                        var invocationRuleResult = CheckSingleOperation(invocationOp, postCfgContext, postCfgReturnState);
                                        if (invocationRuleResult.IsPure)
                                        {
                                            continue;
                                        }

                                        LogDebug($"{indent}    Post-CFG: Found generated-summary impure invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
                                        result = invocationRuleResult;
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
                            if (ShouldSkipPostCfgDirectPurityProbe(operation, semanticModel, activeSmtAnalysis))
                            {
                                continue;
                            }

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
                                    null,
                                    activeSmtAnalysis);
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

                if (result.IsPure &&
                    postCfgExitResourceState.HasValue &&
                    TryCreateMissingOwnedResourceDisposalResult(
                        postCfgExitResourceState.Value,
                        methodSymbol,
                        semanticModel,
                        out var missingDisposeResult))
                {
                    result = missingDisposeResult;
                }

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

        private static bool ShouldSkipPostCfgDirectPurityProbe(
            IOperation operation,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis)
        {
            if (operation.Syntax == null)
            {
                return false;
            }

            foreach (var syntax in GetOperationVisibilitySyntaxCandidates(operation.Syntax))
            {
                if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                        syntax,
                        semanticModel,
                        CancellationToken.None,
                        smtAnalysis))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsImpurityProvenUnreachable(
            PurityAnalysisResult result,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis)
        {
            if (result.IsPure ||
                result.ImpureSyntaxNode == null)
            {
                return false;
            }

            foreach (var syntax in GetOperationVisibilitySyntaxCandidates(result.ImpureSyntaxNode))
            {
                if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                        syntax,
                        semanticModel,
                        CancellationToken.None,
                        smtAnalysis))
                {
                    return true;
                }
            }

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

                if (IsNestedCallableBoundary(ancestor))
                {
                    yield break;
                }
            }
        }

        private static bool IsNestedCallableBoundary(SyntaxNode syntax)
        {
            return syntax is MethodDeclarationSyntax or
                ConstructorDeclarationSyntax or
                OperatorDeclarationSyntax or
                AccessorDeclarationSyntax or
                LocalFunctionStatementSyntax or
                ParenthesizedLambdaExpressionSyntax or
                SimpleLambdaExpressionSyntax or
                AnonymousMethodExpressionSyntax;
        }

        private static PurityAnalysisState AddCompletedStraightLineUsingDisposeFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation,
            IReturnOperation returnOperation)
        {
            var nextState = currentState;
            foreach (var usingOperation in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IUsingOperation>())
            {
                if (usingOperation.Syntax.Span.End > returnOperation.Syntax.SpanStart ||
                    !IsStraightLineUsingStatement(usingOperation.Syntax))
                {
                    continue;
                }

                nextState = AddUsingStatementDisposeFacts(nextState, usingOperation, nextState);
            }

            return nextState;
        }

        private static PurityAnalysisState AddScopeEndResourceDisposeFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation)
        {
            var nextState = currentState;
            foreach (var usingOperation in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IUsingOperation>())
            {
                if (!IsStraightLineUsingStatement(usingOperation.Syntax))
                {
                    continue;
                }

                nextState = AddUsingStatementDisposeFacts(nextState, usingOperation, nextState);
            }

            foreach (var usingDeclaration in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IUsingDeclarationOperation>())
            {
                if (!IsStraightLineUsingStatement(usingDeclaration.Syntax))
                {
                    continue;
                }

                nextState = AddUsingDeclarationDisposeFacts(nextState, usingDeclaration);
            }

            return nextState;
        }

        private static PurityAnalysisState AddStraightLineResourceActionFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation,
            SemanticModel semanticModel)
        {
            var nextState = currentState;
            foreach (var declarationGroup in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IVariableDeclarationGroupOperation>())
            {
                if (!IsStraightLineUsingStatement(declarationGroup.Syntax))
                {
                    continue;
                }

                foreach (var declaration in declarationGroup.Declarations)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        if (declarator.Initializer?.Value is { } initializer)
                        {
                            nextState = AddAssignedAliasFact(
                                nextState,
                                declarator.Symbol,
                                initializer,
                                nextState);
                            nextState = AddOwnedDisposableLocalFacts(
                                nextState,
                                declarator.Symbol,
                                initializer,
                                semanticModel.Compilation);
                        }
                    }
                }
            }

            foreach (var deconstructionAssignment in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IDeconstructionAssignmentOperation>())
            {
                if (!IsStraightLineUsingStatement(deconstructionAssignment.Syntax))
                {
                    continue;
                }

                nextState = AddDeconstructedResourceAcquisitionFacts(
                    nextState,
                    deconstructionAssignment,
                    semanticModel);
            }

            foreach (var assignmentSyntax in methodBodyOperation.Syntax.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (!IsStraightLineUsingStatement(assignmentSyntax))
                {
                    continue;
                }

                nextState = AddDeconstructedResourceAcquisitionFacts(
                    nextState,
                    assignmentSyntax,
                    semanticModel);
            }

            foreach (var invocation in ExecutionVisibility.VisibleDescendants(methodBodyOperation).OfType<IInvocationOperation>())
            {
                if (!IsStraightLineUsingStatement(invocation.Syntax))
                {
                    continue;
                }

                nextState = AddDisposeInvocationFacts(nextState, invocation, nextState);
            }

            nextState = AddFinallyResourceDisposeFacts(nextState, methodBodyOperation, semanticModel);
            return nextState;
        }

        private static PurityAnalysisState AddDeconstructedResourceAcquisitionFacts(
            PurityAnalysisState nextState,
            AssignmentExpressionSyntax assignmentSyntax,
            SemanticModel semanticModel)
        {
            if (!IsDeconstructionAssignmentSyntax(assignmentSyntax.Left))
            {
                return nextState;
            }

            foreach (var assignment in EnumerateDeconstructionSyntaxAssignments(
                         assignmentSyntax.Left,
                         assignmentSyntax.Right,
                         semanticModel))
            {
                var valueOperation = semanticModel.GetOperation(assignment.Value);
                if (valueOperation == null)
                {
                    continue;
                }

                nextState = AddAssignedAliasFact(
                    nextState,
                    assignment.Local,
                    valueOperation,
                    nextState);
                nextState = AddOwnedDisposableLocalFacts(
                    nextState,
                    assignment.Local,
                    valueOperation,
                    semanticModel.Compilation);
            }

            return nextState;
        }

        private static bool IsDeconstructionAssignmentSyntax(ExpressionSyntax target)
        {
            target = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(target);
            return target is TupleExpressionSyntax ||
                target is DeclarationExpressionSyntax { Designation: ParenthesizedVariableDesignationSyntax };
        }

        private static IEnumerable<DeconstructionSyntaxAssignmentElement> EnumerateDeconstructionSyntaxAssignments(
            ExpressionSyntax target,
            ExpressionSyntax value,
            SemanticModel semanticModel)
        {
            target = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(target);
            value = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(value);
            if (target is DeclarationExpressionSyntax declarationExpression)
            {
                foreach (var assignment in EnumerateDeconstructionDesignationAssignments(
                             declarationExpression.Designation,
                             value,
                             semanticModel))
                {
                    yield return assignment;
                }

                yield break;
            }

            if (target is TupleExpressionSyntax targetTuple &&
                value is TupleExpressionSyntax valueTuple)
            {
                var count = Math.Min(targetTuple.Arguments.Count, valueTuple.Arguments.Count);
                for (var i = 0; i < count; i++)
                {
                    foreach (var nested in EnumerateDeconstructionSyntaxAssignments(
                                 targetTuple.Arguments[i].Expression,
                                 valueTuple.Arguments[i].Expression,
                                 semanticModel))
                    {
                        yield return nested;
                    }
                }

                yield break;
            }

            if (target is IdentifierNameSyntax identifierName &&
                semanticModel.GetSymbolInfo(identifierName).Symbol is ILocalSymbol localSymbol)
            {
                yield return new DeconstructionSyntaxAssignmentElement(localSymbol, value);
            }
        }

        private static IEnumerable<DeconstructionSyntaxAssignmentElement> EnumerateDeconstructionDesignationAssignments(
            VariableDesignationSyntax designation,
            ExpressionSyntax value,
            SemanticModel semanticModel)
        {
            value = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(value);
            if (designation is SingleVariableDesignationSyntax singleVariable &&
                semanticModel.GetDeclaredSymbol(singleVariable) is ILocalSymbol localSymbol)
            {
                yield return new DeconstructionSyntaxAssignmentElement(localSymbol, value);
                yield break;
            }

            if (designation is ParenthesizedVariableDesignationSyntax parenthesized &&
                value is TupleExpressionSyntax tuple)
            {
                var count = Math.Min(parenthesized.Variables.Count, tuple.Arguments.Count);
                for (var i = 0; i < count; i++)
                {
                    foreach (var nested in EnumerateDeconstructionDesignationAssignments(
                                 parenthesized.Variables[i],
                                 tuple.Arguments[i].Expression,
                                 semanticModel))
                    {
                        yield return nested;
                    }
                }
            }
        }

        private readonly struct DeconstructionSyntaxAssignmentElement
        {
            public DeconstructionSyntaxAssignmentElement(ILocalSymbol local, ExpressionSyntax value)
            {
                Local = local;
                Value = value;
            }

            public ILocalSymbol Local { get; }

            public ExpressionSyntax Value { get; }
        }

        private static PurityAnalysisState AddDeconstructedResourceAcquisitionFacts(
            PurityAnalysisState nextState,
            IDeconstructionAssignmentOperation deconstructionAssignment,
            SemanticModel semanticModel)
        {
            foreach (var assignment in EnumerateDeconstructionAssignments(
                         deconstructionAssignment.Target,
                         deconstructionAssignment.Value))
            {
                if (TryResolveDeconstructionTargetSymbol(
                        assignment.Target,
                        nextState,
                        semanticModel) is not ILocalSymbol localSymbol)
                {
                    continue;
                }

                nextState = AddAssignedAliasFact(
                    nextState,
                    localSymbol,
                    assignment.Value,
                    nextState);
                nextState = AddOwnedDisposableLocalFacts(
                    nextState,
                    localSymbol,
                    assignment.Value,
                    semanticModel.Compilation);
            }

            return nextState;
        }

        private static PurityAnalysisState AddFinallyResourceDisposeFacts(
            PurityAnalysisState currentState,
            IOperation methodBodyOperation,
            SemanticModel semanticModel)
        {
            var nextState = currentState;
            foreach (var tryStatement in methodBodyOperation.Syntax.DescendantNodes().OfType<TryStatementSyntax>())
            {
                if (tryStatement.Finally?.Block is not { } finallyBlock)
                {
                    continue;
                }

                foreach (var invocation in finallyBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
                {
                    if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                        memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                        semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is not { } resourceSymbol ||
                        !FinallyBlockReleasesResource(finallyBlock, resourceSymbol, semanticModel))
                    {
                        continue;
                    }

                    var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
                    nextState = AddResourceDisposedFacts(
                        nextState,
                        term,
                        resourceSymbol,
                        invocation,
                        "analyzer.resource.finally.dispose",
                        "evidence.resource.finally.dispose");
                }
            }

            return nextState;
        }

        private static bool IsStraightLineUsingStatement(SyntaxNode usingSyntax)
        {
            foreach (var ancestor in usingSyntax.Ancestors())
            {
                if (ancestor is MethodDeclarationSyntax ||
                    ancestor is ConstructorDeclarationSyntax ||
                    ancestor is OperatorDeclarationSyntax ||
                    ancestor is ConversionOperatorDeclarationSyntax ||
                    ancestor is AccessorDeclarationSyntax ||
                    ancestor is LocalFunctionStatementSyntax)
                {
                    return true;
                }

                if (ancestor is IfStatementSyntax ||
                    ancestor is ElseClauseSyntax ||
                    ancestor is SwitchStatementSyntax ||
                    ancestor is SwitchSectionSyntax ||
                    ancestor is WhileStatementSyntax ||
                    ancestor is DoStatementSyntax ||
                    ancestor is ForStatementSyntax ||
                    ancestor is ForEachStatementSyntax ||
                    ancestor is ForEachVariableStatementSyntax ||
                    ancestor is TryStatementSyntax ||
                    ancestor is CatchClauseSyntax)
                {
                    return false;
                }
            }

            return true;
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
            out ImmutableDictionary<ISymbol, INamedTypeSymbol> mergedLocalConcreteTypesFromBlocks,
            out SymbolicState mergedPathStateFromBlocks)
        {
            mergedDelegateTargetsFromBlocks = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            mergedOwnedArrayFlowCapturesFromBlocks = ImmutableHashSet<CaptureId>.Empty;
            mergedOwnedLocalArraysFromBlocks = ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
            mergedLocalConcreteTypesFromBlocks = ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            mergedPathStateFromBlocks = new SymbolicState();
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
            mergedPathStateFromBlocks = MergePathStatesAcrossAll(exitBlockStates.Values.ToArray());

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

        private static bool TryCreateMissingOwnedResourceDisposalResult(
            PurityAnalysisState state,
            IMethodSymbol containingMethodSymbol,
            SemanticModel semanticModel,
            out PurityAnalysisResult result)
        {
            result = PurityAnalysisResult.Pure;

            var ownedResources = new Dictionary<SymbolicTerm, ISymbol?>();
            var releasedResources = new HashSet<SymbolicTerm>();
            foreach (var fact in state.PathState.Facts)
            {
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact)
                {
                    continue;
                }

                switch (fact.Atom)
                {
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime:
                        ownedResources[lifetime.Resource] = fact.Symbol;
                        break;
                    case SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal:
                        ownedResources[disposal.Resource] = fact.Symbol;
                        break;
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Released } lifetime:
                        releasedResources.Add(lifetime.Resource);
                        break;
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Returned } lifetime:
                        releasedResources.Add(lifetime.Resource);
                        break;
                    case SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal:
                        releasedResources.Add(disposal.Resource);
                        break;
                }
            }

            foreach (var resource in ownedResources)
            {
                if (IsResourceReleased(resource.Key, releasedResources, state, new HashSet<SymbolicTerm>()))
                {
                    continue;
                }

                if (resource.Value != null &&
                    IsOwnedResourceReleasedOnAllSyntaxPaths(containingMethodSymbol, resource.Value, semanticModel))
                {
                    continue;
                }

                var syntax = containingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                if (syntax == null)
                {
                    return false;
                }

                result = PurityAnalysisResult.Impure(
                    syntax,
                    PurityEvidence.Create(
                        "resource_missing_dispose",
                        ruleName: "ResourceLifetimeAnalysis",
                        syntaxNode: syntax,
                        symbol: resource.Value,
                    catalogSource: "symbolic_resource_lifetime"));
                return true;
            }

            if (TryFindAliasedOwnedResourceLostByReassignment(
                    containingMethodSymbol,
                    semanticModel,
                    out var aliasLeakSyntax,
                    out var aliasLeakSymbol))
            {
                result = PurityAnalysisResult.Impure(
                    aliasLeakSyntax,
                    PurityEvidence.Create(
                        "resource_missing_dispose",
                        ruleName: "ResourceLifetimeAnalysis",
                        syntaxNode: aliasLeakSyntax,
                        symbol: aliasLeakSymbol,
                        catalogSource: "symbolic_resource_lifetime.alias-preserve"));
                return true;
            }

            return false;
        }

        private static bool IsOwnedResourceReleasedOnAllSyntaxPaths(
            IMethodSymbol containingMethodSymbol,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            foreach (var syntaxReference in containingMethodSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is not MethodDeclarationSyntax { Body: { } body } methodDeclaration)
                {
                    continue;
                }

                var methodSemanticModel = semanticModel.Compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
                for (var index = 0; index < body.Statements.Count; index++)
                {
                    if (!DeclaresSymbol(body.Statements[index], resourceSymbol, methodSemanticModel))
                    {
                        continue;
                    }

                    var remainingStatements = body.Statements.Skip(index + 1).ToArray();
                    var summary = AnalyzeResourceReleaseStatements(
                        remainingStatements,
                        initiallyReleased: false,
                        endIsTerminal: true,
                        resourceSymbol,
                        methodSemanticModel);
                    return summary.AllTerminalPathsReleased;
                }
            }

            return false;
        }

        private static bool DeclaresSymbol(
            StatementSyntax statement,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            foreach (var declarator in statement.DescendantNodesAndSelf().OfType<VariableDeclaratorSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(declarator) is { } declaredSymbol &&
                    SymbolEqualityComparer.Default.Equals(declaredSymbol, resourceSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static ResourceReleasePathSummary AnalyzeResourceReleaseStatements(
            IReadOnlyList<StatementSyntax> statements,
            bool initiallyReleased,
            bool endIsTerminal,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            var allTerminalPathsReleased = true;
            var currentStates = new List<bool> { initiallyReleased };

            foreach (var statement in statements)
            {
                if (currentStates.Count == 0)
                {
                    break;
                }

                var nextStates = new List<bool>();
                foreach (var released in currentStates)
                {
                    var summary = AnalyzeResourceReleaseStatement(
                        statement,
                        released,
                        resourceSymbol,
                        semanticModel);
                    allTerminalPathsReleased &= summary.AllTerminalPathsReleased;
                    nextStates.AddRange(summary.FallthroughReleasedStates);
                }

                currentStates = nextStates;
            }

            if (endIsTerminal)
            {
                allTerminalPathsReleased &= currentStates.All(static released => released);
                currentStates.Clear();
            }

            return new ResourceReleasePathSummary(
                allTerminalPathsReleased,
                currentStates.ToImmutableArray());
        }

        private static ResourceReleasePathSummary AnalyzeResourceReleaseStatement(
            StatementSyntax statement,
            bool initiallyReleased,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            if (statement is ReturnStatementSyntax returnStatement)
            {
                return new ResourceReleasePathSummary(
                    initiallyReleased || IsReturnedSymbol(returnStatement, resourceSymbol, semanticModel),
                    ImmutableArray<bool>.Empty);
            }

            if (statement is IfStatementSyntax ifStatement)
            {
                var thenSummary = AnalyzeResourceReleaseStatements(
                    GetStatementList(ifStatement.Statement),
                    initiallyReleased,
                    endIsTerminal: false,
                    resourceSymbol,
                    semanticModel);
                var elseSummary = ifStatement.Else == null
                    ? new ResourceReleasePathSummary(true, ImmutableArray.Create(initiallyReleased))
                    : AnalyzeResourceReleaseStatements(
                        GetStatementList(ifStatement.Else.Statement),
                        initiallyReleased,
                        endIsTerminal: false,
                        resourceSymbol,
                        semanticModel);

                return new ResourceReleasePathSummary(
                    thenSummary.AllTerminalPathsReleased && elseSummary.AllTerminalPathsReleased,
                    thenSummary.FallthroughReleasedStates.AddRange(elseSummary.FallthroughReleasedStates));
            }

            if (statement is SwitchStatementSyntax switchStatement)
            {
                return AnalyzeSwitchResourceReleaseStatement(
                    switchStatement,
                    initiallyReleased,
                    resourceSymbol,
                    semanticModel);
            }

            if (statement is WhileStatementSyntax or ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax)
            {
                return new ResourceReleasePathSummary(
                    true,
                    ImmutableArray.Create(initiallyReleased));
            }

            if (statement is DoStatementSyntax doStatement)
            {
                return AnalyzeResourceReleaseStatements(
                    GetStatementList(doStatement.Statement),
                    initiallyReleased,
                    endIsTerminal: false,
                    resourceSymbol,
                    semanticModel);
            }

            if (statement is TryStatementSyntax { Finally.Block: { } finallyBlock } &&
                FinallyBlockReleasesResource(finallyBlock, resourceSymbol, semanticModel))
            {
                return new ResourceReleasePathSummary(
                    true,
                    ImmutableArray.Create(true));
            }

            var released = initiallyReleased ||
                DisposesSymbol(statement, resourceSymbol, semanticModel);
            return new ResourceReleasePathSummary(
                true,
                ImmutableArray.Create(released));
        }

        private static ResourceReleasePathSummary AnalyzeSwitchResourceReleaseStatement(
            SwitchStatementSyntax switchStatement,
            bool initiallyReleased,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            var allTerminalPathsReleased = true;
            var fallthroughStates = ImmutableArray.CreateBuilder<bool>();
            var hasDefault = false;

            foreach (var section in switchStatement.Sections)
            {
                hasDefault |= section.Labels.OfType<DefaultSwitchLabelSyntax>().Any();
                var summary = AnalyzeResourceReleaseStatements(
                    section.Statements.ToArray(),
                    initiallyReleased,
                    endIsTerminal: false,
                    resourceSymbol,
                    semanticModel);

                allTerminalPathsReleased &= summary.AllTerminalPathsReleased;
                fallthroughStates.AddRange(summary.FallthroughReleasedStates);
            }

            if (!hasDefault)
            {
                fallthroughStates.Add(initiallyReleased);
            }

            return new ResourceReleasePathSummary(
                allTerminalPathsReleased,
                fallthroughStates.ToImmutable());
        }

        private static IReadOnlyList<StatementSyntax> GetStatementList(StatementSyntax statement)
        {
            return statement is BlockSyntax block
                ? block.Statements.ToArray()
                : new[] { statement };
        }

        private static bool FinallyBlockReleasesResource(
            BlockSyntax finallyBlock,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            var summary = AnalyzeResourceReleaseStatements(
                finallyBlock.Statements.ToArray(),
                initiallyReleased: false,
                endIsTerminal: false,
                resourceSymbol,
                semanticModel);

            return summary.AllTerminalPathsReleased &&
                summary.FallthroughReleasedStates.Length > 0 &&
                summary.FallthroughReleasedStates.All(static released => released);
        }

        private static bool IsReturnedSymbol(
            ReturnStatementSyntax returnStatement,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            if (returnStatement.Expression == null ||
                semanticModel.GetSymbolInfo(returnStatement.Expression).Symbol is not { } returnedSymbol)
            {
                return false;
            }

            return GetResourceSymbolsVisibleAt(
                    resourceSymbol,
                    returnStatement,
                    semanticModel)
                .Contains(returnedSymbol);
        }

        private static bool DisposesSymbol(
            StatementSyntax statement,
            ISymbol resourceSymbol,
            SemanticModel semanticModel)
        {
            var relatedSymbols = GetResourceSymbolsVisibleAt(
                resourceSymbol,
                statement,
                semanticModel);
            foreach (var invocation in statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                    semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is not { } disposedSymbol)
                {
                    continue;
                }

                if (relatedSymbols.Contains(disposedSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<ISymbol> GetResourceSymbolsVisibleAt(
            ISymbol resourceSymbol,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel)
        {
            var containingBlock = observationSyntax
                .AncestorsAndSelf()
                .OfType<BlockSyntax>()
                .LastOrDefault();
            if (containingBlock == null)
            {
                return new HashSet<ISymbol>(SymbolEqualityComparer.Default)
                {
                    resourceSymbol
                };
            }

            return GetRelatedLocalAliases(
                resourceSymbol,
                observationSyntax,
                containingBlock,
                semanticModel,
                CancellationToken.None);
        }

        private readonly struct ResourceReleasePathSummary
        {
            public ResourceReleasePathSummary(
                bool allTerminalPathsReleased,
                ImmutableArray<bool> fallthroughReleasedStates)
            {
                AllTerminalPathsReleased = allTerminalPathsReleased;
                FallthroughReleasedStates = fallthroughReleasedStates;
            }

            public bool AllTerminalPathsReleased { get; }

            public ImmutableArray<bool> FallthroughReleasedStates { get; }
        }

        private static bool TryFindAliasedOwnedResourceLostByReassignment(
            IMethodSymbol containingMethodSymbol,
            SemanticModel semanticModel,
            out SyntaxNode syntax,
            out ISymbol? symbol)
        {
            foreach (var syntaxReference in containingMethodSymbol.DeclaringSyntaxReferences)
            {
                if (syntaxReference.GetSyntax() is not MethodDeclarationSyntax methodDeclaration ||
                    methodDeclaration.Body == null)
                {
                    continue;
                }

                var methodSemanticModel = semanticModel.Compilation.GetSemanticModel(methodDeclaration.SyntaxTree);
                foreach (var declarator in methodDeclaration.Body.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (declarator.Initializer?.Value == null ||
                        methodSemanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol resourceLocal ||
                        methodSemanticModel.GetOperation(declarator.Initializer.Value) is not { } initializerOperation ||
                        !IsOwnedDisposableObjectCreationValue(initializerOperation, methodSemanticModel.Compilation))
                    {
                        continue;
                    }

                    var aliases = methodDeclaration.Body.DescendantNodes()
                        .OfType<VariableDeclaratorSyntax>()
                        .Where(aliasDeclarator => aliasDeclarator.SpanStart > declarator.SpanStart &&
                                                  aliasDeclarator.Initializer?.Value != null &&
                                                  methodSemanticModel.GetSymbolInfo(aliasDeclarator.Initializer.Value).Symbol is ILocalSymbol initializerSymbol &&
                                                  SymbolEqualityComparer.Default.Equals(initializerSymbol, resourceLocal))
                        .Select(aliasDeclarator => methodSemanticModel.GetDeclaredSymbol(aliasDeclarator))
                        .OfType<ILocalSymbol>()
                        .ToArray();
                    if (aliases.Length == 0)
                    {
                        continue;
                    }

                    var reassignment = FindLocalReassignmentAfter(
                        resourceLocal,
                        declarator.SpanStart,
                        methodDeclaration.Body,
                        methodSemanticModel);
                    if (reassignment == null)
                    {
                        continue;
                    }

                    if (WasAnySymbolDisposedInSpan(
                            aliases.Prepend<ISymbol>(resourceLocal),
                            methodDeclaration.Body,
                            declarator.SpanStart,
                            reassignment.SpanStart,
                            methodSemanticModel) ||
                        WasAnySymbolDisposedInSpan(
                            aliases,
                            methodDeclaration.Body,
                            reassignment.SpanStart,
                            methodDeclaration.Body.Span.End,
                            methodSemanticModel) ||
                        IsAnySymbolReturnedAfter(
                            aliases,
                            reassignment.SpanStart,
                            methodDeclaration.Body,
                            methodSemanticModel))
                    {
                        continue;
                    }

                    syntax = methodDeclaration;
                    symbol = aliases[0];
                    return true;
                }
            }

            syntax = null!;
            symbol = null;
            return false;
        }

        private static AssignmentExpressionSyntax? FindLocalReassignmentAfter(
            ILocalSymbol localSymbol,
            int spanStart,
            SyntaxNode searchRoot,
            SemanticModel semanticModel)
        {
            foreach (var assignment in searchRoot.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.SpanStart <= spanStart ||
                    semanticModel.GetSymbolInfo(assignment.Left).Symbol is not ILocalSymbol assignedLocal ||
                    !SymbolEqualityComparer.Default.Equals(assignedLocal, localSymbol))
                {
                    continue;
                }

                return assignment;
            }

            return null;
        }

        private static bool WasAnySymbolDisposedInSpan(
            IEnumerable<ISymbol> symbols,
            SyntaxNode searchRoot,
            int spanStart,
            int spanEnd,
            SemanticModel semanticModel)
        {
            var symbolSet = new HashSet<ISymbol>(symbols, SymbolEqualityComparer.Default);
            foreach (var invocation in searchRoot.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.SpanStart < spanStart ||
                    invocation.SpanStart >= spanEnd ||
                    invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                    semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is not { } disposedSymbol)
                {
                    continue;
                }

                if (symbolSet.Contains(disposedSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAnySymbolReturnedAfter(
            IEnumerable<ISymbol> symbols,
            int spanStart,
            SyntaxNode searchRoot,
            SemanticModel semanticModel)
        {
            var symbolSet = new HashSet<ISymbol>(symbols, SymbolEqualityComparer.Default);
            foreach (var returnStatement in searchRoot.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                if (returnStatement.SpanStart <= spanStart ||
                    returnStatement.Expression == null ||
                    semanticModel.GetSymbolInfo(returnStatement.Expression).Symbol is not { } returnedSymbol)
                {
                    continue;
                }

                if (symbolSet.Contains(returnedSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsResourceReleased(
            SymbolicTerm resource,
            HashSet<SymbolicTerm> releasedResources,
            PurityAnalysisState state,
            HashSet<SymbolicTerm> visitedTerms)
        {
            if (releasedResources.Contains(resource))
            {
                return true;
            }

            if (!visitedTerms.Add(resource))
            {
                return false;
            }

            foreach (var aliasTerm in EnumerateSymbolicAliasTerms(resource, state))
            {
                if (IsResourceReleased(aliasTerm, releasedResources, state, visitedTerms))
                {
                    return true;
                }
            }

            return false;
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

            var blockSourceNode = block.Operations.FirstOrDefault()?.Syntax ?? block.BranchValue?.Syntax;
            if (stateBefore.PathConditions.Length > 0 &&
                ArePathConditionsUnsatisfiable(stateBefore, stateBefore.PathConditions, smtAnalysis, blockSourceNode))
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
                        if (IsImpurityProvenUnreachable(valResult, semanticModel, smtAnalysis))
                        {
                            continue;
                        }

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
                    if (IsImpurityProvenUnreachable(opResult, semanticModel, smtAnalysis))
                    {
                        continue;
                    }

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
                TryCreateThrowBranchImpurity(block.BranchValue, ruleContext, currentStateInBlock, out var throwBranchResult))
            {
                currentStateInBlock = currentStateInBlock.WithImpurity(throwBranchResult, throwBranchResult.ImpureSyntaxNode ?? block.BranchValue.Syntax);
            }
            else if (!currentStateInBlock.HasPotentialImpurity &&
                block.BranchValue != null &&
                ShouldAnalyzeStateSensitiveBranchValue(block.BranchValue.Syntax))
            {
                LogDebug($"    [ATF Block {block.Ordinal}] Checking Branch Value Kind: {block.BranchValue.Kind}, Syntax: {block.BranchValue.Syntax.ToString().Replace("\r\n", " ").Replace("\n", " ")}");

                var branchValueResult = CheckSingleOperation(block.BranchValue, ruleContext, currentStateInBlock);
                if (!branchValueResult.IsPure)
                {
                    if (!IsImpurityProvenUnreachable(branchValueResult, semanticModel, smtAnalysis))
                    {
                        LogDebug($"ApplyTransferFunction IMPURE DETECTED in Block #{block.Ordinal} by Branch Value: {block.BranchValue.Kind} ({block.BranchValue.Syntax})");
                        currentStateInBlock = currentStateInBlock.WithImpurity(branchValueResult, block.BranchValue.Syntax);
                    }
                }
                else
                {
                    currentStateInBlock = UpdateDelegateMapForOperation(block.BranchValue, ruleContext, currentStateInBlock);
                }
            }

            LogDebug($"ApplyTransferFunction END for Block #{block.Ordinal} - Final State: Impure={currentStateInBlock.HasPotentialImpurity}");
            return currentStateInBlock;
        }

        private static bool TryCreateThrowBranchImpurity(
            IOperation branchValue,
            Rules.PurityAnalysisContext context,
            PurityAnalysisState currentState,
            out PurityAnalysisResult result)
        {
            result = PurityAnalysisResult.Pure;

            var throwSyntax = branchValue.Syntax.FirstAncestorOrSelf<ThrowStatementSyntax>() ??
                (SyntaxNode?)branchValue.Syntax.FirstAncestorOrSelf<ThrowExpressionSyntax>();
            if (throwSyntax == null)
            {
                return false;
            }

            var exceptionResult = CheckSingleOperation(branchValue, context, currentState);
            if (!exceptionResult.IsPure)
            {
                result = exceptionResult;
                return true;
            }

            result = PurityAnalysisResult.Impure(
                throwSyntax,
                PurityEvidence.Create(
                    "throw",
                    ruleName: "ThrowOperationPurityRule",
                    syntaxNode: throwSyntax,
                    operationKindOverride: OperationKind.Throw.ToString()));
            return true;
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
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
            SmtAnalysisService smtAnalysis)
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
                null,
                smtAnalysis);

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

        private static bool ShouldAnalyzeStateSensitiveBranchValue(SyntaxNode branchValueSyntax)
        {
            return ShouldAnalyzeExplicitConditionBranchValue(branchValueSyntax) ||
                IsReturnExpressionBranchValue(branchValueSyntax);
        }

        private static bool IsReturnExpressionBranchValue(SyntaxNode branchValueSyntax)
        {
            foreach (var ancestor in branchValueSyntax.AncestorsAndSelf())
            {
                if (ancestor is ReturnStatementSyntax)
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
            var nextPathState = currentState.PathState;
            var addedSymbolicBranchAssumption = SymbolicReachabilityService.TryCollectBranchState(
                currentState.PathState,
                expressionSyntax,
                takeConditionalSuccessor,
                semanticModel,
                CancellationToken.None,
                out var symbolicBranchState,
                currentState.GetSmtSymbolVersion);
            if (addedSymbolicBranchAssumption)
            {
                nextPathState = symbolicBranchState;
            }

            var addedBranchAssumptions = SymbolicReachabilityService.TryAddBranchConditionFacts(
                expressionSyntax,
                takeConditionalSuccessor,
                semanticModel,
                CancellationToken.None,
                nextPathConditionsBuilder,
                currentState.GetSmtSymbolVersion,
                addTranslatedFormulaFallback: true);

            SmtFormula branchFormula;
            if (TryTranslateBranchValueToFormula(branchValue, currentState, out var operationFormula) &&
                operationFormula != null)
            {
                branchFormula = operationFormula;
            }
            else if (TryEncodeSymbolicBranchFormula(
                         currentState.PathState,
                         symbolicBranchState,
                         addedSymbolicBranchAssumption,
                         out var symbolicBranchFormula) &&
                     symbolicBranchFormula != null)
            {
                branchFormula = symbolicBranchFormula;
            }
            else
            {
                if (addedBranchAssumptions)
                {
                    var partialPathConditions = nextPathConditionsBuilder.ToImmutable();
                    if (ArePathConditionsUnsatisfiable(currentState, partialPathConditions, nextPathState, smtAnalysis, expressionSyntax))
                    {
                        return false;
                    }

                    successorState = currentState.WithPathConditionsAndState(partialPathConditions, nextPathState);
                }
                else if (addedSymbolicBranchAssumption)
                {
                    successorState = currentState.WithPathConditionsAndState(
                        currentState.PathConditions,
                        nextPathState);
                }

                return true;
            }

            var edgeFormula = takeConditionalSuccessor
                ? branchFormula
                : SmtFormulaFactory.CreateNot(branchFormula);
            if (!addedBranchAssumptions)
            {
                nextPathConditionsBuilder.Add(edgeFormula);
                nextPathState = AddSymbolicConditionToState(
                    nextPathState,
                    edgeFormula,
                    expressionSyntax,
                    "analyzer.branch.edge",
                    "analyzer.branch.edge");
            }

            var nextPathConditions = nextPathConditionsBuilder.ToImmutable();
            if (ArePathConditionsUnsatisfiable(currentState, nextPathConditions, nextPathState, smtAnalysis, expressionSyntax))
            {
                return false;
            }

            successorState = currentState.WithPathConditionsAndState(nextPathConditions, nextPathState);
            return true;
        }

        private static bool TryEncodeSymbolicBranchFormula(
            SymbolicState originalState,
            SymbolicState branchState,
            bool hasBranchAssumption,
            out SmtFormula? formula)
        {
            formula = null;
            if (!hasBranchAssumption ||
                branchState.PathConditions.Length <= originalState.PathConditions.Length)
            {
                return false;
            }

            var branchCondition = branchState.PathConditions[branchState.PathConditions.Length - 1];
            return SymbolicIrFormulaEncoder.TryEncode(branchCondition, out formula);
        }

        internal static bool TryCreateBranchAssumptionState(
            PurityAnalysisState currentState,
            IOperation? condition,
            SemanticModel semanticModel,
            bool branchWhenTrue,
            SmtAnalysisService smtAnalysis,
            out PurityAnalysisState branchState)
        {
            return TryCreateSuccessorState(
                currentState,
                condition,
                semanticModel,
                branchWhenTrue,
                smtAnalysis,
                out branchState);
        }

        internal static bool TryGetKnownConditionValueFromPathFacts(
            PurityAnalysisState currentState,
            IOperation? condition,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis,
            out bool value)
        {
            value = false;

            if (condition?.ConstantValue.HasValue == true &&
                condition.ConstantValue.Value is bool constantBool)
            {
                value = constantBool;
                return true;
            }

            condition = SkipImplicitConversions(condition);
            if (condition?.Syntax is not ExpressionSyntax expressionSyntax)
            {
                return false;
            }

            if (IsBranchAssumptionUnsatisfiable(currentState, expressionSyntax, branchWhenTrue: true, semanticModel, smtAnalysis))
            {
                value = false;
                return true;
            }

            if (IsBranchAssumptionUnsatisfiable(currentState, expressionSyntax, branchWhenTrue: false, semanticModel, smtAnalysis))
            {
                value = true;
                return true;
            }

            return false;
        }

        internal static bool TryCreateReferenceNullAssumptionState(
            PurityAnalysisState currentState,
            IOperation? value,
            bool isNull,
            SmtAnalysisService smtAnalysis,
            out PurityAnalysisState branchState)
        {
            branchState = currentState;

            value = SkipImplicitConversions(value);
            if (value?.ConstantValue.HasValue == true)
            {
                return (value.ConstantValue.Value == null) == isNull;
            }

            if (!TryCreateReferenceVariableFormula(value, currentState, out var valueFormula))
            {
                return true;
            }

            var nullComparison = SmtFormulaFactory.CreateReferenceNullComparison(valueFormula, isNull);
            var nextPathConditions = currentState.PathConditions.Add(nullComparison);
            var nextPathState = TryCreateReferenceNullPathState(
                currentState,
                value,
                valueFormula,
                isNull,
                out var symbolicNullState)
                    ? symbolicNullState
                    : currentState.PathState;
            if (ArePathConditionsUnsatisfiable(currentState, nextPathConditions, nextPathState, smtAnalysis, value?.Syntax))
            {
                return false;
            }

            branchState = currentState.WithPathConditionsAndState(nextPathConditions, nextPathState);
            return true;
        }

        private static bool TryCreateReferenceNullPathState(
            PurityAnalysisState currentState,
            IOperation? value,
            SmtFormula valueFormula,
            bool isNull,
            out SymbolicState pathState)
        {
            pathState = currentState.PathState;
            value = SkipImplicitConversions(value);
            if (valueFormula is not SmtVariable variable ||
                value?.Syntax is not ExpressionSyntax syntax)
            {
                return false;
            }

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    isNull ? SymbolicRelationOperator.Equal : SymbolicRelationOperator.NotEqual,
                    new SymbolicVariableTerm(variable.Name, SmtValueKind.Reference),
                    new SymbolicNullTerm()),
                syntax,
                "analyzer.null_assumption",
                evidenceKey: isNull ? "analyzer.path.null" : "analyzer.path.not_null");
            pathState = currentState.PathState.AddPathCondition(new SymbolicFactCondition(fact));
            return true;
        }

        internal static bool TryGetKnownReferenceNullValueFromPathFacts(
            PurityAnalysisState currentState,
            IOperation? value,
            SmtAnalysisService smtAnalysis,
            out bool isNull)
        {
            isNull = false;

            value = SkipImplicitConversions(value);
            if (value?.ConstantValue.HasValue == true)
            {
                isNull = value.ConstantValue.Value == null;
                return true;
            }

            if (!TryCreateReferenceVariableFormula(value, currentState, out var valueFormula))
            {
                return false;
            }

            var nullPathConditions = currentState.PathConditions.Add(
                SmtFormulaFactory.CreateReferenceNullComparison(valueFormula, isNull: true));
            var nullPathState = TryCreateReferenceNullPathState(
                currentState,
                value,
                valueFormula,
                isNull: true,
                out var symbolicNullProbeState)
                    ? symbolicNullProbeState
                    : currentState.PathState;
            if (ArePathConditionsUnsatisfiable(currentState, nullPathConditions, nullPathState, smtAnalysis, value?.Syntax))
            {
                isNull = false;
                return true;
            }

            var nonNullPathConditions = currentState.PathConditions.Add(
                SmtFormulaFactory.CreateReferenceNullComparison(valueFormula, isNull: false));
            var nonNullPathState = TryCreateReferenceNullPathState(
                currentState,
                value,
                valueFormula,
                isNull: false,
                out var symbolicNonNullProbeState)
                    ? symbolicNonNullProbeState
                    : currentState.PathState;
            if (ArePathConditionsUnsatisfiable(currentState, nonNullPathConditions, nonNullPathState, smtAnalysis, value?.Syntax))
            {
                isNull = true;
                return true;
            }

            return false;
        }

        private static bool IsBranchAssumptionUnsatisfiable(
            PurityAnalysisState currentState,
            ExpressionSyntax expressionSyntax,
            bool branchWhenTrue,
            SemanticModel semanticModel,
            SmtAnalysisService smtAnalysis)
        {
            var pathState = currentState.PathState;
            if (SymbolicReachabilityService.TryCollectBranchState(
                    currentState.PathState,
                    expressionSyntax,
                    branchWhenTrue,
                    semanticModel,
                    CancellationToken.None,
                    out var branchPathState,
                    currentState.GetSmtSymbolVersion))
            {
                pathState = branchPathState;
            }

            var pathConditionsBuilder = currentState.PathConditions.ToBuilder();
            var addedBranchAssumptions = SymbolicReachabilityService.TryAddBranchConditionFacts(
                expressionSyntax,
                branchWhenTrue,
                semanticModel,
                CancellationToken.None,
                pathConditionsBuilder,
                currentState.GetSmtSymbolVersion,
                collectDomainFactsBeforeBranchAssumptions: true,
                addTranslatedFormulaAlways: true);

            return addedBranchAssumptions &&
                ArePathConditionsUnsatisfiable(currentState, pathConditionsBuilder.ToImmutable(), pathState, smtAnalysis, expressionSyntax);
        }

        private static bool ArePathConditionsUnsatisfiable(
            PurityAnalysisState currentState,
            ImmutableArray<SmtFormula> pathConditions,
            SmtAnalysisService smtAnalysis,
            SyntaxNode? sourceNode = null)
        {
            return ArePathConditionsUnsatisfiable(currentState, pathConditions, currentState.PathState, smtAnalysis, sourceNode);
        }

        private static bool ArePathConditionsUnsatisfiable(
            PurityAnalysisState currentState,
            ImmutableArray<SmtFormula> pathConditions,
            SymbolicState pathState,
            SmtAnalysisService smtAnalysis,
            SyntaxNode? sourceNode = null)
        {
            if (!pathState.PathConditions.IsDefaultOrEmpty || !pathState.Facts.IsDefaultOrEmpty)
            {
                var proof = SymbolicReachabilityService.ClassifyStateFeasibility(pathState, smtAnalysis);
                if (proof.Info.Status == SymbolicProofStatus.Unreachable)
                {
                    return true;
                }
            }

            var proofPathConditions = AppendDefinitelyNullFacts(currentState, pathConditions);
            return SymbolicReachabilityService.PathConditionsAreUnsatisfiableWithOptionalIrFirst(
                proofPathConditions,
                sourceNode,
                smtAnalysis,
                "analyzer.path.condition",
                "analyzer-path-condition");
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
                formula = SmtFormulaFactory.CreateReferenceNullComparison(operandFormula, isNull: true);
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
                formula = SmtFormulaFactory.CreateReferenceVariable(GetSmtVariableName(localSymbol, currentState.GetSmtSymbolVersion));
                return true;
            }

            if (TryResolveTrackedSymbol(operation, currentState) is IParameterSymbol parameterSymbol &&
                parameterSymbol.Type?.IsReferenceType == true)
            {
                formula = SmtFormulaFactory.CreateReferenceVariable(GetSmtVariableName(parameterSymbol, currentState.GetSmtSymbolVersion));
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

                builder.Add(SmtFormulaFactory.CreateReferenceNullComparison(
                    SmtFormulaFactory.CreateReferenceVariable(GetSmtVariableName(localSymbol, currentState.GetSmtSymbolVersion)),
                    isNull: true));
            }

            return builder.ToImmutable();
        }

        private static string GetSmtVariableName(ISymbol symbol, Func<ISymbol, int>? getSymbolVersion = null)
        {
            var name = SymbolicFactFactory.GetSmtVariableName(symbol);
            var version = getSymbolVersion?.Invoke(symbol.OriginalDefinition) ?? 0;
            return version > 0
                ? name + "@v" + version.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        internal static PurityAnalysisResult CheckSingleOperation(IOperation operation, Rules.PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            LogDebug($"    [CSO] Enter CheckSingleOperation for Kind: {operation.Kind}, Syntax: '{operation.Syntax.ToString().Trim()}'");
            LogDebug($"    [CSO] Current DFA State: Impure={currentState.HasPotentialImpurity}, MapCount={currentState.DelegateTargetMap.Count}");

            if (currentState.PathConditions.Length > 0 &&
                ArePathConditionsUnsatisfiable(currentState, currentState.PathConditions, context.SmtAnalysis, operation.Syntax))
            {
                LogDebug($"    [CSO] Current SMT path conditions are unsatisfiable. Treating as Pure: {operation.Syntax}");
                return PurityAnalysisResult.Pure;
            }

            if (currentState.PathConditions.Length > 0 &&
                ExecutionVisibility.IsEvaluationPathUnsatisfiableUsingSmt(
                    operation.Syntax,
                    context.SemanticModel,
                    CancellationToken.None,
                    currentState.PathConditions,
                    currentState.GetSmtSymbolVersion,
                    context.SmtAnalysis))
            {
                LogDebug($"    [CSO] Operation evaluation path is SMT-unreachable in current state. Treating as Pure: {operation.Syntax}");
                return PurityAnalysisResult.Pure;
            }

            if (ExecutionVisibility.IsInStaticallyUnreachableBranchUsingSmt(
                    operation.Syntax,
                    context.SemanticModel,
                    CancellationToken.None,
                    context.SmtAnalysis))
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
                context.PurityCache,
                context.SmtAnalysis);
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

                    nextState = AddCallerVisibleMutationFact(
                        nextState,
                        targetOperation,
                        currentState,
                        operationToTrack.Syntax);

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

                else if (operationToTrack is IDeconstructionAssignmentOperation deconstructionAssignmentOperation)
                {
                    nextState = ApplyDeconstructionAssignmentStateUpdates(
                        nextState,
                        deconstructionAssignmentOperation,
                        currentState,
                        context);
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
                    nextState = AddCallerVisibleMutationFact(
                        nextState,
                        targetOperation,
                        currentState,
                        operationToTrack.Syntax);
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

                else if (operationToTrack is IVariableDeclaratorOperation variableDeclaratorOperation &&
                         variableDeclaratorOperation.Initializer?.Value is { } variableInitializer)
                {
                    nextState = AddDeclaredBorrowFact(
                        nextState,
                        variableDeclaratorOperation.Symbol,
                        variableInitializer,
                        context.SemanticModel);
                }

                else if (operationToTrack is IIncrementOrDecrementOperation incrementOrDecrementOperation)
                {
                    nextState = AddCallerVisibleMutationFact(
                        nextState,
                        incrementOrDecrementOperation.Target,
                        currentState,
                        operationToTrack.Syntax);
                }

                else if (operationToTrack is IInvocationOperation invocationOperation)
                {
                    nextState = AddDisposeInvocationFacts(nextState, invocationOperation, currentState);

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

                else if (operationToTrack is IReturnOperation returnOperation)
                {
                    nextState = AddReturnedOwnedResourceFacts(nextState, returnOperation, currentState);
                }

                  else if (operationToTrack is IUsingOperation usingOperation)
                {
                    nextState = AddUsingStatementDisposeFacts(nextState, usingOperation, currentState);
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
                        nextState = nextState.WithOwnedArrayFlowCapture(flowCaptureOperation.Id, flowCaptureOperation.Syntax);
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
                                    nextState = AddOwnedLocalArrayFacts(
                                        nextState,
                                        declaredSymbol,
                                        initializerValue);
                                }
                                else
                                {
                                    nextState = nextState.WithoutOwnedLocalArray(declaredSymbol);
                                }

                                nextState = AddFreshMutableObjectFacts(
                                    nextState,
                                    declaredSymbol,
                                    initializerValue);

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
                                nextState = AddAssignedAliasFact(
                                    nextState,
                                    declaredSymbol,
                                    initializerValue,
                                    nextState);
                                nextState = AddDeclaredBorrowFact(
                                    nextState,
                                    declaredSymbol,
                                    initializerValue,
                                    context.SemanticModel);
                                if (!IsUsingResourceDeclarator(declarator))
                                {
                                    nextState = AddOwnedDisposableLocalFacts(
                                        nextState,
                                        declaredSymbol,
                                        initializerValue,
                                        context.SemanticModel.Compilation);
                                }
                            }
                        }
                    }
                }


            return nextState;
        }

        private static PurityAnalysisState ApplyDeconstructionAssignmentStateUpdates(
            PurityAnalysisState nextState,
            IDeconstructionAssignmentOperation deconstructionAssignmentOperation,
            PurityAnalysisState currentState,
            Rules.PurityAnalysisContext context)
        {
            foreach (var assignment in EnumerateDeconstructionAssignments(
                         deconstructionAssignmentOperation.Target,
                         deconstructionAssignmentOperation.Value))
            {
                var targetSymbol = TryResolveDeconstructionTargetSymbol(
                    assignment.Target,
                    currentState,
                    context.SemanticModel);
                if (targetSymbol is ILocalSymbol localSymbol)
                {
                    var writtenLocalSymbols = EnumerateWrittenLocalSymbols(localSymbol, context).ToArray();
                    nextState = ApplyWrittenLocalStateUpdates(
                        nextState,
                        writtenLocalSymbols,
                        assignment.Value,
                        currentState,
                        context.SemanticModel,
                        context.SemanticModel.Compilation);
                    nextState = ApplyAssignedDelegateTargets(
                        nextState,
                        targetSymbol,
                        assignment.Target.Type,
                        assignment.Value,
                        writtenLocalSymbols,
                        currentState,
                        "[ATF-DEL-DECONSTRUCT]",
                        "deconstructed value targets are unresolved");
                }
                else if (targetSymbol is IParameterSymbol parameterSymbol)
                {
                    nextState = nextState.WithIncrementedSmtSymbolVersion(parameterSymbol);
                    nextState = AddAssignedValueFact(
                        nextState,
                        parameterSymbol,
                        assignment.Value,
                        currentState,
                        context.SemanticModel);
                }

                nextState = AddCallerVisibleMutationFact(
                    nextState,
                    assignment.Target,
                    currentState,
                    deconstructionAssignmentOperation.Syntax);
            }

            return nextState;
        }

        private static IEnumerable<DeconstructionAssignmentElement> EnumerateDeconstructionAssignments(
            IOperation target,
            IOperation value)
        {
            target = SkipImplicitConversions(target) ?? target;
            value = SkipImplicitConversions(value) ?? value;
            if (target is ITupleOperation targetTuple &&
                value is ITupleOperation valueTuple)
            {
                var count = Math.Min(targetTuple.Elements.Length, valueTuple.Elements.Length);
                for (var i = 0; i < count; i++)
                {
                    foreach (var nested in EnumerateDeconstructionAssignments(
                                 targetTuple.Elements[i],
                                 valueTuple.Elements[i]))
                    {
                        yield return nested;
                    }
                }

                yield break;
            }

            yield return new DeconstructionAssignmentElement(target, value);
        }

        private static ISymbol? TryResolveDeconstructionTargetSymbol(
            IOperation targetOperation,
            PurityAnalysisState currentState,
            SemanticModel semanticModel)
        {
            targetOperation = SkipImplicitConversions(targetOperation) ?? targetOperation;
            if (TryResolveTrackedSymbol(targetOperation, currentState) is { } trackedSymbol)
            {
                return trackedSymbol;
            }

            if (targetOperation is IDeclarationExpressionOperation declarationExpression)
            {
                if (TryResolveTrackedSymbol(declarationExpression.Expression, currentState) is { } declaredTrackedSymbol)
                {
                    return declaredTrackedSymbol;
                }

                if (declarationExpression.Syntax is DeclarationExpressionSyntax { Designation: SingleVariableDesignationSyntax designation } &&
                    semanticModel.GetDeclaredSymbol(designation) is { } declaredSymbol)
                {
                    return declaredSymbol;
                }
            }

            if (targetOperation.Syntax is SingleVariableDesignationSyntax singleVariable &&
                semanticModel.GetDeclaredSymbol(singleVariable) is { } singleVariableSymbol)
            {
                return singleVariableSymbol;
            }

            return targetOperation.Syntax is IdentifierNameSyntax identifier
                ? semanticModel.GetSymbolInfo(identifier).Symbol
                : null;
        }

        private readonly struct DeconstructionAssignmentElement
        {
            public DeconstructionAssignmentElement(IOperation target, IOperation value)
            {
                Target = target;
                Value = value;
            }

            public IOperation Target { get; }

            public IOperation Value { get; }
        }

        private static PurityAnalysisState AddReturnedOwnedResourceFacts(
            PurityAnalysisState nextState,
            IReturnOperation returnOperation,
            PurityAnalysisState currentState)
        {
            if (returnOperation.ReturnedValue == null ||
                TryResolveTrackedSymbol(returnOperation.ReturnedValue, currentState) is not { } resourceSymbol ||
                !HasSymbolicOwnedFactForSymbol(resourceSymbol, currentState))
            {
                return nextState;
            }

            var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
            var returnedFact = SymbolicOwnershipFactFactory.CreateReturnedOwnership(
                term,
                returnOperation.ReturnedValue.Syntax,
                "analyzer.resource.returned",
                resourceSymbol,
                "evidence.resource.returned");
            var lifetimeFact = SymbolicOwnershipFactFactory.CreateResourceLifetime(
                term,
                SymbolicResourceLifetimeState.Returned,
                returnOperation.ReturnedValue.Syntax,
                "analyzer.resource.returned.lifetime",
                resourceSymbol,
                "evidence.resource.returned");

            return nextState.WithPathConditionsAndState(
                nextState.PathConditions,
                nextState.PathState.AddFact(returnedFact).AddFact(lifetimeFact));
        }

        private static PurityAnalysisState AddDisposeInvocationFacts(
            PurityAnalysisState nextState,
            IInvocationOperation invocationOperation,
            PurityAnalysisState currentState)
        {
            if (!IsParameterlessDisposeInvocation(invocationOperation) ||
                invocationOperation.Instance == null ||
                TryResolveTrackedSymbol(invocationOperation.Instance, currentState) is not { } resourceSymbol)
            {
                return nextState;
            }

            var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
            return AddResourceDisposedFacts(
                nextState,
                term,
                resourceSymbol,
                invocationOperation.Syntax,
                "analyzer.resource.dispose",
                "evidence.resource.dispose");
        }

        private static PurityAnalysisState AddUsingStatementDisposeFacts(
            PurityAnalysisState nextState,
            IUsingOperation usingOperation,
            PurityAnalysisState currentState)
        {
            foreach (var resourceSymbol in EnumerateUsingStatementDisposedSymbols(usingOperation, currentState))
            {
                var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
                nextState = AddResourceDisposedFacts(
                    nextState,
                    term,
                    resourceSymbol,
                    usingOperation.Syntax,
                    "analyzer.resource.using.dispose",
                    "evidence.resource.using.dispose");
            }

            return nextState;
        }

        private static PurityAnalysisState AddUsingDeclarationDisposeFacts(
            PurityAnalysisState nextState,
            IUsingDeclarationOperation usingDeclaration)
        {
            foreach (var resourceSymbol in EnumerateUsingDeclarationDisposedSymbols(usingDeclaration))
            {
                var term = CreateSymbolicReferenceTerm(resourceSymbol, nextState);
                nextState = AddResourceDisposedFacts(
                    nextState,
                    term,
                    resourceSymbol,
                    usingDeclaration.Syntax,
                    "analyzer.resource.using-declaration.dispose",
                    "evidence.resource.using-declaration.dispose");
            }

            return nextState;
        }

        private static IEnumerable<ISymbol> EnumerateUsingDeclarationDisposedSymbols(
            IUsingDeclarationOperation usingDeclaration)
        {
            foreach (var declaration in usingDeclaration.DeclarationGroup.Declarations)
            {
                foreach (var declarator in declaration.Declarators)
                {
                    yield return declarator.Symbol;
                }
            }
        }

        private static IEnumerable<ISymbol> EnumerateUsingStatementDisposedSymbols(
            IUsingOperation usingOperation,
            PurityAnalysisState currentState)
        {
            var resourceOperation = usingOperation.Resources;
            if (TryResolveTrackedSymbol(resourceOperation, currentState) is { } resourceSymbol)
            {
                yield return resourceSymbol;
                yield break;
            }

            if (resourceOperation is IVariableDeclarationGroupOperation declarationGroup)
            {
                foreach (var declaration in declarationGroup.Declarations)
                {
                    foreach (var declarator in declaration.Declarators)
                    {
                        yield return declarator.Symbol;
                    }
                }
            }
            else if (resourceOperation is IVariableDeclarationOperation variableDeclaration)
            {
                foreach (var declarator in variableDeclaration.Declarators)
                {
                    yield return declarator.Symbol;
                }
            }
        }

        private static PurityAnalysisState AddResourceDisposedFacts(
            PurityAnalysisState nextState,
            SymbolicTerm term,
            ISymbol resourceSymbol,
            SyntaxNode syntax,
            string provenance,
            string evidenceKey)
        {
            var disposedFact = SymbolicOwnershipFactFactory.CreateDisposal(
                term,
                SymbolicDisposalState.Disposed,
                syntax,
                provenance,
                resourceSymbol,
                evidenceKey);
            var releasedFact = SymbolicOwnershipFactFactory.CreateResourceLifetime(
                term,
                SymbolicResourceLifetimeState.Released,
                syntax,
                provenance + ".lifetime",
                resourceSymbol,
                evidenceKey);

            return nextState.WithPathConditionsAndState(
                nextState.PathConditions,
                nextState.PathState.AddFact(disposedFact).AddFact(releasedFact));
        }

        private static PurityAnalysisState AddCallerVisibleMutationFact(
            PurityAnalysisState nextState,
            IOperation targetOperation,
            PurityAnalysisState currentState,
            SyntaxNode syntax)
        {
            if (!TryCreateCallerVisibleMutationTerm(targetOperation, currentState, out var term, out var symbol))
            {
                return nextState;
            }

            var mutationFact = SymbolicOwnershipFactFactory.CreateMutation(
                term,
                callerVisible: true,
                syntax,
                "analyzer.mutation.caller-visible",
                symbol,
                "evidence.mutation.caller-visible");

            return nextState.WithPathConditionsAndState(
                nextState.PathConditions,
                nextState.PathState.AddFact(mutationFact));
        }

        internal static bool TryCreateCallerVisibleMutationEvidence(
            IOperation operation,
            IOperation targetOperation,
            PurityAnalysisState currentState,
            string ruleName,
            out PurityEvidence evidence)
        {
            if (!TryCreateCallerVisibleMutationTerm(targetOperation, currentState, out var term, out var symbol))
            {
                evidence = default;
                return false;
            }

            var mutationFact = SymbolicOwnershipFactFactory.CreateMutation(
                term,
                callerVisible: true,
                targetOperation.Syntax,
                "analyzer.mutation.caller-visible",
                symbol,
                "evidence.mutation.caller-visible");
            if (mutationFact.Atom is not SymbolicMutationAtom { CallerVisible: true })
            {
                evidence = default;
                return false;
            }

            evidence = PurityEvidence.Create(
                "mutable_state_write",
                ruleName: ruleName,
                operation: operation,
                syntaxNode: operation.Syntax,
                symbol: symbol,
                catalogSource: mutationFact.Provenance);
            return true;
        }

        internal static bool TryCreateReturnEscapeEvidence(
            IReturnOperation returnOperation,
            SyntaxNode escapeSyntax,
            ISymbol escapeSymbol,
            PurityAnalysisState currentState,
            string ruleName,
            string fallbackCatalogSource,
            out PurityEvidence evidence)
        {
            var escapeTerm = CreateSymbolicReferenceTerm(escapeSymbol, currentState);
            var escapeFact = SymbolicOwnershipFactFactory.CreateEscape(
                escapeTerm,
                SymbolicEscapeKind.Return,
                escapeSyntax,
                "analyzer.escape.return",
                escapeSymbol,
                "evidence.escape.return");
            if (escapeFact.Atom is not SymbolicEscapeAtom { Kind: SymbolicEscapeKind.Return })
            {
                evidence = default;
                return false;
            }

            evidence = PurityEvidence.Create(
                "mutable_state_escape",
                ruleName: ruleName,
                operation: returnOperation,
                syntaxNode: escapeSyntax,
                symbol: escapeSymbol,
                catalogSource: string.IsNullOrEmpty(fallbackCatalogSource)
                    ? escapeFact.Provenance
                    : fallbackCatalogSource);
            return true;
        }

        private static PurityEvidence CreateByRefReturnEscapeEvidence(
            IMethodSymbol methodSymbol,
            SyntaxNode escapeSyntax)
        {
            var escapeTerm = new SymbolicVariableTerm(
                methodSymbol.ToDisplayString(_signatureFormat),
                SmtValueKind.Reference);
            var escapeFact = SymbolicOwnershipFactFactory.CreateEscape(
                escapeTerm,
                SymbolicEscapeKind.Return,
                escapeSyntax,
                "analyzer.escape.return.byref",
                methodSymbol,
                "evidence.escape.return.byref");

            var catalogSource = escapeFact.Atom is SymbolicEscapeAtom { Kind: SymbolicEscapeKind.Return }
                ? escapeFact.Provenance
                : "return_by_ref";
            return PurityEvidence.Create(
                "mutable_state_escape",
                ruleName: "ReturnByRefAnalysis",
                syntaxNode: escapeSyntax,
                symbol: methodSymbol,
                catalogSource: catalogSource);
        }

        internal static bool TryCreateCallerVisibleMutationTerm(
            IOperation targetOperation,
            PurityAnalysisState currentState,
            out SymbolicTerm term,
            out ISymbol? symbol)
        {
            var unwrappedTargetOperation = SkipImplicitConversions(targetOperation);
            if (unwrappedTargetOperation == null)
            {
                symbol = null;
                term = null!;
                return false;
            }

            targetOperation = unwrappedTargetOperation;
            switch (targetOperation)
            {
                case IParameterReferenceOperation parameterReference:
                    symbol = parameterReference.Parameter;
                    term = CreateSymbolicReferenceTerm(parameterReference.Parameter, currentState);
                    return true;

                case IFieldReferenceOperation fieldReference:
                    symbol = fieldReference.Field;
                    term = CreateSymbolicReferenceTerm(fieldReference.Field, currentState);
                    return true;

                case IPropertyReferenceOperation propertyReference:
                    symbol = propertyReference.Property;
                    term = CreateSymbolicReferenceTerm(propertyReference.Property, currentState);
                    return true;

                case IArrayElementReferenceOperation arrayElementReference
                    when TryResolveTrackedSymbol(arrayElementReference.ArrayReference, currentState) is IParameterSymbol parameterSymbol:
                    symbol = parameterSymbol;
                    term = CreateSymbolicReferenceTerm(parameterSymbol, currentState);
                    return true;

                default:
                    symbol = null;
                    term = null!;
                    return false;
            }
        }

        private static PurityAnalysisState AddOwnedLocalArrayFacts(
            PurityAnalysisState nextState,
            ISymbol localSymbol,
            IOperation valueOperation)
        {
            var term = CreateSymbolicReferenceTerm(localSymbol, nextState);
            var pathState = nextState.PathState;
            var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwnedValue(
                term,
                valueOperation.Syntax,
                "analyzer.array.acquire",
                localSymbol,
                "evidence.array.acquire");
            foreach (var fact in ownershipFacts)
            {
                pathState = pathState.AddFact(fact);
            }

            return nextState.WithPathConditionsAndState(nextState.PathConditions, pathState);
        }

        private static PurityAnalysisState AddFreshMutableObjectFacts(
            PurityAnalysisState nextState,
            ISymbol localSymbol,
            IOperation valueOperation)
        {
            var unwrappedValue = SkipImplicitConversions(valueOperation);
            if (unwrappedValue is not IObjectCreationOperation objectCreationOperation ||
                !RuleAnalysisHelper.IsFreshMutableEscapingReferenceType(objectCreationOperation.Type))
            {
                return nextState;
            }

            var term = CreateSymbolicReferenceTerm(localSymbol, nextState);
            var pathState = nextState.PathState;
            var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwnedValue(
                term,
                valueOperation.Syntax,
                "analyzer.object.acquire",
                localSymbol,
                "evidence.object.acquire");
            foreach (var fact in ownershipFacts)
            {
                pathState = pathState.AddFact(fact);
            }

            return nextState.WithPathConditionsAndState(nextState.PathConditions, pathState);
        }

        private static PurityAnalysisState AddOwnedDisposableLocalFacts(
            PurityAnalysisState nextState,
            ISymbol localSymbol,
            IOperation valueOperation,
            Compilation compilation)
        {
            if (!IsOwnedDisposableObjectCreationValue(valueOperation, compilation))
            {
                return nextState;
            }

            var term = CreateSymbolicReferenceTerm(localSymbol, nextState);
            if (HasReleasedResourceFact(term, nextState))
            {
                return nextState;
            }

            var pathState = nextState.PathState;
            var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwned(
                term,
                valueOperation.Syntax,
                "analyzer.resource.acquire",
                localSymbol,
                "evidence.resource.acquire");
            foreach (var fact in ownershipFacts)
            {
                pathState = pathState.AddFact(fact);
            }

            pathState = pathState.AddFact(SymbolicOwnershipFactFactory.CreateDisposal(
                term,
                SymbolicDisposalState.NotDisposed,
                valueOperation.Syntax,
                "analyzer.resource.acquire.disposal",
                localSymbol,
                "evidence.resource.acquire"));

            return nextState.WithPathConditionsAndState(nextState.PathConditions, pathState);
        }

        private static bool HasReleasedResourceFact(SymbolicTerm term, PurityAnalysisState state)
        {
            var releasedResources = new HashSet<SymbolicTerm>();
            foreach (var fact in state.PathState.Facts)
            {
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact)
                {
                    continue;
                }

                switch (fact.Atom)
                {
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Released } lifetime:
                        releasedResources.Add(lifetime.Resource);
                        break;
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Returned } lifetime:
                        releasedResources.Add(lifetime.Resource);
                        break;
                    case SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal:
                        releasedResources.Add(disposal.Resource);
                        break;
                }
            }

            return IsResourceReleased(term, releasedResources, state, new HashSet<SymbolicTerm>());
        }

        private static bool IsOwnedDisposableObjectCreationValue(
            IOperation valueOperation,
            Compilation compilation)
        {
            var unwrappedValue = SkipImplicitConversions(valueOperation);
            return unwrappedValue is IObjectCreationOperation objectCreationOperation &&
                objectCreationOperation.Type is { } createdType &&
                IsDisposableResourceType(createdType, compilation);
        }

        private static bool IsDisposableResourceType(ITypeSymbol type, Compilation compilation)
        {
            if (type.SpecialType == SpecialType.System_IDisposable ||
                type.ToDisplayString() == "System.IAsyncDisposable")
            {
                return true;
            }

            return type.AllInterfaces.Any(static interfaceType =>
                interfaceType.SpecialType == SpecialType.System_IDisposable ||
                interfaceType.ToDisplayString() == "System.IAsyncDisposable");
        }

        private static bool IsUsingResourceDeclarator(IVariableDeclaratorOperation declarator)
        {
            foreach (var ancestor in declarator.Syntax.AncestorsAndSelf())
            {
                if (ancestor is UsingStatementSyntax)
                {
                    return true;
                }

                if (ancestor is LocalDeclarationStatementSyntax { UsingKeyword.RawKind: not 0 })
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsParameterlessDisposeInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.ReducedFrom ?? invocationOperation.TargetMethod;
            return targetMethod != null &&
                   targetMethod.Parameters.Length == 0 &&
                   targetMethod.Name is nameof(IDisposable.Dispose) or "DisposeAsync";
        }

        internal static bool HasDisposedResourceFact(PurityAnalysisState currentState, ISymbol resourceSymbol)
        {
            var term = CreateSymbolicReferenceTerm(resourceSymbol, currentState);
            return HasDisposedResourceFactForTerm(
                term,
                currentState,
                new HashSet<SymbolicTerm>());
        }

        private static bool HasDisposedResourceFactBefore(
            PurityAnalysisState currentState,
            ISymbol resourceSymbol,
            SyntaxNode observationSyntax)
        {
            var term = CreateSymbolicReferenceTerm(resourceSymbol, currentState);
            return HasDisposedResourceFactForTermBefore(
                term,
                currentState,
                observationSyntax,
                new HashSet<SymbolicTerm>());
        }

        internal static bool TryCreateUseAfterDisposeEvidence(
            IOperation useOperation,
            IOperation? resourceOperation,
            ISymbol usedMemberSymbol,
            PurityAnalysisState currentState,
            string ruleName,
            out PurityEvidence evidence)
        {
            evidence = PurityEvidence.None;
            if (TryResolveTrackedSymbol(resourceOperation, currentState) is not { } resourceSymbol ||
                !HasDisposedResourceFact(currentState, resourceSymbol))
            {
                return false;
            }

            evidence = PurityEvidence.Create(
                "resource_use_after_dispose",
                ruleName,
                useOperation,
                syntaxNode: useOperation.Syntax,
                symbol: usedMemberSymbol,
                catalogSource: "symbolic_resource_lifetime");
            return true;
        }

        internal static bool TryCreateUseAfterDisposeEvidence(
            IOperation useOperation,
            IOperation? resourceOperation,
            ISymbol usedMemberSymbol,
            PurityAnalysisState currentState,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            string ruleName,
            out PurityEvidence evidence)
        {
            if (TryCreateUseAfterDisposeEvidence(
                    useOperation,
                    resourceOperation,
                    usedMemberSymbol,
                    currentState,
                    ruleName,
                    out evidence))
            {
                return true;
            }

            if (TryResolveTrackedSymbol(resourceOperation, currentState) is not { } resourceSymbol ||
                (!WasResourceDisposedByEarlierUsingStatement(
                     resourceSymbol,
                     useOperation.Syntax,
                     currentState,
                     semanticModel,
                     cancellationToken) &&
                 !WasResourceDisposedByEarlierRelatedLocal(
                     resourceSymbol,
                     useOperation.Syntax,
                     semanticModel,
                     cancellationToken)))
            {
                evidence = PurityEvidence.None;
                return false;
            }

            evidence = PurityEvidence.Create(
                "resource_use_after_dispose",
                ruleName,
                useOperation,
                syntaxNode: useOperation.Syntax,
                symbol: usedMemberSymbol,
                catalogSource: "symbolic_resource_lifetime");
            return true;
        }

        internal static bool TryCreateDoubleDisposeEvidence(
            IInvocationOperation invocationOperation,
            IMethodSymbol invokedMethodSymbol,
            PurityAnalysisState currentState,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            string ruleName,
            out PurityEvidence evidence)
        {
            evidence = PurityEvidence.None;
            if (!IsParameterlessDisposeInvocation(invocationOperation) ||
                invocationOperation.Instance == null ||
                TryResolveTrackedSymbol(invocationOperation.Instance, currentState) is not { } resourceSymbol ||
                (!HasDisposedResourceFactBefore(currentState, resourceSymbol, invocationOperation.Syntax) &&
                 !WasResourceDisposedByEarlierUsingStatement(
                     resourceSymbol,
                     invocationOperation.Syntax,
                     currentState,
                     semanticModel,
                     cancellationToken) &&
                 !WasResourceDisposedByEarlierRelatedLocal(
                     resourceSymbol,
                     invocationOperation.Syntax,
                     semanticModel,
                     cancellationToken)))
            {
                return false;
            }

            evidence = PurityEvidence.Create(
                "resource_double_dispose",
                ruleName,
                invocationOperation,
                symbol: invokedMethodSymbol,
                catalogSource: "symbolic_resource_lifetime");
            return true;
        }

        private static bool WasResourceDisposedByEarlierUsingStatement(
            ISymbol resourceSymbol,
            SyntaxNode useSyntax,
            PurityAnalysisState currentState,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var containingBlock = useSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            foreach (var usingStatement in containingBlock.DescendantNodes().OfType<UsingStatementSyntax>())
            {
                if (usingStatement.Span.End > useSyntax.SpanStart ||
                    usingStatement.Statement == null)
                {
                    continue;
                }

                if (usingStatement.Expression is { } usingExpression &&
                    semanticModel.GetSymbolInfo(usingExpression, cancellationToken).Symbol is { } usingSymbol &&
                    AreSymbolsSameOrAliased(resourceSymbol, usingSymbol, currentState))
                {
                    return true;
                }

                if (usingStatement.Declaration == null)
                {
                    continue;
                }

                foreach (var variable in usingStatement.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is { } declaredUsingSymbol &&
                        AreSymbolsSameOrAliased(resourceSymbol, declaredUsingSymbol, currentState))
                    {
                        return true;
                    }
                }
            }

            foreach (var usingDeclaration in containingBlock.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
            {
                if (!usingDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
                {
                    continue;
                }

                var declarationBlock = usingDeclaration.FirstAncestorOrSelf<BlockSyntax>();
                if (declarationBlock == null ||
                    declarationBlock.Span.End > useSyntax.SpanStart)
                {
                    continue;
                }

                foreach (var variable in usingDeclaration.Declaration.Variables)
                {
                    if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is { } declaredUsingSymbol &&
                        AreSymbolsSameOrAliased(resourceSymbol, declaredUsingSymbol, currentState))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool WasResourceDisposedByEarlierRelatedLocal(
            ISymbol resourceSymbol,
            SyntaxNode useSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var containingBlock = useSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            var relatedSymbols = GetRelatedLocalAliases(
                resourceSymbol,
                useSyntax,
                containingBlock,
                semanticModel,
                cancellationToken);
            foreach (var invocation in containingBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.SpanStart >= useSyntax.SpanStart ||
                    invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                    semanticModel.GetSymbolInfo(memberAccess.Expression, cancellationToken).Symbol is not { } disposedSymbol ||
                    !relatedSymbols.Contains(disposedSymbol) ||
                    !IsPriorDisposalSpanOnCompatiblePath(invocation.SpanStart, useSyntax) ||
                    IsStaleRelatedLocalDisposal(
                        resourceSymbol,
                        disposedSymbol,
                        invocation.SpanStart,
                        useSyntax.SpanStart,
                        containingBlock,
                        semanticModel,
                        cancellationToken))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsStaleRelatedLocalDisposal(
            ISymbol usedResourceSymbol,
            ISymbol disposedSymbol,
            int disposeSpanStart,
            int useSpanStart,
            BlockSyntax containingBlock,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            if (SymbolEqualityComparer.Default.Equals(usedResourceSymbol, disposedSymbol))
            {
                return HasLocalReassignmentBetween(
                    disposedSymbol,
                    disposeSpanStart,
                    useSpanStart,
                    containingBlock,
                    semanticModel,
                    cancellationToken);
            }

            foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.SpanStart >= disposeSpanStart ||
                    declarator.Initializer?.Value == null ||
                    semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not { } declaredSymbol ||
                    !SymbolEqualityComparer.Default.Equals(declaredSymbol, usedResourceSymbol) ||
                    semanticModel.GetSymbolInfo(declarator.Initializer.Value, cancellationToken).Symbol is not { } initializerSymbol ||
                    !SymbolEqualityComparer.Default.Equals(initializerSymbol, disposedSymbol))
                {
                    continue;
                }

                return HasLocalReassignmentBetween(
                    disposedSymbol,
                    declarator.SpanStart,
                    disposeSpanStart,
                    containingBlock,
                    semanticModel,
                    cancellationToken);
            }

            return false;
        }

        private static HashSet<ISymbol> GetRelatedLocalAliases(
            ISymbol resourceSymbol,
            SyntaxNode useSyntax,
            BlockSyntax containingBlock,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var relatedSymbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default)
            {
                resourceSymbol
            };

            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (declarator.SpanStart >= useSyntax.SpanStart ||
                        declarator.Initializer?.Value == null ||
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not { } declaredSymbol ||
                        semanticModel.GetSymbolInfo(declarator.Initializer.Value, cancellationToken).Symbol is not { } initializerSymbol)
                    {
                        continue;
                    }

                    if (relatedSymbols.Contains(declaredSymbol) && relatedSymbols.Add(initializerSymbol))
                    {
                        changed = true;
                    }

                    if (relatedSymbols.Contains(initializerSymbol) &&
                        !HasLocalReassignmentBetween(
                            initializerSymbol,
                            declarator.SpanStart,
                            useSyntax.SpanStart,
                            containingBlock,
                            semanticModel,
                            cancellationToken) &&
                        relatedSymbols.Add(declaredSymbol))
                    {
                        changed = true;
                    }
                }
            }

            return relatedSymbols;
        }

        private static bool HasLocalReassignmentBetween(
            ISymbol symbol,
            int start,
            int end,
            BlockSyntax containingBlock,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var assignment in containingBlock.DescendantNodes().OfType<AssignmentExpressionSyntax>())
            {
                if (assignment.SpanStart <= start ||
                    assignment.SpanStart >= end ||
                    semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol is not { } assignedSymbol)
                {
                    continue;
                }

                if (SymbolEqualityComparer.Default.Equals(assignedSymbol, symbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AreSymbolsSameOrAliased(
            ISymbol first,
            ISymbol second,
            PurityAnalysisState currentState)
        {
            if (SymbolEqualityComparer.Default.Equals(first, second))
            {
                return true;
            }

            var firstTerm = CreateSymbolicReferenceTerm(first, currentState);
            var secondTerm = CreateSymbolicReferenceTerm(second, currentState);
            return EnumerateSymbolicAliasTerms(firstTerm, currentState).Any(aliasTerm => Equals(aliasTerm, secondTerm)) ||
                   EnumerateSymbolicAliasTerms(secondTerm, currentState).Any(aliasTerm => Equals(aliasTerm, firstTerm));
        }

        private static bool HasDisposedResourceFactForTerm(
            SymbolicTerm resourceTerm,
            PurityAnalysisState currentState,
            HashSet<SymbolicTerm> visitedTerms)
        {
            if (!visitedTerms.Add(resourceTerm))
            {
                return false;
            }

            foreach (var fact in currentState.PathState.Facts)
            {
                if (fact.Polarity &&
                    fact.Confidence == SymbolicFactConfidence.Exact &&
                    fact.Atom is SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal &&
                    Equals(disposal.Resource, resourceTerm))
                {
                    return true;
                }
            }

            foreach (var aliasTerm in EnumerateSymbolicAliasTerms(resourceTerm, currentState))
            {
                if (HasDisposedResourceFactForTerm(aliasTerm, currentState, visitedTerms))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDisposedResourceFactForTermBefore(
            SymbolicTerm resourceTerm,
            PurityAnalysisState currentState,
            SyntaxNode observationSyntax,
            HashSet<SymbolicTerm> visitedTerms)
        {
            if (!visitedTerms.Add(resourceTerm))
            {
                return false;
            }

            foreach (var fact in currentState.PathState.Facts)
            {
                if (fact.Polarity &&
                    fact.Confidence == SymbolicFactConfidence.Exact &&
                    IsPriorDisposalFactOnCompatiblePath(fact, observationSyntax) &&
                    fact.Atom is SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal &&
                    Equals(disposal.Resource, resourceTerm))
                {
                    return true;
                }
            }

            foreach (var aliasTerm in EnumerateSymbolicAliasTerms(resourceTerm, currentState))
            {
                if (HasDisposedResourceFactForTermBefore(
                        aliasTerm,
                        currentState,
                        observationSyntax,
                        visitedTerms))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsPriorDisposalFactOnCompatiblePath(
            SymbolicFact fact,
            SyntaxNode observationSyntax)
        {
            return IsPriorDisposalSpanOnCompatiblePath(fact.SourceSpan.Start, observationSyntax);
        }

        private static bool IsPriorDisposalSpanOnCompatiblePath(
            int sourceSpanStart,
            SyntaxNode observationSyntax)
        {
            if (sourceSpanStart >= observationSyntax.SpanStart)
            {
                return false;
            }

            var observationSection = observationSyntax.FirstAncestorOrSelf<SwitchSectionSyntax>();
            if (observationSection == null)
            {
                return true;
            }

            var containingSwitch = observationSection.FirstAncestorOrSelf<SwitchStatementSyntax>();
            if (containingSwitch == null ||
                !containingSwitch.Span.Contains(sourceSpanStart))
            {
                return true;
            }

            return observationSection.Span.Contains(sourceSpanStart);
        }

        private static SymbolicVariableTerm CreateSymbolicReferenceTerm(ISymbol symbol, PurityAnalysisState currentState)
        {
            return new SymbolicVariableTerm(
                GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
                SmtValueKind.Reference);
        }

        internal static bool HasSymbolicBorrowFactForLocal(
            ILocalSymbol localSymbol,
            PurityAnalysisState currentState,
            SymbolicBorrowKind? borrowKind = null)
        {
            var localTerm = CreateSymbolicReferenceTerm(localSymbol, currentState);
            return HasSymbolicBorrowFactForTerm(
                localTerm,
                currentState,
                borrowKind,
                new HashSet<SymbolicTerm>());
        }

        internal static bool HasSymbolicBorrowerFactForSymbol(
            ISymbol ownerSymbol,
            PurityAnalysisState currentState)
        {
            var ownerTerm = CreateSymbolicReferenceTerm(ownerSymbol, currentState);
            return HasSymbolicBorrowerFactForTerm(
                ownerTerm,
                currentState,
                new HashSet<SymbolicTerm>());
        }

        internal static bool TryCreateMutableBorrowConflictEvidence(
            IOperation operation,
            ISymbol? targetSymbol,
            PurityAnalysisState currentState,
            string ruleName,
            out PurityEvidence evidence)
        {
            evidence = PurityEvidence.None;
            if (targetSymbol == null ||
                !HasSymbolicBorrowerFactForSymbol(targetSymbol, currentState))
            {
                return false;
            }

            evidence = PurityEvidence.Create(
                "mutable_state_write",
                ruleName: ruleName,
                operation: operation,
                syntaxNode: operation.Syntax,
                symbol: targetSymbol,
                catalogSource: "analyzer.borrow.mutable-conflict");
            return true;
        }

        internal static bool TryCreateMutableBorrowConflictEvidence(
            IOperation operation,
            ISymbol? targetSymbol,
            PurityAnalysisState currentState,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            string ruleName,
            out PurityEvidence evidence)
        {
            if (TryCreateMutableBorrowConflictEvidence(
                    operation,
                    targetSymbol,
                    currentState,
                    ruleName,
                    out evidence))
            {
                return true;
            }

            if (targetSymbol is ILocalSymbol targetLocal &&
                HasActiveRefLocalBorrowAfterWrite(
                    targetLocal,
                    operation.Syntax,
                    semanticModel,
                    cancellationToken))
            {
                evidence = PurityEvidence.Create(
                    "mutable_state_write",
                    ruleName: ruleName,
                    operation: operation,
                    syntaxNode: operation.Syntax,
                    symbol: targetLocal,
                    catalogSource: "analyzer.borrow.mutable-conflict");
                return true;
            }

            evidence = PurityEvidence.None;
            return false;
        }

        private static bool HasActiveRefLocalBorrowAfterWrite(
            ILocalSymbol targetLocal,
            SyntaxNode writeSyntax,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            var containingBlock = writeSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            var borrowedLocals = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var changed = true;
            while (changed)
            {
                changed = false;
                foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
                {
                    if (declarator.SpanStart >= writeSyntax.SpanStart ||
                        declarator.Initializer?.Value is not RefExpressionSyntax refExpression ||
                        semanticModel.GetDeclaredSymbol(declarator, cancellationToken) is not ILocalSymbol refLocal ||
                        semanticModel.GetSymbolInfo(refExpression.Expression, cancellationToken).Symbol is not ILocalSymbol sourceLocal)
                    {
                        continue;
                    }

                    if ((SymbolEqualityComparer.Default.Equals(sourceLocal, targetLocal) ||
                         borrowedLocals.Contains(sourceLocal)) &&
                        borrowedLocals.Add(refLocal))
                    {
                        changed = true;
                    }
                }
            }

            foreach (var borrowedLocal in borrowedLocals.OfType<ILocalSymbol>())
            {
                if (IsLocalUsedAfter(borrowedLocal, writeSyntax, containingBlock, semanticModel, cancellationToken))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsLocalUsedAfter(
            ILocalSymbol localSymbol,
            SyntaxNode writeSyntax,
            BlockSyntax containingBlock,
            SemanticModel semanticModel,
            CancellationToken cancellationToken)
        {
            foreach (var identifierName in containingBlock.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifierName.SpanStart <= writeSyntax.SpanStart)
                {
                    continue;
                }

                if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is ILocalSymbol usedLocal &&
                    SymbolEqualityComparer.Default.Equals(usedLocal, localSymbol))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSymbolicBorrowerFactForTerm(
            SymbolicTerm ownerTerm,
            PurityAnalysisState currentState,
            HashSet<SymbolicTerm> visitedTerms)
        {
            if (!visitedTerms.Add(ownerTerm))
            {
                return false;
            }

            foreach (var fact in currentState.PathState.Facts)
            {
                if (fact.Polarity &&
                    fact.Confidence == SymbolicFactConfidence.Exact &&
                    fact.Atom is SymbolicBorrowAtom borrow &&
                    Equals(borrow.Owner, ownerTerm))
                {
                    return true;
                }
            }

            foreach (var aliasTerm in EnumerateSymbolicAliasTerms(ownerTerm, currentState))
            {
                if (HasSymbolicBorrowerFactForTerm(aliasTerm, currentState, visitedTerms))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSymbolicBorrowFactForTerm(
            SymbolicTerm localTerm,
            PurityAnalysisState currentState,
            SymbolicBorrowKind? borrowKind,
            HashSet<SymbolicTerm> visitedTerms)
        {
            if (!visitedTerms.Add(localTerm))
            {
                return false;
            }

            foreach (var fact in currentState.PathState.Facts)
            {
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact ||
                    fact.Atom is not SymbolicBorrowAtom borrow ||
                    !Equals(borrow.Borrow, localTerm) ||
                    (borrowKind.HasValue && borrow.Kind != borrowKind.Value))
                {
                    continue;
                }

                return true;
            }

            foreach (var aliasTerm in EnumerateSymbolicAliasTerms(localTerm, currentState))
            {
                if (HasSymbolicBorrowFactForTerm(aliasTerm, currentState, borrowKind, visitedTerms))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool HasSymbolicOwnedFactForSymbol(
            ISymbol symbol,
            PurityAnalysisState currentState)
        {
            var symbolTerm = CreateSymbolicReferenceTerm(symbol, currentState);
            return HasSymbolicOwnedFactForTerm(
                symbolTerm,
                currentState,
                new HashSet<SymbolicTerm>());
        }

        private static bool HasSymbolicOwnedFactForTerm(
            SymbolicTerm symbolTerm,
            PurityAnalysisState currentState,
            HashSet<SymbolicTerm> visitedTerms)
        {
            if (!visitedTerms.Add(symbolTerm))
            {
                return false;
            }

            foreach (var fact in currentState.PathState.Facts)
            {
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact)
                {
                    continue;
                }

                if (fact.Atom is SymbolicOwnershipAtom { Escaped: false } ownership &&
                    Equals(ownership.Value, symbolTerm))
                {
                    return true;
                }

                if (fact.Atom is SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime &&
                    Equals(lifetime.Resource, symbolTerm))
                {
                    return true;
                }
            }

            foreach (var aliasTerm in EnumerateSymbolicAliasTerms(symbolTerm, currentState))
            {
                if (HasSymbolicOwnedFactForTerm(aliasTerm, currentState, visitedTerms))
                {
                    return true;
                }
            }

            return false;
        }

        private static IEnumerable<SymbolicTerm> EnumerateSymbolicAliasTerms(
            SymbolicTerm symbolTerm,
            PurityAnalysisState currentState)
        {
            foreach (var fact in currentState.PathState.Facts)
            {
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact ||
                    fact.Atom is not SymbolicAliasAtom { MayAlias: true } alias)
                {
                    continue;
                }

                if (Equals(alias.Target, symbolTerm))
                {
                    yield return alias.Source;
                }

                if (Equals(alias.Source, symbolTerm))
                {
                    yield return alias.Target;
                }
            }
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

            var nextState = AddAssignedAliasFact(
                currentState,
                targetSymbol,
                valueOperation,
                valueState);
            if (SymbolicReachabilityService.TryCreateAssignedValueFact(
                    targetSymbol,
                    valueExpression,
                    semanticModel,
                    CancellationToken.None,
                    out var assignedFact,
                    valueState.GetSmtSymbolVersion,
                    currentState.GetSmtSymbolVersion) &&
                TryCreateSymbolSmtValue(targetSymbol, currentState, out var targetFormula))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(assignedFact));
                nextState = AddAssignedSymbolicEqualityFact(
                    nextState,
                    targetFormula,
                    valueExpression,
                    valueState,
                    semanticModel,
                    SymbolicIrLowerer.TryLowerTerm,
                    "analyzer.assignment",
                    "analyzer.assignment.value");
            }

            if (SymbolicReachabilityService.TryCreateBuiltInLengthAssignedValueFact(
                    targetSymbol,
                    valueExpression,
                    semanticModel,
                    CancellationToken.None,
                    out var lengthAssignedFact,
                    valueState.GetSmtSymbolVersion,
                    currentState.GetSmtSymbolVersion) &&
                TryCreateBuiltInLengthFormula(targetSymbol, currentState, out var targetLengthFormula))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(lengthAssignedFact));
                nextState = AddAssignedSymbolicEqualityFact(
                    nextState,
                    targetLengthFormula,
                    valueExpression,
                    valueState,
                    semanticModel,
                    TryLowerAssignedLengthTerm,
                    "analyzer.assignment.length",
                    "analyzer.assignment.length");
            }

            if (TryCreateReferenceBackedLengthFact(
                    targetSymbol,
                    valueExpression,
                    currentState,
                    valueState,
                    semanticModel,
                    out var referenceLengthFact))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(referenceLengthFact));
                nextState = AddSymbolicEqualityFactFromFormula(
                    nextState,
                    referenceLengthFact,
                    valueExpression,
                    "analyzer.assignment.reference_length",
                    "analyzer.assignment.reference_length");
            }

            if (TryCreateCollectionExpressionLengthLowerBoundFact(
                    targetSymbol,
                    valueExpression,
                    currentState,
                    out var lowerBoundLengthFact))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(lowerBoundLengthFact));
                nextState = AddSymbolicConditionFromFormula(
                    nextState,
                    lowerBoundLengthFact,
                    valueExpression,
                    "analyzer.assignment.collection_length",
                    "analyzer.assignment.collection_length");
            }

            if (SymbolicReachabilityService.TryCreateStringContentAssignedValueFact(
                    targetSymbol,
                    valueExpression,
                    semanticModel,
                    CancellationToken.None,
                    out var stringAssignedFact,
                    valueState.GetSmtSymbolVersion,
                    currentState.GetSmtSymbolVersion) &&
                TryCreateStringContentFormula(targetSymbol, currentState, out var targetStringFormula))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(stringAssignedFact));
                nextState = AddAssignedSymbolicEqualityFact(
                    nextState,
                    targetStringFormula,
                    valueExpression,
                    valueState,
                    semanticModel,
                    SymbolicIrLowerer.TryLowerStringTerm,
                    "analyzer.assignment.string",
                    "analyzer.assignment.string");
            }

            if (SymbolicReachabilityService.TryCreateAsExpressionAssignedValueFacts(
                    targetSymbol,
                    valueExpression,
                    semanticModel,
                    CancellationToken.None,
                    out var asExpressionFacts,
                    valueState.GetSmtSymbolVersion,
                    currentState.GetSmtSymbolVersion))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.AddRange(asExpressionFacts));
                nextState = AddSymbolicConditionsFromFormulas(
                    nextState,
                    asExpressionFacts,
                    valueExpression,
                    "analyzer.assignment.as_expression",
                    "analyzer.assignment.as_expression");
            }

            if (TryCreateReferenceBackedStringContentFact(
                    targetSymbol,
                    valueExpression,
                    currentState,
                    valueState,
                    semanticModel,
                    out var referenceStringFact))
            {
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(referenceStringFact));
                nextState = AddSymbolicEqualityFactFromFormula(
                    nextState,
                    referenceStringFact,
                    valueExpression,
                    "analyzer.assignment.reference_string",
                    "analyzer.assignment.reference_string");
            }

            if (SymbolicReachabilityService.TryCreateStringNonNullAssignedValueFact(
                    targetSymbol,
                    valueExpression,
                    semanticModel,
                    CancellationToken.None,
                    out var stringNonNullFact,
                    valueState.GetSmtSymbolVersion,
                    currentState.GetSmtSymbolVersion) &&
                TryCreateSymbolSmtValue(targetSymbol, currentState, out var targetReferenceFormula) &&
                targetReferenceFormula is { Kind: SmtValueKind.Reference })
            {
                var targetNonNullFormula = SmtFormulaFactory.CreateReferenceNullComparison(
                    targetReferenceFormula,
                    isNull: false);
                nextState = nextState.WithPathConditions(nextState.PathConditions.Add(stringNonNullFact));
                nextState = AddSymbolicConditionFromFormula(
                    nextState,
                    stringNonNullFact,
                    valueExpression,
                    "analyzer.assignment.string_nonnull",
                    "analyzer.assignment.string_nonnull");
            }

            return nextState;
        }

        private static PurityAnalysisState AddAssignedAliasFact(
            PurityAnalysisState currentState,
            ISymbol targetSymbol,
            IOperation valueOperation,
            PurityAnalysisState valueState)
        {
            var sourceSymbol = TryResolveTrackedSymbol(valueOperation, valueState);
            if (sourceSymbol == null ||
                SymbolEqualityComparer.Default.Equals(sourceSymbol, targetSymbol) ||
                SymbolicFactFactory.GetTrackedSymbolType(sourceSymbol)?.IsReferenceType != true ||
                SymbolicFactFactory.GetTrackedSymbolType(targetSymbol)?.IsReferenceType != true)
            {
                return currentState;
            }

            var sourceTerm = CreateSymbolicReferenceTerm(sourceSymbol, valueState);
            var targetTerm = CreateSymbolicReferenceTerm(targetSymbol, currentState);
            var aliasFact = SymbolicOwnershipFactFactory.CreateAlias(
                sourceTerm,
                targetTerm,
                mayAlias: true,
                valueOperation.Syntax,
                "analyzer.assignment.alias",
                targetSymbol,
                "evidence.assignment.alias");

            return currentState.WithPathConditionsAndState(
                currentState.PathConditions,
                currentState.PathState.AddFact(aliasFact));
        }

        private static PurityAnalysisState AddDeclaredBorrowFact(
            PurityAnalysisState currentState,
            ILocalSymbol declaredSymbol,
            IOperation initializerValue,
            SemanticModel semanticModel)
        {
            var isRefInitializer = initializerValue.Syntax.Parent is RefExpressionSyntax ||
                initializerValue.Syntax.Ancestors().OfType<RefExpressionSyntax>().Any();
            if (!isRefInitializer &&
                declaredSymbol.RefKind is not (RefKind.Ref or RefKind.Out or RefKind.In or RefKind.RefReadOnly))
            {
                return currentState;
            }

            var sourceSymbol = TryResolveTrackedSymbol(initializerValue, currentState) ??
                TryResolveRefInitializerSymbol(initializerValue.Syntax, semanticModel, currentState);
            if (sourceSymbol == null)
            {
                return currentState;
            }

            var borrowKind = declaredSymbol.RefKind is RefKind.In or RefKind.RefReadOnly
                ? SymbolicBorrowKind.Shared
                : SymbolicBorrowKind.Mutable;
            var sourceTerm = CreateSymbolicReferenceTerm(sourceSymbol, currentState);
            var borrowTerm = CreateSymbolicReferenceTerm(declaredSymbol, currentState);
            var borrowFact = SymbolicOwnershipFactFactory.CreateBorrow(
                sourceTerm,
                borrowTerm,
                borrowKind,
                initializerValue.Syntax,
                "analyzer.declaration.borrow",
                declaredSymbol,
                "evidence.declaration.borrow");

            return currentState.WithPathConditionsAndState(
                currentState.PathConditions,
                currentState.PathState.AddFact(borrowFact));
        }

        private static ISymbol? TryResolveRefInitializerSymbol(
            SyntaxNode initializerSyntax,
            SemanticModel semanticModel,
            PurityAnalysisState currentState)
        {
            var refExpression = initializerSyntax.AncestorsAndSelf().OfType<RefExpressionSyntax>().FirstOrDefault();
            if (refExpression == null)
            {
                return null;
            }

            if (semanticModel.GetOperation(refExpression.Expression) is { } operation &&
                TryResolveTrackedSymbol(operation, currentState) is { } operationSymbol)
            {
                return operationSymbol;
            }

            return semanticModel.GetSymbolInfo(refExpression.Expression).Symbol;
        }

        private static PurityAnalysisState AddAssignedSymbolicEqualityFact(
            PurityAnalysisState currentState,
            SmtFormula targetFormula,
            ExpressionSyntax valueExpression,
            PurityAnalysisState valueState,
            SemanticModel semanticModel,
            LowerAssignedSymbolicTerm lowerValueTerm,
            string provenance,
            string evidenceKey)
        {
            if (!SymbolicSmtFormulaLowerer.TryLowerTerm(targetFormula, out var targetTerm) ||
                !lowerValueTerm(
                    valueExpression,
                    new SymbolicLoweringContext(
                        semanticModel,
                        CancellationToken.None,
                        valueState.GetSmtSymbolVersion),
                    out var valueTerm) ||
                !CanCompareSymbolicTerms(targetTerm, valueTerm))
            {
                return currentState;
            }

            var fact = SymbolicFact.Exact(
                new SymbolicRelationAtom(
                    SymbolicRelationOperator.Equal,
                    targetTerm,
                    valueTerm),
                valueExpression,
                provenance,
                evidenceKey: evidenceKey);
            return currentState.WithPathConditionsAndState(
                currentState.PathConditions,
                currentState.PathState.AddPathCondition(new SymbolicFactCondition(fact)));
        }

        private static PurityAnalysisState AddSymbolicEqualityFactFromFormula(
            PurityAnalysisState currentState,
            SmtFormula formula,
            SyntaxNode sourceNode,
            string provenance,
            string evidenceKey)
        {
            if (!SymbolicSmtFormulaLowerer.TryLowerEqualityFact(
                    formula,
                    sourceNode,
                    provenance,
                    evidenceKey,
                    out var fact))
            {
                return currentState;
            }

            return currentState.WithPathConditionsAndState(
                currentState.PathConditions,
                currentState.PathState.AddPathCondition(new SymbolicFactCondition(fact)));
        }

        private static PurityAnalysisState AddSymbolicConditionsFromFormulas(
            PurityAnalysisState currentState,
            ImmutableArray<SmtFormula> formulas,
            SyntaxNode sourceNode,
            string provenance,
            string evidenceKey)
        {
            var nextState = currentState;
            foreach (var formula in formulas)
            {
                nextState = AddSymbolicConditionFromFormula(
                    nextState,
                    formula,
                    sourceNode,
                    provenance,
                    evidenceKey);
            }

            return nextState;
        }

        private static PurityAnalysisState AddSymbolicConditionFromFormula(
            PurityAnalysisState currentState,
            SmtFormula formula,
            SyntaxNode sourceNode,
            string provenance,
            string evidenceKey)
        {
            return currentState.WithPathConditionsAndState(
                currentState.PathConditions,
                AddSymbolicConditionToState(
                    currentState.PathState,
                    formula,
                    sourceNode,
                    provenance,
                    evidenceKey));
        }

        private static SymbolicState AddSymbolicConditionToState(
            SymbolicState state,
            SmtFormula formula,
            SyntaxNode sourceNode,
            string provenance,
            string evidenceKey)
        {
            return SymbolicSmtFormulaLowerer.TryLowerCondition(
                    formula,
                    sourceNode,
                    provenance,
                    evidenceKey,
                    out var condition)
                ? state.AddPathCondition(condition)
                : state;
        }

        private delegate bool LowerAssignedSymbolicTerm(
            ExpressionSyntax expression,
            SymbolicLoweringContext context,
            out SymbolicTerm term);

        private static bool TryLowerAssignedLengthTerm(
            ExpressionSyntax valueExpression,
            SymbolicLoweringContext context,
            out SymbolicTerm term)
        {
            if (SymbolicIrLowerer.TryLowerTerm(valueExpression, context, out var valueTerm))
            {
                if (valueTerm.Kind == SmtValueKind.String ||
                    valueTerm.Kind == SmtValueKind.Reference)
                {
                    term = new SymbolicLengthTerm(valueTerm);
                    return true;
                }
            }

            term = null!;
            return false;
        }

        private static bool CanCompareSymbolicTerms(SymbolicTerm left, SymbolicTerm right)
        {
            return left.Kind == right.Kind ||
                left is SymbolicNullTerm && right.Kind == SmtValueKind.Reference ||
                right is SymbolicNullTerm && left.Kind == SmtValueKind.Reference;
        }

        private static bool TryCreateReferenceBackedLengthFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            PurityAnalysisState currentState,
            PurityAnalysisState valueState,
            SemanticModel semanticModel,
            out SmtFormula fact)
        {
            return SymbolicReachabilityService.TryCreateReferenceBackedLengthFact(
                targetSymbol,
                valueExpression,
                semanticModel,
                CancellationToken.None,
                out fact,
                valueState.GetSmtSymbolVersion,
                currentState.GetSmtSymbolVersion);
        }

        private static bool TryCreateReferenceBackedStringContentFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            PurityAnalysisState currentState,
            PurityAnalysisState valueState,
            SemanticModel semanticModel,
            out SmtFormula fact)
        {
            return SymbolicReachabilityService.TryCreateReferenceBackedStringContentFact(
                targetSymbol,
                valueExpression,
                semanticModel,
                CancellationToken.None,
                out fact,
                valueState.GetSmtSymbolVersion,
                currentState.GetSmtSymbolVersion);
        }

        private static bool TryCreateSymbolSmtValue(
            ISymbol symbol,
            PurityAnalysisState currentState,
            out SmtFormula formula)
        {
            return SymbolicFactFactory.TryCreateSymbolVariableFormula(
                GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
                SymbolicFactFactory.GetTrackedSymbolType(symbol),
                SymbolicFactFactory.IsSupportedSmtIntegralOrEnumType,
                static type => type.IsReferenceType,
                out formula);
        }

        private static bool TryCreateStringContentFormula(
            ISymbol symbol,
            PurityAnalysisState currentState,
            out SmtFormula formula)
        {
            var type = SymbolicFactFactory.GetTrackedSymbolType(symbol);

            return SymbolicFactFactory.TryCreateStringContentFormula(
                GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
                type,
                out formula);
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

            return SymbolicFactFactory.TryCreateBuiltInLengthFormula(
                GetSmtVariableName(symbol, currentState.GetSmtSymbolVersion),
                type,
                out formula);
        }

        private static bool TryCreateCollectionExpressionLengthLowerBoundFact(
            ISymbol targetSymbol,
            ExpressionSyntax valueExpression,
            PurityAnalysisState currentState,
            out SmtFormula fact)
        {
            fact = null!;
            return TryCreateBuiltInLengthFormula(targetSymbol, currentState, out var targetLengthFormula) &&
                SymbolicFactFactory.TryCreateCollectionExpressionLengthLowerBoundFact(
                    targetLengthFormula,
                    UnwrapSmtFactExpression(valueExpression),
                    out fact);
        }

        private static ExpressionSyntax UnwrapSmtFactExpression(ExpressionSyntax expression)
        {
            return CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(expression);
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
                var ownedDisposableAliases = GetOwnedDisposableAliasSymbolsToPreserve(
                    writtenLocalSymbol,
                    currentState,
                    valueOperation.Syntax,
                    semanticModel,
                    compilation);
                nextState = nextState.WithIncrementedSmtSymbolVersion(writtenLocalSymbol);
                nextState = AddPreservedOwnedDisposableAliasFacts(
                    nextState,
                    ownedDisposableAliases,
                    valueOperation.Syntax);
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
                    nextState = AddOwnedLocalArrayFacts(
                        nextState,
                        writtenLocalSymbol,
                        valueOperation);
                }
                else
                {
                    nextState = nextState.WithoutOwnedLocalArray(writtenLocalSymbol);
                }

                nextState = AddOwnedDisposableLocalFacts(
                    nextState,
                    writtenLocalSymbol,
                    valueOperation,
                    compilation);
                nextState = AddFreshMutableObjectFacts(
                    nextState,
                    writtenLocalSymbol,
                    valueOperation);
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

        private static ImmutableArray<ISymbol> GetOwnedDisposableAliasSymbolsToPreserve(
            ISymbol reassignedSymbol,
            PurityAnalysisState currentState,
            SyntaxNode reassignmentSyntax,
            SemanticModel semanticModel,
            Compilation compilation)
        {
            var reassignedTerm = CreateSymbolicReferenceTerm(reassignedSymbol, currentState);
            var builder = ImmutableArray.CreateBuilder<ISymbol>();
            var seen = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
            var hasSymbolicObligation = HasUnreleasedOwnedResourceObligation(reassignedTerm, currentState);
            if (hasSymbolicObligation)
            {
                AddSymbolicAliasSymbolsToPreserve(
                    reassignedSymbol,
                    reassignedTerm,
                    currentState,
                    builder,
                    seen);
            }

            if (!hasSymbolicObligation &&
                IsUndisposedFreshDisposableLocalBeforeReassignment(
                    reassignedSymbol,
                    reassignmentSyntax,
                    semanticModel,
                    compilation))
            {
                AddSyntacticAliasSymbolsToPreserve(
                    reassignedSymbol,
                    reassignmentSyntax,
                    semanticModel,
                    builder,
                    seen);
            }

            return builder.ToImmutable();
        }

        private static void AddSymbolicAliasSymbolsToPreserve(
            ISymbol reassignedSymbol,
            SymbolicTerm reassignedTerm,
            PurityAnalysisState currentState,
            ImmutableArray<ISymbol>.Builder builder,
            HashSet<ISymbol> seen)
        {
            foreach (var fact in currentState.PathState.Facts)
            {
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact ||
                    fact.Atom is not SymbolicAliasAtom { MayAlias: true } alias ||
                    !Equals(alias.Source, reassignedTerm) ||
                    fact.Symbol == null ||
                    SymbolEqualityComparer.Default.Equals(fact.Symbol, reassignedSymbol) ||
                    !seen.Add(fact.Symbol))
                {
                    continue;
                }

                builder.Add(fact.Symbol);
            }
        }

        private static PurityAnalysisState AddPreservedOwnedDisposableAliasFacts(
            PurityAnalysisState nextState,
            ImmutableArray<ISymbol> aliasSymbols,
            SyntaxNode source)
        {
            if (aliasSymbols.IsDefaultOrEmpty)
            {
                return nextState;
            }

            var pathState = nextState.PathState;
            foreach (var aliasSymbol in aliasSymbols)
            {
                var aliasTerm = CreateSymbolicReferenceTerm(aliasSymbol, nextState);
                var ownershipFacts = SymbolicOwnershipFactFactory.CreateFreshOwned(
                    aliasTerm,
                    source,
                    "analyzer.resource.alias-preserve",
                    aliasSymbol,
                    "evidence.resource.alias-preserve");
                foreach (var fact in ownershipFacts)
                {
                    pathState = pathState.AddFact(fact);
                }

                pathState = pathState.AddFact(SymbolicOwnershipFactFactory.CreateDisposal(
                    aliasTerm,
                    SymbolicDisposalState.NotDisposed,
                    source,
                    "analyzer.resource.alias-preserve.disposal",
                    aliasSymbol,
                    "evidence.resource.alias-preserve"));
            }

            return nextState.WithPathConditionsAndState(nextState.PathConditions, pathState);
        }

        private static bool HasUnreleasedOwnedResourceObligation(
            SymbolicTerm resourceTerm,
            PurityAnalysisState state)
        {
            var hasOwnedResource = false;
            var releasedResources = new HashSet<SymbolicTerm>();
            foreach (var fact in state.PathState.Facts)
            {
                if (!fact.Polarity ||
                    fact.Confidence != SymbolicFactConfidence.Exact)
                {
                    continue;
                }

                switch (fact.Atom)
                {
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Owned } lifetime
                        when Equals(lifetime.Resource, resourceTerm):
                        hasOwnedResource = true;
                        break;
                    case SymbolicDisposalAtom { State: SymbolicDisposalState.NotDisposed } disposal
                        when Equals(disposal.Resource, resourceTerm):
                        hasOwnedResource = true;
                        break;
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Released } lifetime:
                        releasedResources.Add(lifetime.Resource);
                        break;
                    case SymbolicResourceLifetimeAtom { State: SymbolicResourceLifetimeState.Returned } lifetime:
                        releasedResources.Add(lifetime.Resource);
                        break;
                    case SymbolicDisposalAtom { State: SymbolicDisposalState.Disposed } disposal:
                        releasedResources.Add(disposal.Resource);
                        break;
                }
            }

            return hasOwnedResource &&
                !IsResourceReleased(resourceTerm, releasedResources, state, new HashSet<SymbolicTerm>());
        }

        private static bool IsUndisposedFreshDisposableLocalBeforeReassignment(
            ISymbol reassignedSymbol,
            SyntaxNode reassignmentSyntax,
            SemanticModel semanticModel,
            Compilation compilation)
        {
            if (reassignedSymbol is not ILocalSymbol localSymbol)
            {
                return false;
            }

            var declaratorSyntax = localSymbol.DeclaringSyntaxReferences
                .Select(static reference => reference.GetSyntax())
                .OfType<VariableDeclaratorSyntax>()
                .FirstOrDefault();
            if (declaratorSyntax?.Initializer?.Value == null ||
                declaratorSyntax.SpanStart >= reassignmentSyntax.SpanStart)
            {
                return false;
            }

            var initializerOperation = semanticModel.GetOperation(declaratorSyntax.Initializer.Value);
            if (!IsOwnedDisposableObjectCreationValue(initializerOperation!, compilation))
            {
                return false;
            }

            return !WasAnySymbolDisposedBeforeObservation(
                EnumerateSyntacticAliases(localSymbol, reassignmentSyntax, semanticModel)
                    .Prepend(localSymbol),
                reassignmentSyntax,
                semanticModel);
        }

        private static void AddSyntacticAliasSymbolsToPreserve(
            ISymbol reassignedSymbol,
            SyntaxNode reassignmentSyntax,
            SemanticModel semanticModel,
            ImmutableArray<ISymbol>.Builder builder,
            HashSet<ISymbol> seen)
        {
            if (reassignedSymbol is not ILocalSymbol localSymbol)
            {
                return;
            }

            foreach (var aliasSymbol in EnumerateSyntacticAliases(localSymbol, reassignmentSyntax, semanticModel))
            {
                if (!SymbolEqualityComparer.Default.Equals(aliasSymbol, reassignedSymbol) &&
                    seen.Add(aliasSymbol))
                {
                    builder.Add(aliasSymbol);
                }
            }
        }

        private static IEnumerable<ILocalSymbol> EnumerateSyntacticAliases(
            ILocalSymbol sourceLocal,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel)
        {
            var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                yield break;
            }

            foreach (var declarator in containingBlock.DescendantNodes().OfType<VariableDeclaratorSyntax>())
            {
                if (declarator.SpanStart >= observationSyntax.SpanStart ||
                    declarator.Initializer?.Value == null ||
                    semanticModel.GetDeclaredSymbol(declarator) is not ILocalSymbol aliasSymbol ||
                    semanticModel.GetSymbolInfo(declarator.Initializer.Value).Symbol is not ILocalSymbol initializerSymbol ||
                    !SymbolEqualityComparer.Default.Equals(initializerSymbol, sourceLocal))
                {
                    continue;
                }

                yield return aliasSymbol;
            }
        }

        private static bool WasAnySymbolDisposedBeforeObservation(
            IEnumerable<ISymbol> symbols,
            SyntaxNode observationSyntax,
            SemanticModel semanticModel)
        {
            var symbolSet = new HashSet<ISymbol>(symbols, SymbolEqualityComparer.Default);
            if (symbolSet.Count == 0)
            {
                return false;
            }

            var containingBlock = observationSyntax.FirstAncestorOrSelf<BlockSyntax>();
            if (containingBlock == null)
            {
                return false;
            }

            foreach (var invocation in containingBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (invocation.SpanStart >= observationSyntax.SpanStart ||
                    invocation.Expression is not MemberAccessExpressionSyntax memberAccess ||
                    memberAccess.Name.Identifier.ValueText is not (nameof(IDisposable.Dispose) or "DisposeAsync") ||
                    semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol is not { } disposedSymbol)
                {
                    continue;
                }

                if (symbolSet.Contains(disposedSymbol))
                {
                    return true;
                }
            }

            return false;
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

        private static bool IsArrayInterfaceGetEnumeratorInvocation(
            IInvocationOperation invocationOperation,
            SemanticModel semanticModel)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.Name != "GetEnumerator" ||
                targetMethod.Parameters.Length != 0)
            {
                return false;
            }

            var invocationSyntax = invocationOperation.Syntax as InvocationExpressionSyntax ??
                invocationOperation.Syntax.FirstAncestorOrSelf<InvocationExpressionSyntax>();
            if (invocationSyntax == null ||
                invocationSyntax.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            var receiverExpression = memberAccess.Expression;
            while (receiverExpression is ParenthesizedExpressionSyntax parenthesized)
            {
                receiverExpression = parenthesized.Expression;
            }

            if (receiverExpression is not CastExpressionSyntax castExpression)
            {
                return false;
            }

            var operandType = semanticModel.GetTypeInfo(castExpression.Expression).ConvertedType ??
                semanticModel.GetTypeInfo(castExpression.Expression).Type;
            return operandType is IArrayTypeSymbol;
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
                 SymbolicTypeFacts.IsReadOnlySpanOfCharType(targetMethod.Parameters[0].Type)) &&
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
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
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
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            {
                return true;
            }

            if (targetMethod.Name == "TryParse" &&
                targetMethod.Parameters.Length == 2 &&
                invocationOperation.Arguments.Length == 2 &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
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
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            {
                return invocationOperation.Arguments.Length == 1 ||
                    HasFormatProviderParameter(targetMethod);
            }

            if (targetMethod.Name == "TryParse" &&
                invocationOperation.Arguments.Length >= 2 &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            {
                return invocationOperation.Arguments.Length == 2 ||
                    HasFormatProviderParameter(targetMethod);
            }

            if ((targetMethod.Name == "ParseExact" || targetMethod.Name == "TryParseExact") &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
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
                SymbolicTypeFacts.IsReadOnlySpanOfCharType(typeSymbol) ||
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
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
                IsDateTimeStylesNone(invocationOperation.Arguments[2].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                targetMethod.Name == "ParseExact" &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
                IsSingleTimeOnlyInvariantFormat(invocationOperation.Arguments[1].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            if (targetMethod.Parameters.Length == 4 &&
                invocationOperation.Arguments.Length == 4 &&
                targetMethod.Name == "ParseExact" &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
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
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Name == "Parse" &&
                targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
                IsDateTimeStylesNone(invocationOperation.Arguments[2].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[1].Value);
            }

            if (targetMethod.Parameters.Length == 3 &&
                invocationOperation.Arguments.Length == 3 &&
                targetMethod.Name == "ParseExact" &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
                IsSingleDateOnlyInvariantFormat(invocationOperation.Arguments[1].Value))
            {
                return IsCultureInfoInvariantCulture(invocationOperation.Arguments[2].Value);
            }

            if (targetMethod.Parameters.Length == 4 &&
                invocationOperation.Arguments.Length == 4 &&
                targetMethod.Name == "ParseExact" &&
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
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
                SymbolicTypeFacts.IsStringOrReadOnlySpanOfCharType(targetMethod.Parameters[0].Type) &&
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
            var targetMethod = invocationOperation.TargetMethod?.ReducedFrom ?? invocationOperation.TargetMethod;
            var targetDefinition = targetMethod?.OriginalDefinition;
            if (targetDefinition == null ||
                targetDefinition.Name != "ToArray" ||
                invocationOperation.Type is not IArrayTypeSymbol arrayType ||
                arrayType.ElementType.SpecialType != SpecialType.System_Char)
            {
                return false;
            }

            var enumerableType = semanticModel.Compilation.GetTypeByMetadataName("System.Linq.Enumerable");
            if (enumerableType == null ||
                !SymbolEqualityComparer.Default.Equals(targetDefinition.ContainingType?.OriginalDefinition, enumerableType))
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
