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
using SearchLib.Purity;
using SearchLib.Smt;
using System.Threading;

namespace PurelySharp.Analyzer.Engine
{

    internal class PurityAnalysisEngine
    {
        private static readonly TimeSpan SmtTimeout = TimeSpan.FromMilliseconds(25);
        private readonly CompilationPurityService? _purityService;

        public PurityAnalysisEngine() { }

        public PurityAnalysisEngine(CompilationPurityService? purityService)
        {
            _purityService = purityService;
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
            public ImmutableHashSet<ISymbol> OwnedLocalArraySymbols { get; }
            public ImmutableHashSet<ISymbol> DefinitelyNullLocalSymbols { get; }
            public ImmutableDictionary<ISymbol, INamedTypeSymbol> LocalConcreteTypes { get; }
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
                ImmutableDictionary<CaptureId, INamedTypeSymbol>? flowCaptureConcreteTypes = null,
                ImmutableArray<SmtFormula>? pathConditions = null,
                ImmutableDictionary<CaptureId, ISymbol>? flowCaptureSymbols = null)
            {
                HasPotentialImpurity = hasPotentialImpurity;
                FirstImpureSyntaxNode = firstImpureSyntaxNode;
                FirstImpurityEvidence = firstImpurityEvidence;

                DelegateTargetMap = delegateTargetMap ?? ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
                FlowCaptures = flowCaptures ?? ImmutableDictionary<CaptureId, PurityAnalysisResult>.Empty;
                FlowCaptureTargets = flowCaptureTargets ?? ImmutableDictionary<CaptureId, PotentialTargets>.Empty;
                FlowCaptureConcreteTypes = flowCaptureConcreteTypes ?? ImmutableDictionary<CaptureId, INamedTypeSymbol>.Empty;
                FlowCaptureSymbols = flowCaptureSymbols ?? ImmutableDictionary.Create<CaptureId, ISymbol>();
                OwnedLocalArraySymbols = ownedLocalArraySymbols ?? ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
                DefinitelyNullLocalSymbols = definitelyNullLocalSymbols ?? ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
                LocalConcreteTypes = localConcreteTypes ?? ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
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
                var mergedOwnedLocalArrays = IntersectOwnedLocalArraySymbolsAcrossAll(stateList.Select(s => s.OwnedLocalArraySymbols));
                var mergedDefinitelyNullLocals = IntersectOwnedLocalArraySymbolsAcrossAll(stateList.Select(s => s.DefinitelyNullLocalSymbols));
                var mergedLocalConcreteTypes = IntersectLocalConcreteTypesAcrossAll(stateList.Select(s => s.LocalConcreteTypes));
                return new PurityAnalysisState(mergedImpurity, firstImpureNode, mergedTargets, mergedCaptures, mergedCaptureTargets, mergedOwnedLocalArrays, mergedDefinitelyNullLocals, firstEvidence, localConcreteTypes: mergedLocalConcreteTypes, flowCaptureConcreteTypes: mergedCaptureConcreteTypes, pathConditions: MergePathConditionsAcrossAll(stateList.Select(s => s.PathConditions)), flowCaptureSymbols: mergedCaptureSymbols);
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
                    this.OwnedLocalArraySymbols.Count != other.OwnedLocalArraySymbols.Count ||
                    this.DefinitelyNullLocalSymbols.Count != other.DefinitelyNullLocalSymbols.Count ||
                    this.LocalConcreteTypes.Count != other.LocalConcreteTypes.Count ||
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

                return hash;
            }

            public static bool operator ==(PurityAnalysisState left, PurityAnalysisState right) => left.Equals(right);
            public static bool operator !=(PurityAnalysisState left, PurityAnalysisState right) => !(left == right);


            public PurityAnalysisState WithImpurity(SyntaxNode node)
            {
                if (HasPotentialImpurity) return this;
                return new PurityAnalysisState(true, node, this.DelegateTargetMap, this.FlowCaptures, this.FlowCaptureTargets, this.OwnedLocalArraySymbols, this.DefinitelyNullLocalSymbols, PurityEvidence.Create("unsupported_operation", syntaxNode: node), localConcreteTypes: this.LocalConcreteTypes, flowCaptureConcreteTypes: this.FlowCaptureConcreteTypes, pathConditions: this.PathConditions, flowCaptureSymbols: this.FlowCaptureSymbols);
            }

            public PurityAnalysisState WithImpurity(PurityAnalysisResult result, SyntaxNode fallbackNode)
            {
                if (HasPotentialImpurity) return this;
                var node = result.ImpureSyntaxNode ?? fallbackNode;
                var evidence = result.Evidence.IsEmpty
                    ? PurityEvidence.Create("unsupported_operation", syntaxNode: node)
                    : result.Evidence.WithSyntax(node);
                return new PurityAnalysisState(true, node, this.DelegateTargetMap, this.FlowCaptures, this.FlowCaptureTargets, this.OwnedLocalArraySymbols, this.DefinitelyNullLocalSymbols, evidence, localConcreteTypes: this.LocalConcreteTypes, flowCaptureConcreteTypes: this.FlowCaptureConcreteTypes, pathConditions: this.PathConditions, flowCaptureSymbols: this.FlowCaptureSymbols);
            }

            public PurityAnalysisState WithDelegateTarget(ISymbol delegateSymbol, PotentialTargets targets)
            {

                var newMap = this.DelegateTargetMap.SetItem(delegateSymbol, targets);
                return new PurityAnalysisState(this.HasPotentialImpurity, this.FirstImpureSyntaxNode, newMap, this.FlowCaptures, this.FlowCaptureTargets, this.OwnedLocalArraySymbols, this.DefinitelyNullLocalSymbols, this.FirstImpurityEvidence, localConcreteTypes: this.LocalConcreteTypes, flowCaptureConcreteTypes: this.FlowCaptureConcreteTypes, pathConditions: this.PathConditions, flowCaptureSymbols: this.FlowCaptureSymbols);
            }

            public PurityAnalysisState WithoutDelegateTarget(ISymbol delegateSymbol)
            {
                if (!this.DelegateTargetMap.ContainsKey(delegateSymbol))
                {
                    return this;
                }

                var newMap = this.DelegateTargetMap.Remove(delegateSymbol);
                return new PurityAnalysisState(this.HasPotentialImpurity, this.FirstImpureSyntaxNode, newMap, this.FlowCaptures, this.FlowCaptureTargets, this.OwnedLocalArraySymbols, this.DefinitelyNullLocalSymbols, this.FirstImpurityEvidence, localConcreteTypes: this.LocalConcreteTypes, flowCaptureConcreteTypes: this.FlowCaptureConcreteTypes, pathConditions: this.PathConditions, flowCaptureSymbols: this.FlowCaptureSymbols);
            }

            public PurityAnalysisState WithFlowCaptureResult(CaptureId id, PurityAnalysisResult result)
            {
                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures.SetItem(id, result), FlowCaptureTargets, OwnedLocalArraySymbols, DefinitelyNullLocalSymbols, FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes, flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
            }

            public PurityAnalysisState WithFlowCaptureTarget(CaptureId id, PotentialTargets targets)
            {
                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets.SetItem(id, targets), OwnedLocalArraySymbols, DefinitelyNullLocalSymbols, FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes, flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
            }

            public PurityAnalysisState WithFlowCaptureConcreteType(CaptureId id, INamedTypeSymbol concreteType)
            {
                if (FlowCaptureConcreteTypes.TryGetValue(id, out var existingType) &&
                    SymbolEqualityComparer.Default.Equals(existingType, concreteType))
                {
                    return this;
                }

                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols, DefinitelyNullLocalSymbols, FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes, flowCaptureConcreteTypes: FlowCaptureConcreteTypes.SetItem(id, concreteType), pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
            }

            public PurityAnalysisState WithFlowCaptureSymbol(CaptureId id, ISymbol symbol)
            {
                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols, DefinitelyNullLocalSymbols, FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes, flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols.SetItem(id, symbol));
            }

            public PurityAnalysisState WithOwnedLocalArray(ISymbol localSymbol)
            {
                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols.Add(localSymbol), DefinitelyNullLocalSymbols.Remove(localSymbol), FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes, flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
            }

            public PurityAnalysisState WithoutOwnedLocalArray(ISymbol localSymbol)
            {
                if (!OwnedLocalArraySymbols.Contains(localSymbol))
                {
                    return this;
                }

                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols.Remove(localSymbol), DefinitelyNullLocalSymbols, FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes, flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
            }

            public bool IsOwnedLocalArraySymbol(ISymbol localSymbol)
            {
                return OwnedLocalArraySymbols.Contains(localSymbol);
            }

            public PurityAnalysisState WithDefinitelyNullLocal(ISymbol localSymbol)
            {
                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols.Remove(localSymbol), DefinitelyNullLocalSymbols.Add(localSymbol), FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes.Remove(localSymbol), flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
            }

            public PurityAnalysisState WithoutDefinitelyNullLocal(ISymbol localSymbol)
            {
                if (!DefinitelyNullLocalSymbols.Contains(localSymbol))
                {
                    return this;
                }

                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols, DefinitelyNullLocalSymbols.Remove(localSymbol), FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes, flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
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

                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols, DefinitelyNullLocalSymbols.Remove(localSymbol), FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes.SetItem(localSymbol, concreteType), flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
            }

            public PurityAnalysisState WithoutLocalConcreteType(ISymbol localSymbol)
            {
                if (!LocalConcreteTypes.ContainsKey(localSymbol))
                {
                    return this;
                }

                return new PurityAnalysisState(HasPotentialImpurity, FirstImpureSyntaxNode, DelegateTargetMap, FlowCaptures, FlowCaptureTargets, OwnedLocalArraySymbols, DefinitelyNullLocalSymbols, FirstImpurityEvidence, localConcreteTypes: LocalConcreteTypes.Remove(localSymbol), flowCaptureConcreteTypes: FlowCaptureConcreteTypes, pathConditions: PathConditions, flowCaptureSymbols: FlowCaptureSymbols);
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
                return new PurityAnalysisState(
                    HasPotentialImpurity,
                    FirstImpureSyntaxNode,
                    DelegateTargetMap,
                    FlowCaptures,
                    FlowCaptureTargets,
                    OwnedLocalArraySymbols,
                    DefinitelyNullLocalSymbols,
                    FirstImpurityEvidence,
                    localConcreteTypes: LocalConcreteTypes,
                    flowCaptureConcreteTypes: FlowCaptureConcreteTypes,
                    pathConditions: pathConditions,
                    flowCaptureSymbols: FlowCaptureSymbols);
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
                if (first.IsEmpty || second.IsEmpty)
                {
                    return ImmutableDictionary.Create<CaptureId, ISymbol>();
                }

                var builder = ImmutableDictionary.CreateBuilder<CaptureId, ISymbol>();
                foreach (var kvp in first)
                {
                    if (second.TryGetValue(kvp.Key, out var otherSymbol) &&
                        SymbolEqualityComparer.Default.Equals(kvp.Value, otherSymbol))
                    {
                        builder[kvp.Key] = kvp.Value;
                    }
                }

                return builder.ToImmutable();
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
                purityCache
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
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache)
        {

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

                if (HasImpureAttribute(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is marked [Impure].");
                    var syntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    var explicitlyImpureResult = ImpureResult(
                        syntax,
                        PurityEvidence.Create(
                            "impure_boundary_attribute",
                            syntaxNode: syntax,
                            symbol: methodSymbol,
                            catalogSource: "attribute"));
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
                    var syntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    var configuredImpureResult = ImpureResult(
                        syntax,
                        PurityEvidence.Create(
                            "catalog_hit",
                            "KnownImpureMethod",
                            syntaxNode: syntax,
                            symbol: methodSymbol,
                            catalogSource: "known_impure_namespace_or_type"));
                    purityCache[methodSymbol] = configuredImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Configured Impure Namespace/Type): {methodSymbol.ToDisplayString()}");
                    return configuredImpureResult;
                }

                if (IsKnownImpure(methodSymbol))
                {
                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is known impure.");
                    var syntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    var knownImpureResult = ImpureResult(
                        syntax,
                        PurityEvidence.Create(
                            "catalog_hit",
                            "KnownImpureMethod",
                            syntaxNode: syntax,
                            symbol: methodSymbol,
                            catalogSource: GetKnownImpureMemberSource(methodSymbol) ?? "known_impure"));
                    purityCache[methodSymbol] = knownImpureResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Known Impure): {methodSymbol.ToDisplayString()}");
                    return knownImpureResult;
                }

                if (methodSymbol.Locations.FirstOrDefault()?.IsInMetadata == true &&
                    TryGetTrustedGeneratedPurity(methodSymbol, semanticModel.Compilation, out var generatedPurity))
                {
                    if (generatedPurity.IsPure)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is trusted pure from generated purity summary.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        return PurityAnalysisResult.Pure;
                    }

                    if (generatedPurity.IsImpure)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is trusted impure from generated purity summary.");
                        var syntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                        var generatedResult = ImpureResult(
                            syntax,
                            PurityEvidence.Create(
                                generatedPurity.PrimaryCategory,
                                syntaxNode: syntax,
                                symbol: methodSymbol,
                                catalogSource: "generated_purity_summary"));
                        purityCache[methodSymbol] = generatedResult;
                        return generatedResult;
                    }
                }


                if (IsKnownPureBCLMember(methodSymbol))
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
                    var syntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    var externResult = ImpureResult(
                        syntax,
                        PurityEvidence.Create(
                            "unknown_external_call",
                            syntaxNode: syntax,
                            symbol: methodSymbol,
                            catalogSource: "extern"));
                    purityCache[methodSymbol] = externResult;
                    LogDebug($"{indent}<< Exit DeterminePurity (Extern): {methodSymbol.ToDisplayString()}");
                    return externResult;
                }

                if (methodSymbol.IsAbstract || bodySyntaxNode == null)
                {
                    if (methodSymbol.MethodKind == MethodKind.Constructor &&
                        !methodSymbol.IsExtern &&
                        methodSymbol.ContainingType?.Locations.Any(location => !location.IsInMetadata) == true)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source constructor without an explicit body. Treating as pure.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Constructor Without Body): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    if (methodSymbol.MethodKind == MethodKind.PropertyGet &&
                        !methodSymbol.IsAbstract &&
                        methodSymbol.ContainingType?.TypeKind != TypeKind.Interface &&
                        methodSymbol.DeclaringSyntaxReferences.Length > 0)
                    {
                        LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is a source property getter without an explicit body. Treating as pure.");
                        purityCache[methodSymbol] = PurityAnalysisResult.Pure;
                        LogDebug($"{indent}<< Exit DeterminePurity (Source Auto Getter): {methodSymbol.ToDisplayString()}");
                        return PurityAnalysisResult.Pure;
                    }

                    LogDebug($"{indent}Method {methodSymbol.ToDisplayString()} is abstract or has no body AND lacks trusted purity evidence. Assuming impure.");
                    var syntax = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
                    var noBodyResult = ImpureResult(
                        syntax,
                        PurityEvidence.Create(
                            "unknown_external_call",
                            syntaxNode: syntax,
                            symbol: methodSymbol,
                            catalogSource: "no_body"));
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
                            out mergedDelegateTargetsFromCfg,
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
                            localConcreteTypes: mergedLocalConcreteTypesFromCfg);
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
                            if (IsInStaticallyUnreachableBranch(firstThrowOp.Syntax, semanticModel))
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
                            if (IsInStaticallyUnreachableBranch(invocationOp.Syntax, semanticModel))
                            {
                                continue;
                            }

                            var hasSemanticKnownImpureCatalogSource = TryGetSemanticKnownImpureCatalogSource(
                                invocationOp,
                                out var semanticKnownImpureCatalogSource);
                            if (invocationOp.TargetMethod != null &&
                                (hasSemanticKnownImpureCatalogSource ||
                                 (IsKnownImpure(invocationOp.TargetMethod.OriginalDefinition) &&
                                  !IsInvariantCultureDeterministicParseInvocation(invocationOp))) &&
                                !IsArrayAsReadOnlyOwnedLocalArrayInvocation(invocationOp, postCfgReturnState) &&
                                !IsTransientCharArrayConsumedByStringConstructor(invocationOp, semanticModel))
                            {
                                LogDebug($"{indent}    Post-CFG: Found Known Impure Invocation IMPURE: {invocationOp.Syntax} calling {invocationOp.TargetMethod.ToDisplayString()}");
                                var targetMethod = invocationOp.TargetMethod.OriginalDefinition;
                                result = PurityAnalysisResult.Impure(
                                    invocationOp.Syntax,
                                    PurityEvidence.Create(
                                        "catalog_hit",
                                        "MethodInvocationPurityRule",
                                        invocationOp,
                                        symbol: targetMethod,
                                        catalogSource: hasSemanticKnownImpureCatalogSource
                                            ? semanticKnownImpureCatalogSource
                                            : GetKnownImpureMemberSource(targetMethod) ?? "known_impure"));
                                goto PostCfgChecksDone;
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


        private static ImmutableDictionary<ISymbol, PotentialTargets> MergeDelegateTargetMapsFromBlockStates(
            IEnumerable<PurityAnalysisState> states)
        {
            var map = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            foreach (var s in states)
            {
                foreach (var kvp in s.DelegateTargetMap)
                {
                    map = map.TryGetValue(kvp.Key, out var cur)
                        ? map.SetItem(kvp.Key, PotentialTargets.Merge(cur, kvp.Value))
                        : map.Add(kvp.Key, kvp.Value);
                }
            }
            return map;
        }

        private static ImmutableHashSet<ISymbol> MergeOwnedLocalArraySymbolsFromBlockStates(
            IEnumerable<PurityAnalysisState> states)
        {
            var builder = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);
            foreach (var state in states)
            {
                foreach (var symbol in state.OwnedLocalArraySymbols)
                {
                    builder.Add(symbol);
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<ISymbol, INamedTypeSymbol> MergeLocalConcreteTypesFromBlockStates(
            IEnumerable<PurityAnalysisState> states)
        {
            var builder = ImmutableDictionary.CreateBuilder<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            var conflictedSymbols = ImmutableHashSet.CreateBuilder<ISymbol>(SymbolEqualityComparer.Default);

            foreach (var state in states)
            {
                foreach (var kvp in state.LocalConcreteTypes)
                {
                    if (conflictedSymbols.Contains(kvp.Key))
                    {
                        continue;
                    }

                    if (builder.TryGetValue(kvp.Key, out var existingType) &&
                        !SymbolEqualityComparer.Default.Equals(existingType, kvp.Value))
                    {
                        builder.Remove(kvp.Key);
                        conflictedSymbols.Add(kvp.Key);
                        continue;
                    }

                    builder[kvp.Key] = kvp.Value;
                }
            }

            return builder.ToImmutable();
        }

        private static PurityAnalysisResult AnalyzePurityUsingCFGInternal(
            SyntaxNode bodyNode,
            SemanticModel semanticModel,
            INamedTypeSymbol enforcePureAttributeSymbol,
            INamedTypeSymbol? allowSynchronizationAttributeSymbol,
            HashSet<IMethodSymbol> visited,
            IMethodSymbol containingMethodSymbol,
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache,
            out ImmutableDictionary<ISymbol, PotentialTargets> mergedDelegateTargetsFromBlocks,
            out ImmutableHashSet<ISymbol> mergedOwnedLocalArraysFromBlocks,
            out ImmutableDictionary<ISymbol, INamedTypeSymbol> mergedLocalConcreteTypesFromBlocks)
        {
            mergedDelegateTargetsFromBlocks = ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
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
                    purityCache);

                exitBlockStates[currentBlock] = stateAfter;
                LogDebug($"  [CFG] State after Block #{currentBlock.Ordinal}: Impure={stateAfter.HasPotentialImpurity}");



                LogDebug($"  [CFG] Propagating stateAfter (Impure={stateAfter.HasPotentialImpurity}) to successors of Block #{currentBlock.Ordinal}.");
                if (TryGetConstantBranchDecision(currentBlock.BranchValue, semanticModel, out var takeConditionalSuccessor))
                {
                    var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);
                    var takenSuccessor = takeConditionalSuccessor
                        ? (trueUsesConditionalSuccessor
                            ? currentBlock.ConditionalSuccessor?.Destination
                            : currentBlock.FallThroughSuccessor?.Destination)
                        : (trueUsesConditionalSuccessor
                            ? currentBlock.FallThroughSuccessor?.Destination
                            : currentBlock.ConditionalSuccessor?.Destination);
                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, takeConditionalSuccessor, out var takenState))
                    {
                        PropagateToSuccessor(takenSuccessor, takenState, blockStates, worklist, inQueue);
                    }
                }
                else
                {
                    var trueUsesConditionalSuccessor = BranchTrueUsesConditionalSuccessor(currentBlock.BranchValue);

                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, trueUsesConditionalSuccessor, out var conditionalState))
                    {
                        PropagateToSuccessor(currentBlock.ConditionalSuccessor?.Destination, conditionalState, blockStates, worklist, inQueue);
                    }

                    if (TryCreateSuccessorState(stateAfter, currentBlock.BranchValue, semanticModel, !trueUsesConditionalSuccessor, out var fallThroughState))
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
            Dictionary<IMethodSymbol, PurityAnalysisResult> purityCache)
        {
            LogDebug($"ApplyTransferFunction START for Block #{block.Ordinal} - Initial State: Impure={stateBefore.HasPotentialImpurity}");

            if (stateBefore.HasPotentialImpurity)
            {
                LogDebug($"ApplyTransferFunction SKIP for Block #{block.Ordinal} - Already impure.");
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
                null);


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

        private static bool IsNestedFunctionDescendant(IOperation operation, IOperation rootOperation)
        {
            if (operation == rootOperation)
            {
                return false;
            }

            for (var parent = operation.Parent; parent != null && parent != rootOperation; parent = parent.Parent)
            {
                if (parent is IAnonymousFunctionOperation || parent is IFlowAnonymousFunctionOperation || parent is ILocalFunctionOperation)
                {
                    return true;
                }
            }

            return false;
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
                if (ExecutionVisibility.IsConditionAlwaysTrue(expressionSyntax, semanticModel))
                {
                    takeConditionalSuccessor = true;
                    return true;
                }

                if (ExecutionVisibility.IsConditionAlwaysFalse(expressionSyntax, semanticModel))
                {
                    takeConditionalSuccessor = false;
                    return true;
                }
            }

            return false;
        }

        private static bool BranchTrueUsesConditionalSuccessor(IOperation? branchValue)
        {
            return branchValue?.Syntax is ExpressionSyntax expressionSyntax &&
                !ShouldAnalyzeExplicitConditionBranchValue(expressionSyntax);
        }

        private static bool TryCreateSuccessorState(
            PurityAnalysisState currentState,
            IOperation? branchValue,
            SemanticModel semanticModel,
            bool takeConditionalSuccessor,
            out PurityAnalysisState successorState)
        {
            successorState = currentState;

            if (branchValue?.Syntax is not ExpressionSyntax expressionSyntax)
            {
                return true;
            }

            if (!TryTranslateBranchValueToFormula(branchValue, currentState, out var formula) &&
                (!CSharpConditionToFormula.TryTranslate(expressionSyntax, semanticModel, CancellationToken.None, out formula) ||
                 formula == null))
            {
                return true;
            }

            var edgeFormula = takeConditionalSuccessor
                ? formula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, formula);
            var nextPathConditions = currentState.PathConditions.Add(edgeFormula);
            var proofPathConditions = AppendDefinitelyNullFacts(currentState, currentState.PathConditions);
            var branchQuery = new PurityProofQuery(
                proofPathConditions,
                new PurityHazard(PurityHazardKind.BranchReachability, edgeFormula));

            using var search = new PurityProofSearch();
            var proofResult = search.Classify(branchQuery, SmtTimeout);
            if (proofResult.Outcome == PurityProofOutcome.ProvablyPure)
            {
                return false;
            }

            successorState = currentState.WithPathConditions(nextPathConditions);
            return true;
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
                formula = new SmtVariable(GetSmtVariableName(localSymbol), SmtValueKind.Reference);
                return true;
            }

            if (TryResolveTrackedSymbol(operation, currentState) is IParameterSymbol parameterSymbol &&
                parameterSymbol.Type?.IsReferenceType == true)
            {
                formula = new SmtVariable(GetSmtVariableName(parameterSymbol), SmtValueKind.Reference);
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
                    new SmtVariable(GetSmtVariableName(localSymbol), SmtValueKind.Reference),
                    new SmtNullConstant()));
            }

            return builder.ToImmutable();
        }

        private static string GetSmtVariableName(ISymbol symbol)
        {
            var start = symbol.Locations.FirstOrDefault()?.SourceSpan.Start ?? 0;
            return symbol.Name + "#" + start.ToString(CultureInfo.InvariantCulture);
        }

        private static bool IsInStaticallyUnreachableBranch(SyntaxNode syntaxNode, SemanticModel semanticModel)
        {
            if (ExecutionVisibility.IsInStaticallyUnreachableBranch(syntaxNode, semanticModel))
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

            var pathConditions = CollectAncestorReachabilityConditions(syntaxNode, semanticModel);
            if (pathConditions.Length == 0)
            {
                return false;
            }

            var query = new PurityProofQuery(
                pathConditions,
                new PurityHazard(PurityHazardKind.BranchReachability, new SmtBooleanConstant(true)));

            using var search = new PurityProofSearch();
            var proofResult = search.Classify(query, SmtTimeout);
            return proofResult.Outcome == PurityProofOutcome.ProvablyPure;
        }

        private static ImmutableArray<SmtFormula> CollectAncestorReachabilityConditions(
            SyntaxNode syntaxNode,
            SemanticModel semanticModel)
        {
            var builder = ImmutableArray.CreateBuilder<SmtFormula>();

            foreach (var ancestor in syntaxNode.Ancestors())
            {
                if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax ifStatementSyntax)
                {
                    if (ifStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                    {
                        AddReachabilityCondition(builder, ifStatementSyntax.Condition, mustBeTrue: true, semanticModel);
                    }
                    else if (ifStatementSyntax.Else?.Statement.Span.Contains(syntaxNode.Span) == true)
                    {
                        AddReachabilityCondition(builder, ifStatementSyntax.Condition, mustBeTrue: false, semanticModel);
                    }
                }
                else if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.ConditionalExpressionSyntax conditionalExpressionSyntax)
                {
                    if (conditionalExpressionSyntax.WhenTrue.Span.Contains(syntaxNode.Span))
                    {
                        AddReachabilityCondition(builder, conditionalExpressionSyntax.Condition, mustBeTrue: true, semanticModel);
                    }
                    else if (conditionalExpressionSyntax.WhenFalse.Span.Contains(syntaxNode.Span))
                    {
                        AddReachabilityCondition(builder, conditionalExpressionSyntax.Condition, mustBeTrue: false, semanticModel);
                    }
                }
                else if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.BinaryExpressionSyntax binaryExpressionSyntax &&
                         binaryExpressionSyntax.Right.Span.Contains(syntaxNode.Span))
                {
                    if (binaryExpressionSyntax.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalAndExpression))
                    {
                        AddReachabilityCondition(builder, binaryExpressionSyntax.Left, mustBeTrue: true, semanticModel);
                    }
                    else if (binaryExpressionSyntax.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.LogicalOrExpression))
                    {
                        AddReachabilityCondition(builder, binaryExpressionSyntax.Left, mustBeTrue: false, semanticModel);
                    }
                }
                else if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.WhileStatementSyntax whileStatementSyntax &&
                         whileStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    AddReachabilityCondition(builder, whileStatementSyntax.Condition, mustBeTrue: true, semanticModel);
                }
                else if (ancestor is Microsoft.CodeAnalysis.CSharp.Syntax.ForStatementSyntax forStatementSyntax &&
                         forStatementSyntax.Condition != null &&
                         forStatementSyntax.Statement.Span.Contains(syntaxNode.Span))
                {
                    AddReachabilityCondition(builder, forStatementSyntax.Condition, mustBeTrue: true, semanticModel);
                }
            }

            return builder.ToImmutable();
        }

        private static void AddReachabilityCondition(
            ImmutableArray<SmtFormula>.Builder builder,
            ExpressionSyntax expressionSyntax,
            bool mustBeTrue,
            SemanticModel semanticModel)
        {
            if (!CSharpConditionToFormula.TryTranslate(expressionSyntax, semanticModel, CancellationToken.None, out var formula) ||
                formula == null)
            {
                return;
            }

            builder.Add(mustBeTrue
                ? formula
                : new SmtUnaryFormula(SmtUnaryOperator.Not, formula));
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

            if (IsInStaticallyUnreachableBranch(operation.Syntax, context.SemanticModel))
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


                    if (IsKnownPureBCLMember(operatorMethod))
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






        internal static bool IsKnownPureBCLMember(ISymbol symbol) => ImpurityCatalog.IsKnownPureBCLMember(symbol);
        internal static bool IsStrictPurityProfile => ImpurityCatalog.IsStrictPurityProfile;

        internal static bool IsKnownPureBCLArrayFactoryOperation(IOperation? operation, out IMethodSymbol factoryMethod)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            if (unwrappedOperation is IInvocationOperation invocation &&
                invocation.Type is IArrayTypeSymbol &&
                !IsArrayEmptyFactory(invocation.TargetMethod.OriginalDefinition) &&
                IsKnownPureBCLMember(invocation.TargetMethod.OriginalDefinition))
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
            out INamedTypeSymbol concreteType)
        {
            operation = SkipImplicitConversions(operation);

            while (operation is IParenthesizedOperation parenthesizedOperation)
            {
                operation = SkipImplicitConversions(parenthesizedOperation.Operand);
            }

            if (operation is IConversionOperation conversionOperation)
            {
                return TryResolveKnownConcreteType(conversionOperation.Operand, currentState, out concreteType);
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
                TryResolveKnownConcreteType(conditionalOperation.WhenTrue, currentState, out var whenTrueType) &&
                TryResolveKnownConcreteType(conditionalOperation.WhenFalse, currentState, out var whenFalseType) &&
                SymbolEqualityComparer.Default.Equals(whenTrueType, whenFalseType))
            {
                concreteType = whenTrueType;
                return true;
            }

            if (operation is ICoalesceOperation coalesceOperation &&
                TryResolveKnownConcreteType(coalesceOperation.Value, currentState, out var coalesceValueType) &&
                TryResolveKnownConcreteType(coalesceOperation.WhenNull, currentState, out var coalesceWhenNullType) &&
                SymbolEqualityComparer.Default.Equals(coalesceValueType, coalesceWhenNullType))
            {
                concreteType = coalesceValueType;
                return true;
            }

            concreteType = null!;
            return false;
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

        internal static bool TryGetTrustedGeneratedPurity(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            out GeneratedPurityCatalog.PurityEntry purity)
        {
            return GeneratedPurityCatalog.Current.TryGetPurity(methodSymbol, compilation, out purity);
        }

        internal static bool IsTrustedGeneratedFreshOwnedArrayReturningMember(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            return TryGetTrustedGeneratedPurity(methodSymbol, compilation, out var purity) &&
                purity.IsPure &&
                purity.IsFreshArrayCandidate;
        }

        internal static bool IsKnownFreshOwnedArrayReturningMember(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            if (methodSymbol == null)
            {
                return false;
            }

            if (IsTrustedGeneratedFreshOwnedArrayReturningMember(methodSymbol, compilation))
            {
                return true;
            }

            var signature = methodSymbol.OriginalDefinition.ToDisplayString();
            if (Constants.KnownFreshOwnedArrayReturningMembers.Contains(signature))
            {
                return true;
            }

            if (methodSymbol.ContainingType?.SpecialType == SpecialType.System_String &&
                signature.StartsWith("string.", StringComparison.Ordinal) &&
                Constants.KnownFreshOwnedArrayReturningMembers.Contains("System.String." + signature.Substring("string.".Length)))
            {
                return true;
            }

            return methodSymbol.IsGenericMethod &&
                Constants.KnownFreshOwnedArrayReturningMembers.Contains(methodSymbol.ConstructedFrom.ToDisplayString());
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


        private static bool HasAttributeNamed(ISymbol symbol, string attributeName, string fullyQualifiedMetadataName)
        {
            if (symbol == null)
            {
                return false;
            }

            return HasDirectAttributeNamed(symbol, attributeName, fullyQualifiedMetadataName) ||
                HasAssemblyAttributeNamed(symbol, attributeName, fullyQualifiedMetadataName);
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


        private static PurityAnalysisState MergeStates(PurityAnalysisState state1, PurityAnalysisState state2)
        {
            LogDebug($"  [Merge] Merging States: S1(Impure={state1.HasPotentialImpurity}, MapCount={state1.DelegateTargetMap.Count}) + S2(Impure={state2.HasPotentialImpurity}, MapCount={state2.DelegateTargetMap.Count})");
            bool mergedImpurity = state1.HasPotentialImpurity || state2.HasPotentialImpurity;
            SyntaxNode? firstImpureNode = state1.FirstImpureSyntaxNode;
            PurityEvidence firstImpurityEvidence = state1.FirstImpurityEvidence;
            if (state1.HasPotentialImpurity && state2.HasPotentialImpurity && state1.FirstImpureSyntaxNode != null && state2.FirstImpureSyntaxNode != null)
            {

                if (state2.FirstImpureSyntaxNode.SpanStart < state1.FirstImpureSyntaxNode.SpanStart)
                {
                    firstImpureNode = state2.FirstImpureSyntaxNode;
                    firstImpurityEvidence = state2.FirstImpurityEvidence;
                }
            }
            else if (state2.HasPotentialImpurity)
            {
                firstImpureNode = state2.FirstImpureSyntaxNode;
                firstImpurityEvidence = state2.FirstImpurityEvidence;
            }


            var finalMap = IntersectDelegateTargetMaps(state1.DelegateTargetMap, state2.DelegateTargetMap);

            var mergedCaptures = PurityAnalysisState.MergeFlowCaptureMapsForPair(state1.FlowCaptures, state2.FlowCaptures);
            var mergedCaptureTargets = IntersectFlowCaptureTargetMaps(state1.FlowCaptureTargets, state2.FlowCaptureTargets);
            var mergedCaptureConcreteTypes = IntersectFlowCaptureConcreteTypes(state1.FlowCaptureConcreteTypes, state2.FlowCaptureConcreteTypes);
            var mergedCaptureSymbols = IntersectFlowCaptureSymbols(state1.FlowCaptureSymbols, state2.FlowCaptureSymbols);
            var mergedOwnedLocalArrays = IntersectOwnedLocalArraySymbols(state1.OwnedLocalArraySymbols, state2.OwnedLocalArraySymbols);
            var mergedDefinitelyNullLocals = IntersectOwnedLocalArraySymbols(state1.DefinitelyNullLocalSymbols, state2.DefinitelyNullLocalSymbols);
            var mergedLocalConcreteTypes = IntersectLocalConcreteTypes(state1.LocalConcreteTypes, state2.LocalConcreteTypes);

            return new PurityAnalysisState(mergedImpurity, firstImpureNode, finalMap, mergedCaptures, mergedCaptureTargets, mergedOwnedLocalArrays, mergedDefinitelyNullLocals, firstImpurityEvidence, localConcreteTypes: mergedLocalConcreteTypes, flowCaptureConcreteTypes: mergedCaptureConcreteTypes, pathConditions: MergePathConditions(state1.PathConditions, state2.PathConditions), flowCaptureSymbols: mergedCaptureSymbols);
        }

        private static ImmutableArray<SmtFormula> MergePathConditions(
            ImmutableArray<SmtFormula> first,
            ImmutableArray<SmtFormula> second)
        {
            if (first.Length != second.Length)
            {
                return ImmutableArray<SmtFormula>.Empty;
            }

            for (var i = 0; i < first.Length; i++)
            {
                if (!Equals(first[i], second[i]))
                {
                    return ImmutableArray<SmtFormula>.Empty;
                }
            }

            return first;
        }

        private static ImmutableArray<SmtFormula> MergePathConditionsAcrossAll(
            IEnumerable<ImmutableArray<SmtFormula>> pathConditionSets)
        {
            using var enumerator = pathConditionSets.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return ImmutableArray<SmtFormula>.Empty;
            }

            var merged = enumerator.Current;
            while (enumerator.MoveNext())
            {
                merged = MergePathConditions(merged, enumerator.Current);
                if (merged.IsEmpty)
                {
                    return merged;
                }
            }

            return merged;
        }

        private static ImmutableDictionary<ISymbol, PotentialTargets> MergeDelegateTargetMapsAcrossAll(
            IEnumerable<ImmutableDictionary<ISymbol, PotentialTargets>> maps)
        {
            using var enumerator = maps.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            }

            var merged = enumerator.Current;
            while (enumerator.MoveNext())
            {
                merged = IntersectDelegateTargetMaps(merged, enumerator.Current);
            }

            return merged;
        }

        private static ImmutableHashSet<ISymbol> IntersectOwnedLocalArraySymbols(
            ImmutableHashSet<ISymbol> first,
            ImmutableHashSet<ISymbol> second)
        {
            return ImmutableHashSet.CreateRange<ISymbol>(
                SymbolEqualityComparer.Default,
                first.Intersect(second, SymbolEqualityComparer.Default));
        }

        private static ImmutableHashSet<ISymbol> IntersectOwnedLocalArraySymbolsAcrossAll(
            IEnumerable<ImmutableHashSet<ISymbol>> symbolSets)
        {
            using var enumerator = symbolSets.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return ImmutableHashSet.Create<ISymbol>(SymbolEqualityComparer.Default);
            }

            var merged = enumerator.Current;
            while (enumerator.MoveNext())
            {
                merged = IntersectOwnedLocalArraySymbols(merged, enumerator.Current);
            }

            return merged;
        }

        private static ImmutableDictionary<CaptureId, ISymbol> IntersectFlowCaptureSymbols(
            ImmutableDictionary<CaptureId, ISymbol> first,
            ImmutableDictionary<CaptureId, ISymbol> second)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return ImmutableDictionary.Create<CaptureId, ISymbol>();
            }

            var builder = ImmutableDictionary.CreateBuilder<CaptureId, ISymbol>();
            foreach (var kvp in first)
            {
                if (second.TryGetValue(kvp.Key, out var otherSymbol) &&
                    SymbolEqualityComparer.Default.Equals(kvp.Value, otherSymbol))
                {
                    builder[kvp.Key] = kvp.Value;
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<ISymbol, INamedTypeSymbol> IntersectLocalConcreteTypes(
            ImmutableDictionary<ISymbol, INamedTypeSymbol> first,
            ImmutableDictionary<ISymbol, INamedTypeSymbol> second)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            }

            var builder = ImmutableDictionary.CreateBuilder<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var kvp in first)
            {
                if (second.TryGetValue(kvp.Key, out var otherType) &&
                    SymbolEqualityComparer.Default.Equals(kvp.Value, otherType))
                {
                    builder[kvp.Key] = kvp.Value;
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<ISymbol, INamedTypeSymbol> IntersectLocalConcreteTypesAcrossAll(
            IEnumerable<ImmutableDictionary<ISymbol, INamedTypeSymbol>> maps)
        {
            using var enumerator = maps.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return ImmutableDictionary.Create<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default);
            }

            var merged = enumerator.Current;
            while (enumerator.MoveNext())
            {
                merged = IntersectLocalConcreteTypes(merged, enumerator.Current);
            }

            return merged;
        }

        private static ImmutableDictionary<CaptureId, INamedTypeSymbol> IntersectFlowCaptureConcreteTypes(
            ImmutableDictionary<CaptureId, INamedTypeSymbol> first,
            ImmutableDictionary<CaptureId, INamedTypeSymbol> second)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return ImmutableDictionary<CaptureId, INamedTypeSymbol>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<CaptureId, INamedTypeSymbol>();
            foreach (var kvp in first)
            {
                if (second.TryGetValue(kvp.Key, out var otherType) &&
                    SymbolEqualityComparer.Default.Equals(kvp.Value, otherType))
                {
                    builder[kvp.Key] = kvp.Value;
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<CaptureId, INamedTypeSymbol> IntersectFlowCaptureConcreteTypesAcrossAll(
            IEnumerable<ImmutableDictionary<CaptureId, INamedTypeSymbol>> maps)
        {
            using var enumerator = maps.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return ImmutableDictionary<CaptureId, INamedTypeSymbol>.Empty;
            }

            var merged = enumerator.Current;
            while (enumerator.MoveNext())
            {
                merged = IntersectFlowCaptureConcreteTypes(merged, enumerator.Current);
            }

            return merged;
        }

        private static ImmutableDictionary<ISymbol, PotentialTargets> IntersectDelegateTargetMaps(
            ImmutableDictionary<ISymbol, PotentialTargets> first,
            ImmutableDictionary<ISymbol, PotentialTargets> second)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return ImmutableDictionary.Create<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            }

            var builder = ImmutableDictionary.CreateBuilder<ISymbol, PotentialTargets>(SymbolEqualityComparer.Default);
            foreach (var kvp in first)
            {
                if (second.TryGetValue(kvp.Key, out var otherTargets))
                {
                    builder[kvp.Key] = PotentialTargets.Merge(kvp.Value, otherTargets);
                }
            }

            return builder.ToImmutable();
        }

        private static ImmutableDictionary<CaptureId, PotentialTargets> MergeFlowCaptureTargetMapsAcrossAll(
            IEnumerable<ImmutableDictionary<CaptureId, PotentialTargets>> maps)
        {
            using var enumerator = maps.GetEnumerator();
            if (!enumerator.MoveNext())
            {
                return ImmutableDictionary<CaptureId, PotentialTargets>.Empty;
            }

            var merged = enumerator.Current;
            while (enumerator.MoveNext())
            {
                merged = IntersectFlowCaptureTargetMaps(merged, enumerator.Current);
            }

            return merged;
        }

        private static ImmutableDictionary<CaptureId, PotentialTargets> IntersectFlowCaptureTargetMaps(
            ImmutableDictionary<CaptureId, PotentialTargets> first,
            ImmutableDictionary<CaptureId, PotentialTargets> second)
        {
            if (first.IsEmpty || second.IsEmpty)
            {
                return ImmutableDictionary<CaptureId, PotentialTargets>.Empty;
            }

            var builder = ImmutableDictionary.CreateBuilder<CaptureId, PotentialTargets>();
            foreach (var kvp in first)
            {
                if (second.TryGetValue(kvp.Key, out var otherTargets))
                {
                    builder[kvp.Key] = PotentialTargets.Merge(kvp.Value, otherTargets);
                }
            }

            return builder.ToImmutable();
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
                    cctorResult.ImpureSyntaxNode ?? typeSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? ImpureResult(null).ImpureSyntaxNode ?? context.ContainingMethodSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() ?? throw new InvalidOperationException("Cannot find syntax node for static constructor impurity"),
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

                    if (targetSymbol is ILocalSymbol coalesceLocalSymbol &&
                        currentState.IsDefinitelyNullLocalSymbol(coalesceLocalSymbol))
                    {
                        foreach (var assignedLocalSymbol in writtenLocalSymbols)
                        {
                            if (TryResolveKnownConcreteType(valueOperation, currentState, out var concreteType))
                            {
                                nextState = nextState.WithLocalConcreteType(assignedLocalSymbol, concreteType);
                            }
                            else
                            {
                                nextState = nextState.WithoutLocalConcreteType(assignedLocalSymbol);
                            }
                        }

                        foreach (var localSymbol in writtenLocalSymbols)
                        {
                            if (IsOwnedLocalArrayValue(valueOperation, currentState, context.SemanticModel.Compilation))
                            {
                                nextState = nextState.WithOwnedLocalArray(localSymbol);
                            }
                            else
                            {
                                nextState = nextState.WithoutOwnedLocalArray(localSymbol);
                            }
                        }

                        foreach (var localSymbol in writtenLocalSymbols)
                        {
                            if (IsDefinitelyNullValue(valueOperation, currentState))
                            {
                                nextState = nextState.WithDefinitelyNullLocal(localSymbol);
                            }
                            else
                            {
                                nextState = nextState.WithoutDefinitelyNullLocal(localSymbol);
                            }
                        }

                        if (valueOperation != null && targetSymbol != null && targetOperation.Type?.TypeKind == TypeKind.Delegate)
                        {
                            PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(valueOperation, currentState);
                            if (valueTargets != null)
                            {
                                foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                                {
                                    nextState = nextState.WithDelegateTarget(writtenTargetSymbol, valueTargets.Value);
                                    LogDebug($"    [ATF-DEL-COALESCE] Updated map for {writtenTargetSymbol.Name} with {valueTargets.Value.MethodSymbols.Count} targets. New Map Count: {nextState.DelegateTargetMap.Count}");
                                }
                            }
                            else
                            {
                                foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                                {
                                    nextState = nextState.WithDelegateTarget(writtenTargetSymbol, PotentialTargets.Unresolved);
                                    LogDebug($"    [ATF-DEL-COALESCE] Marked map for {writtenTargetSymbol.Name} unresolved because coalesce-assigned value targets are unresolved. New Map Count: {nextState.DelegateTargetMap.Count}");
                                }
                            }
                        }
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

                    foreach (var assignedLocalSymbol in writtenLocalSymbols)
                    {
                        if (TryResolveKnownConcreteType(valueOperation, currentState, out var concreteType))
                        {
                            nextState = nextState.WithLocalConcreteType(assignedLocalSymbol, concreteType);
                        }
                        else
                        {
                            nextState = nextState.WithoutLocalConcreteType(assignedLocalSymbol);
                        }
                    }

                    foreach (var localSymbol in writtenLocalSymbols)
                    {
                        if (IsOwnedLocalArrayValue(valueOperation, currentState, context.SemanticModel.Compilation))
                        {
                            nextState = nextState.WithOwnedLocalArray(localSymbol);
                        }
                        else
                        {
                            nextState = nextState.WithoutOwnedLocalArray(localSymbol);
                        }
                    }

                    foreach (var localSymbol in writtenLocalSymbols)
                    {
                        if (IsDefinitelyNullValue(valueOperation, currentState))
                        {
                            nextState = nextState.WithDefinitelyNullLocal(localSymbol);
                        }
                        else
                        {
                            nextState = nextState.WithoutDefinitelyNullLocal(localSymbol);
                        }
                    }

                    if (valueOperation != null && targetSymbol != null && targetOperation.Type?.TypeKind == TypeKind.Delegate)
                    {
                        PurityAnalysisEngine.PotentialTargets? valueTargets = ResolvePotentialTargets(valueOperation, currentState);
                        if (valueTargets != null)
                        {
                            foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                            {
                                nextState = nextState.WithDelegateTarget(writtenTargetSymbol, valueTargets.Value);
                                LogDebug($"    [ATF-DEL-ASSIGN] Updated map for {writtenTargetSymbol.Name} with {valueTargets.Value.MethodSymbols.Count} targets. New Map Count: {nextState.DelegateTargetMap.Count}");
                            }
                        }
                        else
                        {
                            foreach (var writtenTargetSymbol in GetAssignmentTargetSymbols(targetSymbol, writtenLocalSymbols))
                            {
                                nextState = nextState.WithDelegateTarget(writtenTargetSymbol, PotentialTargets.Unresolved);
                                LogDebug($"    [ATF-DEL-ASSIGN] Marked map for {writtenTargetSymbol.Name} unresolved because assigned value targets are unresolved. New Map Count: {nextState.DelegateTargetMap.Count}");
                            }
                        }
                    }
                }

                  else if (operationToTrack is IInvocationOperation invocationOperation)
                {
                    foreach (var argument in invocationOperation.Arguments)
                    {
                        if (argument.Parameter?.RefKind is not (RefKind.Ref or RefKind.Out))
                        {
                            continue;
                        }

                        if (TryResolveTrackedSymbol(SkipImplicitConversions(argument.Value), currentState) is ILocalSymbol localSymbol)
                        {
                            foreach (var writtenLocalSymbol in EnumerateWrittenLocalSymbols(localSymbol, context))
                            {
                                nextState = nextState
                                    .WithoutLocalConcreteType(writtenLocalSymbol)
                                    .WithoutOwnedLocalArray(writtenLocalSymbol)
                                    .WithoutDefinitelyNullLocal(writtenLocalSymbol);

                                if (writtenLocalSymbol.Type?.TypeKind == TypeKind.Delegate)
                                {
                                    nextState = nextState.WithDelegateTarget(writtenLocalSymbol, PotentialTargets.Unresolved);
                                }
                            }
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

                    if (TryResolveKnownConcreteType(flowCaptureOperation.Value, currentState, out var concreteType))
                    {
                        nextState = nextState.WithFlowCaptureConcreteType(flowCaptureOperation.Id, concreteType);
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

                                if (TryResolveKnownConcreteType(initializerValue, nextState, out var concreteType))
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
                            }
                        }
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

            if (unwrappedValue is IArrayCreationOperation ||
                IsArrayCollectionExpressionOperation(unwrappedValue) ||
                IsKnownPureBCLArrayFactoryOperation(unwrappedValue, out _))
            {
                return true;
            }

            if (unwrappedValue is IInvocationOperation invocationOperation &&
                invocationOperation.Type is IArrayTypeSymbol &&
                IsKnownFreshOwnedArrayReturningMember(invocationOperation.TargetMethod.OriginalDefinition, compilation))
            {
                return true;
            }

            return unwrappedValue is ILocalReferenceOperation localReference &&
                   currentState.IsOwnedLocalArraySymbol(localReference.Local);
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

        internal static bool IsArrayAsReadOnlyOwnedLocalArrayInvocation(
            IInvocationOperation invocationOperation,
            PurityAnalysisState currentState)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                targetMethod.Name != "AsReadOnly" ||
                targetMethod.ContainingType?.ToDisplayString() != "System.Array" ||
                invocationOperation.Arguments.Length != 1)
            {
                return false;
            }

            var argumentValue = UnwrapArrayOwnershipPreservingConversions(invocationOperation.Arguments[0].Value);
            if (IsArrayEmptyInvocation(argumentValue))
            {
                return true;
            }

            return argumentValue is ILocalReferenceOperation localReference &&
                   currentState.IsOwnedLocalArraySymbol(localReference.Local);
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
                return false;
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

        private static bool IsCurrentCultureSensitiveDateLikeParseOrFormatInvocation(IInvocationOperation invocationOperation)
        {
            var targetMethod = invocationOperation.TargetMethod?.OriginalDefinition;
            if (targetMethod == null ||
                !IsCultureSensitiveDateLikeType(targetMethod.ContainingType))
            {
                return false;
            }

            if (targetMethod.Name == "Parse" &&
                invocationOperation.Arguments.Length == 1 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return true;
            }

            if (targetMethod.Name == "TryParse" &&
                invocationOperation.Arguments.Length == 2 &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type))
            {
                return true;
            }

            if ((targetMethod.Name == "ParseExact" || targetMethod.Name == "TryParseExact") &&
                IsDateOnlyOrTimeOnlyType(targetMethod.ContainingType) &&
                IsStringOrReadOnlySpanOfChar(targetMethod.Parameters[0].Type) &&
                IsFormatSpecifierType(targetMethod.Parameters[1].Type))
            {
                return invocationOperation.Arguments.Length == (targetMethod.Name == "ParseExact" ? 2 : 3);
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

        private static bool IsTimeSpanStylesNone(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is int styles &&
                styles == 0;
        }

        private static bool IsDateTimeStylesNone(IOperation? operation)
        {
            var unwrappedOperation = SkipImplicitConversions(operation);
            return unwrappedOperation?.ConstantValue.HasValue == true &&
                unwrappedOperation.ConstantValue.Value is int styles &&
                styles == 0;
        }

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
