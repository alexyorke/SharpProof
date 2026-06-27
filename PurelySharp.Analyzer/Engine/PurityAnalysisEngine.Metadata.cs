using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace PurelySharp.Analyzer.Engine
{
    internal partial class PurityAnalysisEngine
    {
        internal static PurityAnalysisResult ImpureResult(
            IOperation operation,
            string category,
            string? ruleName = null,
            ISymbol? symbol = null,
            string? catalogSource = null)
        {
            return PurityAnalysisResult.Impure(
                operation.Syntax,
                PurityEvidence.Create(
                    category,
                    ruleName,
                    operation,
                    symbol: symbol,
                    catalogSource: catalogSource));
        }

        internal static PurityAnalysisResult ImpureResult(
            SyntaxNode? syntaxNode,
            string category,
            string? ruleName = null,
            ISymbol? symbol = null,
            string? catalogSource = null)
        {
            return ImpureResult(
                syntaxNode,
                PurityEvidence.Create(
                    category,
                    ruleName: ruleName,
                    syntaxNode: syntaxNode,
                    symbol: symbol,
                    catalogSource: catalogSource));
        }

        internal static bool TryGetTrustedGeneratedPurity(
            IMethodSymbol methodSymbol,
            Compilation compilation,
            out GeneratedPurityCatalog.PurityEntry purity)
        {
            return GeneratedPurityCatalog.Current.TryGetPurity(methodSymbol, compilation, out purity);
        }

        internal readonly struct TrustedMethodPurityMetadata
        {
            public TrustedMethodPurityMetadata(
                string? knownImpureMemberSource,
                bool hasTrustedGeneratedPurity,
                GeneratedPurityCatalog.PurityEntry generatedPurity)
            {
                KnownImpureMemberSource = knownImpureMemberSource;
                HasTrustedGeneratedPurity = hasTrustedGeneratedPurity;
                GeneratedPurity = generatedPurity;
            }

            public string? KnownImpureMemberSource { get; }
            public bool HasConfiguredKnownImpureMember =>
                string.Equals(KnownImpureMemberSource, "config_known_impure", StringComparison.Ordinal);
            public bool HasTrustedGeneratedPurity { get; }
            public GeneratedPurityCatalog.PurityEntry GeneratedPurity { get; }
            public bool AllowsKnownPureFallback => !HasTrustedGeneratedPurity;
        }

        internal static TrustedMethodPurityMetadata GetTrustedMethodPurityMetadata(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            if (methodSymbol == null)
            {
                return default;
            }

            var originalDefinition = methodSymbol.OriginalDefinition;
            var knownImpureMemberSource = GetKnownImpureMemberSource(originalDefinition);
            var hasConfiguredKnownImpureMember = string.Equals(
                knownImpureMemberSource,
                "config_known_impure",
                StringComparison.Ordinal);

            GeneratedPurityCatalog.PurityEntry generatedPurity = default;
            var hasTrustedGeneratedPurity = originalDefinition.Locations.FirstOrDefault()?.IsInMetadata == true &&
                !hasConfiguredKnownImpureMember &&
                TryGetTrustedGeneratedPurity(originalDefinition, compilation, out generatedPurity);
            hasTrustedGeneratedPurity = hasTrustedGeneratedPurity && generatedPurity.IsDefinitive;

            return new TrustedMethodPurityMetadata(
                knownImpureMemberSource,
                hasTrustedGeneratedPurity,
                generatedPurity);
        }

        internal static bool TryGetTrustedGeneratedFieldPurity(
            IFieldSymbol fieldSymbol,
            Compilation compilation,
            out GeneratedPurityCatalog.PurityEntry purity)
        {
            return GeneratedPurityCatalog.Current.TryGetFieldPurity(fieldSymbol, compilation, out purity);
        }

        internal static bool HasTrustedGeneratedPurityCoverage(
            IMethodSymbol methodSymbol,
            Compilation compilation)
        {
            return TryGetTrustedGeneratedPurity(methodSymbol.OriginalDefinition, compilation, out var purity) &&
                purity.IsDefinitive;
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

            if (TryGetTrustedGeneratedPurity(methodSymbol, compilation, out var purity))
            {
                return purity.IsPure && purity.IsFreshArrayCandidate;
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
    }
}
