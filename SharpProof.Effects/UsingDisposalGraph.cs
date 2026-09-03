using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class UsingDisposalGraph
{
    internal static (
        List<(ITypeSymbol Type, IOperation Resource, IOperation Origin)> Acquired,
        int ReachableDisposalCount) AcquireResources(
        IVariableDeclarationGroupOperation group,
        Func<IOperation?, bool> canCompleteNormally,
        Func<IOperation, IOperation, bool> canExitAbruptly,
        bool scopeExitReachable)
    {
        var acquired = new List<(
            ITypeSymbol Type,
            IOperation Resource,
            IOperation Origin)>();
        var allInitializersComplete = true;
        var reachableDisposalCount = 0;
        foreach (var declarator in group.Declarations
                     .SelectMany(static declaration => declaration.Declarators))
        {
            var resource = declarator.Initializer?.Value;
            if (resource != null && canExitAbruptly(resource, resource))
            {
                reachableDisposalCount = acquired.Count;
            }
            if (!canCompleteNormally(resource))
            {
                allInitializersComplete = false;
                break;
            }
            if (resource != null)
            {
                acquired.Add((
                    declarator.Symbol.Type,
                    resource,
                    declarator));
            }
        }
        if (scopeExitReachable && allInitializersComplete)
        {
            reachableDisposalCount = acquired.Count;
        }

        return (acquired, reachableDisposalCount);
    }

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
        var allTargets = new List<int>();
        var seenTargets = new HashSet<int>();
        var hasUnconditionalGoto = false;
        foreach (var branch in operation.DescendantsAndSelf()
                     .OfType<IBranchOperation>())
        {
            if (branch.Syntax is not GotoStatementSyntax)
            {
                continue;
            }

            hasUnconditionalGoto |=
                IsUnconditionalAtOperationLevel(branch, operation);
            foreach (var reference in branch.Target.DeclaringSyntaxReferences)
            {
                var target = reference.GetSyntax();
                if (target.SyntaxTree != scope.Syntax.SyntaxTree ||
                    !scope.Syntax.Span.Contains(target.Span))
                {
                    continue;
                }

                var targetIndex = -1;
                for (var index = 0;
                     index < scope.Operations.Length;
                     index++)
                {
                    var candidate = scope.Operations[index];
                    if (candidate.Syntax.Span.Contains(target.Span) ||
                        candidate.Syntax.Span.IntersectsWith(target.Span) ||
                        target.Span.Contains(candidate.Syntax.Span))
                    {
                        targetIndex = index;
                        break;
                    }
                }

                if (targetIndex < 0)
                {
                    targetIndex = scope.Operations
                        .Select((candidate, index) => (candidate, index))
                        .First(item =>
                            item.candidate.Syntax.Span.Start >= target.Span.Start)
                        .index;
                }

                if (seenTargets.Add(targetIndex))
                {
                    allTargets.Add(targetIndex);
                }
            }
        }

        var activeTargets = new List<int>();
        var leavesActiveLifetime = false;
        foreach (var target in allTargets)
        {
            if (target >= firstActiveOperation)
            {
                activeTargets.Add(target);
            }
            else
            {
                leavesActiveLifetime = true;
            }
        }

        return new(
            activeTargets,
            hasUnconditionalGoto,
            leavesActiveLifetime);
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
