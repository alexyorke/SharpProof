using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Test;

internal static class AnalyzerTestHost
{
    private const string SuggestMissingEnforcePureOption = "sharpproof_suggest_missing_enforce_pure";
    private static readonly CSharpParseOptions PreviewParseOptions = new(LanguageVersion.Preview);

    private static readonly CSharpCompilationOptions DefaultCompilationOptions =
        new(OutputKind.DynamicallyLinkedLibrary);

    private static readonly CSharpCompilationOptions UnsafeCompilationOptions =
        DefaultCompilationOptions.WithAllowUnsafe(true);

    private static readonly ConcurrentDictionary<AnalyzerFeatures, ImmutableArray<DiagnosticAnalyzer>>
        AnalyzerInstances =
            new();

    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedPlatformReferences =
        new(CreateTrustedPlatformReferences);

    private static readonly Lazy<ImmutableArray<MetadataReference>> TrustedPlatformReferencesWithEnforcePure =
        new(CreateTrustedPlatformReferencesWithEnforcePure);

    private static readonly Lazy<ImmutableArray<MetadataReference>> MinimalFrameworkReferences =
        new(CreateMinimalFrameworkReferences);

    private static readonly Lazy<ImmutableArray<MetadataReference>> MinimalFrameworkReferencesWithEnforcePure =
        new(CreateMinimalFrameworkReferencesWithEnforcePure);

    private static readonly Lazy<CSharpCompilation> TrustedPlatformCompilationTemplate =
        new(() => CreateCompilationTemplate(
            TrustedPlatformReferencesWithEnforcePure.Value,
            DefaultCompilationOptions));

    private static readonly Lazy<CSharpCompilation> TrustedPlatformUnsafeCompilationTemplate =
        new(() => CreateCompilationTemplate(
            TrustedPlatformReferencesWithEnforcePure.Value,
            UnsafeCompilationOptions));

    private static readonly Lazy<CSharpCompilation> MinimalFrameworkCompilationTemplate =
        new(() => CreateCompilationTemplate(
            MinimalFrameworkReferences.Value,
            DefaultCompilationOptions));

    private static readonly Lazy<CSharpCompilation> MinimalFrameworkUnsafeCompilationTemplate =
        new(() => CreateCompilationTemplate(
            MinimalFrameworkReferences.Value,
            UnsafeCompilationOptions));

    private static readonly Lazy<CSharpCompilation> MinimalFrameworkWithEnforcePureCompilationTemplate =
        new(() => CreateCompilationTemplate(
            MinimalFrameworkReferencesWithEnforcePure.Value,
            DefaultCompilationOptions));

    private static readonly Lazy<CSharpCompilation> MinimalFrameworkWithEnforcePureUnsafeCompilationTemplate =
        new(() => CreateCompilationTemplate(
            MinimalFrameworkReferencesWithEnforcePure.Value,
            UnsafeCompilationOptions));

    private static readonly Lazy<MetadataReference> EnforcePureAttributeReference =
        new(() => MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));

    private static readonly ConcurrentDictionary<string, AnalyzerOptions> AnalyzerOptionsWithoutAdditionalFilesCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ConditionContext> ConditionContextCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<string, ConditionImplicationContext> ConditionImplicationContextCache =
        new(StringComparer.Ordinal);

    private static readonly ConcurrentDictionary<SourceContextCacheKey, SourceContext> SourceContextCache = new();

    private static readonly AnalyzerOptions EmptyAnalyzerOptions =
        new(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string>.Empty));

    private static readonly ImmutableDictionary<string, ReportDiagnostic> CommonBugDiagnosticOptions =
        Enumerable.Range(48, 29).ToImmutableDictionary(
            static number => $"SP{number:0000}",
            static number => number is 55 or 58 or 62 or 63 or 65 or 67 or 68 or 70 or 72 or 73
                ? ReportDiagnostic.Info
                : ReportDiagnostic.Warn,
            StringComparer.Ordinal);

    public static string CreateExpressionBodiedPropertyContractSource(
        string attributeText,
        bool disablePurityPlacementDiagnostic = false)
    {
        if (string.IsNullOrWhiteSpace(attributeText))
            throw new ArgumentException("Attribute text is required.", nameof(attributeText));

        const string sourceTemplate = """
                                      using SharpProof.Attributes;

                                      public sealed class TestClass
                                      {
                                          [ATTRIBUTE]
                                          public int Value => 42;
                                      }
                                      """;
        var pragma = disablePurityPlacementDiagnostic
            ? "#pragma warning disable SP0004\n"
            : string.Empty;
        return pragma + sourceTemplate.Replace("ATTRIBUTE", attributeText, StringComparison.Ordinal);
    }

    public static ImmutableDictionary<string, string> CreateExceptionFlowOptions(
        bool? reportExceptions = true,
        bool? checkedExceptions = true)
    {
        var options = ImmutableDictionary<string, string>.Empty;
        if (reportExceptions.HasValue)
            options = options.Add(
                "sharpproof_report_exceptions",
                reportExceptions.Value ? "true" : "false");
        if (checkedExceptions.HasValue)
            options = options.Add(
                "sharpproof_checked_exceptions",
                checkedExceptions.Value ? "true" : "false");
        return options;
    }

    public static Task<ImmutableArray<Diagnostic>> GetExceptionFlowDiagnosticsAsync(
        string source,
        string compilationName,
        bool? reportExceptions = true,
        bool? checkedExceptions = true,
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool concurrentAnalysis = false)
    {
        return GetDiagnosticsAsync(
            source,
            CreateExceptionFlowOptions(reportExceptions, checkedExceptions),
            frameworkReferences: frameworkReferences,
            concurrentAnalysis: concurrentAnalysis,
            compilationName: compilationName);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        ImmutableDictionary<string, string>? globalOptions = null,
        bool allowUnsafe = false,
        ImmutableArray<AdditionalText>? additionalFiles = null,
        string? sourcePath = null,
        bool autoEnableEffectSummaryJsonForAdditionalFiles = true,
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool concurrentAnalysis = false,
        string compilationName = "AnalyzerTestHost",
        AnalyzerFeatures analyzerFeatures = AnalyzerFeatures.All)
    {
        return await GetDiagnosticsAsync(
            source,
            globalOptions,
            allowUnsafe,
            additionalFiles,
            sourcePath,
            autoEnableEffectSummaryJsonForAdditionalFiles,
            frameworkReferences,
            concurrentAnalysis,
            null,
            compilationName,
            analyzerFeatures);
    }

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        ImmutableDictionary<string, string>? globalOptions,
        bool allowUnsafe,
        ImmutableArray<AdditionalText>? additionalFiles,
        string? sourcePath,
        bool autoEnableEffectSummaryJsonForAdditionalFiles,
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool concurrentAnalysis = false,
        ImmutableArray<MetadataReference>? additionalMetadataReferences = null,
        string compilationName = "AnalyzerTestHost",
        AnalyzerFeatures analyzerFeatures = AnalyzerFeatures.All)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            PreviewParseOptions,
            sourcePath ?? string.Empty);
        var references = frameworkReferences.HasValue
            ? EnsureEnforcePureAttributeReference(frameworkReferences.Value)
            : TrustedPlatformReferencesWithEnforcePure.Value;
        if (additionalMetadataReferences.HasValue) references = references.AddRange(additionalMetadataReferences.Value);

        var compilationOptions = allowUnsafe ? UnsafeCompilationOptions : DefaultCompilationOptions;
        if (analyzerFeatures == AnalyzerFeatures.CommonBugs)
            compilationOptions = compilationOptions.WithSpecificDiagnosticOptions(
                compilationOptions.SpecificDiagnosticOptions.SetItems(CommonBugDiagnosticOptions));
        var compilation = CreateCompilation(
            compilationName,
            references,
            compilationOptions,
            syntaxTree);

        var analyzerOptions = CreateAnalyzerOptions(
            ApplyFileLevelDiagnosticOptions(source, globalOptions),
            additionalFiles,
            autoEnableEffectSummaryJsonForAdditionalFiles);

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            GetAnalyzers(analyzerFeatures),
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                null,
                concurrentAnalysis,
                false,
                false));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(AnalyzerFeatures features)
    {
        return AnalyzerInstances.GetOrAdd(
            AnalyzerFeatureDependencies.Expand(features),
            static expandedFeatures => ImmutableArray.Create<DiagnosticAnalyzer>(
                new SharpProofAnalyzer(expandedFeatures)));
    }

    public static AnalyzerOptions CreateAnalyzerOptions(
        ImmutableDictionary<string, string>? globalOptions = null,
        ImmutableArray<AdditionalText>? additionalFiles = null,
        bool autoEnableEffectSummaryJsonForAdditionalFiles = true)
    {
        var analyzerAdditionalFiles = additionalFiles ?? ImmutableArray<AdditionalText>.Empty;
        var analyzerGlobalOptions = globalOptions ?? ImmutableDictionary<string, string>.Empty;
        if (autoEnableEffectSummaryJsonForAdditionalFiles &&
            analyzerAdditionalFiles.Length > 0 &&
            !analyzerGlobalOptions.ContainsKey("sharpproof_enable_effect_summary_json"))
            analyzerGlobalOptions = analyzerGlobalOptions.Add(
                "sharpproof_enable_effect_summary_json",
                "true");

        if (analyzerAdditionalFiles.Length == 0 &&
            analyzerGlobalOptions.Count == 0)
            return EmptyAnalyzerOptions;

        if (analyzerAdditionalFiles.Length == 0)
            return AnalyzerOptionsWithoutAdditionalFilesCache.GetOrAdd(
                CreateGlobalOptionsCacheKey(analyzerGlobalOptions),
                static cacheKey => CreateAnalyzerOptionsWithoutAdditionalFiles(cacheKey));

        return new AnalyzerOptions(
            analyzerAdditionalFiles,
            new TestAnalyzerConfigOptionsProvider(analyzerGlobalOptions));
    }

    internal static bool HasFileLevelMissingPuritySuppression(string source)
    {
        return source.AsSpan().TrimStart().StartsWith(
            "#pragma warning disable SP0004".AsSpan(),
            StringComparison.Ordinal);
    }

    private static ImmutableDictionary<string, string>? ApplyFileLevelDiagnosticOptions(
        string source,
        ImmutableDictionary<string, string>? globalOptions)
    {
        if (!HasFileLevelMissingPuritySuppression(source) ||
            globalOptions?.ContainsKey(SuggestMissingEnforcePureOption) == true)
            return globalOptions;

        return (globalOptions ?? ImmutableDictionary<string, string>.Empty)
            .Add(SuggestMissingEnforcePureOption, "false");
    }

    public static Diagnostic SingleDiagnostic(
        ImmutableArray<Diagnostic> diagnostics,
        string diagnosticId)
    {
        return diagnostics.Single(diagnostic => diagnostic.Id == diagnosticId);
    }

    public static (string Source, string? ExpectedSpanText) StripSp0002Markup(string markedSource)
    {
        return StripDiagnosticMarkup(markedSource, SharpProofDiagnostics.PurityNotVerifiedId, false);
    }

    public static (string Source, string ExpectedSpanText) StripRequiredSp0002Markup(string markedSource)
    {
        var (source, expectedSpanText) = StripDiagnosticMarkup(
            markedSource,
            SharpProofDiagnostics.PurityNotVerifiedId,
            true);
        return (source, expectedSpanText!);
    }

    public static async Task<(string Source, Diagnostic? Diagnostic)> AssertOptionalSingleSp0002Async(
        string markedSource,
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool concurrentAnalysis = false,
        AnalyzerFeatures analyzerFeatures = AnalyzerFeatures.Purity)
    {
        var (source, expectedSpanText) = StripSp0002Markup(markedSource);
        var diagnostics = await GetDiagnosticsAsync(
            source,
            frameworkReferences: frameworkReferences,
            concurrentAnalysis: concurrentAnalysis,
            analyzerFeatures: analyzerFeatures);
        var purityDiagnostics = diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
            .ToArray();

        if (expectedSpanText == null)
        {
            Assert.That(purityDiagnostics, Is.Empty);
            Assert.That(diagnostics, Is.Empty);
            return (source, null);
        }

        Assert.That(purityDiagnostics, Has.Length.EqualTo(1));
        Assert.That(diagnostics, Has.Length.EqualTo(1));

        var diagnostic = purityDiagnostics[0];
        AssertDiagnosticSpan(source, diagnostic, expectedSpanText);
        return (source, diagnostic);
    }

    public static async Task<(string Source, Diagnostic Diagnostic)> AssertSingleSp0002Async(
        string markedSource,
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool concurrentAnalysis = false,
        AnalyzerFeatures analyzerFeatures = AnalyzerFeatures.Purity)
    {
        var (source, expectedSpanText) = StripRequiredSp0002Markup(markedSource);
        var diagnostics = await GetDiagnosticsAsync(
            source,
            frameworkReferences: frameworkReferences,
            concurrentAnalysis: concurrentAnalysis,
            analyzerFeatures: analyzerFeatures);
        var diagnostic = SingleDiagnostic(diagnostics, SharpProofDiagnostics.PurityNotVerifiedId);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        AssertDiagnosticSpan(source, diagnostic, expectedSpanText);
        return (source, diagnostic);
    }

    public static async Task<(string Source, Diagnostic Diagnostic)> AssertSingleDiagnosticAsync(
        string markedSource,
        string diagnosticId,
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool concurrentAnalysis = false,
        AnalyzerFeatures analyzerFeatures = AnalyzerFeatures.All)
    {
        var (source, expectedSpanText) = StripDiagnosticMarkup(
            markedSource,
            diagnosticId,
            true);
        var diagnostics = await GetDiagnosticsAsync(
            source,
            frameworkReferences: frameworkReferences,
            concurrentAnalysis: concurrentAnalysis,
            analyzerFeatures: analyzerFeatures);
        var diagnostic = SingleDiagnostic(diagnostics, diagnosticId);

        Assert.That(diagnostics, Has.Length.EqualTo(1));
        AssertDiagnosticSpan(source, diagnostic, expectedSpanText!);
        return (source, diagnostic);
    }

    public static void AssertDiagnosticSpan(
        string source,
        Diagnostic diagnostic,
        string expectedSpanText)
    {
        var actualSpanText = source.Substring(
            diagnostic.Location.SourceSpan.Start,
            diagnostic.Location.SourceSpan.Length);
        Assert.That(actualSpanText, Is.EqualTo(expectedSpanText));
    }

    private static (string Source, string? ExpectedSpanText) StripDiagnosticMarkup(
        string markedSource,
        string diagnosticId,
        bool required)
    {
        var prefix = "{|" + diagnosticId + ":";
        const string suffix = "|}";
        var start = markedSource.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            if (required) throw new InvalidOperationException("Expected " + diagnosticId + " markup start.");

            return (markedSource, null);
        }

        var contentStart = start + prefix.Length;
        var end = markedSource.IndexOf(suffix, contentStart, StringComparison.Ordinal);
        if (end < 0) throw new InvalidOperationException("Expected " + diagnosticId + " markup end.");

        var expectedSpanText = markedSource.Substring(contentStart, end - contentStart);
        var source = markedSource.Remove(end, suffix.Length).Remove(start, prefix.Length);
        return (source, expectedSpanText);
    }

    public static ConditionContext CreateConditionContext(string parameterList, string conditionExpression)
    {
        return CreateConditionContext(parameterList, conditionExpression, "");
    }

    public static ConditionContext CreateConditionContext(
        string parameterList,
        string conditionExpression,
        string extraSource)
    {
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

        return ConditionContextCache.GetOrAdd(
            source,
            static conditionSource => CreateConditionContextCore(conditionSource));
    }

    private static ConditionContext CreateConditionContextCore(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, PreviewParseOptions);
        var compilation = CreateCompilation(
            "ConditionHost",
            GetMinimalFrameworkReferences(),
            DefaultCompilationOptions,
            syntaxTree);

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
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool allowUnsafe = false,
        string? sourcePath = null,
        CSharpParseOptions? parseOptions = null,
        CSharpCompilationOptions? compilationOptions = null,
        ImmutableArray<MetadataReference>? additionalMetadataReferences = null)
    {
        if (CanUseSourceContextCache(
                frameworkReferences,
                allowUnsafe,
                sourcePath,
                parseOptions,
                compilationOptions,
                additionalMetadataReferences))
            return SourceContextCache.GetOrAdd(
                new SourceContextCacheKey(source, compilationName, string.Empty),
                static key => CreateSourceContextCore(
                    key.Source,
                    key.CompilationName,
                    sourcePath: key.SourcePath,
                    frameworkReferences: null,
                    allowUnsafe: false,
                    parseOptions: null,
                    compilationOptions: null,
                    additionalMetadataReferences: null));

        return CreateSourceContextCore(
            source,
            compilationName,
            frameworkReferences,
            allowUnsafe,
            sourcePath,
            parseOptions,
            compilationOptions,
            additionalMetadataReferences);
    }

    private static SourceContext CreateSourceContextCore(
        string source,
        string compilationName,
        ImmutableArray<MetadataReference>? frameworkReferences = null,
        bool allowUnsafe = false,
        string? sourcePath = null,
        CSharpParseOptions? parseOptions = null,
        CSharpCompilationOptions? compilationOptions = null,
        ImmutableArray<MetadataReference>? additionalMetadataReferences = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            parseOptions ?? PreviewParseOptions,
            sourcePath ?? string.Empty);

        var references = frameworkReferences ?? GetMinimalFrameworkReferences();
        if (additionalMetadataReferences.HasValue) references = references.AddRange(additionalMetadataReferences.Value);

        var options = compilationOptions ?? (allowUnsafe ? UnsafeCompilationOptions : DefaultCompilationOptions);
        var compilation = CreateCompilation(
            compilationName,
            references,
            options,
            syntaxTree);

        return new SourceContext(
            compilation,
            compilation.GetSemanticModel(syntaxTree),
            syntaxTree,
            syntaxTree.GetRoot());
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

        return ConditionImplicationContextCache.GetOrAdd(
            source,
            static implicationSource => CreateConditionImplicationContextCore(implicationSource));
    }

    private static ConditionImplicationContext CreateConditionImplicationContextCore(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, PreviewParseOptions);
        var compilation = CreateCompilation(
            "ConditionHost",
            GetMinimalFrameworkReferences(),
            DefaultCompilationOptions,
            syntaxTree);

        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var variables = syntaxTree.GetRoot()
            .DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .Select(variable => variable.Initializer!.Value)
            .ToArray();

        return new ConditionImplicationContext(semanticModel, variables[0], variables[1]);
    }

    private static bool CanUseSourceContextCache(
        ImmutableArray<MetadataReference>? frameworkReferences,
        bool allowUnsafe,
        string? sourcePath,
        CSharpParseOptions? parseOptions,
        CSharpCompilationOptions? compilationOptions,
        ImmutableArray<MetadataReference>? additionalMetadataReferences)
    {
        if (allowUnsafe ||
            !string.IsNullOrEmpty(sourcePath) ||
            parseOptions is not null ||
            compilationOptions is not null ||
            additionalMetadataReferences.HasValue)
            return false;

        if (!frameworkReferences.HasValue) return true;

        var references = frameworkReferences.Value;
        var minimalReferences = GetMinimalFrameworkReferences();
        return references.Length == minimalReferences.Length &&
               references.SequenceEqual(minimalReferences);
    }

    internal static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
    {
        return TrustedPlatformReferences.Value;
    }

    internal static ImmutableArray<MetadataReference> GetMinimalFrameworkReferences()
    {
        return MinimalFrameworkReferences.Value;
    }

    private static ImmutableArray<MetadataReference> CreateTrustedPlatformReferences()
    {
        var trustedPlatformAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
            return ImmutableArray.Create<MetadataReference>(
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Console).Assembly.Location));

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
            .Cast<MetadataReference>()
            .ToImmutableArray();
    }

    private static ImmutableArray<MetadataReference> CreateTrustedPlatformReferencesWithEnforcePure()
    {
        var references = TrustedPlatformReferences.Value;
        if (references.IsDefault) references = ImmutableArray<MetadataReference>.Empty;

        return references.Add(EnforcePureAttributeReference.Value);
    }

    private static ImmutableArray<MetadataReference> CreateMinimalFrameworkReferences()
    {
        var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase)
        {
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
            foreach (var path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(fileName, "System.Runtime", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "netstandard", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.Runtime.Extensions", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.Runtime.Numerics", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.ObjectModel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "System.Text.RegularExpressions", StringComparison.OrdinalIgnoreCase))
                    references[path] = MetadataReference.CreateFromFile(path);
            }

        return references.Values.ToImmutableArray();
    }

    private static ImmutableArray<MetadataReference> CreateMinimalFrameworkReferencesWithEnforcePure()
    {
        return MinimalFrameworkReferences.Value.Add(EnforcePureAttributeReference.Value);
    }

    private static ImmutableArray<MetadataReference> EnsureEnforcePureAttributeReference(
        ImmutableArray<MetadataReference> references)
    {
        if (references == MinimalFrameworkReferences.Value) return MinimalFrameworkReferencesWithEnforcePure.Value;

        var enforcePurePath = EnforcePureAttributeReference.Value.Display;
        foreach (var reference in references)
            if (string.Equals(reference.Display, enforcePurePath, StringComparison.OrdinalIgnoreCase))
                return references;

        return references.Add(EnforcePureAttributeReference.Value);
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        ImmutableArray<MetadataReference> references,
        CSharpCompilationOptions options,
        SyntaxTree syntaxTree)
    {
        var template = GetCompilationTemplate(references, options);
        if (template != null)
            return template.Value
                .WithAssemblyName(assemblyName)
                .AddSyntaxTrees(syntaxTree);

        return CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            references,
            options);
    }

    private static Lazy<CSharpCompilation>? GetCompilationTemplate(
        ImmutableArray<MetadataReference> references,
        CSharpCompilationOptions options)
    {
        if (!options.SpecificDiagnosticOptions.IsEmpty) return null;

        var allowUnsafe = options.AllowUnsafe;
        if (references == TrustedPlatformReferencesWithEnforcePure.Value)
            return allowUnsafe
                ? TrustedPlatformUnsafeCompilationTemplate
                : TrustedPlatformCompilationTemplate;

        if (references == MinimalFrameworkReferences.Value)
            return allowUnsafe
                ? MinimalFrameworkUnsafeCompilationTemplate
                : MinimalFrameworkCompilationTemplate;

        if (references == MinimalFrameworkReferencesWithEnforcePure.Value)
            return allowUnsafe
                ? MinimalFrameworkWithEnforcePureUnsafeCompilationTemplate
                : MinimalFrameworkWithEnforcePureCompilationTemplate;

        return null;
    }

    private static CSharpCompilation CreateCompilationTemplate(
        ImmutableArray<MetadataReference> references,
        CSharpCompilationOptions options)
    {
        return CSharpCompilation.Create(
            "AnalyzerTestHost.Template",
            references: references,
            options: options);
    }

    private static string CreateGlobalOptionsCacheKey(ImmutableDictionary<string, string> analyzerGlobalOptions)
    {
        var builder = new StringBuilder();
        foreach (var pair in analyzerGlobalOptions.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            AppendLengthPrefixed(builder, pair.Key);
            AppendLengthPrefixed(builder, pair.Value);
        }

        return builder.ToString();
    }

    private static AnalyzerOptions CreateAnalyzerOptionsWithoutAdditionalFiles(string cacheKey)
    {
        if (string.IsNullOrEmpty(cacheKey)) return EmptyAnalyzerOptions;

        var builder = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        var index = 0;
        while (index < cacheKey.Length)
        {
            if (!TryReadLengthPrefixed(cacheKey, ref index, out var key) ||
                !TryReadLengthPrefixed(cacheKey, ref index, out var value) ||
                key.Length == 0)
                break;

            builder[key] = value;
        }

        return new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(builder.ToImmutable()));
    }

    private static void AppendLengthPrefixed(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
    }

    private static bool TryReadLengthPrefixed(string text, ref int index, out string value)
    {
        value = string.Empty;
        var separatorIndex = text.IndexOf(':', index);
        if (separatorIndex < index ||
            !int.TryParse(text.AsSpan(index, separatorIndex - index), out var length) ||
            length < 0)
            return false;

        var valueStart = separatorIndex + 1;
        var valueEnd = valueStart + length;
        if (valueEnd > text.Length) return false;

        value = text.Substring(valueStart, length);
        index = valueEnd;
        return true;
    }

    internal readonly record struct ConditionContext(
        SemanticModel SemanticModel,
        ExpressionSyntax Expression);

    internal readonly record struct ConditionImplicationContext(
        SemanticModel SemanticModel,
        ExpressionSyntax PathCondition,
        ExpressionSyntax Conclusion);

    internal readonly record struct SourceContext(
        CSharpCompilation Compilation,
        SemanticModel SemanticModel,
        SyntaxTree SyntaxTree,
        SyntaxNode Root);

    private readonly record struct SourceContextCacheKey(
        string Source,
        string CompilationName,
        string SourcePath);

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
        private readonly AnalyzerConfigOptions _emptyOptions =
            new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

        public TestAnalyzerConfigOptionsProvider(ImmutableDictionary<string, string> globalOptions)
        {
            GlobalOptions = new TestAnalyzerConfigOptions(globalOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _emptyOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _emptyOptions;
        }
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
