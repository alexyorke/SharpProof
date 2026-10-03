using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class PrimaryConstructorAnalysisOrderingTests
{
    internal const string Source = """
        using SharpProof.Attributes;
        public static class Guard {
            public static int Positive(int value) {
                Contract.Requires(value > 0);
                return value;
            }
        }
        public class Base { public Base(int value) { } }
        public sealed class Derived(int marker) : Base(
            double.NaN switch { < 0.0 => 0, _ => Guard.Positive(-1) }) { }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task PrimaryConstructorReportsRequiresInEitherCallbackOrder(bool dedicatedFirst)
    {
        var diagnostics = await AnalyzeScheduledAsync(Source, dedicatedFirst);
        AnalyzerTestHost.AssertIds(diagnostics, "SP0027");
    }

    [TestCase("record", false, 1)]
    [TestCase("record", true, 1)]
    [TestCase("struct-member", false, 1)]
    [TestCase("struct-member", true, 1)]
    [TestCase("implicit-default", false, 1)]
    [TestCase("implicit-default", true, 1)]
    [TestCase("member", false, 2)]
    [TestCase("member", true, 2)]
    [TestCase("lambda", false, 1)]
    [TestCase("lambda", true, 1)]
    [TestCase("local", false, 1)]
    [TestCase("local", true, 1)]
    [TestCase("generated", false, 0)]
    [TestCase("generated", true, 0)]
    [TestCase("suppressed", false, 0)]
    [TestCase("suppressed", true, 0)]
    public async Task ConstructorOrderPreservesOtherOwnersAndControls(string kind, bool dedicatedFirst, int expected)
    {
        var source = kind switch
        {
            "record" => Source.Replace("public class Base", "public record Base", StringComparison.Ordinal)
                .Replace("public sealed class Derived", "public sealed record Derived", StringComparison.Ordinal),
            "struct-member" => "using SharpProof.Attributes; public static class Guard { public static int Positive(int value) { Contract.Requires(value > 0); return value; } } public struct Derived(int marker) { private int _value = Guard.Positive(-1); }",
            "implicit-default" => "using SharpProof.Attributes; public class Base { public Base() { Contract.Requires(false); } } public sealed class Derived : Base { }",
            "member" => Source.Replace("Guard.Positive(-1) }) { }", "Guard.Positive(-1) }) { private int _value = Guard.Positive(-2); }", StringComparison.Ordinal),
            "lambda" or "local" => Source.Replace("Base(int value)", "Base(System.Func<int> value)", StringComparison.Ordinal)
                .Replace("double.NaN switch { < 0.0 => 0, _ => Guard.Positive(-1) }", kind == "lambda"
                    ? "() => Guard.Positive(-1)" : "() => { int Local() => Guard.Positive(-1); return Local(); }", StringComparison.Ordinal),
            "suppressed" => Source.Replace("public sealed class Derived", "[method: SharpProofSuppress(\"The constructor is validated externally.\")] public sealed class Derived", StringComparison.Ordinal),
            _ => Source
        };
        var diagnostics = await AnalyzeScheduledAsync(source, dedicatedFirst,
            kind == "generated" ? "Generated.Primary.g.cs" : "input.cs");
        AnalyzerTestHost.AssertIds(diagnostics, "SP0027", expected);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LightweightExternalPreconditionOrderRemainsExact(bool dedicatedFirst)
    {
        var reference = AnalyzerTestHost.EmitReference("using SharpProof.Attributes; public static class Guard { public static int Positive([Positive] int value) => value; }", "ExternalGuard");
        var source = "public class Base { public Base(int value) { } } public sealed class Derived(int marker) : Base(Guard.Positive(-1)) { }";
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"], additionalReferences: [reference]);
        var factory = new OrderedFactory(dedicatedFirst);
        var actual = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", analyzer: new SharpProofAnalyzer(factory));
        AnalyzerTestHost.AssertIds(factory.Diagnostics.Concat(actual), "SP0027");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DeterministicConstructorScheduleMatchesBugGolden(bool dedicatedFirst)
    {
        const string caseName = "primary-constructor-analysis-order";
        var fixture = GoldenTest.Load("analyzer", caseName);
        var diagnostics = await AnalyzeScheduledAsync(fixture.Source, dedicatedFirst, caseName + ".cs");
        GoldenTest.Compare(fixture, GoldenAnalyzerTests.FormatDiagnostics(diagnostics));
    }

    internal static async Task<ImmutableArray<Diagnostic>> AnalyzeScheduledAsync(string source, bool dedicatedFirst, string filePath = "input.cs")
    {
        var compilation = AnalyzerTestHost.CreateCompilation(source, ["SP0027"], filePath: filePath);
        var factory = new OrderedFactory(dedicatedFirst);
        var actual = await AnalyzerTestHost.AnalyzeAsync(compilation, "contracts", analyzer: new SharpProofAnalyzer(factory));
        return [.. factory.Diagnostics.Concat(actual).OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    private sealed class OrderedFactory(bool dedicatedFirst) : IAnalyzerSessionFactory
    {
        internal List<Diagnostic> Diagnostics { get; } = [];

        public AnalyzerSession Create(Compilation compilation, AnalyzerConfiguration configuration, CancellationToken cancellationToken)
        {
            var session = new AnalyzerSession(compilation, configuration, cancellationToken);
            var declaration = compilation.SyntaxTrees.Single().GetRoot(cancellationToken).DescendantNodes()
                .OfType<TypeDeclarationSyntax>().Single(type => type.Identifier.ValueText == "Derived");
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            Assert.That(PrimaryConstructorCallableInventory.TryGet(declaration, model, cancellationToken, out var constructor) ||
                PrimaryConstructorCallableInventory.TryGetSynthesizedDefault(declaration, model, cancellationToken, out constructor), Is.True);
            if (dedicatedFirst)
            {
                _ = AnalyzerFeaturePipeline.AnalyzePrimaryConstructor(constructor, declaration, model,
                    session, Diagnostics.Add, cancellationToken);
            }
            // This is the generic operation-block route's exact tree call.
            // Running it before Roslyn schedules the syntax callback exposes
            // ownership order without sleeps, retries or synthetic timeouts.
            var outcome = RequiresCallSiteTreeAnalyzer.Analyze(constructor, declaration, model,
                session, Diagnostics.Add, cancellationToken);
            TestContext.Out.WriteLine($"dedicatedFirst={dedicatedFirst}; genericOutcome={outcome}; diagnostics={Diagnostics.Count}");
            return session;
        }
    }
}
