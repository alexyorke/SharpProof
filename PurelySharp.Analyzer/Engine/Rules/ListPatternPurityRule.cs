using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace PurelySharp.Analyzer.Engine.Rules
{
    internal sealed class ListPatternPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds =>
            ImmutableArray.Create(OperationKind.ListPattern, OperationKind.SlicePattern);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(
            IOperation operation,
            PurityAnalysisContext context,
            PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is IListPatternOperation listPattern)
            {
                var lengthResult = CheckMemberPurity(listPattern.LengthSymbol, operation, context);
                if (!lengthResult.IsPure)
                {
                    return lengthResult;
                }

                var indexerResult = CheckMemberPurity(listPattern.IndexerSymbol, operation, context);
                if (!indexerResult.IsPure)
                {
                    return indexerResult;
                }

                foreach (var pattern in listPattern.Patterns)
                {
                    var patternResult = PurityAnalysisEngine.CheckSingleOperation(pattern, context, currentState);
                    if (!patternResult.IsPure)
                    {
                        return patternResult;
                    }
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (operation is ISlicePatternOperation slicePattern)
            {
                var sliceResult = CheckMemberPurity(slicePattern.SliceSymbol, operation, context);
                if (!sliceResult.IsPure)
                {
                    return sliceResult;
                }

                return slicePattern.Pattern == null
                    ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                    : PurityAnalysisEngine.CheckSingleOperation(slicePattern.Pattern, context, currentState);
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckMemberPurity(
            ISymbol? member,
            IOperation operation,
            PurityAnalysisContext context)
        {
            if (member == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (member is IPropertySymbol property)
            {
                if (PurityAnalysisEngine.IsKnownImpure(property))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "catalog_hit",
                            nameof(ListPatternPurityRule),
                            operation,
                            syntaxNode: operation.Syntax,
                            symbol: property,
                            catalogSource: PurityAnalysisEngine.GetKnownImpureMemberSource(property) ?? "known_impure"));
                }

                return CheckMethodPurity(property.GetMethod, operation, context);
            }

            if (member is IMethodSymbol method)
            {
                return CheckMethodPurity(method, operation, context);
            }

            if (PurityAnalysisEngine.IsKnownImpure(member))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        nameof(ListPatternPurityRule),
                        operation,
                        syntaxNode: operation.Syntax,
                        symbol: member,
                        catalogSource: PurityAnalysisEngine.GetKnownImpureMemberSource(member) ?? "known_impure"));
            }

            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisEngine.PurityAnalysisResult CheckMethodPurity(
            IMethodSymbol? method,
            IOperation operation,
            PurityAnalysisContext context)
        {
            if (method == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var originalDefinition = method.OriginalDefinition;
            GeneratedPurityCatalog.PurityEntry generatedPurity = default;
            var hasTrustedGeneratedPurity = originalDefinition.Locations.FirstOrDefault()?.IsInMetadata == true &&
                PurityAnalysisEngine.TryGetTrustedGeneratedPurity(
                    originalDefinition,
                    context.SemanticModel.Compilation,
                    out generatedPurity);

            if (PurityAnalysisEngine.HasPureExternalAttribute(originalDefinition))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (hasTrustedGeneratedPurity)
            {
                if (generatedPurity.IsPure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (generatedPurity.IsImpure)
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        operation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            generatedPurity.PrimaryCategory,
                            nameof(ListPatternPurityRule),
                            operation,
                            syntaxNode: operation.Syntax,
                            symbol: originalDefinition,
                            catalogSource: "generated_purity_summary"));
                }
            }

            if (!hasTrustedGeneratedPurity &&
                PurityAnalysisEngine.IsKnownPureBCLMember(originalDefinition))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (PurityAnalysisEngine.IsKnownImpure(originalDefinition))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    operation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "catalog_hit",
                        nameof(ListPatternPurityRule),
                        operation,
                        syntaxNode: operation.Syntax,
                        symbol: originalDefinition,
                        catalogSource: PurityAnalysisEngine.GetKnownImpureMemberSource(originalDefinition) ?? "known_impure"));
            }

            if (!hasTrustedGeneratedPurity &&
                originalDefinition.DeclaringSyntaxReferences.Length == 0 &&
                !PurityAnalysisEngine.HasImpureAttribute(originalDefinition))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var result = PurityAnalysisEngine.GetCalleePurity(originalDefinition, context);
            return result.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : result.WithCallee(originalDefinition, operation.Syntax);
        }
    }
}
