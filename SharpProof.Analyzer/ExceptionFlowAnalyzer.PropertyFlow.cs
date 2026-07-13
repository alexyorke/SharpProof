using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static IEnumerable<SyntaxNode> GetPropertyAccessNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            if (node is MemberAccessExpressionSyntax memberAccess)
            {
                if (IsWriteOnlyTarget(memberAccess)) continue;

                if (semanticModel.GetSymbolInfo(memberAccess, cancellationToken).Symbol is IPropertySymbol)
                    yield return memberAccess;
            }
            else if (node is IdentifierNameSyntax identifierName)
            {
                if (identifierName.Parent is MemberAccessExpressionSyntax ||
                    IsWriteOnlyTarget(identifierName))
                    continue;

                if (semanticModel.GetSymbolInfo(identifierName, cancellationToken).Symbol is IPropertySymbol)
                    yield return identifierName;
            }
            else if (node is ElementAccessExpressionSyntax elementAccess)
            {
                if (IsWriteOnlyTarget(elementAccess)) continue;

                if (semanticModel.GetSymbolInfo(elementAccess, cancellationToken).Symbol is IPropertySymbol)
                    yield return elementAccess;
            }
    }

    private static IEnumerable<SyntaxNode> GetPropertyWriteNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        foreach (var node in GetRelevantDescendants<SyntaxNode>(methodNode))
            if (TryGetPropertySetterMethod(node, semanticModel, cancellationToken, out _))
                yield return node;
    }

    private static bool IsWriteOnlyTarget(SyntaxNode node)
    {
        return node.Parent is AssignmentExpressionSyntax assignment &&
               assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
               ReferenceEquals(assignment.Left, node);
    }

    private static bool TryResolveExactConcreteType(
        IOperation? operation,
        IReadOnlyDictionary<ISymbol, INamedTypeSymbol>? knownExactLocals,
        out INamedTypeSymbol exactReceiverType)
    {
        exactReceiverType = null!;
        var current = operation;

        while (true)
        {
            current = PurityAnalysisEngine.SkipImplicitConversions(current);
            if (current == null) return false;

            if (PurityConcreteReceiverResolver.TryResolveKnownSystemTypeRuntimeReceiver(
                    current,
                    current.SemanticModel?.Compilation,
                    out exactReceiverType))
                return true;

            if (current is IObjectCreationOperation objectCreationOperation &&
                objectCreationOperation.Type is INamedTypeSymbol objectCreationType)
            {
                exactReceiverType = objectCreationType;
                return true;
            }

            if (current is ILocalReferenceOperation localReferenceOperation &&
                knownExactLocals != null &&
                knownExactLocals.TryGetValue(localReferenceOperation.Local.OriginalDefinition, out var localExactType))
            {
                exactReceiverType = localExactType;
                return true;
            }

            if (current is IConditionalOperation conditionalOperation)
                return TryResolveCommonExactConcreteType(
                    conditionalOperation.WhenTrue,
                    conditionalOperation.WhenFalse,
                    knownExactLocals,
                    out exactReceiverType);

            if (current is ICoalesceOperation coalesceOperation)
                return TryResolveCommonExactConcreteType(
                    coalesceOperation.Value,
                    coalesceOperation.WhenNull,
                    knownExactLocals,
                    out exactReceiverType);

            if (current is IConversionOperation conversionOperation)
            {
                current = conversionOperation.Operand;
                continue;
            }

            if (current is IParenthesizedOperation parenthesizedOperation)
            {
                current = parenthesizedOperation.Operand;
                continue;
            }

            return false;
        }
    }

    private static bool TryResolveCommonExactConcreteType(
        IOperation? first,
        IOperation? second,
        IReadOnlyDictionary<ISymbol, INamedTypeSymbol>? knownExactLocals,
        out INamedTypeSymbol exactType)
    {
        if (TryResolveExactConcreteType(first, knownExactLocals, out var firstType) &&
            TryResolveExactConcreteType(second, knownExactLocals, out var secondType) &&
            SymbolEqualityComparer.Default.Equals(firstType, secondType))
        {
            exactType = firstType;
            return true;
        }

        exactType = null!;
        return false;
    }

    private static IReadOnlyDictionary<ISymbol, INamedTypeSymbol>? GetKnownExactLocalTypesBefore(
        SyntaxNode callSite,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        if (!TryGetContainingStatement(callSite, out var containingStatement) ||
            !TryGetContainingStatementList(containingStatement!, out var statements))
            return null;

        Dictionary<ISymbol, INamedTypeSymbol>? knownExactLocals = null;
        foreach (var statement in statements)
        {
            if (ReferenceEquals(statement, containingStatement)) break;

            UpdateKnownExactLocalTypes(statement, semanticModel, cancellationToken, ref knownExactLocals);
        }

        return knownExactLocals;
    }

    private static void UpdateKnownExactLocalTypes(
        StatementSyntax statement,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ref Dictionary<ISymbol, INamedTypeSymbol>? knownExactLocals)
    {
        if (statement is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol localSymbol)
                    continue;

                UpdateKnownExactLocalType(
                    localSymbol,
                    variable.Initializer?.Value,
                    semanticModel,
                    cancellationToken,
                    ref knownExactLocals);
            }

            return;
        }

        if (statement is ExpressionStatementSyntax
            {
                Expression: AssignmentExpressionSyntax
                {
                    RawKind: (int)SyntaxKind.SimpleAssignmentExpression
                } assignment
            } &&
            TryGetAssignedLocalSymbol(assignment.Left, semanticModel, cancellationToken, out var assignedLocalSymbol))
        {
            UpdateKnownExactLocalType(
                assignedLocalSymbol!,
                assignment.Right,
                semanticModel,
                cancellationToken,
                ref knownExactLocals);
        }
    }

    private static void UpdateKnownExactLocalType(
        ILocalSymbol localSymbol,
        ExpressionSyntax? valueExpression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        ref Dictionary<ISymbol, INamedTypeSymbol>? knownExactLocals)
    {
        var key = localSymbol.OriginalDefinition;
        if (valueExpression != null &&
            semanticModel.GetOperation(valueExpression, cancellationToken) is { } valueOperation &&
            TryResolveExactConcreteType(valueOperation, knownExactLocals, out var exactType))
        {
            (knownExactLocals ??= new Dictionary<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default))[key] =
                exactType;
            return;
        }

        knownExactLocals?.Remove(key);
    }

    private static bool TryGetContainingStatement(SyntaxNode node, out StatementSyntax? statement)
    {
        statement = node.FirstAncestorOrSelf<StatementSyntax>();
        return statement != null;
    }

    private static bool TryGetContainingStatementList(
        StatementSyntax statement,
        out SyntaxList<StatementSyntax> statements)
    {
        if (statement.Parent is BlockSyntax block)
        {
            statements = block.Statements;
            return true;
        }

        if (statement.Parent is SwitchSectionSyntax switchSection)
        {
            statements = switchSection.Statements;
            return true;
        }

        statements = default;
        return false;
    }

    private static bool TryGetAssignedLocalSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ILocalSymbol? localSymbol)
    {
        localSymbol = null;
        if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not ILocalSymbol symbol) return false;

        localSymbol = symbol.OriginalDefinition as ILocalSymbol ?? symbol;
        return true;
    }

    private static bool TryGetPropertySetterMethod(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IMethodSymbol? setterMethod)
    {
        setterMethod = null;
        if (!IsWriteOnlyTarget(node)) return false;

        if (semanticModel.GetSymbolInfo(node, cancellationToken).Symbol is not IPropertySymbol propertySymbol ||
            propertySymbol.SetMethod == null)
            return false;

        setterMethod = propertySymbol.SetMethod;
        return true;
    }
}
