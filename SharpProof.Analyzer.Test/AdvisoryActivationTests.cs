using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using NUnit.Framework;
using SharpProof.Analyzer.Configuration;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AdvisoryActivationTests
{
    [TestCase("Requires")]
    [TestCase(@"\u0052equires")]
    [TestCase(@"\U00000052equires")]
    public async Task ContractIdentifierSpellingActivatesCallAnalysis(
        string identifier)
    {
        var source = """
            using SharpProof.Attributes;

            internal static class ContractFixture {
                internal static void Positive(int value) {
                    Contract.__REQUIRES__(value > 0);
                }

                internal static void Call() {
                    Positive(-1);
                }
            }
            """.Replace(
                "__REQUIRES__",
                identifier,
                StringComparison.Ordinal);
        var compilation = AnalyzerTestHost.CreateCompilation(
            source,
            ["SP0027"]);
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: null,
            analyzer: new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0027"]));
        }
    }

    [Test]
    public async Task UnicodeEscapeDecoysDoNotActivateCallAnalysis()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            internal static class Decoy {
                private const string Text = @"Contract.\u0052equires(false)";
                // Contract.\U00000052equires(false);
                internal static int Identity(int value) => value;
            }
            """,
            ["SP0027"]);
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: null,
            analyzer: new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task SourceWithoutContractSyntaxDoesNotActivateCallAnalysis()
    {
        var compilation = AnalyzerTestHost.CreateCompilation(
            """
            internal static class Plain {
                internal static int Identity(int value) => value;
            }
            """,
            ["SP0027"]);
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            mode: null,
            analyzer: new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [Test]
    public async Task CompilationReferenceNestedParameterContractActivatesCallAnalysis()
    {
        var external = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            namespace External.Contracts {
                namespace Empty {
                }

                public static class Container {
                    public static class Nested {
                        public static void RequirePositive(
                            [Positive] int value) {
                        }
                    }
                }
            }
            """,
            []);
        var caller = AnalyzerTestHost.CreateCompilation(
            """
            internal static class Caller {
                internal static void Call() {
                    External.Contracts.Container.Nested.RequirePositive(-1);
                }
            }
            """,
            ["SP0027"],
            [external.ToMetadataReference()]);
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            caller,
            mode: null,
            analyzer: new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0027"]));
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Does.Not.Contain("AD0001"));
        }
    }

    [Test]
    public async Task CompilationReferenceRequiresClauseActivatesCallAnalysis()
    {
        var external = AnalyzerTestHost.CreateCompilation(
            """
            using SharpProof.Attributes;

            namespace External.Contracts {
                public static class Guard {
                    public static void RequirePositive(int value) {
                        Contract.Requires(value > 0);
                    }
                }
            }
            """,
            []);
        var caller = AnalyzerTestHost.CreateCompilation(
            """
            internal static class Caller {
                internal static void Call() {
                    External.Contracts.Guard.RequirePositive(-1);
                }
            }
            """,
            ["SP0027"],
            [external.ToMetadataReference()]);
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            caller,
            mode: null,
            analyzer: new SharpProofAnalyzer(factory));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factory.CreateCount, Is.EqualTo(1));
            Assert.That(
                diagnostics.Select(static diagnostic => diagnostic.Id),
                Is.EqualTo(["SP0027"]));
        }
    }

    [Test]
    public async Task CompilationReferenceWithoutClosedContractsKeepsFastPath()
    {
        var external = AnalyzerTestHost.CreateCompilation(
            """
            namespace External {
                namespace Empty {
                }

                public sealed class Service {
                    public void Accept(int value) {
                    }

                    public sealed class Nested {
                        public int Echo(int value) => value;
                    }
                }
            }
            """,
            []);
        var caller = AnalyzerTestHost.CreateCompilation(
            """
            internal static class Caller {
                internal static int Call(External.Service service) {
                    service.Accept(1);
                    return new External.Service.Nested().Echo(2);
                }
            }
            """,
            [],
            [external.ToMetadataReference()]);
        var factory = new RecordingSessionFactory();

        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            caller,
            mode: null,
            analyzer: new SharpProofAnalyzer(factory));

        Assert.That(diagnostics, Is.Empty);
        Assert.That(factory.CreateCount, Is.Zero);
    }

    [TestCase(GeneratedKind.MarkedGenerated, true)]
    [TestCase(GeneratedKind.NotGenerated, false)]
    public void CompilerGeneratedKindOverridesFallbackDetection(
        GeneratedKind generatedKind,
        bool expected)
    {
        var compilation = CreateMethodCompilation(
            "internal sealed class Subject { internal void Run() { } }");
        var tree = compilation.SyntaxTrees.Single();
        compilation = compilation.WithOptions(
            compilation.Options.WithSyntaxTreeOptionsProvider(
                new FixedGeneratedProvider(generatedKind)));
        var method = GetMethod(compilation);

        Assert.That(
            AnalyzerGeneratedCodePolicy.IsGenerated(
                method,
                tree,
                compilation,
                CancellationToken.None),
            Is.EqualTo(expected));
    }

    [TestCase("// <auto-generated />\n", true)]
    [TestCase("// <auto-generated/>\n", true)]
    [TestCase("// <autogenerated />\n", true)]
    [TestCase("// <AUTO-GENERATED />\n", true)]
    [TestCase("/* <auto-generated /> */\n", true)]
    [TestCase("// This handwritten source discusses <auto-generated />.\n", false)]
    [TestCase("// Copyright: never add <autogenerated/> to source.\n", false)]
    [TestCase("// <auto-generated marker is malformed.\n", false)]
    [TestCase("// prefix <auto-generated />\n", false)]
    [TestCase("// <auto-generated /> suffix\n", false)]
    [TestCase("// License header.\n// <auto-generated />\n", false)]
    [TestCase("/// <auto-generated />\n", false)]
    public void GeneratedHeadersRequireAnExactFirstComment(
        string header,
        bool expected)
    {
        var compilation = CreateMethodCompilation(
            header +
            "internal sealed class Subject { internal void Run() { } }");
        var tree = compilation.SyntaxTrees.Single();

        Assert.That(
            AnalyzerGeneratedCodePolicy.IsGenerated(
                GetMethod(compilation),
                tree,
                compilation,
                CancellationToken.None),
            Is.EqualTo(expected));
    }

    [Test]
    public void GeneratedCodeAttributeAndEmptyTreesUseConservativeFallbacks()
    {
        var attributed = CreateMethodCompilation(
            """
            [System.CodeDom.Compiler.GeneratedCode("test", "1")]
            internal sealed class Subject {
                internal void Run() {
                }
            }
            """);
        attributed = attributed.WithOptions(
            attributed.Options.WithSyntaxTreeOptionsProvider(
                new FixedGeneratedProvider(GeneratedKind.Unknown)));
        var attributedTree = attributed.SyntaxTrees.Single();

        var plain = CreateMethodCompilation(
            "internal sealed class Subject { internal void Run() { } }");
        plain = plain.WithOptions(
            plain.Options.WithSyntaxTreeOptionsProvider(
                new FixedGeneratedProvider(GeneratedKind.Unknown)));
        var plainMethod = GetMethod(plain);
        var emptyTree = CSharpSyntaxTree.ParseText(string.Empty);
        var referenceFree = CSharpCompilation.Create(
            "ReferenceFree",
            [emptyTree],
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary)
                .WithSyntaxTreeOptionsProvider(
                    new FixedGeneratedProvider(GeneratedKind.Unknown)));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                AnalyzerGeneratedCodePolicy.IsGenerated(
                    GetMethod(attributed),
                    attributedTree,
                    attributed,
                    CancellationToken.None),
                Is.True);
            Assert.That(
                AnalyzerGeneratedCodePolicy.IsGenerated(
                    plainMethod,
                    plain.SyntaxTrees.Single(),
                    plain,
                    CancellationToken.None),
                Is.False);
            Assert.That(
                AnalyzerGeneratedCodePolicy.IsGenerated(
                    plainMethod,
                    emptyTree,
                    referenceFree,
                    CancellationToken.None),
                Is.False);
        }
    }

    private static CSharpCompilation CreateMethodCompilation(string source)
    {
        return AnalyzerTestHost.CreateCompilation(source, []);
    }

    private static IMethodSymbol GetMethod(CSharpCompilation compilation)
    {
        return compilation.GetTypeByMetadataName("Subject")!
            .GetMembers("Run")
            .OfType<IMethodSymbol>()
            .Single();
    }

    private sealed class RecordingSessionFactory : IAnalyzerSessionFactory
    {
        private int _createCount;

        internal int CreateCount => Volatile.Read(ref _createCount);

        public AnalyzerSession Create(
            Compilation compilation,
            AnalyzerConfiguration configuration,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _createCount);
            return new AnalyzerSession(
                compilation,
                configuration,
                cancellationToken);
        }
    }

    private sealed class FixedGeneratedProvider(GeneratedKind generatedKind)
        : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(
            SyntaxTree tree,
            CancellationToken cancellationToken)
        {
            return generatedKind;
        }

        public override bool TryGetDiagnosticValue(
            SyntaxTree tree,
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }

        public override bool TryGetGlobalDiagnosticValue(
            string diagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }
    }
}
