using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class CollectionExpressionPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.CollectionExpression);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation is ICollectionExpressionOperation collectionExpression))
            {
                PurityAnalysisEngine.LogDebug($"WARNING: CollectionExpressionPurityRule called with unexpected operation type: {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"CollectionExpressionRule: Analyzing {collectionExpression.Syntax}");

            ITypeSymbol? targetType = collectionExpression.Type;

            if (targetType != null)
            {
                string targetTypeName = targetType.OriginalDefinition.ToDisplayString();
                PurityAnalysisEngine.LogDebug($" CollectionExpressionRule: Target Type is {targetTypeName}");

                var isFreshLocalArrayInitialization =
                    targetType is IArrayTypeSymbol &&
                    IsFreshLocalArrayInitialization(collectionExpression);

                if (!IsPureCollectionExpressionTargetType(targetType) &&
                    !isFreshLocalArrayInitialization)
                {
                    PurityAnalysisEngine.LogDebug($" CollectionExpressionRule: Target type '{targetTypeName}' is not a known pure collection-expression target. Marking IMPURE.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        collectionExpression.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            targetType is IArrayTypeSymbol ? "mutable_state_write" : "unsupported_operation",
                            ruleName: nameof(CollectionExpressionPurityRule),
                            operation: collectionExpression,
                            syntaxNode: collectionExpression.Syntax,
                            symbol: targetType,
                            catalogSource: "collection_expression_target"));
                }

                if (isFreshLocalArrayInitialization)
                {
                    PurityAnalysisEngine.LogDebug($" CollectionExpressionRule: Array collection expression '{collectionExpression.Syntax}' initializes a fresh local array. Treating allocation as PURE.");
                }
            }
            else
            {
                PurityAnalysisEngine.LogDebug(" CollectionExpressionRule: Target type unknown — classifying by element operations only.");
            }

            foreach (var element in collectionExpression.Elements)
            {
                if (element is null)
                    continue;

                var elementResult = PurityAnalysisEngine.CheckSingleOperation(element, context, currentState);
                if (!elementResult.IsPure)
                {
                    var node = elementResult.ImpureSyntaxNode ?? collectionExpression.Syntax;
                    PurityAnalysisEngine.LogDebug($" CollectionExpressionRule: Impure element. Marking IMPURE at {node}.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(node, elementResult.Evidence);
                }
            }

            PurityAnalysisEngine.LogDebug(" CollectionExpressionRule: Target and elements accepted. Final Result: PURE.");
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;
        }

        /// <summary>
        /// Types for which a collection expression is treated as constructing immutable / stack-only
        /// data without hidden mutation (arrays, <see cref="List{T}"/>, etc. remain impure targets).
        /// </summary>
        private static bool IsPureCollectionExpressionTargetType(ITypeSymbol type)
        {
            var def = type.OriginalDefinition;

            if (def.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System.Collections.Immutable")
                return true;

            if (def is INamedTypeSymbol named &&
                named.TypeArguments.Length == 1 &&
                named.ContainingNamespace?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) == "global::System" &&
                (named.Name == "ReadOnlySpan" || named.Name == "Span"))
            {
                return true;
            }

            return false;
        }

        private static bool IsFreshLocalArrayInitialization(ICollectionExpressionOperation collectionExpression)
        {
            IOperation? current = collectionExpression.Parent;

            if (current is IConversionOperation conversionOperation)
            {
                current = conversionOperation.Parent;
            }

            if (current is IVariableInitializerOperation variableInitializer &&
                variableInitializer.Parent is IVariableDeclaratorOperation variableDeclarator &&
                variableDeclarator.Symbol.Type is IArrayTypeSymbol)
            {
                return true;
            }

            if (current is IAssignmentOperation assignmentOperation &&
                assignmentOperation.Target is ILocalReferenceOperation localReference &&
                localReference.Type is IArrayTypeSymbol)
            {
                return true;
            }

            return false;
        }
    }
}
