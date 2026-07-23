using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;
namespace SharpProof.Test;
internal static class AnalyzerTestHost {
    private static readonly CSharpParseOptions PreviewParseOptions = new(LanguageVersion.Preview);
    private static readonly CSharpCompilationOptions DefaultCompilationOptions =
        new(OutputKind.DynamicallyLinkedLibrary);
    private static readonly ImmutableArray<DiagnosticAnalyzer> AnalyzerInstances =
        [new SharpProofAnalyzer()];
    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedPlatformReferences =
        new(CreateTrustedPlatformReferences);
    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedPlatformReferencesWithEnforcePure =
        new(CreateTrustedPlatformReferencesWithEnforcePure);
    private static readonly Lazy<MetadataReference> EnforcePureAttributeReference =
        new(() => MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));
    private static readonly AnalyzerOptions EmptyAnalyzerOptions = new([]);
    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(string source) {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, PreviewParseOptions, string.Empty);
        var compilation = CreateCompilation(
            "AnalyzerTestHost",
            TrustedPlatformReferencesWithEnforcePure.Value,
            DefaultCompilationOptions,
            syntaxTree);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            AnalyzerInstances,
            new CompilationWithAnalyzersOptions(EmptyAnalyzerOptions, null, false, false, false));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }
    internal static string GetRepositoryRoot() {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null) {
            if (File.Exists(Path.Combine(directory.FullName, "SharpProof.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not find repository root.");
    }
    private static ImmutableArray<MetadataReference> CreateTrustedPlatformReferences() {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            return
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            ];
        return [.. trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()];
    }
    private static ImmutableArray<MetadataReference> CreateTrustedPlatformReferencesWithEnforcePure() {
        var references = TrustedPlatformReferences.Value;
        if (references.IsDefault) references = [];
        return references.Add(EnforcePureAttributeReference.Value);
    }
    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        ImmutableArray<MetadataReference> references,
        CSharpCompilationOptions options,
        SyntaxTree syntaxTree) => CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            options);
}
