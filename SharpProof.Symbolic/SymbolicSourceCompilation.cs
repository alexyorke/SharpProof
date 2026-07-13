using System.Collections.Concurrent;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SharpProof.Symbolic;

internal static class SymbolicSourceCompilation
{
    private static readonly ConcurrentDictionary<string, ImmutableArray<MetadataReference>>
        TrustedPlatformReferenceCache = new(StringComparer.Ordinal);

    public static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        var cacheKey = trustedPlatformAssemblies ?? string.Empty;
        return TrustedPlatformReferenceCache.GetOrAdd(
            cacheKey,
            static value => string.IsNullOrWhiteSpace(value)
                ? ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                : value.Split(Path.PathSeparator)
                    .Where(static path => !string.IsNullOrWhiteSpace(path))
                    .Select(static path => MetadataReference.CreateFromFile(path))
                    .ToImmutableArray<MetadataReference>());
    }

    public static (SyntaxTree SyntaxTree, Compilation Compilation) CreateQuery(
        string sourceText,
        string filePath,
        IEnumerable<MetadataReference>? references,
        CancellationToken cancellationToken,
        SymbolicSourceCompilationProfile? profile = null)
    {
        return Create(
            sourceText,
            filePath,
            "SharpProof.Symbolic.Query.cs",
            "SharpProof.Symbolic.Query",
            references,
            cancellationToken,
            profile);
    }

    public static (SyntaxTree SyntaxTree, Compilation Compilation) Create(
        string sourceText,
        string filePath,
        string defaultFilePath,
        string assemblyName,
        IEnumerable<MetadataReference>? references,
        CancellationToken cancellationToken,
        SymbolicSourceCompilationProfile? profile = null)
    {
        if (sourceText == null) throw new ArgumentNullException(nameof(sourceText));

        if (string.IsNullOrWhiteSpace(filePath)) filePath = defaultFilePath;

        var normalizedProfile = profile ?? SymbolicSourceCompilationProfile.Default;
        var parseOptions = new CSharpParseOptions(
            normalizedProfile.LanguageVersion,
            normalizedProfile.DocumentationMode,
            SourceCodeKind.Regular,
            normalizedProfile.PreprocessorSymbols);
        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            parseOptions,
            filePath,
            cancellationToken: cancellationToken);
        var referenceArray = SymbolicQueryOptionHelpers.NormalizeReferences(references, nameof(references));
        if (referenceArray.IsDefaultOrEmpty) referenceArray = GetTrustedPlatformReferences();

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: normalizedProfile.OptimizationLevel,
            allowUnsafe: normalizedProfile.AllowUnsafe,
            platform: normalizedProfile.Platform,
            nullableContextOptions: normalizedProfile.NullableContext);
        var compilation = CSharpCompilation.Create(
            normalizedProfile.AssemblyName ?? assemblyName,
            new[] { syntaxTree },
            referenceArray,
            compilationOptions);
        return (syntaxTree, compilation);
    }
}
