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

    private static IMethodSymbol? FindMethod(
        INamedTypeSymbol? containingType,
        string name)
    {
        return containingType?.GetMembers(name)
            .OfType<IMethodSymbol>()
            .SingleOrDefault();
    }
}
