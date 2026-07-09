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
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Analyzer.Engine.Rules;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;
using SharpProof.Symbolic.Smt;
using SearchLib.Smt;
using System.Threading;

namespace SharpProof.Analyzer.Engine
{

    internal partial class PurityAnalysisEngine
    {

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
                return builder.ToImmutable();
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
                if (string.IsNullOrEmpty(category))
                {
                    return "unknown";
                }

                return category switch
                {
                    "unknown_external_call" => "unverified external call",
                    "bcl_fallback_probably_pure" => "unverified framework metadata member",
                    "bcl_fallback_probably_impure" => "unverified framework metadata member",
                    "bcl_fallback_unknown" => "unverified framework metadata member",
                    _ => category,
                };
            }

            private static void AddIfPresent(ImmutableDictionary<string, string?>.Builder builder, string key, string value)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    builder[key] = value;
                }
            }
        }






    }
}
