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

    // The source methods marked [ModuleInitializer]. C# admits the attribute
    // only on a method declared as a type member, so a syntax pass that
    // skips member bodies sees every candidate; only declarations with an
    // attribute list are bound. The pass is exact whatever the assembly's
    // size, and runs once per compilation.
    internal static ImmutableArray<IMethodSymbol> ModuleInitializers(CSharpCompilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = RepositoryParameterGuard.NotNull(compilation, nameof(compilation));
        if (ModuleInitializerSets.TryGetValue(compilation, out var known))
        { return known.Methods; }
        var methods = ImmutableArray.CreateBuilder<IMethodSymbol>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SemanticModel? model = null;
            foreach (var declaration in tree.GetRoot(cancellationToken)
                         .DescendantNodes(static node => node is not (BaseMethodDeclarationSyntax or BasePropertyDeclarationSyntax or
                             BaseFieldDeclarationSyntax or GlobalStatementSyntax or EnumDeclarationSyntax or DelegateDeclarationSyntax))
                         .OfType<MethodDeclarationSyntax>()
                         .Where(static declaration => declaration.AttributeLists.Count != 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                model ??= Frontend.Host.CompilationModelProvider.GetSemanticModel(compilation, tree);
                if (model.GetDeclaredSymbol(declaration, cancellationToken) is { } method &&
                    method.GetAttributes().Any(IsModuleInitializerAttribute))
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
