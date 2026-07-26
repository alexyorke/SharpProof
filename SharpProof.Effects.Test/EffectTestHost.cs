namespace SharpProof.Effects.Test;

internal static class EffectTestHost {
    private static readonly ImmutableArray<MetadataReference> PlatformReferences =
        CreatePlatformReferences();

    internal static CSharpCompilation CreateCompilation(
        string source,
        params MetadataReference[] additionalReferences) =>
        CreateCompilation(
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
                path: "EffectsTest.cs")],
            "EffectsTest",
            additionalReferences);

    internal static CSharpCompilation CreateCompilation(
        IEnumerable<SyntaxTree> syntaxTrees,
        string assemblyName = "EffectsTest",
        params MetadataReference[] additionalReferences) {
        var references = PlatformReferences
            .Add(MetadataReference.CreateFromFile(
                typeof(EffectContractAttribute).Assembly.Location))
            .AddRange(additionalReferences);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Enable,
                deterministic: true));
        RequireNoErrors(compilation);
        return compilation;
    }

    internal static PortableExecutableReference EmitReference(
        string source,
        string assemblyName) =>
        EmitImage(source, assemblyName).Reference;

    internal static EmittedAssemblyImage EmitImage(
        string source,
        string assemblyName) {
        var compilation = CreateCompilation(
            [CSharpSyntaxTree.ParseText(
                source,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
                path: assemblyName + ".cs")],
            assemblyName);
        return EmitImage(compilation);
    }

    internal static EmittedAssemblyImage EmitImage(
        CSharpCompilation compilation) {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
            throw new InvalidOperationException(FormatErrors(result.Diagnostics));
        var image = stream.ToArray();
        return new EmittedAssemblyImage(
            MetadataReference.CreateFromImage(image),
            image);
    }

    internal static IMethodSymbol RequireMethod(
        Compilation compilation,
        string typeMetadataName,
        string methodName) {
        var type = compilation.GetTypeByMetadataName(typeMetadataName) ??
                   throw new InvalidOperationException(
                       $"Type '{typeMetadataName}' was not found.");
        return type.GetMembers(methodName)
                   .OfType<IMethodSymbol>()
                   .Single(static method => method.MethodKind == MethodKind.Ordinary);
    }

    internal static INamedTypeSymbol RequireType(
        Compilation compilation,
        string metadataName) =>
        compilation.GetTypeByMetadataName(metadataName) ??
        throw new InvalidOperationException(
            $"Type '{metadataName}' was not found.");

    private static ImmutableArray<MetadataReference> CreatePlatformReferences() {
        var trustedAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ??
            throw new InvalidOperationException(
                "The runtime did not expose trusted platform assemblies.");
        return [.. trustedAssemblies
            .Split(Path.PathSeparator)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path))];
    }

    private static void RequireNoErrors(Compilation compilation) {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();
        if (!errors.IsDefaultOrEmpty)
            throw new InvalidOperationException(FormatErrors(errors));
    }

    private static string FormatErrors(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static diagnostic => diagnostic.ToString()));
}

internal sealed class EmittedAssemblyImage(
    PortableExecutableReference reference,
    byte[] image) {
    internal PortableExecutableReference Reference { get; } = reference;
    internal byte[] Image { get; } = image;
}
