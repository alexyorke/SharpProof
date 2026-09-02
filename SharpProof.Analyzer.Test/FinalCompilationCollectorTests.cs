using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.CompilerCollector;
using SharpProof.Testing;
using SharpProof.Worker.Protocol;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class FinalCompilationCollectorTests
{
    [Test]
    public async Task PrimaryConstructorSameNamedOverloadIsInventoried()
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("primary-constructor-overload");
        var compilation = CreateCompilation(
            """
            using SharpProof.Attributes;
            [method: DoesNotThrow]
            public sealed class Subject(int value) {
                public Subject(string value) : this(0) { }
            }
            """);

        var diagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(path));
        Assert.That(diagnostics, Is.Empty);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(path));

        Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(1));
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
    }

    [TestCase('\uD800')]
    [TestCase('\uDC00')]
    public void TextHashRejectsLoneSurrogatesBeforeEncoding(char value)
    {
        var malformed = SourceText.From(new string(value, 1), Encoding.Unicode);

        var exception = Assert.Throws<InvalidDataException>((Action)(() =>
            _ = CompilerCompilationCapture.ComputeTextSha256(malformed)));
        Assert.That(exception!.Message, Does.Contain("ill-formed UTF-16"));
    }

    [Test]
    public void TextHashDistinguishesValidPairFromReplacementCharacter()
    {
        var pair = CompilerCompilationCapture.ComputeTextSha256(
            SourceText.From("\U0001F600", Encoding.Unicode));
        var replacement = CompilerCompilationCapture.ComputeTextSha256(
            SourceText.From("\uFFFD", Encoding.Unicode));

        Assert.That(pair, Is.Not.EqualTo(replacement));
    }

    [TestCase("Source.cs", '\uD800')]
    [TestCase("Source.cs", '\uDC00')]
    [TestCase("Generated.Subject.g.cs", '\uD800')]
    [TestCase("Generated.Subject.g.cs", '\uDC00')]
    public async Task MalformedSourceOrGeneratedTextProducesTypedDiagnostic(
        string filePath,
        char surrogate)
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("ill-formed-text");
        var compilation = CreateCompilation();
        var malformed = CSharpSyntaxTree.ParseText(
            "// " + new string(surrogate, 1),
            (CSharpParseOptions)compilation.SyntaxTrees.Single().Options,
            filePath,
            Encoding.Unicode);

        var diagnostics = await AnalyzeCollectorAsync(
            compilation.AddSyntaxTrees(malformed),
            Options(path));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(
                diagnostics.Single().GetMessage(CultureInfo.InvariantCulture),
                Does.Contain("ill-formed UTF-16"));
            Assert.That(File.Exists(path), Is.False);
        }
    }

    [Test]
    public async Task StatefulAdditionalTextCannotAuthenticateALaterValue()
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("stateful-additional-text");
        var additional = new StatefulAdditionalText(
            "proof.inputs",
            "generator-value",
            "later-value");

        Assert.That(
            additional.GetText().ToString(),
            Is.EqualTo("generator-value"));
        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation(),
            Options(path),
            [additional]);

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(
                diagnostics.FirstOrDefault()?.GetMessage(
                    CultureInfo.InvariantCulture) ?? string.Empty,
                Does.Contain("stable compiler input"));
            Assert.That(File.Exists(path), Is.False);
        }
    }

    [Test]
    public async Task ValidPairAndReplacementRoundTripWithDistinctFingerprints()
    {
        using var workspace = new CollectorWorkspace();
        var pairPath = workspace.SealPath("pair");
        var replacementPath = workspace.SealPath("replacement");
        var prefix =
            "using SharpProof.Attributes; internal static class Subject { " +
            "internal static string Identity(string value) { " +
            "Contract.Ensures(Contract.Result<string>() == \"";
        const string suffix = "\"); return value; } }";
        var pair = CreateCompilation(prefix + "\\uD83D\\uDE00" + suffix);
        var replacement = CreateCompilation(
            prefix + "\\uFFFD" + suffix);

        var pairArtifact = await EmitArtifact(pair, pairPath);
        var replacementArtifact = await EmitArtifact(
            replacement,
            replacementPath);
        var pairRoundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(pairArtifact));
        var replacementRoundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(replacementArtifact));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                pairArtifact.CompilationSha256,
                Is.Not.EqualTo(replacementArtifact.CompilationSha256));
            Assert.That(
                pairArtifact.Manifest.Hash,
                Is.Not.EqualTo(replacementArtifact.Manifest.Hash));
            Assert.That(
                pairArtifact.Manifest.Claims.Single().ClaimId,
                Is.Not.EqualTo(
                    replacementArtifact.Manifest.Claims.Single().ClaimId));
            Assert.That(
                pairRoundTrip.Compilation.SyntaxTrees[0].Sha256,
                Is.EqualTo(pairArtifact.Compilation.SyntaxTrees[0].Sha256));
            Assert.That(
                replacementRoundTrip.Compilation.SyntaxTrees[0].Sha256,
                Is.EqualTo(
                    replacementArtifact.Compilation.SyntaxTrees[0].Sha256));
            Assert.That(pairRoundTrip.CompilerDiagnostics, Is.Empty);
            Assert.That(replacementRoundTrip.CompilerDiagnostics, Is.Empty);
            Assert.That(pairRoundTrip.Callables.Single().FailureReason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(replacementRoundTrip.Callables.Single().FailureReason,
                Is.EqualTo(WorkerClaimReason.None));
        }
    }

    [TestCase("D800")]
    [TestCase("DC00")]
    public async Task IllFormedCompilerStringTermsBecomeUnsupportedExpression(
        string escape)
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("ill-formed-string-term-" + escape);
        var compilation = CreateCompilation(
            "using SharpProof.Attributes; " +
            "internal static class Subject { " +
            "internal static string Identity(string value) { " +
            "Contract.Ensures(Contract.Result<string>() == \"\\u" + escape +
            "\"); return value; } }");

        var diagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(path));
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(path));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(diagnostics, Is.Empty);
            Assert.That(artifact.CompilerDiagnostics, Is.Empty);
            Assert.That(
                artifact.Callables.Single().FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
        }
    }

    [TestCase("class", "public sealed class Subject(int value) { }")]
    [TestCase("record", "public sealed record Subject(int value);")]
    public async Task PrimaryConstructorSelectionIsInventoriedExactlyOnce(
        string _,
        string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("primary-constructor");
        var compilation = CreateCompilation(
            "using SharpProof.Attributes;\n[method: DoesNotThrow]\n" + declaration);

        var diagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(path));
        Assert.That(diagnostics, Is.Empty);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(path));

        Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(1));
        Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(artifact.Callables, Has.Length.EqualTo(1));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                artifact.Manifest.Callables[0].SelectedFeatures,
                Is.EqualTo([WorkerSelectedFeature.Effects]));
            Assert.That(
                artifact.Manifest.Callables[0].ClaimIds,
                Is.EqualTo([artifact.Manifest.Claims[0].ClaimId]));
            Assert.That(
                artifact.Callables[0].FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        }
    }

    [Test]
    public async Task GeneratedPrimaryConstructorSelectionIsInventoried()
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("generated-primary-constructor");
        var compilation = CreateCompilation();
        var generated = CSharpSyntaxTree.ParseText(
            """
            // <auto-generated />
            using SharpProof.Attributes;
            [method: DoesNotThrow]
            internal sealed class GeneratedSubject(int value) { }
            """,
            (CSharpParseOptions)compilation.SyntaxTrees.Single().Options,
            "Generated.PrimaryConstructor.g.cs");

        var diagnostics = await AnalyzeCollectorAsync(
            compilation.AddSyntaxTrees(generated),
            Options(path));
        Assert.That(diagnostics, Is.Empty);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(path));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
        }
    }

    private const string OutputKey =
        "build_property._SharpProofCompilerManifestPath";
    private const string TargetFrameworkKey =
        "build_property._SharpProofCompilationTargetFramework",
        ProjectDirectoryKey = "build_property._SharpProofProjectDirectory";
    private const string MaximumExpressionDepthKey =
        "build_property.SharpProofVerifyMaximumExpressionDepth";
    private const string SpecificationPacksKey =
        "build_property.SharpProofSpecificationPacks";

    [Test]
    public async Task CollectorIsInactiveWithoutAPathAndForTheOffProfile()
    {
        using var workspace = new CollectorWorkspace();
        var compilation = CreateCompilation();

        var withoutPath = await AnalyzeCollectorAsync(
            compilation,
            Options(path: null));
        var off = await AnalyzeCollectorAsync(
            compilation,
            Options(workspace.SealPath("off"), profile: "off"));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(withoutPath, Is.Empty);
            Assert.That(off, Is.Empty);
            Assert.That(Directory.EnumerateFiles(workspace.Path), Is.Empty);
        }
    }

    [Test]
    public async Task RuntimeEnabledGhostContractsPreventArtifactEmission()
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("runtime-contracts");
        var compilation = CreateCompilation(
            """
            #define SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;

            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() == value);
                    return value;
                }
            }
            """);

        var collectorDiagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(path));
        var diagnostics = await AnalyzerTestHost.AnalyzeAsync(
            compilation,
            Options(path));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(collectorDiagnostics, Is.Empty);
            AnalyzerTestHost.AssertIds(diagnostics, "SP0025");
            Assert.That(File.Exists(path), Is.False);
        }
    }

    [Test]
    public async Task FinalCompilationSealIsCanonicalAndIncludesGeneratedInputs()
    {
        using var workspace = new CollectorWorkspace();
        var compilation = CreateCompilation();
        var generated = CSharpSyntaxTree.ParseText(
            """
            // <auto-generated />
            using SharpProof.Attributes;
            internal static class Generated {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    Contract.Assume(value >= 0);
                    return value;
                }
            }

            """,
            ((CSharpParseOptions)compilation.SyntaxTrees.Single().Options)
                .WithPreprocessorSymbols("GENERATED"),
            "Generated.Contract.g.cs");
        compilation = compilation.AddSyntaxTrees(generated);
        var additional = ImmutableArray.Create<AdditionalText>(
            new MemoryAdditionalText("proof.inputs", "value=1"));
        var path = workspace.SealPath("canonical");

        var firstDiagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(path),
            additional);
        var first = await File.ReadAllBytesAsync(path);
        var secondDiagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(path),
            additional);
        var second = await File.ReadAllBytesAsync(path);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            Encoding.UTF8.GetString(first));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(firstDiagnostics, Is.Empty);
            Assert.That(secondDiagnostics, Is.Empty);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.Take(3), Is.Not.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }));
            Assert.That(first, Does.Not.Contain((byte)'\r'));
            Assert.That(artifact.Schema, Is.EqualTo("SharpProof.CompilerManifest"));
            Assert.That(
                artifact.SchemaVersion,
                Is.EqualTo(CompilerManifestArtifactVersions.Current));
            Assert.That(artifact.ProtocolVersion, Is.EqualTo("11"));
            Assert.That(artifact.Compilation.TargetFramework, Is.EqualTo("net9.0"));
            Assert.That(artifact.Features, Is.EqualTo(WorkerFeatureSet.All));
            Assert.That(
                artifact.MaximumExpressionDepth,
                Is.EqualTo(64));
            Assert.That(artifact.CompilerDiagnostics, Is.Empty);
            Assert.That(artifact.Callables, Has.Length.EqualTo(1));
            Assert.That(artifact.CompilationSha256, Has.Length.EqualTo(64));
            Assert.That(
                artifact.Compilation.SyntaxTrees
                    .Select(static tree => tree.TextLength),
                Is.EqualTo(compilation.SyntaxTrees.Select(
                    static tree => tree.GetText().Length)));
            Assert.That(artifact.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(
                artifact.Manifest.Callables.Single().Assumptions,
                Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task SemanticCompilerInputsInvalidateTheSeal()
    {
        using var workspace = new CollectorWorkspace();
        var baseline = CreateCompilation();
        var baselineHash = await EmitHash(
            baseline,
            workspace.SealPath("baseline"),
            additional: "value=1");
        var sourceHash = await EmitHash(
            CreateCompilation("internal static class Fixture { const int Value = 2; }"),
            workspace.SealPath("source"),
            additional: "value=1");
        var tree = baseline.SyntaxTrees.Single();
        var parseTree = tree.WithRootAndOptions(
            await tree.GetRootAsync(),
            ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols("CHANGED"));
        var parseHash = await EmitHash(
            baseline.ReplaceSyntaxTree(tree, parseTree),
            workspace.SealPath("parse"),
            additional: "value=1");
        var reference = baseline.References
            .OfType<PortableExecutableReference>()
            .First();
        var aliasHash = await EmitHash(
            baseline.ReplaceReference(
                reference,
                reference.WithAliases(["ChangedAlias"])),
            workspace.SealPath("alias"),
            additional: "value=1");
        var additionalHash = await EmitHash(
            baseline,
            workspace.SealPath("additional"),
            additional: "value=2");
        var policyHash = await EmitHash(
            baseline,
            workspace.SealPath("policy"),
            additional: "value=1",
            verifyPolicy: "require-proven");
        var assumptionHash = await EmitHash(
            baseline,
            workspace.SealPath("assumption"),
            additional: "value=1",
            assumptionPolicy: "warn");
        var featuresHash = await EmitHash(
            baseline,
            workspace.SealPath("features"),
            additional: "value=1",
            features: "effects");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                new[] {
                    baselineHash, sourceHash, parseHash, aliasHash,
                    additionalHash
                }
                    .Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(5));
            Assert.That(
                new[] {
                    policyHash, assumptionHash, featuresHash
                },
                Is.All.EqualTo(baselineHash));
        }
    }

    [Test]
    public async Task ExecutableEntryPointSelectionChangesAuthenticatedSnapshot()
    {
        using var workspace = new CollectorWorkspace();
        var compilation = CreateCompilation(
            """
            internal static class FirstEntryPoint {
                public static void Main() { }
            }
            internal static class SecondEntryPoint {
                public static void Main() { }
            }
            """);
        var executable = compilation.Options.WithOutputKind(
            OutputKind.ConsoleApplication);
        var firstHash = await EmitHash(
            compilation.WithOptions(executable.WithMainTypeName(
                "FirstEntryPoint")),
            workspace.SealPath("first-entry-point"),
            additional: "value=1");
        var secondHash = await EmitHash(
            compilation.WithOptions(executable.WithMainTypeName(
                "SecondEntryPoint")),
            workspace.SealPath("second-entry-point"),
            additional: "value=1");

        Assert.That(secondHash, Is.Not.EqualTo(firstHash));
    }

    [Test]
    public async Task DiagnosticPolicyAndRealizedErrorsInvalidateTheSeal()
    {
        using var workspace = new CollectorWorkspace();
        var compilation = CreateCompilation(
            """
            using SharpProof.Attributes;
            internal static class Fixture {
                internal static int Method() {
                    int unused;
                    Contract.Ensures(Contract.Result<int>() == 1);
                    return 1;
                }
            }
            """);
        var baseline = await EmitArtifact(
            compilation, workspace.SealPath("diagnostic-baseline"));
        var warningLevel = await EmitArtifact(
            compilation.WithOptions(compilation.Options.WithWarningLevel(0)),
            workspace.SealPath("diagnostic-warning-level"));
        var generalError = await EmitArtifact(
            compilation.WithOptions(compilation.Options
                .WithGeneralDiagnosticOption(ReportDiagnostic.Error)),
            workspace.SealPath("diagnostic-general"),
            allowCompilationErrors: true);
        var specificError = await EmitArtifact(
            compilation.WithOptions(compilation.Options
                .WithSpecificDiagnosticOptions(
                    compilation.Options.SpecificDiagnosticOptions.SetItem(
                        "CS0168", ReportDiagnostic.Error))),
            workspace.SealPath("diagnostic-specific"),
            allowCompilationErrors: true);
        var providerError = await EmitArtifact(
            compilation.WithOptions(compilation.Options
                .WithSyntaxTreeOptionsProvider(
                    new FixedDiagnosticProvider(
                        "CS0168", ReportDiagnostic.Error))),
            workspace.SealPath("diagnostic-provider"),
            allowCompilationErrors: true);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(baseline.CompilerDiagnostics, Is.Empty);
            Assert.That(warningLevel.CompilerDiagnostics, Is.Empty);
            Assert.That(
                new[] { generalError, specificError, providerError },
                Has.All.Matches<CompilerManifestArtifact>(artifact =>
                    artifact.CompilerDiagnostics.Any(diagnostic =>
                        diagnostic.Code == "compiler.CS0168")));
            Assert.That(
                new[] {
                    baseline.CompilationSha256,
                    warningLevel.CompilationSha256,
                    generalError.CompilationSha256,
                    specificError.CompilationSha256,
                    providerError.CompilationSha256
                }.Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(5));
            Assert.That(
                providerError.Callables.Single().FailureReason,
                Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        }
    }

    [Test]
    public async Task SuppressedWarningAsErrorDoesNotInvalidateTheSeal()
    {
        using var workspace = new CollectorWorkspace();
        var compilation = CreateCompilation(
            """
            #pragma warning disable CS0168
            using SharpProof.Attributes;
            internal static class Fixture {
                [DoesNotThrow]
                internal static int Method() {
                    int unused;
                    return 1;
                }
            }
            #pragma warning restore CS0168
            """).WithOptions(
                CreateCompilation().Options.WithGeneralDiagnosticOption(
                    ReportDiagnostic.Error));

        var artifact = await EmitArtifact(
            compilation,
            workspace.SealPath("suppressed-warning-as-error"));

        Assert.That(artifact.CompilerDiagnostics, Is.Empty);
        Assert.That(
            artifact.Callables.Single().FailureReason,
            Is.Not.EqualTo(CompilerCallableProducerReasonCatalog.DiagnosticFailureReason));
    }

    [TestCase("?", "first.cs")]
    [TestCase("class C {\n    void M() {\n        Missing();\n    }\n}", "ordinary.cs")]
    [TestCase("#line 100 \"mapped.cs\"\nclass C { void M() { Missing(); } }", "physical.cs")]
    [TestCase("class C {", "end.cs")]
    [TestCase("class Generated { void M() { Missing(); } }", "Generated.Subject.g.cs")]
    public async Task CompilerDiagnosticLocationsUseOneBasedMappedCoordinates(
        string source,
        string path)
    {
        using var workspace = new CollectorWorkspace();
        var seed = CreateCompilation();
        var tree = CSharpSyntaxTree.ParseText(
            source,
            (CSharpParseOptions)seed.SyntaxTrees.Single().Options,
            path,
            Encoding.UTF8);
        var compilation = seed.RemoveAllSyntaxTrees().AddSyntaxTrees(tree);
        var expected = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error &&
                diagnostic.Location.IsInSource)
            .Select(static diagnostic =>
            {
                var mapped = diagnostic.Location.GetMappedLineSpan();
                return new
                {
                    Code = "compiler." + diagnostic.Id,
                    Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
                    diagnostic.Location.SourceSpan.Start,
                    diagnostic.Location.SourceSpan.Length,
                    Path = mapped.Path ?? string.Empty,
                    Line = mapped.StartLinePosition.Line + 1,
                    Column = mapped.StartLinePosition.Character + 1
                };
            })
            .ToArray();
        Assert.That(expected, Is.Not.Empty);

        var artifact = await EmitArtifact(
            compilation,
            workspace.SealPath("diagnostic-location"),
            allowCompilationErrors: true);
        var actual = artifact.CompilerDiagnostics.Select(static diagnostic =>
            new
            {
                diagnostic.Code,
                diagnostic.Message,
                diagnostic.Location.Start,
                diagnostic.Location.Length,
                diagnostic.Location.Path,
                diagnostic.Location.Line,
                diagnostic.Location.Column
            }).ToArray();

        Assert.That(actual, Is.EquivalentTo(expected));
    }

    [Test]
    public async Task EmissionFailureIsTypedAndDoesNotEscapeAsAd0001()
    {
        using var workspace = new CollectorWorkspace();

        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation(),
            Options(workspace.Path));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
        Assert.That(
            diagnostics[0].DefaultSeverity,
            Is.EqualTo(DiagnosticSeverity.Error));
    }

    [TestCase("0")]
    [TestCase("257")]
    [TestCase("not-a-number")]
    public async Task InvalidExpressionDepthFailsArtifactEmission(string value)
    {
        using var workspace = new CollectorWorkspace();

        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation(),
            Options(
                workspace.SealPath("invalid-depth"),
                maximumExpressionDepth: value));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
    }

    [TestCase(";dotnet.scalar")]
    [TestCase("dotnet.scalar;")]
    [TestCase(";;dotnet.scalar")]
    [TestCase(" ;dotnet.scalar")]
    public async Task BlankSpecificationPackSegmentFailsArtifactEmission(
        string value)
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("blank-specification-pack");
        var options = Options(path);
        options[SpecificationPacksKey] = value;

        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation(),
            options);

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(File.Exists(path), Is.False);
        }
    }

    [Test]
    public async Task EmptyOrUnsetSpecificationPacksRemainNoPacksDefault()
    {
        using var workspace = new CollectorWorkspace();
        string?[] values = [null, string.Empty, "   "];

        for (var index = 0; index < values.Length; index++)
        {
            var path = workspace.SealPath("no-specification-packs-" + index);
            var options = Options(path);
            if (values[index] != null)
            {
                options[SpecificationPacksKey] = values[index]!;
            }

            var diagnostics = await AnalyzeCollectorAsync(
                CreateCompilation(),
                options);

            Assert.That(diagnostics, Is.Empty);
            var artifact = CompilerManifestArtifactJson.Deserialize(
                await File.ReadAllTextAsync(path));
            using (Assert.EnterMultipleScope())
            {
                Assert.That(artifact.SpecificationPackIds, Is.Empty);
                Assert.That(artifact.Compilation.SpecificationPackIds, Is.Empty);
            }
        }
    }

    [Test]
    public async Task NonFileReferenceFailsWithTypedInfrastructureDiagnostic()
    {
        using var workspace = new CollectorWorkspace();
        var compilation = CreateCompilation();
        var reference = compilation.References
            .OfType<PortableExecutableReference>()
            .First();
        var image = await File.ReadAllBytesAsync(reference.FilePath!);
        var inMemory = MetadataReference.CreateFromImage(image);

        var diagnostics = await AnalyzeCollectorAsync(
            compilation.ReplaceReference(reference, inMemory),
            Options(workspace.SealPath("in-memory")));

        AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
    }

    [Test]
    public async Task ReferencePathMustMatchLoadedMetadata()
    {
        using var workspace = new CollectorWorkspace();
        var imageA = EmitImage(
            "internal static class VersionA { const int Value = 1; }",
            "TwinReference");
        var imageB = EmitImage(
            "internal static class VersionB { const int Value = 2; }",
            "TwinReference");
        var backingPath = Path.Combine(workspace.Path, "TwinReference.dll");
        await File.WriteAllBytesAsync(backingPath, imageB);
        var mismatched = MetadataReference.CreateFromImage(
            imageA,
            filePath: backingPath);
        var artifactPath = workspace.SealPath("reference-mismatch");

        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation().AddReferences(mismatched),
            Options(artifactPath));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(File.Exists(artifactPath), Is.False);
        }
    }

    [Test]
    public async Task ReferencePathMustMatchRawMetadataWhenMvidIsUnchanged()
    {
        using var workspace = new CollectorWorkspace();
        var image = EmitImage(
            "internal static class OriginalMarker { const int Value = 1; }",
            "PatchedReference");
        var patched = PatchAscii(image, "OriginalMarker", "XriginalMarker");
        var backingPath = Path.Combine(workspace.Path, "PatchedReference.dll");
        await File.WriteAllBytesAsync(backingPath, patched);
        var mismatched = MetadataReference.CreateFromImage(
            image,
            filePath: backingPath);
        var artifactPath = workspace.SealPath("patched-reference-mismatch");

        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation().AddReferences(mismatched),
            Options(artifactPath));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(File.Exists(artifactPath), Is.False);
        }
    }

    [Test]
    public async Task LinkedNetmoduleProvenanceCapturesCompleteClosure()
    {
        using var workspace = new CollectorWorkspace();
        var firstPath = Path.Combine(workspace.Path, "A.netmodule");
        var secondPath = Path.Combine(workspace.Path, "B.netmodule");
        var manifestPath = Path.Combine(workspace.Path, "Linked.dll");
        var firstImage = EmitImage(
            "public static class A { public static int Value => 1; }",
            "A",
            OutputKind.NetModule);
        var secondImage = EmitImage(
            "public static class B { public static int Value => 2; }",
            "B",
            OutputKind.NetModule);
        await File.WriteAllBytesAsync(firstPath, firstImage);
        await File.WriteAllBytesAsync(secondPath, secondImage);
        var moduleProperties = new MetadataReferenceProperties(
            MetadataImageKind.Module);
        var firstReference = MetadataReference.CreateFromImage(
            firstImage,
            moduleProperties,
            filePath: firstPath);
        var secondReference = MetadataReference.CreateFromImage(
            secondImage,
            moduleProperties,
            filePath: secondPath);
        var manifest = CreateCompilation(
                "public static class Linked { public static int Value => A.Value + B.Value; }")
            .WithAssemblyName("Linked")
            .AddReferences(
                firstReference,
                secondReference);
        var manifestImage = EmitImage(manifest);
        await File.WriteAllBytesAsync(manifestPath, manifestImage);
        using var assemblyMetadata = AssemblyMetadata.Create(
            ModuleMetadata.CreateFromImage(manifestImage),
            ModuleMetadata.CreateFromImage(firstImage),
            ModuleMetadata.CreateFromImage(secondImage));
        var reference = assemblyMetadata.GetReference(filePath: manifestPath);
        var subject = CreateCompilation().AddReferences(reference);
        Assert.That(subject.GetAssemblyOrModuleSymbol(reference), Is.Not.Null);

        var artifact = await EmitArtifact(
            subject,
            workspace.SealPath("linked-modules"));
        var captured = artifact.Compilation.References.Single(item =>
            item.Modules[0].Path.EndsWith(
                "/Linked.dll",
                StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                captured.Modules.Select(static module => module.Name),
                Is.EqualTo(["Linked.dll", "A.netmodule", "B.netmodule"]));
            Assert.That(
                captured.Modules.Select(static module => module.Path),
                Is.EqualTo(new[] { manifestPath, firstPath, secondPath }
                    .Select(NormalizePath)));
            Assert.That(
                captured.Modules.Select(static module => module.Mvid),
                Is.EqualTo(new[] { manifestPath, firstPath, secondPath }
                    .Select(ReadMvid)));
            Assert.That(
                captured.Modules.Select(static module => module.Sha256),
                Is.EqualTo(new[] { manifestPath, firstPath, secondPath }
                    .Select(ReadSha256)));
            Assert.That(
                captured.Modules.Select(static module => module.SizeBytes),
                Is.EqualTo(new[] { manifestPath, firstPath, secondPath }
                    .Select(static path => new FileInfo(path).Length)));
        }
    }

    [Test]
    public async Task ReferenceCapturePreservesRecursiveAliases()
    {
        using var workspace = new CollectorWorkspace();
        var referencePath = Path.Combine(
            workspace.Path,
            "RecursiveAlias.dll");
        var image = EmitImage(
            "internal static class RecursiveAlias {}",
            "RecursiveAlias");
        await File.WriteAllBytesAsync(referencePath, image);
        var withRecursiveAliases = typeof(MetadataReferenceProperties)
            .GetMethod(
                "WithRecursiveAliases",
                BindingFlags.Instance | BindingFlags.NonPublic) ??
            throw new InvalidOperationException(
                "Recursive reference aliases are unavailable.");
        var properties = (MetadataReferenceProperties)withRecursiveAliases.Invoke(
            new MetadataReferenceProperties(
                MetadataImageKind.Assembly,
                aliases: ["recursive"]),
            [true])!;
        var reference = MetadataReference.CreateFromFile(
            referencePath,
            properties);

        var captured = CompilerCompilationCapture.CaptureReferences(
            [reference],
            CompilerCompilationCapture.ReferenceCaptureLimits.Default,
            CancellationToken.None);

        Assert.That(captured.Single().HasRecursiveAliases, Is.True);
    }

    [Test]
    public async Task ReferenceCaptureEnforcesModuleClosureAndCountLimits()
    {
        using var workspace = new CollectorWorkspace();
        var firstImage = EmitImage(
            "internal static class First {}",
            "FirstLimit");
        var secondImage = EmitImage(
            "internal static class Second {}",
            "SecondLimit");
        var firstPath = Path.Combine(workspace.Path, "FirstLimit.dll");
        var secondPath = Path.Combine(workspace.Path, "SecondLimit.dll");
        await File.WriteAllBytesAsync(firstPath, firstImage);
        await File.WriteAllBytesAsync(secondPath, secondImage);
        MetadataReference[] references =
        [
            MetadataReference.CreateFromImage(
                firstImage,
                filePath: firstPath),
            MetadataReference.CreateFromImage(
                secondImage,
                filePath: secondPath)
        ];
        var maximumModule = Math.Max(
            firstImage.LongLength,
            secondImage.LongLength);
        var closure = firstImage.LongLength + secondImage.LongLength;

        var captured = CompilerCompilationCapture.CaptureReferences(
            references,
            new CompilerCompilationCapture.ReferenceCaptureLimits(
                maximumModule,
                closure,
                references.Length),
            CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                captured.SelectMany(static item => item.Modules)
                    .Select(static module => module.SizeBytes),
                Is.EqualTo(new[]
                {
                    firstImage.LongLength,
                    secondImage.LongLength
                }));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerCompilationCapture.CaptureReferences(
                    references,
                    new CompilerCompilationCapture.ReferenceCaptureLimits(
                        maximumModule - 1,
                        closure,
                        references.Length),
                    CancellationToken.None)));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerCompilationCapture.CaptureReferences(
                    references,
                    new CompilerCompilationCapture.ReferenceCaptureLimits(
                        maximumModule,
                        closure - 1,
                        references.Length),
                    CancellationToken.None)));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerCompilationCapture.CaptureReferences(
                    references,
                    new CompilerCompilationCapture.ReferenceCaptureLimits(
                        maximumModule,
                        closure,
                        references.Length - 1),
                    CancellationToken.None)));
        }
    }

    [Test]
    public async Task StaleLinkedNetmoduleIsRejected()
    {
        using var workspace = new CollectorWorkspace();
        var modulePath = Path.Combine(workspace.Path, "LinkedPart.netmodule");
        var manifestPath = Path.Combine(workspace.Path, "StaleLinked.dll");
        var moduleImage = EmitImage(
            "public static class LinkedPart { public static int Value => 1; }",
            "LinkedPart",
            OutputKind.NetModule);
        await File.WriteAllBytesAsync(modulePath, moduleImage);
        var moduleReference = MetadataReference.CreateFromImage(
            moduleImage,
            new MetadataReferenceProperties(MetadataImageKind.Module),
            filePath: modulePath);
        var manifest = CreateCompilation(
                "public static class StaleLinked { public static int Value => LinkedPart.Value; }")
            .WithAssemblyName("StaleLinked")
            .AddReferences(moduleReference);
        var manifestImage = EmitImage(manifest);
        await File.WriteAllBytesAsync(manifestPath, manifestImage);
        using var assemblyMetadata = AssemblyMetadata.Create(
            ModuleMetadata.CreateFromImage(manifestImage),
            ModuleMetadata.CreateFromImage(moduleImage));
        var reference = assemblyMetadata.GetReference(filePath: manifestPath);
        var subject = CreateCompilation().AddReferences(reference);
        Assert.That(subject.GetAssemblyOrModuleSymbol(reference), Is.Not.Null);
        await File.WriteAllBytesAsync(
            modulePath,
            EmitImage(
                "public static class LinkedPart { public static int Value => 2; }",
                "LinkedPart",
                OutputKind.NetModule));
        var artifactPath = workspace.SealPath("stale-linked-module");

        var diagnostics = await AnalyzeCollectorAsync(
            subject,
            Options(artifactPath));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(File.Exists(artifactPath), Is.False);
        }
    }

    [TestCase("")]
    [TestCase(".")]
    [TestCase("..")]
    [TestCase("nested/part.netmodule")]
    [Platform("Linux")]
    public void LinkedNetmoduleMustBeANonemptySiblingFileName(
        string moduleName)
    {
        using var workspace = new CollectorWorkspace();
        var manifestPath = Path.Combine(workspace.Path, "Linked.dll");

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerCompilationCapture.ResolveSiblingModule(
                manifestPath,
                moduleName)));
    }

    [TestCase("part.netmodule:payload")]
    [TestCase("CON.netmodule")]
    [TestCase("part\\name.netmodule")]
    [Platform("Linux")]
    public void LinkedNetmoduleAllowsOrdinaryLinuxFileNameCharacters(
        string moduleName)
    {
        using var workspace = new CollectorWorkspace();
        var manifestPath = Path.Combine(workspace.Path, "Linked.dll");

        Assert.That(
            CompilerCompilationCapture.ResolveSiblingModule(
                manifestPath,
                moduleName),
            Is.EqualTo(Path.Combine(workspace.Path, moduleName)));
    }

    [Test]
    public async Task TreeLocalConfigurationPreventsArtifactEmission()
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("tree-configuration");
        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation(),
            new TreeOptionsProvider(
                Options(path),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sharpproof_profile"] = "strict"
                }));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(File.Exists(path), Is.False);
        }
    }

    [Test]
    public async Task TreeLocalConfigurationGateDoesNotEmitAnArtifact()
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("tree-configuration-gate");
        _ = await AnalyzeCollectorAsync(
            CreateCompilation(),
            new TreeOptionsProvider(
                Options(path),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["sharpproof_profile"] = "strict"
                }));

        Assert.That(File.Exists(path), Is.False);
    }

    [Test]
    public async Task TreeConfigurationProviderFailureFailsArtifactEmission()
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("tree-provider-failure");
        var diagnostics = await AnalyzeCollectorAsync(
            CreateCompilation(),
            new ThrowingTreeOptionsProvider(Options(path)));

        using (Assert.EnterMultipleScope())
        {
            AnalyzerTestHost.AssertIds(diagnostics, "SP0049");
            Assert.That(File.Exists(path), Is.False);
        }
    }

    [TestCase("advisory", "off", "all", false)]
    [TestCase("off", "advisory", "all", false)]
    [TestCase("invalid", "advisory", "all", false)]
    [TestCase("   ", "off", "all", false)]
    [TestCase(" AdViSoRy ", "strict", "contracts", false)]
    [TestCase("advisory", "strict", "effects", false)]
    [TestCase("advisory", "strict", "invalid", false)]
    [TestCase(" strict ", "strict", "all", true)]
    public async Task CollectorRejectsConflictingConfigurationAliases(
        string rawProfile,
        string buildProfile,
        string features,
        bool shouldEmit)
    {
        using var workspace = new CollectorWorkspace();
        var path = workspace.SealPath("profile-alias-order");
        var options = Options(
            path,
            profile: buildProfile,
            features: features);
        options["sharpproof_profile"] = rawProfile;

        _ = await AnalyzeCollectorAsync(CreateCompilation(), options);

        Assert.That(File.Exists(path), Is.EqualTo(shouldEmit));
    }

    private static async Task<string> EmitHash(
        CSharpCompilation compilation,
        string path,
        string additional,
        string verifyPolicy = "advisory",
        string assumptionPolicy = "allow",
        string features = "all")
    {
        var diagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(
                path,
                features: features,
                verifyPolicy: verifyPolicy,
                assumptionPolicy: assumptionPolicy),
            [new MemoryAdditionalText("proof.inputs", additional)]);
        Assert.That(diagnostics, Is.Empty);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(path));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Compilation.AdditionalFiles, Has.Length.EqualTo(1));
            Assert.That(
                artifact.Compilation.AdditionalFiles[0].Path,
                Does.EndWith("/proof.inputs"));
            Assert.That(
                artifact.Compilation.AdditionalFiles[0].Sha256,
                Has.Length.EqualTo(64));
        }
        return artifact.CompilationSha256;
    }

    private static async Task<CompilerManifestArtifact> EmitArtifact(
        CSharpCompilation compilation,
        string path,
        bool allowCompilationErrors = false)
    {
        var diagnostics = await AnalyzeCollectorAsync(
            compilation,
            Options(path),
            allowCompilationErrors: allowCompilationErrors);
        Assert.That(diagnostics, Is.Empty);
        return CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(path));
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeCollectorAsync(
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, string> values,
        ImmutableArray<AdditionalText> additionalFiles = default,
        bool allowCompilationErrors = false)
    {
        return AnalyzerTestHost.AnalyzeAsync(
            compilation,
            values,
            additionalFiles,
            new FinalCompilationCollectorAnalyzer(),
            allowCompilationErrors);
    }

    private static Task<ImmutableArray<Diagnostic>> AnalyzeCollectorAsync(
        CSharpCompilation compilation,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        return AnalyzerTestHost.AnalyzeAsync(
            compilation,
            optionsProvider,
            new FinalCompilationCollectorAnalyzer());
    }

    private static CSharpCompilation CreateCompilation(
        string source = "internal static class Fixture { const int Value = 1; }")
    {
        return AnalyzerTestHost.CreateCompilation(source, []);
    }

    private static byte[] EmitImage(
        string source,
        string assemblyName,
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary)
    {
        var compilation = CreateCompilation(source);
        return EmitImage(compilation
            .WithAssemblyName(assemblyName)
            .WithOptions(compilation.Options
                .WithOutputKind(outputKind)));
    }

    private static byte[] EmitImage(CSharpCompilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));
        }
        return stream.ToArray();
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace('\\', '/');
    }

    private static string ReadMvid(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new PEReader(stream);
        var metadata = reader.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid)
            .ToString("D");
    }

    private static string ReadSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var hash = SHA256.Create();
        return SharpProof.Ir.HashEncoding.ToLowerHex(hash.ComputeHash(stream));
    }

    private static byte[] PatchAscii(
        byte[] image,
        string original,
        string replacement)
    {
        var originalBytes = Encoding.ASCII.GetBytes(original);
        var replacementBytes = Encoding.ASCII.GetBytes(replacement);
        Assert.That(replacementBytes, Has.Length.EqualTo(originalBytes.Length));
        var offsets = Enumerable.Range(
                0, image.Length - originalBytes.Length + 1)
            .Where(offset => image.AsSpan(offset, originalBytes.Length)
                .SequenceEqual(originalBytes))
            .ToArray();
        Assert.That(offsets, Has.Length.EqualTo(1));
        var patched = (byte[])image.Clone();
        replacementBytes.CopyTo(patched, offsets[0]);
        return patched;
    }

    private static Dictionary<string, string> Options(
        string? path,
        string profile = "advisory",
        string features = "all",
        string verifyPolicy = "advisory",
        string assumptionPolicy = "allow",
        string maximumExpressionDepth = "64")
    {
        var values = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            [TargetFrameworkKey] = "net9.0",
            [ProjectDirectoryKey] = Environment.CurrentDirectory,
            [MaximumExpressionDepthKey] = maximumExpressionDepth,
            ["build_property.SharpProofProfile"] = profile,
            ["build_property.SharpProofFeatures"] = features,
            ["build_property.SharpProofVerifyPolicy"] = verifyPolicy,
            ["build_property.SharpProofAssumptionPolicy"] = assumptionPolicy
        };
        if (path != null)
        {
            values[OutputKey] = path;
        }

        return values;
    }

    private sealed class MemoryAdditionalText(
        string path,
        string content) : AdditionalText, ICompilerAdditionalTextSnapshot
    {
        private readonly SourceText _text = SourceText.From(
            content,
            Encoding.UTF8);

        public override string Path { get; } = path;

        SourceText ICompilerAdditionalTextSnapshot.CapturedText => _text;

        public override SourceText GetText(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _text;
        }
    }

    private sealed class StatefulAdditionalText(
        string path,
        string first,
        string later) : AdditionalText
    {
        private int _readCount;

        public override string Path { get; } = path;

        public override SourceText GetText(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SourceText.From(
                Interlocked.Increment(ref _readCount) == 1 ? first : later,
                Encoding.UTF8);
        }
    }

    private sealed class TreeOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues,
        IReadOnlyDictionary<string, string> treeValues)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global =
            new DictionaryAnalyzerConfigOptions(globalValues);
        private readonly AnalyzerConfigOptions _tree =
            new DictionaryAnalyzerConfigOptions(treeValues);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return _tree;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return _tree;
        }
    }

    private sealed class ThrowingTreeOptionsProvider(
        IReadOnlyDictionary<string, string> globalValues)
        : AnalyzerConfigOptionsProvider
    {
        private readonly AnalyzerConfigOptions _global =
            new DictionaryAnalyzerConfigOptions(globalValues);

        public override AnalyzerConfigOptions GlobalOptions => _global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            throw new InvalidOperationException("tree options unavailable");
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            throw new InvalidOperationException("additional options unavailable");
        }
    }

    private sealed class FixedDiagnosticProvider(
        string diagnosticId,
        ReportDiagnostic reportDiagnostic)
        : SyntaxTreeOptionsProvider
    {
        public override GeneratedKind IsGenerated(
            SyntaxTree tree,
            CancellationToken cancellationToken)
        {
            return GeneratedKind.Unknown;
        }

        public override bool TryGetDiagnosticValue(
            SyntaxTree tree,
            string requestedDiagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = reportDiagnostic;
            return string.Equals(
                requestedDiagnosticId,
                diagnosticId,
                StringComparison.Ordinal);
        }

        public override bool TryGetGlobalDiagnosticValue(
            string requestedDiagnosticId,
            CancellationToken cancellationToken,
            out ReportDiagnostic severity)
        {
            severity = ReportDiagnostic.Default;
            return false;
        }
    }

    private sealed class CollectorWorkspace : IDisposable
    {
        internal CollectorWorkspace()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SharpProof.FinalCompilationCollector",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        internal string Path
        {
            get;
        }
        internal string SealPath(string name)
        {
            return System.IO.Path.Combine(Path, name + ".seal");
        }

        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "SharpProof.FinalCompilationCollector"));
            if (!resolved.StartsWith(
                    root + System.IO.Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Collector workspace escaped its temporary root.");
            }

            Directory.Delete(resolved, recursive: true);
        }
    }
}
