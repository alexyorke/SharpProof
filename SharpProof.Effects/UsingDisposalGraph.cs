using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class UsingDisposalGraph
{
    internal static bool CanReachDeclarationDisposal(
        IUsingDeclarationOperation declaration,
        Func<IOperation?, bool> canCompleteNormally,
        Func<IOperation, IOperation, bool> canExitAbruptly,
        Func<IUsingDeclarationOperation, bool> canDisposalsCompleteNormally)
    {
        if (declaration.Parent is not IBlockOperation block)
        {
            return true;
        }
        var index = block.Operations.IndexOf(declaration);
        if (index < 0)
        {
            return true;
        }
        var pending = new Queue<int>();
        var visited = new HashSet<int>();
        pending.Enqueue(index + 1);
        while (pending.Count != 0)
        {
            var operationIndex = pending.Dequeue();
            if (operationIndex >= block.Operations.Length)
            {
                return true;
            }
            if (!visited.Add(operationIndex))
            {
                continue;
            }
            var operation = block.Operations[operationIndex];
            var internalBranches = GetInternalGotoTargets(
                operation,
                block,
                index + 1);
            if (internalBranches.LeavesActiveLifetime)
            {
                return true;
            }
            foreach (var target in internalBranches.Targets)
            {
                pending.Enqueue(target);
            }
            if (canExitAbruptly(operation, block))
            {
                return true;
            }
            if (operation is IUsingDeclarationOperation laterUsing &&
                !canDisposalsCompleteNormally(laterUsing))
            {
                continue;
            }
            if ((canCompleteNormally(operation) ||
                    operation is ILabeledOperation labeled &&
                    labeled.ChildOperations.All(canCompleteNormally)) &&
                !internalBranches.HasUnconditionalGoto)
            {
                pending.Enqueue(operationIndex + 1);
            }
        }
        return false;
    }

    internal static ITypeSymbol GetConcreteResourceType(
        ITypeSymbol declaredType,
        IOperation resource)
    {
        resource = DefiniteOperationFacts.UnwrapHarmlessValue(resource);
        return declaredType is INamedTypeSymbol { TypeKind: TypeKind.Interface } &&
            resource.Type is INamedTypeSymbol
            { TypeKind: not TypeKind.Interface } concrete
            ? concrete
            : declaredType;
    }

    private static InternalGotoTargets GetInternalGotoTargets(
        IOperation operation,
        IBlockOperation scope,
        int firstActiveOperation)
    {
        var branches = operation.DescendantsAndSelf()
            .OfType<IBranchOperation>()
            .Where(branch => branch.Syntax is GotoStatementSyntax)
            .ToArray();
        var allTargets = branches
            .SelectMany(static branch => branch.Target.DeclaringSyntaxReferences)
            .Select(static reference => reference.GetSyntax())
            .Where(target =>
                target.SyntaxTree == scope.Syntax.SyntaxTree &&
                scope.Syntax.Span.Contains(target.Span))
            .Select(target => scope.Operations.IndexOf(
                scope.Operations.FirstOrDefault(candidate =>
                    candidate.Syntax.Span.Contains(target.Span) ||
                    candidate.Syntax.Span.IntersectsWith(target.Span) ||
                    target.Span.Contains(candidate.Syntax.Span)) ??
                scope.Operations.First(candidate =>
                    candidate.Syntax.Span.Start >= target.Span.Start)))
            .Distinct()
            .ToArray();
        return new(
            allTargets.Where(target => target >= firstActiveOperation).ToArray(),
            branches.Any(branch =>
                IsUnconditionalAtOperationLevel(branch, operation)),
            allTargets.Any(target => target < firstActiveOperation));
    }

    private static bool IsUnconditionalAtOperationLevel(
        IBranchOperation branch,
        IOperation operation)
    {
        if (ReferenceEquals(branch, operation))
        {
            return true;
        }
        for (var parent = branch.Parent; parent != null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, operation))
            {
                return true;
            }
            if (parent is not ILabeledOperation)
            {
                return false;
            }
        }
        return false;
    }

    private sealed record InternalGotoTargets(
        IReadOnlyList<int> Targets,
        bool HasUnconditionalGoto,
        bool LeavesActiveLifetime);
}
