using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Test;

[TestFixture]
public class Sp0004ConfigurationTests
{
    [Test]
    public async Task Sp0004_ScopeOff_SuppressesMissingPuritySuggestions()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Pure() => 1;
}", ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_scope", "off"));

        Assert.That(DiagnosticMessages(diagnostics), Has.None.Contains("Pure"));
    }

    [Test]
    public async Task Sp0004_PerTreeScopeOff_SuppressesMissingPuritySuggestions()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Pure() => 1;
}",
            ImmutableDictionary<string, string>.Empty,
            treeOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_scope",
                "off"));

        Assert.That(DiagnosticMessages(diagnostics), Has.None.Contains("Pure"));
    }

    [Test]
    public async Task Sp0004_InvalidPerTreeConfigurationValue_ReportsDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(
            @"public class TestClass
{
}",
            ImmutableDictionary<string, string>.Empty,
            treeOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_scope",
                "sometimes"));

        var diagnostic = diagnostics.Single(diagnostic =>
            diagnostic.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty],
            Is.EqualTo("sharpproof_suggest_missing_enforce_pure_scope"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ConfigurationValueProperty], Is.EqualTo("sometimes"));
        Assert.That(diagnostic.Location.GetLineSpan().StartLinePosition.Line, Is.EqualTo(0));
    }

    [Test]
    public async Task GlobalOnlyPerTreeConfigurationValue_ReportsScopeDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(
            @"public class TestClass
{
}",
            ImmutableDictionary<string, string>.Empty,
            treeOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_smt_mode", "deep"));

        var diagnostic = diagnostics.Single(diagnostic =>
            diagnostic.Id == SharpProofDiagnostics.InvalidAnalyzerConfigurationId);
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ConfigurationKeyProperty],
            Is.EqualTo("sharpproof_smt_mode"));
        Assert.That(diagnostic.Properties[SharpProofDiagnostics.ConfigurationValueProperty], Is.EqualTo("deep"));
        Assert.That(
            diagnostic.Properties[SharpProofDiagnostics.ConfigurationInvalidReasonProperty],
            Does.Contain("compilation-global"));
    }

    [Test]
    public async Task GlobalOnlyConfigurationValueMirroredInTreeOptions_DoesNotReportScopeDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(
            @"public class TestClass
{
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_attribute_stub_namespaces", "<global>"),
            treeOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_attribute_stub_namespaces",
                "<global>"));

        Assert.That(
            diagnostics.Select(diagnostic => diagnostic.Id),
            Has.None.EqualTo(SharpProofDiagnostics.InvalidAnalyzerConfigurationId));
    }

    [Test]
    public async Task Sp0004_LegacyBooleanFalse_SuppressesMissingPuritySuggestions()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Pure() => 1;
}", ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure", "false"));

        Assert.That(DiagnosticMessages(diagnostics), Has.None.Contains("Pure"));
    }

    [Test]
    public async Task Sp0004_PerTreeBooleanTrue_ReenablesGlobalBooleanFalse()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Pure() => 1;
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure", "false"),
            treeOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure",
                "true"));

        Assert.That(DiagnosticMessages(diagnostics), Has.Some.Contains("Pure"));
    }

    [Test]
    public async Task Sp0004_ScopePublic_ReportsPublicMethodsOnly()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int PublicPure() => 1;
    internal int InternalPure() => 2;
    private int PrivatePure() => 3;
    protected int ProtectedPure() => 4;
    protected internal int ProtectedInternalPure() => 5;
    private protected int PrivateProtectedPure() => 6;
}", ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_scope", "public"));

        var messages = DiagnosticMessages(diagnostics);
        Assert.That(messages, Has.Some.Contains("PublicPure"));
        Assert.That(messages, Has.Some.Contains("ProtectedPure"));
        Assert.That(messages, Has.Some.Contains("ProtectedInternalPure"));
        Assert.That(messages, Has.None.Contains("Method 'InternalPure'"));
        Assert.That(messages, Has.None.Contains("Method 'PrivatePure'"));
        Assert.That(messages, Has.None.Contains("PrivateProtectedPure"));
    }

    [Test]
    public async Task Sp0004_ScopeInternal_ReportsInternalMethodsOnly()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int PublicPure() => 1;
    internal int InternalPure() => 2;
    private int PrivatePure() => 3;
}", ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_scope", "internal"));

        var messages = DiagnosticMessages(diagnostics);
        Assert.That(messages, Has.None.Contains("PublicPure"));
        Assert.That(messages, Has.Some.Contains("InternalPure"));
        Assert.That(messages, Has.None.Contains("PrivatePure"));
    }

    [Test]
    public async Task Sp0004_ExcludeTests_SuppressesTestNamedCode()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
namespace Acme.Tests
{
    public class CalculatorTests
    {
        public int Pure() => 1;
    }
}", ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_exclude_tests", "true"));

        Assert.That(DiagnosticMessages(diagnostics), Has.None.Contains("Pure"));
    }

    [Test]
    public async Task Sp0004_ExcludeTests_SuppressesRootLevelTestsDirectory()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
namespace Acme.Production
{
    public class Calculator
    {
        public int Pure() => 1;
    }
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_exclude_tests",
                "true"),
            Path.Combine("tests", "Calculator.cs"));

        Assert.That(DiagnosticMessages(diagnostics), Has.None.Contains("Pure"));
    }

    [Test]
    public async Task PurityProfile_DefaultBalanced_AllowsThisMutableFieldRead()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using SharpProof.Attributes;

public class TestClass
{
    private int _value = 1;

    [EnforcePure]
    public int Read() => _value;
}", ImmutableDictionary<string, string>.Empty);

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
            Has.None.EqualTo(SharpProofDiagnostics.PurityNotVerifiedId));
    }

    [Test]
    public async Task PurityProfile_Strict_DiagnosesThisMutableFieldRead()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using SharpProof.Attributes;

public class TestClass
{
    private int _value = 1;

    [EnforcePure]
    public int Read() => _value;
}", ImmutableDictionary<string, string>.Empty.Add("sharpproof_purity_profile", "strict"));

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
            Does.Contain(SharpProofDiagnostics.PurityNotVerifiedId));
    }

    [Test]
    public async Task EmitExplanations_PerTreeTrue_EmitsExplanationDiagnostic()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using SharpProof.Attributes;
using System;

public class TestClass
{
    [EnforcePure]
    public void Write() => Console.WriteLine(""x"");
}",
            ImmutableDictionary<string, string>.Empty,
            treeOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_emit_explanations", "true"));

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
            Does.Contain(SharpProofDiagnostics.PurityExplanationId));
    }

    [Test]
    public async Task EmitExplanations_PerTreeFalse_OverridesGlobalTrue()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
using SharpProof.Attributes;
using System;

public class TestClass
{
    [EnforcePure]
    public void Write() => Console.WriteLine(""x"");
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_emit_explanations", "true"),
            treeOptions: ImmutableDictionary<string, string>.Empty.Add("sharpproof_emit_explanations", "false"));

        Assert.That(diagnostics.Select(diagnostic => diagnostic.Id),
            Has.None.EqualTo(SharpProofDiagnostics.PurityExplanationId));
    }

    [Test]
    public async Task Sp0004_ExcludeGenerated_SuppressesGeneratedFilePaths()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class GeneratedType
{
    public int Pure() => 1;
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_exclude_generated",
                "true"),
            Path.Combine("obj", "Generated.g.cs"));

        Assert.That(DiagnosticMessages(diagnostics), Has.None.Contains("Pure"));
    }

    [Test]
    public async Task Sp0004_NamespaceFilters_ReportOnlyMatchingNamespaces()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
namespace Allowed.Feature
{
    public class Calculator
    {
        public int AllowedPure() => 1;
    }
}

namespace Other.Feature
{
    public class Calculator
    {
        public int OtherPure() => 2;
    }
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_namespace_filters",
                "Allowed"));

        var messages = DiagnosticMessages(diagnostics);
        Assert.That(messages, Has.Some.Contains("AllowedPure"));
        Assert.That(messages, Has.None.Contains("OtherPure"));
    }

    [Test]
    public async Task Sp0004_PerTreeEmptyNamespaceFilters_ClearsGlobalNamespaceFilters()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
namespace Allowed.Feature
{
    public class Calculator
    {
        public int AllowedPure() => 1;
    }
}

namespace Other.Feature
{
    public class Calculator
    {
        public int OtherPure() => 2;
    }
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_namespace_filters",
                "Allowed"),
            treeOptions: ImmutableDictionary<string, string>.Empty.Add(
                "sharpproof_suggest_missing_enforce_pure_namespace_filters", ""));

        var messages = DiagnosticMessages(diagnostics);
        Assert.That(messages, Has.Some.Contains("AllowedPure"));
        Assert.That(messages, Has.Some.Contains("OtherPure"));
    }

    [Test]
    public async Task Sp0004_MinComplexity_SuppressesTinyMethods()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Tiny() => 1;

    public int Bigger(int x)
    {
        var y = x + 1;
        var z = y * 2;
        return z;
    }
}", ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_min_complexity", "3"));

        var messages = DiagnosticMessages(diagnostics);
        Assert.That(messages, Has.None.Contains("Tiny"));
        Assert.That(messages, Has.Some.Contains("Bigger"));
    }

    [Test]
    public async Task Sp0004_PerTreeMinComplexityZero_ClearsGlobalMinComplexity()
    {
        var diagnostics = await GetAnalyzerDiagnosticsAsync(@"
public class TestClass
{
    public int Tiny() => 1;
}",
            ImmutableDictionary<string, string>.Empty.Add("sharpproof_suggest_missing_enforce_pure_min_complexity",
                "3"),
            treeOptions: ImmutableDictionary<string, string>.Empty.Add(
                "sharpproof_suggest_missing_enforce_pure_min_complexity", "0"));

        Assert.That(DiagnosticMessages(diagnostics), Has.Some.Contains("Tiny"));
    }

    [Test]
    public async Task ConfiguredKnownImpureMethods_AreIsolatedAcrossConcurrentCompilations()
    {
        const int methodCount = 40;
        var methods = string.Join(Environment.NewLine, Enumerable.Range(0, methodCount)
            .Select(index => $"    [EnforcePure] public void Test{index}() => Configured.Danger();"));
        var source = @"
using SharpProof.Attributes;

public static class Configured
{
    public static void Danger() { }
}

public class TestClass
{
" + methods + @"
}";

        var baseOptions = ImmutableDictionary<string, string>.Empty
            .Add("sharpproof_suggest_missing_enforce_pure", "false");
        var configuredOptions = baseOptions
            .Add("sharpproof_known_impure_methods", "Configured.Danger");
        var emptyOptions = baseOptions;

        var tasks = Enumerable.Range(0, 16)
            .Select(index => GetAnalyzerDiagnosticsAsync(
                source,
                index % 2 == 0 ? configuredOptions : emptyOptions,
                concurrentAnalysis: true))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        for (var index = 0; index < results.Length; index++)
        {
            var sp0002Count = results[index]
                .Count(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId);
            if (index % 2 == 0)
                Assert.That(sp0002Count, Is.EqualTo(methodCount),
                    $"Configured compilation {index} should see its impure override.");
            else
                Assert.That(sp0002Count, Is.Zero,
                    $"Unconfigured compilation {index} should not see another compilation's impure override.");
        }
    }

    private static ImmutableArray<string> DiagnosticMessages(ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics
            .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.MissingEnforcePureAttributeId)
            .Select(diagnostic => diagnostic.GetMessage())
            .ToImmutableArray();
    }

    private static async Task<ImmutableArray<Diagnostic>> GetAnalyzerDiagnosticsAsync(
        string source,
        ImmutableDictionary<string, string> globalOptions,
        string? filePath = null,
        ImmutableDictionary<string, string>? treeOptions = null,
        bool concurrentAnalysis = false)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            filePath ?? Path.Combine("src", "ProductionCode.cs"));
        var references = GetTrustedPlatformReferences()
            .Add(MetadataReference.CreateFromFile(typeof(EnforcePureAttribute).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "Sp0004ConfigurationTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var analyzerOptions = new AnalyzerOptions(
            ImmutableArray<AdditionalText>.Empty,
            new TestAnalyzerConfigOptionsProvider(globalOptions,
                treeOptions ?? ImmutableDictionary<string, string>.Empty));

        var compilationWithAnalyzers = compilation.WithAnalyzers(
            ImmutableArray.Create<DiagnosticAnalyzer>(new SharpProofAnalyzer()),
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                null,
                concurrentAnalysis,
                false,
                false));

        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static ImmutableArray<MetadataReference> GetTrustedPlatformReferences()
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

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _emptyOptions =
            new TestAnalyzerConfigOptions(ImmutableDictionary<string, string>.Empty);

        private readonly AnalyzerConfigOptions _treeOptions;

        public TestAnalyzerConfigOptionsProvider(
            ImmutableDictionary<string, string> globalOptions,
            ImmutableDictionary<string, string> treeOptions)
        {
            GlobalOptions = new TestAnalyzerConfigOptions(globalOptions);
            _treeOptions = new TestAnalyzerConfigOptions(treeOptions);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _treeOptions;
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