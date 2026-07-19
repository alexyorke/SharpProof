using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer;

internal static partial class ExceptionFlowAnalyzer
{
    private static IEnumerable<MethodCallCandidate> GetLocalDelegateTargetInvocationNodes(
        SyntaxNode methodNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
    {
        var knownTargets = new Dictionary<ISymbol, ImmutableHashSet<IMethodSymbol>>(SymbolEq.Default);
        foreach (var node in GetRelevantDescendantsAndSelf<SyntaxNode>(methodNode))
        {
            UpdateKnownDelegateTargets(node, semanticModel, cancellationToken, knownTargets);
            if (node is not InvocationExpressionSyntax invocation) continue;

            if (semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol directMethod &&
                directMethod.MethodKind != MethodKind.DelegateInvoke)
                continue;

            foreach (var targetMethod in ResolveDelegateTargets(
                         invocation,
                         semanticModel,
                         cancellationToken,
                         knownTargets))
                yield return new MethodCallCandidate(invocation, targetMethod);
        }
    }

    private static void UpdateKnownDelegateTargets(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IDictionary<ISymbol, ImmutableHashSet<IMethodSymbol>> knownTargets)
    {
        if (TryGetDeconstructionAssignment(node, semanticModel, cancellationToken, out var deconstruction))
        {
            UpdateDeconstructionDelegateTargets(
                deconstruction.Target,
                deconstruction.Value,
                knownTargets);
            return;
        }

        if (node is LocalDeclarationStatementSyntax localDeclaration)
        {
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol localSymbol)
                    continue;

                if (variable.Initializer?.Value is ExpressionSyntax initializer &&
                    semanticModel.GetSymbolInfo(initializer, cancellationToken).Symbol is IMethodSymbol targetMethod)
                    SetKnownDelegateTarget(
                        knownTargets,
                        localSymbol.OriginalDefinition,
                        targetMethod,
                        variable);
            }
        }
        else if (node is AssignmentExpressionSyntax assignment &&
                 TryGetInvokedLocalSymbol(assignment.Left, semanticModel, cancellationToken, out var localSymbol))
        {
            if (assignment.Right is ExpressionSyntax rightExpression &&
                semanticModel.GetSymbolInfo(rightExpression, cancellationToken).Symbol is IMethodSymbol targetMethod)
                SetKnownDelegateTarget(knownTargets, localSymbol!, targetMethod, assignment);
            else
                knownTargets.Remove(localSymbol!);
        }
    }

    private static bool TryGetDeconstructionAssignment(
        SyntaxNode node,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out IDeconstructionAssignmentOperation deconstruction)
    {
        deconstruction = null!;
        if (node is not AssignmentExpressionSyntax and not LocalDeclarationStatementSyntax) return false;

        var operation = semanticModel.GetOperation(node, cancellationToken);
        if (operation is IDeconstructionAssignmentOperation direct)
        {
            deconstruction = direct;
            return true;
        }

        if (operation == null) return false;

        var pending = new Stack<IOperation>();
        pending.Push(operation);
        while (pending.Count > 0)
        {
            var candidate = pending.Pop();
            if (candidate is IDeconstructionAssignmentOperation nested)
            {
                deconstruction = nested;
                return true;
            }

            foreach (var child in candidate.ChildOperations) pending.Push(child);
        }

        return false;
    }

    private static void UpdateDeconstructionDelegateTargets(
        IOperation target,
        IOperation value,
        IDictionary<ISymbol, ImmutableHashSet<IMethodSymbol>> knownTargets)
    {
        target = UnwrapDelegateAssignmentOperation(target);
        value = UnwrapDelegateAssignmentOperation(value);

        if (target is ITupleOperation targetTuple)
        {
            if (value is ITupleOperation valueTuple && valueTuple.Elements.Length == targetTuple.Elements.Length)
            {
                for (var index = 0; index < targetTuple.Elements.Length; index++)
                    UpdateDeconstructionDelegateTargets(
                        targetTuple.Elements[index],
                        valueTuple.Elements[index],
                        knownTargets);
                return;
            }

            foreach (var element in targetTuple.Elements)
                InvalidateDelegateTargets(element, knownTargets);
            return;
        }

        if (!TryGetDelegateAssignmentSymbol(target, out var targetSymbol)) return;

        if (TryResolveDelegateAssignmentValue(value, knownTargets, out var targetMethods))
            knownTargets[targetSymbol] = targetMethods;
        else
            knownTargets.Remove(targetSymbol);
    }

    private static void InvalidateDelegateTargets(
        IOperation target,
        IDictionary<ISymbol, ImmutableHashSet<IMethodSymbol>> knownTargets)
    {
        target = UnwrapDelegateAssignmentOperation(target);
        if (target is ITupleOperation tuple)
        {
            foreach (var element in tuple.Elements) InvalidateDelegateTargets(element, knownTargets);
            return;
        }

        if (TryGetDelegateAssignmentSymbol(target, out var symbol)) knownTargets.Remove(symbol);
    }

    private static bool TryGetDelegateAssignmentSymbol(IOperation operation, out ISymbol symbol)
    {
        operation = UnwrapDelegateAssignmentOperation(operation);
        switch (operation)
        {
            case ILocalReferenceOperation local:
                symbol = local.Local.OriginalDefinition;
                return true;
            case IParameterReferenceOperation parameter:
                symbol = parameter.Parameter.OriginalDefinition;
                return true;
            case IDeclarationExpressionOperation declaration:
                return TryGetDelegateAssignmentSymbol(declaration.Expression, out symbol);
            default:
                symbol = null!;
                return false;
        }
    }

    private static bool TryResolveDelegateAssignmentValue(
        IOperation operation,
        IDictionary<ISymbol, ImmutableHashSet<IMethodSymbol>> knownTargets,
        out ImmutableHashSet<IMethodSymbol> methods)
    {
        operation = UnwrapDelegateAssignmentOperation(operation);
        switch (operation)
        {
            case IMethodReferenceOperation methodReference:
                methods = ImmutableHashSet.Create<IMethodSymbol>(
                    SymbolEq.Default,
                    methodReference.Method);
                return true;
            case IDelegateCreationOperation delegateCreation:
                return TryResolveDelegateAssignmentValue(delegateCreation.Target, knownTargets, out methods);
            case ILocalReferenceOperation local
                when knownTargets.TryGetValue(local.Local.OriginalDefinition, out methods!):
                return true;
            case IParameterReferenceOperation parameter
                when knownTargets.TryGetValue(parameter.Parameter.OriginalDefinition, out methods!):
                return true;
            default:
                methods = null!;
                return false;
        }
    }

    private static IOperation UnwrapDelegateAssignmentOperation(IOperation operation)
    {
        while (true)
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IDeclarationExpressionOperation declaration:
                    operation = declaration.Expression;
                    continue;
                default:
                    return operation;
            }
    }

    private static IEnumerable<IMethodSymbol> ResolveDelegateTargets(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<ISymbol, ImmutableHashSet<IMethodSymbol>> knownTargets)
    {
        if (!TryGetInvokedLocalSymbol(invocation.Expression, semanticModel, cancellationToken, out var localSymbol))
            return Enumerable.Empty<IMethodSymbol>();

        return knownTargets.TryGetValue(localSymbol!, out var targetMethods)
            ? targetMethods
            : Enumerable.Empty<IMethodSymbol>();
    }

    private static void SetKnownDelegateTarget(
        IDictionary<ISymbol, ImmutableHashSet<IMethodSymbol>> knownTargets,
        ISymbol symbol,
        IMethodSymbol target,
        SyntaxNode assignment)
    {
        var isConditional = assignment.Ancestors().Any(static ancestor =>
            ancestor is IfStatementSyntax or SwitchStatementSyntax or SwitchExpressionSyntax or
                ConditionalExpressionSyntax or WhileStatementSyntax or DoStatementSyntax or
                ForStatementSyntax or ForEachStatementSyntax or ForEachVariableStatementSyntax);
        if (isConditional && knownTargets.TryGetValue(symbol, out var existing))
            knownTargets[symbol] = existing.Add(target);
        else
            knownTargets[symbol] = ImmutableHashSet.Create<IMethodSymbol>(SymbolEq.Default, target);
    }

    private static bool TryGetInvokedLocalSymbol(
        ExpressionSyntax expression,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ISymbol? localSymbol)
    {
        localSymbol = null;
        var symbol = semanticModel.GetSymbolInfo(expression, cancellationToken).Symbol;
        if (symbol is not ILocalSymbol and not IParameterSymbol) return false;

        localSymbol = symbol.OriginalDefinition;
        return true;
    }

    private static IMethodSymbol? FindObjectCreationConstructor(IOperation root)
    {
        var pending = new Stack<IOperation>();
        pending.Push(root);
        while (pending.Count != 0)
        {
            var operation = pending.Pop();
            if (operation is IObjectCreationOperation objectCreation)
                return objectCreation.Constructor;

            foreach (var child in operation.ChildOperations)
                pending.Push(child);
        }

        return null;
    }
}
