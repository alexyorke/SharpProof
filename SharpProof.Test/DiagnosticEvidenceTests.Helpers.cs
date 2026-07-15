using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Test;

public partial class DiagnosticEvidenceTests
{
    private static readonly MetadataReference EnforcePureAttributeReference =
        MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location);

    private static readonly ImmutableArray<MetadataReference> GeneratedPurityProbeReferences =
        AnalyzerTestHost.GetTrustedPlatformReferences().Add(EnforcePureAttributeReference);

    private static readonly Lazy<GeneratedPurityCatalog> DefaultGeneratedPurityCatalog = new(
        () => CreateGeneratedPurityCatalog(CreateGeneratedPurityAnalyzerOptions()),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private static ImmutableDictionary<string, string> ReportExceptionsOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_report_exceptions", "true");
    }

    private static ImmutableDictionary<string, string> CheckedExceptionsOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_checked_exceptions", "true");
    }

    private static ImmutableDictionary<string, string> RuntimeHazardSitesOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "sites");
    }

    private static ImmutableDictionary<string, string> RuntimeHazardAllOptions()
    {
        return ImmutableDictionary<string, string>.Empty.Add("sharpproof_runtime_hazard_mode", "all");
    }

    private static ImmutableDictionary<string, string> ReportAndCheckedExceptionsOptions()
    {
        return ReportExceptionsOptions().Add("sharpproof_checked_exceptions", "true");
    }

    private static void AssertExceptionEdgesPropertyContains(Diagnostic diagnostic, params string[] expectedFragments)
    {
        Assert.That(
            diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var serializedEdges) &&
            !string.IsNullOrWhiteSpace(serializedEdges),
            Is.True,
            "Expected sharpproof.exceptions.edges on diagnostic.");

        foreach (var expectedFragment in expectedFragments)
            Assert.That(serializedEdges, Does.Contain(expectedFragment));
    }

    private static void AssertExceptionEdgesPropertyContainsIfPresent(Diagnostic diagnostic,
        params string[] expectedFragments)
    {
        if (!diagnostic.Properties.TryGetValue(SharpProofDiagnostics.ExceptionEdgesProperty, out var serializedEdges) ||
            string.IsNullOrWhiteSpace(serializedEdges))
            return;

        foreach (var expectedFragment in expectedFragments)
            Assert.That(serializedEdges, Does.Contain(expectedFragment));
    }

    private static void AssertMatchingSummaryAndSiteEvidence(
        ImmutableArray<Diagnostic> diagnostics,
        string exceptionType,
        string category,
        string source)
    {
        var summaryDiagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.ExceptionSummaryId)
                .ToImmutableArray(),
            SharpProofDiagnostics.ExceptionSummaryId);
        var siteDiagnostic = AnalyzerTestHost.SingleDiagnostic(
            diagnostics.Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.UncaughtExceptionSiteId)
                .ToImmutableArray(),
            SharpProofDiagnostics.UncaughtExceptionSiteId);

        foreach (var diagnostic in new[] { summaryDiagnostic, siteDiagnostic })
        {
            Assert.That(
                diagnostic.Properties[SharpProofDiagnostics.ExceptionTypesProperty],
                Is.EqualTo(exceptionType));
            Assert.That(
                diagnostic.Properties[SharpProofDiagnostics.ExceptionCategoriesProperty],
                Is.EqualTo(category));
            Assert.That(
                diagnostic.Properties[SharpProofDiagnostics.ExceptionSourcesProperty],
                Is.EqualTo(source));
        }
    }

    private static void AssertSp0002Evidence(
        Diagnostic diagnostic,
        string? category = null,
        string? rule = null,
        string? catalogSource = null,
        string? symbolContains = null,
        string? operationKind = null)
    {
        if (category != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCategoryProperty], Is.EqualTo(category));

        if (rule != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityRuleProperty], Is.EqualTo(rule));

        if (operationKind != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityOperationKindProperty],
                Is.EqualTo(operationKind));

        if (catalogSource != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpurityCatalogSourceProperty],
                Is.EqualTo(catalogSource));

        if (symbolContains != null)
            Assert.That(diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty],
                Does.Contain(symbolContains));
    }

    private static string CreateEffectSummaryJson(
        string assemblyPath,
        string symbol,
        string[] thrownExceptionTypes,
        params string[] transitiveThrownExceptionTypes)
    {
        return GeneratedPurityTestSupport.CreateEffectSummaryJson(
            assemblyPath,
            symbol,
            thrownExceptionTypes,
            transitiveThrownExceptionTypes);
    }

    private static string CreatePuritySummaryJson(
        string assemblyPath,
        string actualMethodLookupSymbol,
        string classification,
        string categoriesJson,
        string? symbolOverride = null)
    {
        return GeneratedPurityTestSupport.CreatePuritySummaryJson(
            assemblyPath,
            actualMethodLookupSymbol,
            classification,
            categoriesJson,
            symbolOverride);
    }

    private static AnalyzerOptions CreateAnalyzerOptions(
        ImmutableDictionary<string, string>? globalOptions = null,
        ImmutableArray<AdditionalText>? additionalFiles = null)
    {
        return AnalyzerTestHost.CreateAnalyzerOptions(globalOptions, additionalFiles);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        ImmutableDictionary<string, string>? globalOptions = null,
        bool allowUnsafe = false,
        ImmutableArray<AdditionalText>? additionalFiles = null,
        ImmutableArray<MetadataReference>? additionalMetadataReferences = null)
    {
        return await AnalyzerTestHost.GetDiagnosticsAsync(
            source,
            globalOptions,
            allowUnsafe,
            additionalFiles,
            null,
            true,
            additionalMetadataReferences: additionalMetadataReferences,
            compilationName: "DiagnosticEvidenceTests");
    }

    private static AnalyzerOptions CreateGeneratedPurityAnalyzerOptions()
    {
        return CreateAnalyzerOptions();
    }

    private static CSharpCompilation CreateGeneratedPurityProbeCompilation(SyntaxTree syntaxTree)
    {
        return CSharpCompilation.Create(
            "GeneratedPurityProbe",
            new[] { syntaxTree },
            GeneratedPurityProbeReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static GeneratedPurityProbeContext CreateGeneratedPurityProbeContext(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CreateGeneratedPurityProbeCompilation(syntaxTree);
        return new GeneratedPurityProbeContext(
            syntaxTree,
            compilation,
            compilation.GetSemanticModel(syntaxTree),
            syntaxTree.GetRoot());
    }

    private static GeneratedPurityCatalog CreateGeneratedPurityCatalog(AnalyzerOptions analyzerOptions)
    {
        return GeneratedPurityCatalog.FromOptions(analyzerOptions, CancellationToken.None);
    }

    private static GeneratedPurityCatalog GetGeneratedPurityCatalog(AnalyzerOptions? analyzerOptions = null)
    {
        return analyzerOptions is null
            ? DefaultGeneratedPurityCatalog.Value
            : CreateGeneratedPurityCatalog(analyzerOptions);
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        IMethodSymbol methodSymbol,
        Compilation compilation,
        AnalyzerOptions? analyzerOptions = null)
    {
        return ResolveGeneratedPurity(GetGeneratedPurityCatalog(analyzerOptions), methodSymbol, compilation);
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        IFieldSymbol fieldSymbol,
        Compilation compilation,
        AnalyzerOptions? analyzerOptions = null)
    {
        return ResolveGeneratedPurity(GetGeneratedPurityCatalog(analyzerOptions), fieldSymbol, compilation);
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        ISymbol symbol,
        Compilation compilation,
        AnalyzerOptions? analyzerOptions = null)
    {
        return ResolveGeneratedPurity(GetGeneratedPurityCatalog(analyzerOptions), symbol, compilation);
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        GeneratedPurityCatalog catalog,
        ISymbol symbol,
        Compilation compilation)
    {
        return symbol switch
        {
            IPropertySymbol { GetMethod: not null } property =>
                ResolveGeneratedPurity(catalog, property.GetMethod, compilation),
            IMethodSymbol method => ResolveGeneratedPurity(catalog, method, compilation),
            IFieldSymbol field => ResolveGeneratedPurity(catalog, field, compilation),
            _ => throw new InvalidOperationException(
                "Unsupported generated purity symbol: " + symbol.ToDisplayString())
        };
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        GeneratedPurityCatalog catalog,
        IMethodSymbol method,
        Compilation compilation)
    {
        var matched = catalog.TryGetPurity(method.OriginalDefinition, compilation, out var purity);
        return CreateGeneratedPurityResolution(matched, purity);
    }

    private static GeneratedPurityResolution ResolveGeneratedPurity(
        GeneratedPurityCatalog catalog,
        IFieldSymbol field,
        Compilation compilation)
    {
        var matched = catalog.TryGetFieldPurity(field.OriginalDefinition, compilation, out var purity);
        return CreateGeneratedPurityResolution(matched, purity);
    }

    private static GeneratedPurityResolution CreateGeneratedPurityResolution(
        bool matched,
        GeneratedPurityCatalog.PurityEntry purity)
    {
        return matched
            ? new GeneratedPurityResolution(
                true,
                purity.Classification,
                purity.PrimaryCategory,
                purity.Categories,
                purity.FreshnessClassification,
                purity.EffectVisibilityClassification)
            : GeneratedPurityResolution.Unmatched;
    }

    private static GeneratedPurityResolution ResolveGeneratedPurityByExpressionText(
        GeneratedPurityProbeContext probe,
        string expressionText,
        AnalyzerOptions? analyzerOptions = null)
    {
        var objectCreations = probe.Root
            .DescendantNodes()
            .OfType<ObjectCreationExpressionSyntax>()
            .Where(node => node.ToString() == expressionText)
            .ToArray();
        if (objectCreations.Length > 0)
        {
            var symbol = objectCreations
                .Select(node => (IMethodSymbol)probe.SemanticModel.GetSymbolInfo(node).Symbol!)
                .Select(static symbol => symbol.OriginalDefinition)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .Single();
            return ResolveGeneratedPurity(symbol, probe.Compilation, analyzerOptions);
        }

        var invocations = probe.Root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(node => node.ToString() == expressionText)
            .ToArray();
        if (invocations.Length > 0)
        {
            var symbol = invocations
                .Select(node => (IMethodSymbol)probe.SemanticModel.GetSymbolInfo(node).Symbol!)
                .Select(static symbol => symbol.OriginalDefinition)
                .Distinct<IMethodSymbol>(SymbolEqualityComparer.Default)
                .Single();
            return ResolveGeneratedPurity(symbol, probe.Compilation, analyzerOptions);
        }

        var memberAccesses = probe.Root
            .DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .Where(node => node.ToString() == expressionText)
            .ToArray();
        if (memberAccesses.Length > 0)
        {
            var symbols = memberAccesses
                .Select(node => probe.SemanticModel.GetSymbolInfo(node).Symbol)
                .OfType<ISymbol>()
                .Distinct(SymbolEqualityComparer.Default)
                .ToArray();
            return ResolveGeneratedPurity(symbols.Single(), probe.Compilation, analyzerOptions);
        }

        throw new InvalidOperationException("Could not resolve generated purity expression: " + expressionText);
    }

    private static GeneratedPurityResolution[] ResolveGeneratedPurityByExpressionTexts(
        GeneratedPurityProbeContext probe,
        AnalyzerOptions? analyzerOptions = null,
        params string[] expressionTexts)
    {
        return expressionTexts
            .Select(expressionText => ResolveGeneratedPurityByExpressionText(probe, expressionText, analyzerOptions))
            .ToArray();
    }

    private static ImmutableArray<AdditionalText> CreateSyntheticGeneratedPurityAdditionalFiles(
        string assemblyPath,
        params (string FileName, string ActualMethodLookupSymbol, string DisplaySymbol, string Classification, string
            CategoriesJson)[] entries)
    {
        return GeneratedPurityTestSupport.CreateSyntheticGeneratedPurityAdditionalFiles(
            entries.Select(entry => (
                assemblyPath,
                entry.FileName,
                entry.ActualMethodLookupSymbol,
                entry.DisplaySymbol,
                entry.Classification,
                entry.CategoriesJson)).ToArray());
    }

    private static string FormatJsonArray(params string[] values)
    {
        return GeneratedPurityTestSupport.FormatJsonArray(values);
    }

    private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
    {
        return AnalyzerTestHost.GetTrustedPlatformReferences();
    }

    private static MetadataOnlyAssemblyFixture CreateMetadataOnlyAssemblyFixture(
        string assemblyName,
        string source)
    {
        var tempDirectory = Path.Combine(
            TestContext.CurrentContext.WorkDirectory,
            assemblyName + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        var assemblyPath = Path.Combine(tempDirectory, assemblyName + ".dll");
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emitResult = compilation.Emit(assemblyPath);
        Assert.That(
            emitResult.Success,
            Is.True,
            string.Join(Environment.NewLine, emitResult.Diagnostics.Select(diagnostic => diagnostic.ToString())));

        return new MetadataOnlyAssemblyFixture(tempDirectory, assemblyPath);
    }

    private readonly record struct GeneratedPurityResolution(
        bool Matched,
        string Classification,
        string PrimaryCategory,
        ImmutableArray<string> Categories,
        string FreshnessClassification,
        string EffectVisibilityClassification)
    {
        public static GeneratedPurityResolution Unmatched { get; } = new(
            false,
            string.Empty,
            string.Empty,
            ImmutableArray<string>.Empty,
            string.Empty,
            string.Empty);
    }

    private readonly record struct GeneratedPurityProbeContext(
        SyntaxTree SyntaxTree,
        Compilation Compilation,
        SemanticModel SemanticModel,
        SyntaxNode Root);

    private sealed class MetadataOnlyAssemblyFixture : IDisposable
    {
        public MetadataOnlyAssemblyFixture(string directoryPath, string assemblyPath)
        {
            DirectoryPath = directoryPath;
            AssemblyPath = assemblyPath;
            Reference = MetadataReference.CreateFromFile(assemblyPath);
        }

        public string DirectoryPath { get; }
        public string AssemblyPath { get; }
        public MetadataReference Reference { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true);
        }
    }
}
