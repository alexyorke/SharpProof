using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    private static readonly Lazy<ImmutableArray<MetadataReference>> MinimalFrameworkReferences =
        new(CreateMinimalFrameworkReferences);

    private static readonly Lazy<MetadataReference> EnforcePureAttributeReference =
        new(() => MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));

    private static readonly ConcurrentDictionary<string, ConditionContext> ConditionContextCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ConditionImplicationContext> ConditionImplicationContextCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<SourceContextCacheKey, SourceContext> SourceContextCache = new();

    private static readonly AnalyzerOptions EmptyAnalyzerOptions =
        new([], new TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string>.Empty));

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
    public static AnalyzerOptions CreateAnalyzerOptions(ImmutableDictionary<string, string>? globalOptions = null) {
        var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
        if (analyzerGlobalOptions.Count == 0) return EmptyAnalyzerOptions;

        return new AnalyzerOptions([], new TestAnalyzerConfigOptionsProvider(analyzerGlobalOptions));
    }
    public static ConditionContext CreateConditionContext(string parameterList, string conditionExpression)
        => CreateConditionContext(parameterList, conditionExpression, "");
    public static ConditionContext CreateConditionContext(string parameterList, string conditionExpression, string extraSource) {
        var source = $$"""
            {{extraSource}}
            public static class ConditionHost
            {
                public static bool Evaluate({{parameterList}})
                {
                    return {{conditionExpression}};
                }
            }
            """;

        return ConditionContextCache.GetOrAdd(source, static conditionSource => CreateConditionContextCore(conditionSource));
    }
    private static ConditionContext CreateConditionContextCore(string source) {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, PreviewParseOptions);
        var compilation = CreateCompilation("ConditionHost", GetMinimalFrameworkReferences(), DefaultCompilationOptions, syntaxTree);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var returnExpression = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "Evaluate")
            .Body!
            .Statements
            .OfType<ReturnStatementSyntax>()
            .Single()
            .Expression!;

        return new ConditionContext(semanticModel, returnExpression);
    }
    public static SourceContext CreateSourceContext(
        string source,
        string compilationName,
        ImmutableArray<MetadataReference>? frameworkReferences = null) {
        var references = frameworkReferences ?? GetMinimalFrameworkReferences();
        if (references.SequenceEqual(GetMinimalFrameworkReferences()))
            return SourceContextCache.GetOrAdd(
                new SourceContextCacheKey(source, compilationName),
                static key => CreateSourceContextCore(key.Source, key.CompilationName, GetMinimalFrameworkReferences()));

        return CreateSourceContextCore(source, compilationName, references);
    }
    private static SourceContext CreateSourceContextCore(
        string source,
        string compilationName,
        ImmutableArray<MetadataReference> references) {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, PreviewParseOptions, string.Empty);
        var compilation = CreateCompilation(compilationName, references, DefaultCompilationOptions, syntaxTree);

        return new SourceContext(compilation, compilation.GetSemanticModel(syntaxTree), syntaxTree, syntaxTree.GetRoot());
    }
    public static ConditionImplicationContext CreateConditionImplicationContext(
        string parameterList,
        string pathCondition,
        string conclusion) {
        var source = $$"""
                       public static class ConditionHost
                       {
                           public static bool Evaluate({{parameterList}})
                           {
                               var path = {{pathCondition}};
                               var conclusion = {{conclusion}};
                               return path && conclusion;
                           }
                       }
                       """;

        return ConditionImplicationContextCache.GetOrAdd(
            source,
            static implicationSource => CreateConditionImplicationContextCore(implicationSource));
    }
    private static ConditionImplicationContext CreateConditionImplicationContextCore(string source) {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, PreviewParseOptions);
        var compilation = CreateCompilation("ConditionHost", GetMinimalFrameworkReferences(), DefaultCompilationOptions, syntaxTree);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var variables = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(variable => variable.Initializer!.Value)
            .ToArray();

        return new ConditionImplicationContext(semanticModel, variables[0], variables[1]);
    }
    internal static ImmutableArray<MetadataReference> GetTrustedPlatformReferences() => TrustedPlatformReferences.Value;
    internal static ImmutableArray<MetadataReference> GetMinimalFrameworkReferences() => MinimalFrameworkReferences.Value;
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
    private static ImmutableArray<MetadataReference> CreateMinimalFrameworkReferences() {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase) {
            [typeof(object).Assembly.Location] = MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            [typeof(Console).Assembly.Location] = MetadataReference.CreateFromFile(typeof(Console).Assembly.Location),
            [typeof(Enumerable).Assembly.Location] =
                MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            [typeof(List<>).Assembly.Location] = MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location),
            [typeof(ImmutableArray).Assembly.Location] =
                MetadataReference.CreateFromFile(typeof(ImmutableArray).Assembly.Location),
            [typeof(NotNullIfNotNullAttribute).Assembly.Location] =
                MetadataReference.CreateFromFile(typeof(NotNullIfNotNullAttribute).Assembly.Location)
        };

        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator)) {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(fileName, "System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "netstandard", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.Runtime.Extensions", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.Runtime.Numerics", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.ObjectModel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.Text.RegularExpressions", StringComparison.OrdinalIgnoreCase))
                    references[path] = MetadataReference.CreateFromFile(path);
            }
        return [.. references.Values];
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

    internal readonly record struct ConditionContext(SemanticModel SemanticModel, ExpressionSyntax Expression);

    internal readonly record struct ConditionImplicationContext(
        SemanticModel SemanticModel,
        ExpressionSyntax PathCondition,
        ExpressionSyntax Conclusion);

    internal readonly record struct SourceContext(
        CSharpCompilation Compilation,
        SemanticModel SemanticModel,
        SyntaxTree SyntaxTree,
        SyntaxNode Root);

    private readonly record struct SourceContextCacheKey(string Source, string CompilationName);

    private sealed class TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string,
        string> globalOptions) : AnalyzerConfigOptionsProvider {
        private readonly AnalyzerConfigOptions _emptyOptions =
            new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

        public override AnalyzerConfigOptions GlobalOptions { get; } = new TestAnalyzerConfigOptions(globalOptions);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
    }
    private sealed class TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions {
        private readonly ImmutableDictionary<string, string> _values = values;

        public override bool TryGetValue(string key, out string value) {
            if (_values.TryGetValue(key, out var found)) {
                value = found;
                return true;
            }
            value = string.Empty;
            return false;
        }
    }
}
