namespace SharpProof.Effects;

internal sealed class EffectKnownSymbols
{
    private readonly object _awaiterCacheGate = new();
    private readonly Dictionary<INamedTypeSymbol, IMethodSymbol?> _awaiterContinuations =
        new(SymbolEqualityComparer.Default);

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

        lock (_awaiterCacheGate)
        {
            if (_awaiterContinuations.TryGetValue(namedAwaiter, out var cached))
            {
                return cached;
            }

            var resolved = FindInterfaceImplementation(
                    namedAwaiter,
                    CriticalNotifyCompletionUnsafeOnCompleted) ??
                FindInterfaceImplementation(
                    namedAwaiter,
                    NotifyCompletionOnCompleted);
            _awaiterContinuations.Add(namedAwaiter, resolved);
            return resolved;
        }
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
