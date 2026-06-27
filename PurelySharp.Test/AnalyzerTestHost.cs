using System.Collections.Immutable;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using PurelySharp.Analyzer;

namespace PurelySharp.Test
{
    internal static class AnalyzerTestHost
    {
        internal readonly record struct ConditionContext(
            SemanticModel SemanticModel,
            ExpressionSyntax Expression);

        internal readonly record struct ConditionImplicationContext(
            SemanticModel SemanticModel,
            ExpressionSyntax PathCondition,
            ExpressionSyntax Conclusion);

        public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
            string source,
            ImmutableDictionary<string, string>? globalOptions = null,
            bool allowUnsafe = false,
            ImmutableArray<AdditionalText>? additionalFiles = null)
        {
            return await GetDiagnosticsAsync(
                source,
                globalOptions,
                allowUnsafe,
                additionalFiles,
                additionalMetadataReferences: null,
                compilationName: "AnalyzerTestHost");
        }

        public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
            string source,
            ImmutableDictionary<string, string>? globalOptions,
            bool allowUnsafe,
            ImmutableArray<AdditionalText>? additionalFiles,
            ImmutableArray<MetadataReference>? additionalMetadataReferences,
            string compilationName)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var references = GetTrustedPlatformReferences()
                .Add(MetadataReference.CreateFromFile(typeof(PurelySharp.Attributes.EnforcePureAttribute).Assembly.Location));
            if (additionalMetadataReferences.HasValue)
            {
                references = references.AddRange(additionalMetadataReferences.Value);
            }

            var compilation = CSharpCompilation.Create(
                compilationName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: allowUnsafe));

            var analyzerOptions = CreateAnalyzerOptions(globalOptions, additionalFiles);

            var compilationWithAnalyzers = compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new PurelySharpAnalyzer()),
                new CompilationWithAnalyzersOptions(
                    analyzerOptions,
                    onAnalyzerException: null,
                    concurrentAnalysis: false,
                    logAnalyzerExecutionTime: false,
                    reportSuppressedDiagnostics: false));

            return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
        }

        public static AnalyzerOptions CreateAnalyzerOptions(
            ImmutableDictionary<string, string>? globalOptions = null,
            ImmutableArray<AdditionalText>? additionalFiles = null)
        {
            var analyzerAdditionalFiles = additionalFiles ?? ImmutableArray<AdditionalText>.Empty;
            var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
            if (analyzerAdditionalFiles.Length > 0 &&
                !analyzerGlobalOptions.ContainsKey("purelysharp_enable_effect_summary_json"))
            {
                analyzerGlobalOptions = analyzerGlobalOptions.Add(
                    "purelysharp_enable_effect_summary_json",
                    "true");
            }

            return new AnalyzerOptions(
                analyzerAdditionalFiles,
                new TestAnalyzerConfigOptionsProvider(analyzerGlobalOptions));
        }

        public static Diagnostic SingleDiagnostic(
            ImmutableArray<Diagnostic> diagnostics,
            string diagnosticId)
        {
            return diagnostics.Single(diagnostic => diagnostic.Id == diagnosticId);
        }

        public static ConditionContext CreateConditionContext(string parameterList, string conditionExpression)
        {
            var source = $$"""
public static class ConditionHost
{
    public static bool Evaluate({{parameterList}})
    {
        return {{conditionExpression}};
    }
}
""";

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ConditionHost",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var returnExpression = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<ReturnStatementSyntax>()
                .Single()
                .Expression!;

            return new ConditionContext(semanticModel, returnExpression);
        }

        public static ConditionImplicationContext CreateConditionImplicationContext(
            string parameterList,
            string pathCondition,
            string conclusion)
        {
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

            var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
            var compilation = CSharpCompilation.Create(
                "ConditionHost",
                new[] { syntaxTree },
                GetTrustedPlatformReferences(),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var variables = syntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<VariableDeclaratorSyntax>()
                .Select(variable => variable.Initializer!.Value)
                .ToArray();

            return new ConditionImplicationContext(semanticModel, variables[0], variables[1]);
        }

        internal static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
        {
            var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
            if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            {
                return ImmutableArray.Create<MetadataReference>(
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));
            }

            return trustedPlatformAssemblies
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path))
                .Cast<MetadataReference>()
                .ToImmutableArray();
        }

        internal sealed class InMemoryAdditionalText : AdditionalText
        {
            private readonly string _text;

            public InMemoryAdditionalText(string path, string text)
            {
                Path = path;
                _text = text;
            }

            public override string Path { get; }

            public override SourceText GetText(CancellationToken cancellationToken = default)
            {
                return SourceText.From(_text);
            }
        }

        private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
        {
            private readonly AnalyzerConfigOptions _globalOptions;
            private readonly AnalyzerConfigOptions _emptyOptions = new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

            public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
            {
                _globalOptions = new TestAnalyzerConfigOptions(globalOptions);
            }

            public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

            public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => _emptyOptions;

            public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => _emptyOptions;
        }

        private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _values;

            public TestAnalyzerConfigOptions(ImmutableDictionary<string, string> values)
            {
                _values = values;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (_values.TryGetValue(key, out var found))
                {
                    value = found;
                    return true;
                }

                value = string.Empty;
                return false;
            }
        }
    }
}
