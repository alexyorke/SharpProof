namespace SharpProof.Compiler.Semantics;

// Why initialization the runtime may run on entry to a callable is not known
// to be free of observable effects.
internal enum EntryInitializationGap
{
    None,
    // The declaring type or an enclosing type has a static constructor, or a
    // static initializer that is not a scalar constant stored into readonly
    // state.
    TypeInitializer,
    // The callable's assembly declares a module initializer.
    ModuleInitializer
}

// Entry initialization shared by the compiler collector, which decides
// whether a body's effects are complete at entry, and the analyzer, which
// names the gap when they are not.
internal static class EntryInitialization
{
    private sealed class ModuleInitializerSet(ImmutableArray<IMethodSymbol> methods)
    {
        internal ImmutableArray<IMethodSymbol> Methods { get; } = methods.IsDefault
            ? throw new ArgumentException("Array must be initialized.", nameof(methods)) : methods;
    }

    private static readonly ConditionalWeakTable<Compilation, ModuleInitializerSet> ModuleInitializerSets = new();

    internal static EntryInitializationGap FindEffectGap(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        return !HasNoEffectTypeInitialization(compilation, type, cancellationToken) ? EntryInitializationGap.TypeInitializer
            : !ModuleInitializers(compilation, cancellationToken).IsEmpty ? EntryInitializationGap.ModuleInitializer
            : EntryInitializationGap.None;
    }

    // A compiler-generated static constructor that only stores scalar
    // constants into readonly statics has no effect a body could observe.
    internal static bool HasNoEffectTypeInitialization(CSharpCompilation compilation, INamedTypeSymbol type,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        type = RepositoryParameterGuard.NotNull(type, nameof(type));
        var remaining = TotalBodyLowering.MaximumWork;
        for (var current = type; current != null; current = current.ContainingType)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (--remaining < 0 || current.StaticConstructors.Any(constructor => !constructor.IsImplicitlyDeclared))
            { return false; }
            foreach (var member in current.GetMembers().Where(member => member.IsStatic && member is IFieldSymbol { IsConst: false } or IPropertySymbol))
            {
                foreach (var reference in member.DeclaringSyntaxReferences)
                {
                    if (--remaining < 0)
                    { return false; }
                    var syntax = reference.GetSyntax(cancellationToken);
                    var value = syntax switch
                    {
                        VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                        PropertyDeclarationSyntax property => property.Initializer?.Value,
                        _ => null
                    };
                    if (value != null && (member is IFieldSymbol { IsReadOnly: false } or IPropertySymbol { SetMethod: not null } ||
                        !Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, reference.SyntaxTree)
                            .GetConstantValue(value, cancellationToken).HasValue ||
                        !CSharpOperationSemantics.IsScalar(member is IFieldSymbol field ? field.Type : ((IPropertySymbol)member).Type)))
                    { return false; }
                }
            }
        }
        return true;
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
        attribute = RepositoryParameterGuard.NotNull(attribute, nameof(attribute));
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
