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
            current = SkipImplicitConversions(current);
            if (current == null) return false;

            if (PurityAnalysisEngine.TryResolveKnownSystemTypeRuntimeReceiver(
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
            {
                if (TryResolveExactConcreteType(conditionalOperation.WhenTrue, knownExactLocals,
                        out var whenTrueType) &&
                    TryResolveExactConcreteType(conditionalOperation.WhenFalse, knownExactLocals,
                        out var whenFalseType) &&
                    SymbolEqualityComparer.Default.Equals(whenTrueType, whenFalseType))
                {
                    exactReceiverType = whenTrueType;
                    return true;
                }

                return false;
            }

            if (current is ICoalesceOperation coalesceOperation)
            {
                if (TryResolveExactConcreteType(coalesceOperation.Value, knownExactLocals, out var leftType) &&
                    TryResolveExactConcreteType(coalesceOperation.WhenNull, knownExactLocals, out var rightType) &&
                    SymbolEqualityComparer.Default.Equals(leftType, rightType))
                {
                    exactReceiverType = leftType;
                    return true;
                }

                return false;
            }

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

                if (variable.Initializer?.Value != null &&
                    semanticModel.GetOperation(variable.Initializer.Value, cancellationToken) is
                    { } initializerOperation &&
                    TryResolveExactConcreteType(initializerOperation, knownExactLocals, out var exactType))
                    (knownExactLocals ??= new Dictionary<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default))[
                        localSymbol.OriginalDefinition] = exactType;
                else
                    knownExactLocals?.Remove(localSymbol.OriginalDefinition);
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
            if (semanticModel.GetOperation(assignment.Right, cancellationToken) is { } rightOperation &&
                TryResolveExactConcreteType(rightOperation, knownExactLocals, out var exactType))
                (knownExactLocals ??= new Dictionary<ISymbol, INamedTypeSymbol>(SymbolEqualityComparer.Default))[
                    assignedLocalSymbol!] = exactType;
            else
                knownExactLocals?.Remove(assignedLocalSymbol!);
        }
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
        out ISymbol? localSymbol)
    {
        localSymbol = null;
        if (semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol is not ILocalSymbol symbol) return false;

        localSymbol = symbol.OriginalDefinition;
        return true;
    }

    private static IOperation? SkipImplicitConversions(IOperation? operation)
    {
        while (operation is IConversionOperation conversionOperation &&
               conversionOperation.IsImplicit)
            operation = conversionOperation.Operand;

        return operation;
    }

    private static bool IsBaseReference(IOperation? operation)
    {
        var current = operation;
        while (current is IConversionOperation conversionOperation && conversionOperation.IsImplicit)
            current = conversionOperation.Operand;

        return current is IInstanceReferenceOperation instanceReferenceOperation &&
               instanceReferenceOperation.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance &&
               current.Syntax.IsKind(SyntaxKind.BaseExpression);
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
