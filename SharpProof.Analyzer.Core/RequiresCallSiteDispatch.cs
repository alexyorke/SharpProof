using SharpProof.Roslyn;

namespace SharpProof.Analyzer;

internal static class RequiresCallSiteDispatch
{
    internal static IMethodSymbol ResolveExactTarget(
        IMethodSymbol target,
        IOperation? instance,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        target = ArgumentNullGuard.NotNull(target, nameof(target)).ReducedFrom ?? target;
        if (target.IsStatic ||
            instance == null ||
            TryGetExactReceiverType(instance) is not { } receiverType)
        {
            return target;
        }

        if (target.ContainingType.TypeKind == TypeKind.Interface)
        {
            return receiverType.FindImplementationForInterfaceMember(target)
                    as IMethodSymbol ??
                target;
        }

        if (!target.IsVirtual && !target.IsAbstract && !target.IsOverride)
        {
            return target;
        }

        for (var currentType = receiverType;
             currentType != null;
             currentType = currentType.BaseType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var candidate in currentType.GetMembers(target.Name)
                         .OfType<IMethodSymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Overrides(candidate, target))
                {
                    return candidate;
                }
            }
        }

        return target;
    }

    private static INamedTypeSymbol? TryGetExactReceiverType(
        IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                case IConversionOperation
                {
                    OperatorMethod: null
                } conversion when conversion.Conversion.IsReference:
                    operation = conversion.Operand;
                    continue;
                case IObjectCreationOperation
                {
                    Type: INamedTypeSymbol type
                }:
                    return type;
                case IInstanceReferenceOperation
                {
                    Type: INamedTypeSymbol { IsSealed: true } type
                }:
                    return type;
                default:
                    return null;
            }
        }
    }

    private static bool Overrides(
        IMethodSymbol candidate,
        IMethodSymbol target)
    {
        return RoslynCfgFactory.OverridesMethod(candidate, target);
    }
}
