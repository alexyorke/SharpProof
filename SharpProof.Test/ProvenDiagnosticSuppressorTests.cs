using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Attributes;

namespace SharpProof.Test;

[TestFixture]
public sealed class ProvenDiagnosticSuppressorTests
{
    private static readonly ImmutableDictionary<string, string> EnabledOptions =
        ImmutableDictionary<string, string>.Empty.Add(
            "sharpproof_suppress_proven_diagnostics",
            "true");

    [Test]
    public void SupportedSuppressions_AreStableAllowlistedAndProofLinked()
    {
        var descriptors = new SharpProofDiagnosticSuppressor().SupportedSuppressions;

        Assert.That(
            descriptors.Select(static descriptor => descriptor.SuppressedDiagnosticId),
            Is.EquivalentTo(new[]
            {
                "CS8509", "CS8524", "CS8602", "CS8605", "CS8629", "CS8670", "CS8846", "S2259",
                "S3655", "V3064", "V3080", "V3095", "V3106", "V3151", "V3152", "V3218"
            }));
        Assert.That(
            descriptors.Select(static descriptor => descriptor.Id),
            Is.EqualTo(Enumerable.Range(1, 16).Select(static index => $"SPS{index:0000}")));
        Assert.That(descriptors.Select(static descriptor => descriptor.Justification.ToString()),
            Is.All.Contains("--runtime-hazards"));
        Assert.That(descriptors.Select(static descriptor => descriptor.Justification.ToString()),
            Is.All.Contains("docs/proven-diagnostic-suppression.md"));
    }

    [Test]
    public async Task OptIn_ExactRequiresProof_SuppressesCompilerNullDereference()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [Requires("value != null")]
                public int Length(string? value) => value.Length;
            }
            """,
            EnabledOptions);

        AssertSuppressed(diagnostics, "CS8602");
    }

    [Test]
    public async Task DefaultOff_LeavesExactCompilerDiagnosticVisible()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [Requires("value != null")]
                public int Length(string? value) => value.Length;
            }
            """,
            ImmutableDictionary<string, string>.Empty);

        AssertVisible(diagnostics, "CS8602");
    }

    [Test]
    public async Task UnknownProof_LeavesCompilerDiagnosticVisible()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            #nullable enable

            public sealed class TestClass
            {
                public int Length(string? value) => value.Length;
            }
            """,
            EnabledOptions);

        AssertVisible(diagnostics, "CS8602");
    }

    [Test]
    public async Task DisabledSmt_LeavesExactCompilerDiagnosticVisible()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [Requires("value != null")]
                public int Length(string? value) => value.Length;
            }
            """,
            EnabledOptions.Add("sharpproof_smt_mode", "disabled"));

        AssertVisible(diagnostics, "CS8602");
    }

    [Test]
    public async Task DiagnosticIdAllowlist_LeavesExcludedDiagnosticVisible()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            #nullable enable
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [Requires("value != null")]
                public int Length(string? value) => value.Length;
            }
            """,
            EnabledOptions.Add("sharpproof_suppression_diagnostic_ids", "CS8629"));

        AssertVisible(diagnostics, "CS8602");
    }

    [Test]
    public async Task ExactRequiresProof_SuppressesExternalDivideAndBoundsDiagnostics()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [Requires("divisor != 0")]
                public int Divide(int value, int divisor) => value / divisor;

                [Requires("index >= 0 && index < values.Length")]
                public int Read(int[] values, int index) => values[index];
            }
            """,
            EnabledOptions,
            new ExternalHazardAnalyzer("V3151", SyntaxKind.DivideExpression),
            new ExternalHazardAnalyzer("V3106", SyntaxKind.ElementAccessExpression));

        AssertSuppressed(diagnostics, "V3151");
        AssertSuppressed(diagnostics, "V3106");
    }

    [Test]
    public async Task DiagnosticOutsideHazardSpan_RemainsVisible()
    {
        var diagnostics = await GetDiagnosticsAsync(
            """
            using SharpProof.Attributes;

            public sealed class TestClass
            {
                [Requires("divisor != 0")]
                public int Divide(int value, int divisor) => value / divisor;
            }
            """,
            EnabledOptions,
            new ExternalHazardAnalyzer(
                "V3151",
                SyntaxKind.DivideExpression,
                static node => node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().Single().Identifier.GetLocation()));

        AssertVisible(diagnostics, "V3151");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(
        string source,
        ImmutableDictionary<string, string> globalOptions,
        params DiagnosticAnalyzer[] externalAnalyzers)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            "input.cs");
        var references = AnalyzerTestHost.GetTrustedPlatformReferences()
            .Add(MetadataReference.CreateFromFile(typeof(RequiresAttribute).Assembly.Location));
        var compilation = CSharpCompilation.Create(
            "ProvenDiagnosticSuppressorTests",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
        var analyzers = externalAnalyzers
            .Append<DiagnosticAnalyzer>(new SharpProofDiagnosticSuppressor())
            .ToImmutableArray();
        var analyzerOptions = AnalyzerTestHost.CreateAnalyzerOptions(globalOptions);
        var compilationWithAnalyzers = compilation.WithAnalyzers(
            analyzers,
            new CompilationWithAnalyzersOptions(
                analyzerOptions,
                null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: true));

        return await compilationWithAnalyzers.GetAllDiagnosticsAsync();
    }

    private static void AssertSuppressed(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        var diagnostic = diagnostics.Single(item => item.Id == diagnosticId);
        Assert.That(diagnostic.IsSuppressed, Is.True, diagnostic.ToString());
    }

    private static void AssertVisible(ImmutableArray<Diagnostic> diagnostics, string diagnosticId)
    {
        var diagnostic = diagnostics.Single(item => item.Id == diagnosticId);
        Assert.That(diagnostic.IsSuppressed, Is.False, diagnostic.ToString());
    }

#pragma warning disable RS1001
    private sealed class ExternalHazardAnalyzer : DiagnosticAnalyzer
    {
        private readonly DiagnosticDescriptor _descriptor;
        private readonly Func<SyntaxNode, Location> _getLocation;
        private readonly SyntaxKind _syntaxKind;

        public ExternalHazardAnalyzer(
            string diagnosticId,
            SyntaxKind syntaxKind,
            Func<SyntaxNode, Location>? getLocation = null)
        {
            _descriptor = new DiagnosticDescriptor(
                diagnosticId,
                "Potential runtime hazard",
                "Potential runtime hazard",
                "External",
                DiagnosticSeverity.Warning,
                isEnabledByDefault: true);
            _syntaxKind = syntaxKind;
            _getLocation = getLocation ?? (static node => node.GetLocation());
        }

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
            ImmutableArray.Create(_descriptor);

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(
                syntaxContext => syntaxContext.ReportDiagnostic(
                    Diagnostic.Create(_descriptor, _getLocation(syntaxContext.Node))),
                _syntaxKind);
        }
    }
#pragma warning restore RS1001
}
