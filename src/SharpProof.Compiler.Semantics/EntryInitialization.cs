namespace SharpProof.Compiler.Semantics;

// Why initialization the runtime may run on entry to a callable is not known
// to be free of observable effects.
internal enum EntryInitializationGap
{
    None,
    // The declaring type or an enclosing type has a static initializer or a
    // static constructor outside the admitted class (see
    // InitializationEffect).
    TypeInitializer,
    // The callable's assembly declares a module initializer.
    ModuleInitializer
}

internal static class EntryInitialization
{
    private sealed class ModuleInitializerSet(ImmutableArray<IMethodSymbol> methods)
    {
        internal ImmutableArray<IMethodSymbol> Methods { get; } = methods.IsDefault
            ? throw new ArgumentException("Array must be initialized.", nameof(methods)) : methods;
    }

    private static readonly ConditionalWeakTable<Compilation, ModuleInitializerSet> ModuleInitializerSets = new();

    // A gap for a claim that counts allocation, or, with admitFreshValues,
    // for a claim that ignores it.
    internal static EntryInitializationGap FindEffectGap(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken, bool admitFreshValues = false)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var effect = ClassifyTypeInitialization(compilation, type, cancellationToken);
        return effect == InitializationEffect.Effectful || effect == InitializationEffect.FreshValues && !admitFreshValues
            ? EntryInitializationGap.TypeInitializer
            : !ModuleInitializers(compilation, cancellationToken).IsEmpty ? EntryInitializationGap.ModuleInitializer
            : EntryInitializationGap.None;
    }

    // All initialization the runtime may run on entry to a member of the
    // type: the type's, its enclosing types' and the module's.
    internal static InitializationEffect ClassifyEntry(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var effect = ClassifyTypeInitialization(compilation, type, cancellationToken);
        return effect != InitializationEffect.Effectful && !ModuleInitializers(compilation, cancellationToken).IsEmpty
            ? InitializationEffect.Effectful : effect;
    }

    internal static bool HasNoEffectTypeInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return ClassifyTypeInitialization(compilation, type, cancellationToken) == InitializationEffect.None;
    }

    // The declaring type and, conservatively, every enclosing type.
    internal static InitializationEffect ClassifyTypeInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return TypeInitialization.Classify(compilation, type, cancellationToken);
    }

    // Every method of the assembly marked [ModuleInitializer]: ordinary
    // methods and also accessors, operators, conversions, destructors and
    // the accessors a `method:` target reaches, so the walk visits every
    // method member of every type rather than declarations of one syntax
    // kind. It is exact whatever the assembly's size, and runs once per
    // compilation.
    internal static ImmutableArray<IMethodSymbol> ModuleInitializers(CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        if (ModuleInitializerSets.TryGetValue(compilation, out var known))
        { return known.Methods; }
        var methods = ImmutableArray.CreateBuilder<IMethodSymbol>();
        var pending = new Stack<INamespaceOrTypeSymbol>();
        pending.Push(compilation.Assembly.GlobalNamespace);
        while (pending.Count != 0)
        {
            foreach (var member in pending.Pop().GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member is INamespaceOrTypeSymbol nested)
                { pending.Push(nested); }
                else if (member is IMethodSymbol method && method.GetAttributes().Any(IsModuleInitializerAttribute))
                { methods.Add(method); }
            }
        }
        return ModuleInitializerSets.GetValue(compilation, _ => new(methods.ToImmutable())).Methods;
    }

    private static bool IsModuleInitializerAttribute(AttributeData attribute)
    {
        return attribute.AttributeClass is
        {
            Name: "ModuleInitializerAttribute",
            ContainingNamespace:
            {
                Name: "CompilerServices",
                ContainingNamespace:
                {
                    Name: "Runtime",
                    ContainingNamespace: { Name: "System", ContainingNamespace.IsGlobalNamespace: true }
                }
            }
        };
    }
}
