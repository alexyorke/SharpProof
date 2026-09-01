namespace SharpProof.Effects;

internal sealed class EffectKnownSymbols
{
    internal EffectKnownSymbols(Compilation compilation)
    {
        compilation = ArgumentNullGuard.NotNull(
            compilation,
            nameof(compilation));
        CriticalNotifyCompletionUnsafeOnCompleted = FindMethod(
            compilation.GetTypeByMetadataName(
                FrameworkTypeMetadataNames.ICriticalNotifyCompletion),
            "UnsafeOnCompleted");
        NotifyCompletionOnCompleted = FindMethod(
            compilation.GetTypeByMetadataName(
                FrameworkTypeMetadataNames.INotifyCompletion),
            "OnCompleted");
    }

    internal IMethodSymbol? CriticalNotifyCompletionUnsafeOnCompleted { get; }

    internal IMethodSymbol? NotifyCompletionOnCompleted { get; }

    internal IMethodSymbol? FindAwaitContinuationMethod(
        ITypeSymbol awaiterType)
    {
        if (awaiterType is not INamedTypeSymbol namedAwaiter)
        {
            return null;
        }

        return FindInterfaceImplementation(
                namedAwaiter,
                CriticalNotifyCompletionUnsafeOnCompleted) ??
            FindInterfaceImplementation(
                namedAwaiter,
                NotifyCompletionOnCompleted);
    }

    private static IMethodSymbol? FindMethod(
        INamedTypeSymbol? containingType,
        string name)
    {
        return containingType?.GetMembers(name)
            .OfType<IMethodSymbol>()
            .SingleOrDefault();
    }

    private static IMethodSymbol? FindInterfaceImplementation(
        INamedTypeSymbol awaiterType,
        IMethodSymbol? interfaceMethod)
    {
        if (interfaceMethod == null)
        {
            return null;
        }

        return awaiterType.FindImplementationForInterfaceMember(
            interfaceMethod) as IMethodSymbol;
    }
}
