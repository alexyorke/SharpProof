using System.Globalization;
using System.Reflection;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerArchitectureTests
{
    private static readonly string[] ExpectedAnalyzerReferences = [
        "SharpProof.Analyzer.Core",
        "SharpProof.CompilerArtifact",
        "SharpProof.Contracts",
        "SharpProof.Dataflow",
        "SharpProof.Effects",
        "SharpProof.Frontend",
        "SharpProof.Ir",
        "SharpProof.Specs",
        "SharpProof.Worker.Protocol"
    ];

    private static readonly string[] ExpectedAnalyzerOutputAssemblies = [
        "SharpProof.Analyzer",
        "SharpProof.Analyzer.Core",
        "SharpProof.Attributes",
        "SharpProof.CompilerArtifact",
        "SharpProof.CompilerCollector",
        "SharpProof.Contracts",
        "SharpProof.Dataflow",
        "SharpProof.Effects",
        "SharpProof.Frontend",
        "SharpProof.Ir",
        "SharpProof.Specs",
        "SharpProof.Summaries",
        "SharpProof.Worker.Protocol"
    ];

    [Test]
    public void AnalyzerHostRejectsCompilationErrors()
    {
        Func<Task> analyze = async () =>
        {
            await AnalyzerTestHost.AnalyzeAsync(
                "public sealed class Fixture { MissingType Value; }",
                mode: null,
                enabledIds: []);
        };
        var exception = Assert.ThrowsAsync<InvalidOperationException>(analyze);

        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.Message, Does.Contain("CS0246"));
    }

    [Test]
    public async Task AnalyzerHostAllowsExplicitMalformedSourceFixtures()
    {
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            "public sealed class Fixture { MissingType Value; }",
            mode: null,
            enabledIds: [],
            allowCompilationErrors: true);

        Assert.That(diagnostics, Is.Empty);
    }

    [Test]
    public async Task ConcurrentRunsProduceTheSameDiagnostics()
    {
        const string source = """
            using SharpProof.Attributes;

            public static class Fixture {
                private static int state;

                [EnforcePure]
                public static void First() {
                    state = 1;
                }

                [ZeroAllocations]
                public static object Second() => new object();
            }
            """;
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => AnalyzerTestHost.AnalyzeAsync(
                source,
                "effects",
                ["SP0002", "SP0045"]))
            .ToArray();
        var results = await Task.WhenAll(tasks);
        var expected = Snapshot(results[0]);

        Assert.That(
            results.Select(static result => Snapshot(result)),
            Is.All.EqualTo(expected));
    }

    [Test]
    public void AnalyzerAssemblyReferencesOnlyCurrentFrontendLayers()
    {
        var references = typeof(SharpProofAnalyzer).Assembly
            .GetReferencedAssemblies()
            .Select(static name => name.Name)
            .Where(static name => name != null &&
                                  name.StartsWith(
                                      "SharpProof.",
                                      StringComparison.Ordinal))
            .ToArray();

        Assert.That(
            references,
            Is.SubsetOf(ExpectedAnalyzerReferences));
    }

    [Test]
    public void PortableAnalysisAssembliesDoNotReferenceRuntimeAttributes()
    {
        var assemblies = new[] {
            typeof(SharpProofAnalyzer).Assembly,
            typeof(SharpProof.Effects.EffectSummaryProjector).Assembly
        };

        foreach (var assembly in assemblies)
        {
            Assert.That(
                assembly.GetReferencedAssemblies()
                    .Select(static reference => reference.Name),
                Does.Not.Contain("SharpProof.Attributes"),
                assembly.GetName().Name);
        }
    }

    [Test]
    public void AnalyzerOutputContainsOnlyCurrentFrontendLayersAndNoSolver()
    {
        var output = Path.GetDirectoryName(
            typeof(SharpProofAnalyzer).Assembly.Location) ??
            throw new InvalidOperationException("Analyzer output path is unavailable.");
        var names = Directory.EnumerateFiles(output, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToArray();
        var testAssembly =
            typeof(AnalyzerArchitectureTests).Assembly.GetName().Name;
        var sharpProofNames = names
            .Where(static name =>
                name.StartsWith("SharpProof.", StringComparison.Ordinal))
            .Where(name => !string.Equals(
                name,
                testAssembly,
                StringComparison.Ordinal))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                sharpProofNames,
                Is.SubsetOf(ExpectedAnalyzerOutputAssemblies));
            Assert.That(names, Does.Not.Contain("Microsoft.Z3"));
        }
    }

    [Test]
    public void AnalyzerProjectKeepsTheSolverOutOfProcess()
    {
        var root = TestRepository.FindRoot();
        var project = File.ReadAllText(
            Path.Combine(
                root,
                "SharpProof.Analyzer",
                "SharpProof.Analyzer.csproj"));

        Assert.That(project, Does.Not.Contain("Microsoft.Z3"));
        Assert.That(project, Does.Not.Contain("SharpProof.Smt"));
        Assert.That(project, Does.Not.Contain("SharpProof.Verify"));
        Assert.That(
            project,
            Does.Contain(
                "<SharpProofUsesMetaAnalyzer>true</SharpProofUsesMetaAnalyzer>"));
    }

    [Test]
    public void OperationKindGateIsExhaustiveAndFutureKindsFailClosed()
    {
        var runtimeKinds = Enum.GetValues<OperationKind>().Distinct().ToArray();

        Assert.That(
            LanguageSubsetGate.OperationKindDecisions.Keys,
            Is.EquivalentTo(runtimeKinds));
        Assert.That(
            LanguageSubsetGate.OperationKindDecisions.TryGetValue(
                (OperationKind)int.MaxValue,
                out _),
            Is.False);
    }

    [Test]
    public void SubsetAbstentionsUseAClosedTypedReason()
    {
        Assert.That(
            Enum.GetValues<LanguageSubsetAbstentionReason>(),
            Is.EquivalentTo(new[] {
                LanguageSubsetAbstentionReason.None,
                LanguageSubsetAbstentionReason.UnsupportedCallable,
                LanguageSubsetAbstentionReason.MissingOperationRoot,
                LanguageSubsetAbstentionReason.UnsupportedOperationKind,
                LanguageSubsetAbstentionReason.UnsupportedType,
                LanguageSubsetAbstentionReason.UnsupportedOperationShape
            }));
        var abstention = LanguageSubsetDecision.Abstain(
            LanguageSubsetAbstentionReason.UnsupportedOperationKind,
            OperationKind.DynamicInvocation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(abstention.IsSupported, Is.False);
            Assert.That(
                abstention.Reason,
                Is.EqualTo(
                    LanguageSubsetAbstentionReason.UnsupportedOperationKind));
            Assert.That(
                abstention.OperationKind,
                Is.EqualTo(OperationKind.DynamicInvocation));
        }
    }

    [Test]
    public void ReleaseTrackingMatchesCurrentSupportedDescriptors()
    {
        var root = TestRepository.FindRoot();
        var analyzerDirectory = Path.Combine(
            root,
            "SharpProof.Analyzer");
        var shipped = ReadReleaseSection(
            Path.Combine(
                analyzerDirectory,
                "AnalyzerReleases.Shipped.md"),
            "New Rules");
        var unshipped = ReadReleaseSection(
            Path.Combine(
                analyzerDirectory,
                "AnalyzerReleases.Unshipped.md"),
            "New Rules");
        var descriptors = new SharpProofAnalyzer()
            .SupportedDiagnostics
            .Where(static descriptor => !descriptor.Id.StartsWith(
                "SPCF",
                StringComparison.Ordinal))
            .ToDictionary(
                static descriptor => descriptor.Id,
                StringComparer.Ordinal);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                shipped,
                Is.Empty,
                "The 0.2 release line must not inherit pre-0.2 rules.");
            Assert.That(
                unshipped.Keys,
                Is.EquivalentTo(descriptors.Keys));
            Assert.That(unshipped, Has.Count.EqualTo(13));
        }
        foreach (var descriptor in descriptors.Values)
        {
            var rule = unshipped[descriptor.Id];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    rule.Category,
                    Is.EqualTo(descriptor.Category),
                    descriptor.Id);
                Assert.That(
                    rule.Severity,
                    Is.EqualTo(ReleaseSeverity(descriptor)),
                    descriptor.Id);
            }
        }
    }

    private static Dictionary<string, ReleaseRule>
        ReadReleaseSection(
            string path,
            string section)
    {
        var result = new Dictionary<string, ReleaseRule>(
            StringComparer.Ordinal);
        var inSection = false;
        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("### ", StringComparison.Ordinal))
            {
                inSection = string.Equals(
                    line.Substring("### ".Length).Trim(),
                    section,
                    StringComparison.Ordinal);
                continue;
            }
            if (!inSection)
            {
                continue;
            }

            var columns = line
                .Split('|')
                .Select(static column => column.Trim())
                .ToArray();
            if (columns.Length < 4 ||
                columns[0].Length != 6 ||
                !columns[0].StartsWith("SP", StringComparison.Ordinal) ||
                !int.TryParse(columns[0].AsSpan(2), out _))
            {
                continue;
            }

            var rule = new ReleaseRule(
                columns[0],
                columns[1],
                columns[2]);
            Assert.That(
                result.TryAdd(rule.Id, rule),
                Is.True,
                "Duplicate release row " + rule.Id + " in " + path + ".");
        }
        return result;
    }

    private static string ReleaseSeverity(
        DiagnosticDescriptor descriptor)
    {
        return descriptor.IsEnabledByDefault
            ? descriptor.DefaultSeverity.ToString()
            : "Disabled";
    }

    private static string Snapshot(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        return string.Join(
            "\n",
            diagnostics.Select(diagnostic =>
                diagnostic.Id + "|" +
                diagnostic.Location.SourceSpan.Start + "|" +
                diagnostic.GetMessage(CultureInfo.InvariantCulture)));
    }

    private sealed record ReleaseRule(
        string Id,
        string Category,
        string Severity);
}
