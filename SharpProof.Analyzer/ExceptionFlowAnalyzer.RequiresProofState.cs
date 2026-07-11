using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    internal static SymbolicState? CreateStableMethodEntryRequiresState(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        SharpProofAttributeIdentityPolicy attributePolicy,
        CancellationToken cancellationToken)
    {
        if (semanticModel.GetDeclaredSymbol(methodNode, cancellationToken) is not IMethodSymbol methodSymbol)
            return null;

        var contracts = RequiresContractHelpers.ValidContracts(
            methodSymbol,
            attributePolicy,
            cancellationToken);
        if (contracts.IsDefaultOrEmpty) return null;

        var conditions = new List<SymbolicCondition>();
        var position = RequiresContractHelpers.GetMethodEntrySpeculativePosition(methodNode);
        foreach (var contract in contracts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RequiresContractHelpers.TryParseCondition(
                    contract.Condition,
                    out var conditionStatement,
                    out var conditionExpression) ||
                RequiresContractHelpers.ContainsResultReference(conditionExpression) ||
                !RequiresContractHelpers.TryCreateSpeculativeConditionModel(
                    semanticModel,
                    position,
                    conditionStatement,
                    out var conditionSemanticModel) ||
                !IsStableParameterCondition(
                    conditionExpression,
                    conditionSemanticModel,
                    methodSymbol,
                    methodNode,
                    semanticModel,
                    cancellationToken))
                continue;

            var lowering = SymbolicSemanticPipeline.LowerCondition(
                conditionExpression,
                new SymbolicLoweringContext(conditionSemanticModel, cancellationToken));
            if (lowering is not { IsExact: true, Value: { } condition }) continue;

            conditions.Add(condition);
        }

        return conditions.Count == 0
            ? null
            : new SymbolicState(pathConditions: conditions).Normalize();
    }

    private static bool IsStableParameterCondition(
        ExpressionSyntax conditionExpression,
        SemanticModel conditionSemanticModel,
        IMethodSymbol methodSymbol,
        SyntaxNode methodNode,
        SemanticModel methodSemanticModel,
        CancellationToken cancellationToken)
    {
        if (conditionExpression.DescendantNodesAndSelf().Any(static node =>
                node is InvocationExpressionSyntax or ElementAccessExpressionSyntax))
            return false;

        foreach (var memberAccess in conditionExpression.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>())
        {
            var member = conditionSemanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol;
            if (!IsStableConditionMember(member, memberAccess, conditionSemanticModel, cancellationToken))
                return false;
        }

        var referencedSymbols = CollectLocalAndParameterSymbols(
            conditionExpression,
            conditionSemanticModel,
            cancellationToken);
        if (referencedSymbols.Any(symbol =>
                symbol is not IParameterSymbol parameter ||
                !SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, methodSymbol)))
            return false;

        return !AnySymbolMutatedInSyntax(
            methodNode,
            referencedSymbols,
            methodSemanticModel,
            cancellationToken);
    }

    private static bool IsStableConditionMember(
        ISymbol? member,
        MemberAccessExpressionSyntax memberAccess,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (member is not IPropertySymbol property) return false;

        var receiverType = semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type;
        if (property.Name == "Length" &&
            (receiverType is IArrayTypeSymbol || receiverType?.SpecialType == SpecialType.System_String))
            return true;

        return property.Name == "HasValue" &&
               receiverType is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
    }
}
