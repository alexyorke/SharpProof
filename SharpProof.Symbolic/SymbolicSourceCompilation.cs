namespace SharpProof.Symbolic;
internal static class SymbolicSourceCompilation {
    private static readonly ConcurrentDictionary<string, ImmutableArray<MetadataReference>>
        TrustedPlatformReferenceCache = new(StringComparer.Ordinal);
    public static ImmutableArray<MetadataReference> GetTrustedPlatformReferences() {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var cacheKey = trustedPlatformAssemblies ?? string.Empty;
        return TrustedPlatformReferenceCache.GetOrAdd(
            cacheKey,
            static value => string.IsNullOrWhiteSpace(value)
                ? [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]
                : [.. value.Split(Path.PathSeparator)
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(static path => MetadataReference.CreateFromFile(path))]);
    }
    public static (SyntaxTree SyntaxTree, Compilation Compilation) Create(
        string sourceText,
        string filePath,
        SymbolicSourceCompilationKind kind,
        IEnumerable<MetadataReference>? references,
        CancellationToken cancellationToken) {
        var assemblyName = "SharpProof.Symbolic." + kind;
        return Create(sourceText, filePath, assemblyName + ".cs", assemblyName, references, cancellationToken);
    }
    public static (SyntaxTree SyntaxTree, Compilation Compilation) Create(
        string sourceText,
        string filePath,
        string defaultFilePath,
        string assemblyName,
        IEnumerable<MetadataReference>? references,
        CancellationToken cancellationToken) {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));
        if (string.IsNullOrWhiteSpace(filePath)) filePath = defaultFilePath;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview, DocumentationMode.Parse, SourceCodeKind.Regular);
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, filePath, cancellationToken: cancellationToken);
        var referenceArray = NormalizeReferences(references);
        if (referenceArray.IsDefaultOrEmpty) referenceArray = GetTrustedPlatformReferences();
        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Debug,
            platform: Platform.AnyCpu,
            nullableContextOptions: NullableContextOptions.Disable);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            referenceArray,
            compilationOptions);
        return (syntaxTree, compilation);
    }
    private static ImmutableArray<MetadataReference> NormalizeReferences(IEnumerable<MetadataReference>? references) {
        if (references == null) return [];
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var reference in references)
            builder.Add(reference ?? throw new ArgumentException("References cannot contain null entries.", nameof(references)));
        return builder.ToImmutable();
    }
}
internal enum SymbolicSourceCompilationKind {
    Query,
    RuntimeHazards,
    Complexity
}
