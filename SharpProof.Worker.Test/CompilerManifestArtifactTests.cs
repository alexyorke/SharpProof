using System.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
[Parallelizable(ParallelScope.Children)]
public sealed class CompilerManifestArtifactTests
{
    private const string SourceMarker =
        "sharp-proof-source-must-not-be-embedded";

    private const string DoesNotThrowIdentityWithEnsuresSource =
        """
        using SharpProof.Attributes;
        internal static class Subject {
            [DoesNotThrow]
            internal static int Identity(int value) {
                Contract.Ensures(Contract.Result<int>() == value);
                return value;
            }
        }
        """;

    private const string DoesNotThrowIdentitySource =
        """
        using SharpProof.Attributes;
        internal static class Subject {
            [DoesNotThrow]
            internal static int Identity(int value) => value;
        }
        """;

    private const string UnknownAggregateExceptionSource =
        """
        using System;
        using System.Collections.Generic;
        using SharpProof.Attributes;
        internal static class Subject {
            [DoesNotThrow]
            internal static AggregateException Create() =>
                new AggregateException((IEnumerable<Exception>)null!);
        }
        """;

    private const string ZeroAllocationInlineSource =
        """
        using SharpProof.Attributes;
        internal static class Subject {
            [ZeroAllocations]
            internal static object Allocate() => new object();
        }
        """;

    private const string ZeroAllocationSplitSource =
        """
        using SharpProof.Attributes;
        internal static class Subject {
            [ZeroAllocations]
            internal static object Allocate() =>
                new object();
        }
        """;

    private const string NonNegativeIdentitySource =
        """
        using SharpProof.Attributes;
        internal static class Subject {
            internal static int Identity(int value) {
                Contract.Ensures(Contract.Result<int>() == value);
                Contract.Ensures(Contract.Result<int>() >= 0);
                return value;
            }
        }
        """;

    private const string EmptyArraySource =
        """
        using SharpProof.Attributes;
        internal static class Subject {
            internal static int[] Empty() {
                Contract.Ensures(Contract.Result<int[]>() != null);
                return System.Array.Empty<int>();
            }
        }
        """;

    [Test]
    public void CompilerManifestCasesUseBoundedParallelism()
    {
        var fixtureAttribute = typeof(CompilerManifestArtifactTests)
            .GetCustomAttributesData()
            .Single(static attribute =>
                attribute.AttributeType == typeof(ParallelizableAttribute));
        var workerAttribute = typeof(CompilerManifestArtifactTests).Assembly
            .GetCustomAttributesData()
            .Single(static attribute =>
                attribute.AttributeType == typeof(LevelOfParallelismAttribute));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                Convert.ToInt32(
                    fixtureAttribute.ConstructorArguments.Single().Value,
                    CultureInfo.InvariantCulture),
                Is.EqualTo((int)ParallelScope.Children));
            Assert.That(
                Convert.ToInt32(
                    workerAttribute.ConstructorArguments.Single().Value,
                    CultureInfo.InvariantCulture),
                Is.EqualTo(4));
        }
    }

    [Test]
    public void ArtifactRecordsCompilerAndSyntaxEvidenceWithoutSourceText()
    {
        var parse = new CSharpParseOptions(LanguageVersion.CSharp12)
            .WithFeatures([
                new KeyValuePair<string, string>(
                    "sharp-proof-test-feature",
                    "enabled")
            ]);
        var source =
            "internal sealed class Subject { private const string Value = \"" +
            SourceMarker +
            "\"; }\n";
        var artifact = CreateArtifact(parse, source);
        var tree = artifact.Compilation.SyntaxTrees.Single();
        var json = CompilerManifestArtifactJson.Serialize(artifact);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                artifact.Compilation.CompilerVersion,
                Is.EqualTo(
                    typeof(Compilation).Assembly.GetName()
                        .Version!.ToString()));
            Assert.That(
                artifact.Compilation.CSharpCompilerVersion,
                Is.EqualTo(
                    typeof(CSharpCompilation).Assembly.GetName()
                        .Version!.ToString()));
            Assert.That(
                Guid.TryParseExact(
                    artifact.Compilation.CompilerMvid,
                    "D",
                    out _),
                Is.True);
            Assert.That(
                Guid.TryParseExact(
                    artifact.Compilation.CSharpCompilerMvid,
                    "D",
                    out _),
                Is.True);
            Assert.That(
                tree.Sha256,
                Is.EqualTo(WorkerProtocolJson.ComputeSha256(
                    Encoding.UTF8.GetBytes(source))));
            Assert.That(tree.TextLength, Is.EqualTo(source.Length));
            Assert.That(
                tree.Features.Select(static feature =>
                    new KeyValuePair<string, string>(
                        feature.Key,
                        feature.Value)),
                Is.EqualTo(parse.Features));
            Assert.That(
                artifact.Compilation.Options.ResolverPolicy,
                Is.EqualTo(CompilerResolverPolicy.EvidenceOnly));
            Assert.That(json, Does.Not.Contain(SourceMarker));
            Assert.That(json, Does.Not.Contain("\"text\":"));
        }
    }

    [Test]
    public void CompilerFeatureSetCannotBeExpandedPastDiscoveredScope()
    {
        var artifact = CreateFeatureArtifact(
            WorkerFeatureSet.Effects,
            DoesNotThrowIdentityWithEnsuresSource);

        Assert.That(artifact.Features, Is.EqualTo(WorkerFeatureSet.Effects));
        Assert.That(artifact.Manifest.Claims.Select(static claim => claim.Kind),
            Is.All.EqualTo(WorkerClaimKind.Effect));

        artifact.Features = WorkerFeatureSet.All;

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void CompilerFeatureSetCannotBeReducedPastDiscoveredScope()
    {
        var artifact = CreateFeatureArtifact(
            WorkerFeatureSet.All,
            DoesNotThrowIdentityWithEnsuresSource);

        Assert.That(artifact.Manifest.Claims.Select(static claim => claim.Kind),
            Has.Some.EqualTo(WorkerClaimKind.Postcondition));
        Assert.That(artifact.Manifest.Claims.Select(static claim => claim.Kind),
            Has.Some.EqualTo(WorkerClaimKind.Effect));

        artifact.Features = WorkerFeatureSet.Effects;

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void ReSealedFeatureSelectionCannotEscapeTheGlobalProfile()
    {
        var artifact = CreateFeatureArtifact(
            WorkerFeatureSet.Effects,
            DoesNotThrowIdentitySource);
        var callable = artifact.Manifest.Callables.Single();
        callable.SelectedFeatures = [WorkerSelectedFeature.Contracts];
        WorkerProtocolJson.SealManifest(artifact.Manifest);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void CompilerIdentityIsProvenanceRatherThanWorkerGate()
    {
        var artifact = CreateArtifact();
        artifact.Compilation.CompilerMvid =
            Guid.NewGuid().ToString("D");
        artifact.Compilation.CSharpCompilerMvid =
            Guid.NewGuid().ToString("D");
        artifact.CompilationSha256 =
            CompilationFingerprint.ComputeSha256(
                artifact.Compilation, []);

        var roundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));
        var callables =
            CompilerManifestArtifactJson.DecodeCallables(roundTrip);

        Assert.That(callables, Is.Empty);
    }

    [Test]
    public void Sp034MalformedCaptureEvidenceIsRejected()
    {
        Action<CompilerCompilationSnapshot>[] corruptions =
        [
            snapshot => snapshot.CompilerVersion = "not-a-version",
            snapshot => snapshot.CompilerMvid =
                snapshot.CompilerMvid.ToUpperInvariant(),
            snapshot => snapshot.SyntaxTrees[0].LanguageVersion =
                "not-a-csharp-language-version",
            snapshot => snapshot.AssemblyIdentity = "not-an-assembly-identity",
            snapshot => snapshot.References[0].Identity =
                "not-an-assembly-identity",
            snapshot => snapshot.AssemblyName = "DifferentAssembly",
            snapshot => snapshot.Options.MainTypeName = null!
        ];

        foreach (var corrupt in corruptions)
        {
            AssertMalformedCapture(corrupt);
        }
    }

    [Test]
    public void Sp034VersionsRequireExactSystemVersionRoundTrips()
    {
        foreach (var version in new[]
                 { "1.2", "1.2.3", "1.2.3.4" })
        {
            var artifact = CreateArtifact();
            artifact.Compilation.CompilerVersion = version;
            artifact.Compilation.CSharpCompilerVersion = version;
            artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
                artifact.Compilation, []);

            AssertWellFormedCapture(artifact);
        }

        foreach (var version in new[]
                 { "not-a-version", "01.2.3.4", "1.2.3.4.5", "1.2.3.4 " })
        {
            AssertMalformedCapture(snapshot =>
            {
                snapshot.CompilerVersion = version;
                snapshot.CSharpCompilerVersion = version;
            });
        }
    }

    [Test]
    public void Sp034MvidsRequireLowercaseNonNilDFormat()
    {
        const string valid = "01234567-89ab-cdef-0123-456789abcdef";
        var validArtifact = CreateArtifact();
        validArtifact.Compilation.CompilerMvid = valid;
        validArtifact.Compilation.CSharpCompilerMvid = valid;
        foreach (var module in validArtifact.Compilation.References.SelectMany(
                     static reference => reference.Modules))
        {
            module.Mvid = valid;
        }
        validArtifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            validArtifact.Compilation, []);
        AssertWellFormedCapture(validArtifact);

        var invalid = new[]
        {
            valid.ToUpperInvariant(),
            "01234567-89Ab-cdef-0123-456789abcdef",
            "{" + valid + "}",
            "0123456789abcdef0123456789abcdef",
            Guid.Empty.ToString("D")
        };
        foreach (var value in invalid)
        {
            AssertMalformedCapture(snapshot => snapshot.CompilerMvid = value);
            AssertMalformedCapture(snapshot =>
                snapshot.CSharpCompilerMvid = value);
            AssertMalformedCapture(snapshot =>
                snapshot.References[0].Modules[0].Mvid = value);
        }
    }

    [Test]
    public void Sp034LanguageVersionsAreTheCaptureEnumSpellings()
    {
        var valid = new[]
        {
            "Default", "CSharp1", "CSharp2", "CSharp3", "CSharp4",
            "CSharp5", "CSharp6", "CSharp7", "CSharp7_1", "CSharp7_2",
            "CSharp7_3", "CSharp8", "CSharp9", "CSharp10", "CSharp11",
            "CSharp12", "CSharp13", "CSharp14", "LatestMajor", "Preview",
            "Latest"
        };
        foreach (var languageVersion in valid)
        {
            var artifact = CreateArtifact();
            artifact.Compilation.SyntaxTrees[0].LanguageVersion = languageVersion;
            artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
                artifact.Compilation, []);

            AssertWellFormedCapture(artifact, languageVersion);
        }

        foreach (var languageVersion in new[]
                 { "not-a-csharp-language-version", "CSharp7.1", "CSharp15", "" })
        {
            AssertMalformedCapture(snapshot =>
                snapshot.SyntaxTrees[0].LanguageVersion = languageVersion);
        }
    }

    [Test]
    public void Sp034AssemblyIdentitiesRoundTripAndBindTheAssemblyName()
    {
        var valid = CreateArtifact();
        AssertWellFormedCapture(valid);

        var identity = valid.Compilation.AssemblyIdentity;
        foreach (var malformed in new[]
                 {
                     "not-an-assembly-identity",
                     identity.ToUpperInvariant(),
                     identity.Replace(
                         "Version=", "version=", StringComparison.Ordinal),
                     identity + ", " + new string('x', 1024)
                 })
        {
            AssertMalformedCapture(snapshot => snapshot.AssemblyIdentity = malformed);
            AssertMalformedCapture(snapshot =>
                snapshot.References[0].Identity = malformed);
        }

        AssertMalformedCapture(snapshot => snapshot.AssemblyName = "Different");

        var moduleIdentity = CreateArtifact();
        var module = moduleIdentity.Compilation.References[0].Modules[0];
        moduleIdentity.Compilation.References[0].Kind = "Module";
        moduleIdentity.Compilation.References[0].Identity = module.Name;
        moduleIdentity.Compilation.References[0].Modules = [module];
        moduleIdentity.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            moduleIdentity.Compilation, []);
        AssertWellFormedCapture(moduleIdentity);

        AssertMalformedCapture(snapshot =>
        {
            snapshot.References[0].Kind = "Module";
            snapshot.References[0].Identity = "different-module-name";
            snapshot.References[0].Modules = [snapshot.References[0].Modules[0]];
        });
    }

    [Test]
    public void Sp034ReferenceRolesRejectModuleOnlyProperties()
    {
        Action<CompilerReferenceSnapshot>[] corruptions =
        [
            reference =>
            {
                reference.Kind = "Module";
                reference.Identity = reference.Modules[0].Name;
                reference.EmbedInteropTypes = true;
            },
            reference =>
            {
                reference.Kind = "Module";
                reference.Identity = reference.Modules[0].Name;
                reference.Aliases = ["module-alias"];
            }
        ];

        foreach (var corrupt in corruptions)
        {
            AssertMalformedCapture(snapshot => corrupt(snapshot.References[0]));
        }
    }

    [Test]
    public void Sp034SyntaxTreePathsMustBeCaptureCanonical()
    {
        AssertMalformedCapture(snapshot => snapshot.SyntaxTrees[0].Path += "/.");
    }

    [Test]
    public void Sp034EmptySyntaxTreesRetainDerivedCaptureValues()
    {
        AssertMalformedCapture(
            snapshot =>
            {
                var tree = snapshot.SyntaxTrees[0];
                tree.TextLength = 0;
                tree.Sha256 = new string('a', 64);
                tree.EffectivePreprocessorSymbols = ["fabricated"];
            },
            source: string.Empty);
    }

    [Test]
    public void Sp034EmptySyntaxTreesAcceptDuplicateRawPreprocessorSymbols()
    {
        var artifact = CreateArtifact(
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: ["DUPLICATE", "DUPLICATE"]),
            source: string.Empty);

        AssertWellFormedCapture(artifact);
    }

    [Test]
    public void CompilerCallableFailuresUseOnlyProducerReasons()
    {
        var allowed = new HashSet<WorkerClaimReason>
        {
            WorkerClaimReason.UnsupportedCallable,
            WorkerClaimReason.UnsupportedContract,
            WorkerClaimReason.UnsupportedBody,
            WorkerClaimReason.UnsupportedExpression
        };
        var rejected = Enum.GetValues<WorkerClaimReason>()
            .Where(reason => reason is not (
                WorkerClaimReason.Unspecified or
                WorkerClaimReason.None) &&
                !allowed.Contains(reason))
            .ToArray();
        Assert.That(rejected, Does.Contain(WorkerClaimReason.MethodTimeout));
        var artifact = CreateUnsupportedLoopArtifact();

        foreach (var reason in allowed)
        {
            artifact.Callables.Single().FailureReason = reason;
            Assert.That(
                CompilerManifestArtifactJson.DecodeCallables(artifact)
                    .Single().FailureReason,
                Is.EqualTo(reason),
                reason.ToString());
        }

        foreach (var reason in rejected)
        {
            artifact.Callables.Single().FailureReason = reason;
            Assert.Throws<JsonException>(
                (Action)(() =>
                    CompilerManifestArtifactJson.DecodeCallables(artifact)),
                reason.ToString());
        }
    }

    [Test]
    public void FailedCallablesCannotRetainExecutablePayloadAcrossWireBoundary()
    {
        var artifact = CreateContractArtifact();
        artifact.Callables.Single().FailureReason =
            WorkerClaimReason.UnsupportedBody;
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void CompilerDiagnosticCallableStateIsProducerCanonical()
    {
        var artifact = CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return;
                }
            }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.CompilerDiagnostics, Is.Not.Empty);
            Assert.That(artifact.Callables, Is.Not.Empty);
            Assert.That(
                artifact.Callables.Select(static callable =>
                    callable.FailureReason),
                Is.All.EqualTo(WorkerClaimReason.UnsupportedCallable));
        }
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));

        artifact.Callables[0].FailureReason = WorkerClaimReason.UnsupportedBody;
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void RecomputedOuterHashCannotHideMalformedNestedEvidence()
    {
        Action<CompilerCompilationSnapshot>[] corruptions = [
            snapshot => snapshot.Options.ReferencesSupersedeLowerVersions = true,
            snapshot => snapshot.Options.Usings = [string.Empty],
            snapshot => snapshot.ProjectDirectory += "/.",
            snapshot => snapshot.SyntaxTrees[0].Path = null!,
            snapshot => snapshot.SyntaxTrees[0].Features = null!,
            snapshot => snapshot.SyntaxTrees[0].Features = [
                new() { Key = "z", Value = "1" },
                new() { Key = "a", Value = "2" }
            ],
            snapshot => snapshot.SyntaxTrees[0].PreprocessorSymbols = ["z", "a"],
            snapshot => snapshot.SyntaxTrees[0].EffectivePreprocessorSymbols = ["z", "a"],
            snapshot => snapshot.SyntaxTrees[0].Sha256 = "invalid",
            snapshot => snapshot.SyntaxTrees[0].TextLength = -1,
            snapshot => snapshot.References[0].Aliases = null!,
            snapshot => snapshot.References[0].Aliases = ["z", "a"],
            snapshot => snapshot.References[0].Kind = "invalid",
            snapshot => snapshot.References[0].Kind = "Module",
            snapshot => snapshot.References[0].Modules[0].Mvid = "invalid",
            snapshot => snapshot.References[0].Modules[0].SizeBytes = 0,
            snapshot => snapshot.References[0].Modules[0].SizeBytes =
                CompilerReferenceLimits.MaximumModuleBytes + 1L,
            snapshot => snapshot.Options.WarningLevel = -1,
            snapshot => snapshot.Options.SpecificDiagnosticOptions = null!,
            snapshot => snapshot.Options.SpecificDiagnosticOptions = [null!],
            snapshot => snapshot.Options.SpecificDiagnosticOptions = [
                new() { Id = " ", ReportDiagnostic = CompilerReportDiagnostic.Error }
            ],
            snapshot => snapshot.Options.SpecificDiagnosticOptions = [
                new() { Id = "CS0002", ReportDiagnostic = CompilerReportDiagnostic.Error },
                new() { Id = "CS0001", ReportDiagnostic = CompilerReportDiagnostic.Error }
            ],
            snapshot => snapshot.Options.SpecificDiagnosticOptions = [
                new() { Id = "CS0001", ReportDiagnostic = CompilerReportDiagnostic.Error },
                new() { Id = "CS0001", ReportDiagnostic = CompilerReportDiagnostic.Warn }
            ],
            snapshot => snapshot.References[0].Modules = [null!],
            snapshot => snapshot.References[0].Modules[0].Name = " ",
            snapshot => snapshot.References[0].Modules = [
                snapshot.References[0].Modules[0],
                snapshot.References[0].Modules[0]
            ]
        ];

        foreach (var corrupt in corruptions)
        {
            var artifact = CreateArtifact();
            corrupt(artifact.Compilation);
            artifact.CompilationSha256 =
                CompilationFingerprint.ComputeSha256(
                    artifact.Compilation, []);
            var json = JsonSerializer.Serialize(
                    artifact,
                    WorkerProtocolJson.Options) +
                "\n";

            Assert.Throws<JsonException>(
                (Action)(() =>
                    CompilerManifestArtifactJson.Deserialize(json)));
        }
    }

    [Test]
    public void ReferenceClosureResourceLimitsAreValidated()
    {
        var closure = CreateArtifact();
        closure.Compilation.References[0].Modules = CreateModuleRows(
            count: 5,
            sizeBytes: CompilerReferenceLimits.MaximumModuleBytes);
        var count = CreateArtifact();
        count.Compilation.References[0].Modules = CreateModuleRows(
            CompilerReferenceLimits.MaximumModuleCount + 1,
            sizeBytes: 1);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<JsonException>((Action)(() =>
                CompilationFingerprint.ValidateShape(closure.Compilation)));
            Assert.Throws<JsonException>((Action)(() =>
                CompilationFingerprint.ValidateShape(count.Compilation)));
        }
    }

    [Test]
    [Platform("Linux")]
    public void CaseDistinctModulePathsRemainDistinct()
    {
        var artifact = CreateArtifact();
        AddCaseVariantModule(artifact);

        AssertWellFormedCapture(artifact);
    }

    [Test]
    [Platform("Linux")]
    public void CapturePreservesBackslashFilenameCharacters()
    {
        using var temporary = new TempDirectory("SharpProof.BackslashPath-");
        var root = temporary.FullName;
        var projectDirectory = Path.Combine(root, "literal\\backslash");
        Directory.CreateDirectory(projectDirectory);
        var compilation = CreateCompilation(
            new CSharpParseOptions(LanguageVersion.CSharp12),
            "internal sealed class Subject {}\n",
            includeContractReference: false);
        var artifact = CompilerManifestArtifactProducer.Create(
            compilation,
            projectDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);

        Assert.That(
            artifact.Compilation.ProjectDirectory,
            Does.Contain("literal\\backslash"));
    }

    [Test]
    public void NullableSchemaShapesFailWithJsonException()
    {
        var previousSchema = CreateArtifact();
        previousSchema.SchemaVersion =
            CompilerManifestArtifactVersions.Current - 1;
        var modules = CreateArtifact();
        modules.Compilation.References[0].Modules = null!;
        modules.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            modules.Compilation, []);

        CompilerManifestArtifact[] malformedDiagnostics = [
            CreateArtifact(),
            CreateArtifact(),
            CreateArtifact(),
            CreateArtifact(),
            CreateArtifact()
        ];
        malformedDiagnostics[0].CompilerDiagnostics = null!;
        malformedDiagnostics[1].CompilerDiagnostics = [null!];
        malformedDiagnostics[2].CompilerDiagnostics = [Diagnostic(
            "a", length: 1, line: 1, column: 1)];
        malformedDiagnostics[2].CompilerDiagnostics[0].Location = null!;
        malformedDiagnostics[3].Compilation.Options.GeneralDiagnosticOption =
            (CompilerReportDiagnostic)int.MaxValue;
        malformedDiagnostics[4].Compilation.Options.SpecificDiagnosticOptions = [
            new()
            {
                Id = "CS0001",
                ReportDiagnostic = (CompilerReportDiagnostic)int.MaxValue
            }
        ];

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                JsonSerializer.Serialize(
                    previousSchema, WorkerProtocolJson.Options) + "\n")));
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                JsonSerializer.Serialize(modules, WorkerProtocolJson.Options) + "\n")));
        foreach (var artifact in malformedDiagnostics)
        {
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(artifact)));
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Deserialize(
                    JsonSerializer.Serialize(artifact, WorkerProtocolJson.Options) + "\n")));
        }
    }

    [Test]
    public void CompilerDiagnosticLocationsUseTheSharedOneBasedOrNoneShape()
    {
        var valid = CreateArtifact();
        valid.CompilerDiagnostics = [Diagnostic(
            "valid", length: 0, line: 1, column: 1)];
        BindDiagnostics(valid);
        valid.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            valid.Compilation, valid.CompilerDiagnostics);
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(valid)));

        var none = CreateArtifact();
        none.CompilerDiagnostics = [new CompilerDiagnosticArtifact
        {
            Code = "compiler.NONE",
            Message = "non-source",
            Location = new WorkerSourceLocation()
        }];
        none.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            none.Compilation, none.CompilerDiagnostics);
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(none)));

        foreach (var location in new[]
                 {
                     new WorkerSourceLocation
                     {
                         Path = "same.cs", Start = 0, Length = 0,
                         Line = 0, Column = 1
                     },
                     new WorkerSourceLocation
                     {
                         Path = "same.cs", Start = 0, Length = 0,
                         Line = 1, Column = 0
                     },
                     new WorkerSourceLocation
                     {
                         Path = "", Start = 0, Length = 1,
                         Line = 0, Column = 0
                     },
                     new WorkerSourceLocation
                     {
                         Path = "", Start = -1, Length = 0,
                         Line = 0, Column = 0
                     }
                 })
        {
            var malformed = CreateArtifact();
            malformed.CompilerDiagnostics = [new CompilerDiagnosticArtifact
            {
                Code = "compiler.BAD",
                Message = "bad geometry",
                Location = location
            }];
            malformed.CompilationSha256 = CompilationFingerprint.ComputeSha256(
                malformed.Compilation, malformed.CompilerDiagnostics);
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(malformed)));
        }
    }

    [TestCase("worker.infrastructure")]
    [TestCase("SP0001")]
    [TestCase("compiler.")]
    [TestCase("Compiler.CS1001")]
    [TestCase("compiler. CS1001")]
    [TestCase("compiler.CS1001 ")]
    [TestCase("compiler.CS-1001")]
    [TestCase("compiler.CS.1001")]
    [TestCase(" compiler.CS1001")]
    [TestCase("compiler.CS/1001")]
    public void CompilerDiagnosticCodesRequireTheExactReservedNamespace(
        string code)
    {
        var artifact = CreateArtifact();
        artifact.CompilerDiagnostics = [Diagnostic(
            "invalid code", length: 1, line: 1, column: 1)];
        BindDiagnostics(artifact);
        artifact.CompilerDiagnostics[0].Code = code;
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation, artifact.CompilerDiagnostics);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void CompilerDiagnosticCodesRejectControlCharacters()
    {
        var artifact = CreateArtifact();
        artifact.CompilerDiagnostics = [Diagnostic(
            "invalid code", length: 1, line: 1, column: 1)];
        BindDiagnostics(artifact);
        artifact.CompilerDiagnostics[0].Code = "compiler.CS" + (char)1;
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation, artifact.CompilerDiagnostics);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [TestCase("compiler.CS1001")]
    [TestCase("compiler.ERR_Test")]
    [TestCase("compiler.A1_b2")]
    public void CompilerDiagnosticCodesAcceptCanonicalRoslynIdGrammar(
        string code)
    {
        var artifact = CreateArtifact();
        artifact.CompilerDiagnostics = [Diagnostic(
            "canonical code", length: 1, line: 1, column: 1)];
        BindDiagnostics(artifact);
        artifact.CompilerDiagnostics[0].Code = code;
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation, artifact.CompilerDiagnostics);

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact))));
    }

    [Test]
    public void RelationalEvidenceSchemaVersionsAreExactPins()
    {
        Action<CompilerManifestArtifact>[] corruptions =
        [
            artifact => artifact.RelationalSummarySchemaVersion = 0,
            artifact => artifact.RelationalSummarySchemaVersion =
                CompilerRelationalSummaryVersions.Current + 1,
            artifact => artifact.SpecificationPackSchemaVersion = 0,
            artifact => artifact.SpecificationPackSchemaVersion =
                CompilerSpecificationPackVersions.Current + 1
        ];

        foreach (var corrupt in corruptions)
        {
            var artifact = CreateArtifact();
            corrupt(artifact);

            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(artifact)));
        }
    }

    [Test]
    public void SpecificationPackAuthorityIsSealedWhenUnusedAndFingerprintBound()
    {
        var unused = CreateArtifact(specificationPacks: ["dotnet.scalar"]);
        var unset = CreateArtifact();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(unused.SpecificationPackIds,
                Is.EqualTo(["dotnet.scalar"]));
            Assert.That(unused.Compilation.SpecificationPackIds,
                Is.EqualTo(["dotnet.scalar"]));
            Assert.That(unused.SpecificationPackCatalogVersion,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Current));
            Assert.That(unused.SpecificationPackCatalogSha256,
                Is.EqualTo(CompilerSpecificationPackCatalogVersions.Sha256));
            Assert.That(unused.CompilerDiagnostics, Is.Empty);
            Assert.That(unused.CompilationSha256,
                Is.Not.EqualTo(unset.CompilationSha256));
        }

        unused.SpecificationPackIds = [];
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(unused)));

        var tamperedCatalog = CreateArtifact(
            specificationPacks: ["dotnet.scalar"]);
        tamperedCatalog.Compilation.SpecificationPackCatalogSha256 =
            new string('0', 64);
        tamperedCatalog.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            tamperedCatalog.Compilation,
            tamperedCatalog.CompilerDiagnostics);
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(tamperedCatalog)));
    }

    [Test]
    public void SpecificationPackAuthorityFieldsCannotSilentlyDefaultOnWire()
    {
        var json = CompilerManifestArtifactJson.Serialize(CreateArtifact());
        var withoutCatalogVersion = json.Replace(
            "\"specificationPackCatalogVersion\":1,",
            string.Empty,
            StringComparison.Ordinal);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(withoutCatalogVersion)));
    }

    [Test]
    public void CompilerDiagnosticsHaveTotalCanonicalOrderingAndFingerprint()
    {
        var diagnostics = new[]
        {
            Diagnostic("b", length: 1, line: 1, column: 1),
            Diagnostic("a", length: 2, line: 1, column: 1),
            Diagnostic("a", length: 1, line: 2, column: 1),
            Diagnostic("a", length: 1, line: 1, column: 2),
            Diagnostic("a", length: 1, line: 1, column: 1)
        };
        var artifact = CreateArtifact();
        artifact.CompilerDiagnostics = [.. diagnostics.Reverse()];
        BindDiagnostics(artifact);
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation, artifact.CompilerDiagnostics);
        var reversedHash = artifact.CompilationSha256;

        var roundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));
        var canonicalHash = CompilationFingerprint.ComputeSha256(
            artifact.Compilation, diagnostics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(reversedHash, Is.EqualTo(canonicalHash));
            Assert.That(
                roundTrip.CompilerDiagnostics.Select(static diagnostic => (
                    diagnostic.Location.Length,
                    diagnostic.Message,
                    diagnostic.Location.Line,
                    diagnostic.Location.Column)),
                Is.EqualTo(new[]
                {
                    (1, "a", 1, 1),
                    (1, "b", 1, 1),
                    (2, "a", 1, 1),
                    (1, "a", 1, 2),
                    (1, "a", 2, 1)
                }));
        }
    }

    [Test]
    public void UnknownCompilerOptionNameIsRejected()
    {
        var json = CompilerManifestArtifactJson.Serialize(CreateArtifact())
            .Replace(
                "\"outputKind\":\"DynamicallyLinkedLibrary\"",
                "\"outputKind\":\"FutureOutputKind\"",
                StringComparison.Ordinal);

        Assert.Throws<JsonException>(
            (Action)(() => CompilerManifestArtifactJson.Deserialize(json)));
    }

    [Test]
    public void AdditionalFilesRejectNoncanonicalOrdering()
    {
        AssertMalformedAdditionalFiles(
            AdditionalFile("z.input", 'b'),
            AdditionalFile("a.input", 'a'));
    }

    [Test]
    public void AdditionalFilesRejectDuplicateNormalizedPaths()
    {
        AssertMalformedAdditionalFiles(
            AdditionalFile("same.input", 'a'),
            AdditionalFile("same.input", 'b'));
    }

    [Test]
    [Platform("Linux")]
    public void AdditionalFilesPermitCaseDistinctPaths()
    {
        var artifact = CreateArtifact();
        artifact.Compilation.AdditionalFiles = [
            AdditionalFile("CASE.input", 'a'),
            AdditionalFile("case.input", 'b')
        ];
        artifact.CompilationSha256 =
            CompilationFingerprint.ComputeSha256(artifact.Compilation, []);

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact))));
    }

    [Test]
    public void AdditionalFilesRejectNoncanonicalPaths()
    {
        AssertMalformedAdditionalFiles(new CompilerAdditionalFileSnapshot
        {
            Path = Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "nested",
                    "..",
                    "input.txt")
                .Replace('\\', '/'),
            Sha256 = new string('a', 64)
        });
    }

    [Test]
    public void SerializationEnforcesWorkerInputByteLimit()
    {
        var artifact = CreateArtifact();
        artifact.CompilerDiagnostics = [Diagnostic("x", 1, 1, 1)];
        BindDiagnostics(artifact);
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation,
            artifact.CompilerDiagnostics);
        var initial = CompilerManifestArtifactJson.Serialize(artifact);
        var padding = CompilerManifestArtifactFile.MaximumBytes -
            Encoding.UTF8.GetByteCount(initial);
        Assert.That(padding, Is.GreaterThan(0));

        artifact.CompilerDiagnostics[0].Message += new string('x', padding);
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation,
            artifact.CompilerDiagnostics);
        var exact = CompilerManifestArtifactJson.Serialize(artifact);
        Assert.That(
            Encoding.UTF8.GetByteCount(exact),
            Is.EqualTo(CompilerManifestArtifactFile.MaximumBytes));

        artifact.CompilerDiagnostics[0].Message += "x";
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation,
            artifact.CompilerDiagnostics);
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void EffectEvidenceMustMatchIndependentCompilerAuthority()
    {
        var refuted = CreateContractArtifact(ZeroAllocationInlineSource);
        var refutedEvidence = refuted.Callables.Single().EffectClaims.Single();
        Assert.That(refutedEvidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(refutedEvidence.Replay, Is.Not.Null);

        var unknown = CreateContractArtifact(UnknownAggregateExceptionSource);
        var unknownEvidence = unknown.Callables.Single().EffectClaims.Single();
        Assert.That(unknownEvidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(unknownEvidence.Reason, Is.EqualTo(WorkerClaimReason.EffectSummaryIncomplete));

        Action<CompilerEffectClaimArtifact> transition = static evidence =>
        {
            evidence.Outcome = WorkerClaimOutcome.Proven;
            evidence.Reason = WorkerClaimReason.None;
            evidence.Certainty =
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
            evidence.Witness = null;
            evidence.Replay = null;
        };

        var transitionSources = new[] { refuted, unknown };
        foreach (var source in transitionSources)
        {
            var artifact = CloneArtifact(source);
            transition(artifact.Callables.Single().EffectClaims.Single());
            CompilerEffectClaimArtifactCodec.Seal(
                artifact.Callables.Single().EffectClaims.Single());

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void EffectAuthorityBindsConstraintsEvidenceAndSourceTreeOrigin()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                [EffectContract(SharpProofEffect.None, Complete = true)]
                internal static void First() { }

                [EffectContract(SharpProofEffect.Allocates, Complete = true)]
                internal static void Second() { }
            }
            """;
        var honest = CreateContractArtifact(source);
        var honestCallables = honest.Callables
            .SelectMany(static callable => callable.EffectClaims)
            .Where(static evidence =>
                evidence.ContractKind == WorkerEffectContractKind.EffectContract)
            .ToArray();
        Assert.That(honestCallables, Has.Length.EqualTo(2));
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(honest)));

        var changedConstraint = CloneArtifact(honest);
        var changedConstraintEvidence = changedConstraint.Callables
            .SelectMany(static callable => callable.EffectClaims)
            .First(static evidence =>
                evidence.ContractKind == WorkerEffectContractKind.EffectContract);
        changedConstraintEvidence.Constraint.AllowedEffects =
            changedConstraintEvidence.Constraint.AllowedEffects == WorkerEffectSet.Allocates
                ? WorkerEffectSet.ReadsReceiverState
                : WorkerEffectSet.Allocates;
        CompilerEffectClaimArtifactCodec.Seal(changedConstraintEvidence);
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(changedConstraint)));

        var swappedEvidence = CloneArtifact(honest);
        var swapped = swappedEvidence.Callables
            .SelectMany(static callable => callable.EffectClaims)
            .Where(static evidence =>
                evidence.ContractKind == WorkerEffectContractKind.EffectContract)
            .ToArray();
        CopyEffectPayload(swapped[1], swapped[0]);
        CompilerEffectClaimArtifactCodec.Seal(swapped[0]);
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(swappedEvidence)));

        var changedOrigin = CloneArtifact(honest);
        var originEvidence = changedOrigin.Callables
            .SelectMany(static callable => callable.EffectClaims)
            .First(static evidence =>
                evidence.ContractKind == WorkerEffectContractKind.EffectContract);
        var authority = changedOrigin.Callables
            .SelectMany(static callable => callable.EffectAuthorities)
            .Single(item => item.ClaimId == originEvidence.ClaimId);
        authority.SourceTreeSha256 = new string('a', 64);
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(changedOrigin)));
    }

    [Test]
    public void EffectAuthoritiesContributeToFeatureScopeFingerprint()
    {
        var artifact = CreateEffectArtifact();
        var original = artifact.FeatureScopeSha256;
        artifact.Callables.Single().EffectAuthorities.Single()
            .SourceTreeSha256 = new string('0', 64);

        Assert.That(
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact),
            Is.Not.EqualTo(original));
    }

    [Test]
    public void EffectAuthorityMismatchIsRejectedAtWireAndHydrationBoundaries()
    {
        var artifact = CreateEffectArtifact();
        artifact.Callables.Single().EffectAuthorities.Single()
            .SourceTreeSha256 = new string('0', 64);
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void HonestEffectAuthorityPreservesWorkerResultClassification()
    {
        var artifact = CreateContractArtifact(ZeroAllocationInlineSource);
        var target = CompilerManifestArtifactJson.DecodeCallables(artifact).Single();
        var result = EffectClaimResultAssembler.Assemble(
            target, target.EffectClaims.Single());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.EffectWitness, Is.Not.Null);
        }
    }

    private static void CopyEffectPayload(
        CompilerEffectClaimArtifact source,
        CompilerEffectClaimArtifact destination)
    {
        destination.ContractKind = source.ContractKind;
        destination.Outcome = source.Outcome;
        destination.Reason = source.Reason;
        destination.Certainty = source.Certainty;
        destination.Constraint = new CompilerEffectConstraintArtifact
        {
            AllowedEffects = source.Constraint.AllowedEffects,
            AllowedCapabilities = source.Constraint.AllowedCapabilities,
            AllowedExceptionTypes = [.. source.Constraint.AllowedExceptionTypes]
        };
        destination.Witness = source.Witness == null
            ? null
            : new WorkerEffectViolationWitness
            {
                Kind = source.Witness.Kind,
                Detail = source.Witness.Detail,
                Effects = source.Witness.Effects,
                Capabilities = source.Witness.Capabilities,
                ExactExceptionTypeHierarchy =
                    [.. source.Witness.ExactExceptionTypeHierarchy],
                Location = new WorkerSourceLocation
                {
                    Path = source.Witness.Location.Path,
                    Start = source.Witness.Location.Start,
                    Length = source.Witness.Location.Length,
                    Line = source.Witness.Location.Line,
                    Column = source.Witness.Location.Column
                }
            };
        destination.Replay = source.Replay == null
            ? null
            : new CompilerEffectReplayArtifact
            {
                PathKind = source.Replay.PathKind,
                ConstraintSha256 = source.Replay.ConstraintSha256,
                Events = [.. source.Replay.Events.Select(static value =>
                    new CompilerEffectReplayEventArtifact
                    {
                        Ordinal = value.Ordinal,
                        Kind = value.Kind,
                        SyntaxTreeOrdinal = value.SyntaxTreeOrdinal,
                        SyntaxTreeSha256 = value.SyntaxTreeSha256,
                        SyntaxTreeSnapshotSha256 =
                            value.SyntaxTreeSnapshotSha256,
                        SyntaxTreeLineMapSha256 =
                            value.SyntaxTreeLineMapSha256,
                        SyntaxStart = value.SyntaxStart,
                        SyntaxLength = value.SyntaxLength,
                        OperationIdentitySha256 = value.OperationIdentitySha256,
                        MemberIdentity = value.MemberIdentity,
                        MemberDocumentationId = value.MemberDocumentationId,
                        TypeIdentity = value.TypeIdentity,
                        TypeDocumentationId = value.TypeDocumentationId,
                        SpecWitnessIdentifier = value.SpecWitnessIdentifier,
                        ScalarOperands = [.. value.ScalarOperands],
                        ExactExceptionTypeHierarchy =
                            [.. value.ExactExceptionTypeHierarchy],
                        SourceTreeOrdinal = value.SourceTreeOrdinal,
                        SourceTreePath = value.SourceTreePath,
                        SourceTreeSha256 = value.SourceTreeSha256,
                        SourceLineMapSha256 = value.SourceLineMapSha256,
                        Location = new WorkerSourceLocation
                        {
                            Path = value.Location.Path,
                            Start = value.Location.Start,
                            Length = value.Location.Length,
                            Line = value.Location.Line,
                            Column = value.Location.Column
                        }
                    })]
            };
        destination.Evidence = source.Evidence;
    }

    [Test]
    public void MalformedSuccessfulCallableFailsAtWireAndHydrationBoundaries()
    {
        var artifact = CreateContractArtifact();
        var valid =
            CompilerManifestArtifactJson.DecodeCallables(artifact);
        Assert.That(valid, Has.Length.EqualTo(1));
        Assert.That(valid[0].IsSuccess, Is.True);

        artifact.Callables[0].Clauses[0].Root = int.MaxValue;
        artifact.CompilationSha256 =
            CompilationFingerprint.ComputeSha256(
                artifact.Compilation, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(artifact)));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void NonBooleanContractClauseFailsDuringHydration()
    {
        const string callableId = "M:Subject.Verify";
        const string assumptionId = "spa1:non-boolean";
        var factory = new IrFactory();
        var entry = new WorkerCallableManifestEntry
        {
            CallableId = callableId,
            Assumptions =
            [
                new WorkerAssumptionEvidence
                {
                    Id = assumptionId,
                    Kind = WorkerAssumptionKind.Precondition
                }
            ]
        };
        var preparation = new CompilerCallablePreparation(
            factory,
            entry,
            [
                new CompilerPreparedClause(
                    CompilerContractKind.Requires,
                    factory.Integer(1),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    null,
                    assumptionId)
            ],
            [],
            WorkerClaimReason.None,
            null);
        var artifact = CompilerLoweredArtifact.Encode(preparation);
        var manifest = new WorkerClaimManifest
        {
            Callables = [entry]
        };

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerLoweredArtifact.Decode(
                [artifact],
                manifest,
                new CompilerCompilationSnapshot())));
    }

    [Test]
    public void LoweredProgramAboveTheReplayInstructionBoundFailsHydration()
    {
        var artifact = CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    value = value;
                    return value;
                }
            }
            """);
        var block = artifact.Callables[0].Graph!.Blocks.First(static candidate =>
            candidate.Instructions.Any(static instruction =>
                instruction.Kind == IrInstructionKind.Assign));
        var assignment = block.Instructions.First(static instruction =>
            instruction.Kind == IrInstructionKind.Assign);
        block.Instructions = [
            .. Enumerable.Repeat(
                assignment,
                CompilerPreparedBody.MaximumInstructions),
            .. block.Instructions
        ];

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
    }

    [Test]
    public void ReachableCycleFailsCanonicalLoweredBodyHydration()
    {
        var artifact = CreateContractArtifact();
        var graph = artifact.Callables[0].Graph!;
        var block = graph.Blocks[graph.Entry];
        var terminal = block.Instructions[^1];
        Assert.That(terminal.Kind, Is.EqualTo(IrInstructionKind.Return));
        block.Instructions[^1] = new PortableIrInstruction(
            IrInstructionKind.Goto,
            terminal.Operation,
            a: 0);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
    }

    [TestCase(64, false)]
    [TestCase(65, true)]
    public void ReachableBlockLimitIsExactDuringCanonicalHydration(
        int blockCount,
        bool malformed)
    {
        var artifact = CreateContractArtifact();
        ReplaceWithLinearBody(artifact.Callables[0].Graph!, blockCount);

        if (malformed)
        {
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
        else
        {
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(
                    CanonicalRoundTrip(artifact))));
        }
    }

    [Test]
    public void UnreachableCycleDoesNotConsumeTheReachableBodyBudget()
    {
        var artifact = CreateContractArtifact();
        var graph = artifact.Callables[0].Graph!;
        var terminal = graph.Blocks[0].Instructions[^1];
        var unreachable = graph.Blocks.Length;
        graph.Blocks = [
            .. graph.Blocks,
            new PortableIrBlock(
                instructions: [new PortableIrInstruction(
                    IrInstructionKind.Goto,
                    terminal.Operation,
                    a: unreachable)])
        ];

        var resealed = CanonicalRoundTrip(artifact);

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(resealed)));
    }

    [Test]
    public void SuccessfulPostconditionCallableRequiresALoweredBody()
    {
        var artifact = CreateContractArtifact();
        var callable = artifact.Callables[0];
        callable.Body = null;
        callable.Graph!.HasProgram = false;
        callable.Graph.Blocks = [];
        callable.Graph.Entry = -1;

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
    }

    [Test]
    public void SuccessfulCallableWithoutPostconditionsMayRemainBodyless()
    {
        var requiresOnly = CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Requires(value >= 0);
                    return value;
                }
            }
            """);
        var effectOnly = CreateEffectArtifact();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(requiresOnly.Callables[0].Body, Is.Null);
            Assert.That(effectOnly.Callables[0].Body, Is.Null);
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(
                    CanonicalRoundTrip(requiresOnly))));
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(
                    CanonicalRoundTrip(effectOnly))));
        }
    }

    [Test]
    public void ValueReturningBodyRequiresAnExactReturnValue()
    {
        var valid = CreateContractArtifact();
        var missing = CloneArtifact(valid);
        var missingReturn = missing.Callables[0].Graph!.Blocks[0]
            .Instructions[^1];
        Assert.That(missingReturn.Kind, Is.EqualTo(IrInstructionKind.Return));
        missingReturn.A = -1;

        var wrongType = CloneArtifact(valid);
        var wrongGraph = wrongType.Callables[0].Graph!;
        var wrongReturn = wrongGraph.Blocks[0]
            .Instructions[^1];
        wrongReturn.A = wrongGraph.Roots[0];

        var honest = CanonicalRoundTrip(valid);

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(
                    missing)));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(
                    wrongType)));
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(honest)));
        }
    }

    [Test]
    public void EnsuresRowsExactlyMatchManifestClaimIdentityAndEvidence()
    {
        const string source = NonNegativeIdentitySource;
        var valid = CreateContractArtifact(source);
        var rows = valid.Callables[0].Clauses.Where(
            static row => row.Kind == CompilerContractKind.Ensures).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(rows.Select(static row => row.ClaimId),
                Is.EqualTo(valid.Manifest.Claims.Select(static claim => claim.ClaimId)));
            Assert.That(rows.Select(static row => row.Evidence),
                Is.All.EqualTo(CompilerContractEvidence.CompilerBoundInvocation));
        }
        Action<CompilerClauseArtifact[]>[] corruptions = [
            values => values[0].ClaimId = null,
            values => values[1].ClaimId = values[0].ClaimId,
            values => values[0].ClaimId = "spc1:invented",
            values => (values[0].ClaimId, values[1].ClaimId) = (values[1].ClaimId, values[0].ClaimId),
            values => values[0].Evidence = CompilerContractEvidence.Companion
        ];
        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            corrupt([.. artifact.Callables[0].Clauses.Where(
                static row => row.Kind == CompilerContractKind.Ensures)]);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void EffectEvidenceExactlyMatchesManifestAndFailsClosed()
    {
        var valid = CreateEffectArtifact();
        var claim = valid.Manifest.Claims.Single();
        var evidence = valid.Callables.Single().EffectClaims.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(claim.EffectContractKind,
                Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
            Assert.That(evidence.ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(evidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(evidence.Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(CompilerManifestArtifactJson.DecodeCallables(valid).Single()
                .EffectClaims, Has.Length.EqualTo(1));
        }

        Action<CompilerCallableArtifact>[] corruptions = [
            value => value.EffectClaims = [],
            value => value.EffectClaims = [value.EffectClaims[0], value.EffectClaims[0]],
            value => value.EffectClaims[0].ClaimId = "spc1:invented",
            value => value.EffectClaims[0].ContractKind =
                WorkerEffectContractKind.ZeroAllocations,
            value => value.EffectClaims[0].Evidence += ";invented=true",
            value => value.EffectClaims[0].Outcome = WorkerClaimOutcome.Refuted,
            value => value.EffectClaims[0].Certainty =
                WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary
        ];
        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            corrupt(artifact.Callables[0]);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void ResealedEffectVerdictsCannotChangeCompilerOutcome()
    {
        foreach (var source in new[] {
                     ZeroAllocationInlineSource,
                     UnknownAggregateExceptionSource
                 })
        {
            var artifact = CreateContractArtifact(source);
            var evidence = artifact.Callables.Single().EffectClaims.Single();
            Assert.That(evidence.Outcome,
                Is.Not.EqualTo(WorkerClaimOutcome.Proven));

            evidence.Outcome = WorkerClaimOutcome.Proven;
            evidence.Reason = WorkerClaimReason.None;
            evidence.Certainty =
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary;
            evidence.Witness = null;
            evidence.Replay = null;
            CompilerEffectClaimArtifactCodec.Seal(evidence);

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void ResourceLimitedEffectEvidenceHydratesAsTypedUnknown()
    {
        var artifact = CreateEffectArtifact();
        var evidence = artifact.Callables.Single().EffectClaims.Single();
        evidence.Outcome = WorkerClaimOutcome.Unknown;
        evidence.Reason = WorkerClaimReason.ResourceLimit;
        evidence.Certainty =
            WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary;
        evidence.Witness = null;
        evidence.Replay = null;
        CompilerEffectClaimArtifactCodec.Seal(evidence);
        var authority = artifact.Callables.Single().EffectAuthorities
            .Single(item => item.ClaimId == evidence.ClaimId);
        authority.Outcome = evidence.Outcome;
        authority.Reason = evidence.Reason;
        authority.Certainty = evidence.Certainty;
        authority.Witness = null;
        authority.Replay = null;

        var target = CompilerManifestArtifactJson.DecodeCallables(artifact)
            .Single();
        var hydrated = target.EffectClaims.Single();
        var result = EffectClaimResultAssembler.Assemble(target, hydrated);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hydrated.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(hydrated.Reason,
                Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(hydrated.Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary));
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason,
                Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary));
        }
    }

    [Test]
    public void EffectEvidenceRejectsInvalidReasonCertaintyCrossProducts()
    {
        var invalidTuples = new[]
        {
            (WorkerClaimReason.ResourceLimit,
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary),
            (WorkerClaimReason.ResourceLimit,
                WorkerEffectEvidenceCertainty.DefiniteViolation),
            (WorkerClaimReason.UnsupportedBody,
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary)
        };
        var valid = CreateEffectArtifact();

        foreach (var (reason, certainty) in invalidTuples)
        {
            var artifact = CloneArtifact(valid);
            var evidence = artifact.Callables.Single().EffectClaims.Single();
            evidence.Outcome = WorkerClaimOutcome.Unknown;
            evidence.Reason = reason;
            evidence.Certainty = certainty;
            evidence.Witness = null;
            evidence.Replay = null;
            CompilerEffectClaimArtifactCodec.Seal(evidence);

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)),
                $"{reason}/{certainty} must remain invalid.");
        }
    }

    [Test]
    public void UnsupportedDefiniteEffectViolationFailsClosedWithoutReplay()
    {
        var artifact = CreateContractArtifact(
            """
            using System;
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow]
                internal static void Throw() =>
                    throw new InvalidOperationException();
            }
            """);
        var evidence = artifact.Callables.Single().EffectClaims.Single();

        Assert.That(
            evidence.Outcome,
            Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(
            evidence.Reason,
            Is.EqualTo(WorkerClaimReason.None));
        Assert.That(
            evidence.Certainty,
            Is.EqualTo(
                WorkerEffectEvidenceCertainty.DefiniteViolation));
        Assert.That(
            evidence.Witness?.Kind,
            Is.EqualTo("explicit-throw"));
        Assert.That(
            evidence.Witness?.Effects,
            Is.EqualTo(WorkerEffectSet.Throws));
        Assert.That(
            evidence.Witness?.ExactExceptionTypeHierarchy,
            Is.Not.Empty);
        Assert.That(
            evidence.Replay?.Events.Single().Kind,
            Is.EqualTo(CompilerEffectReplayEventKind.ExplicitThrow));
        Assert.That(
            CompilerManifestArtifactJson.DecodeCallables(artifact)
                .Single().EffectClaims.Single().Reason,
            Is.EqualTo(WorkerClaimReason.None));
    }

    [Test]
    public void AllocationEffectReplayRoundTripsCompilerEvidence()
    {
        const string expression = "new object()";
        const string source = ZeroAllocationSplitSource;
        var artifact = CreateContractArtifact(source);
        var json = CompilerManifestArtifactJson.Serialize(artifact);
        var roundTrip =
            CompilerManifestArtifactJson.Deserialize(json);
        var decodedTarget = CompilerManifestArtifactJson
            .DecodeCallables(roundTrip)
            .Single();
        var evidence = decodedTarget.EffectClaims.Single();
        var replay = evidence.Replay;
        var @event = replay?.Events.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                evidence.Reason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                evidence.Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty
                        .DefiniteViolation));
            Assert.That(evidence.Witness, Is.Not.Null);
            Assert.That(
                decodedTarget.Compilation,
                Is.SameAs(roundTrip.Compilation));
            Assert.That(replay, Is.Not.Null);
            Assert.That(
                replay?.PathKind,
                Is.EqualTo(
                    CompilerEffectReplayPathKind.Unconditional));
            Assert.That(replay?.Events, Has.Length.EqualTo(1));
            Assert.That(
                replay?.ConstraintSha256,
                Is.EqualTo(
                    CompilerEffectClaimArtifactCodec
                        .ComputeConstraintSha256(
                            evidence.ContractKind,
                            evidence.Constraint)));
            Assert.That(
                @event?.Kind,
                Is.EqualTo(
                    CompilerEffectReplayEventKind
                        .ManagedObjectAllocation));
            Assert.That(@event?.Ordinal, Is.Zero);
            Assert.That(@event?.SyntaxTreeOrdinal, Is.Zero);
            Assert.That(
                @event?.SyntaxTreeSha256,
                Is.EqualTo(
                    roundTrip.Compilation.SyntaxTrees[0]
                        .Sha256));
            Assert.That(
                @event?.SyntaxStart,
                Is.EqualTo(
                    source.IndexOf(
                        expression,
                        StringComparison.Ordinal)));
            Assert.That(
                @event?.SyntaxLength,
                Is.EqualTo(expression.Length));
            Assert.That(
                @event?.OperationIdentitySha256,
                Is.EqualTo(
                    CompilerEffectClaimArtifactCodec
                        .ComputeReplayOperationSha256(
                            @event!)));
            Assert.That(@event?.MemberIdentity, Is.Not.Empty);
            Assert.That(
                @event?.MemberDocumentationId,
                Is.Not.Null.And.Not.Empty);
            Assert.That(@event?.TypeIdentity, Is.Not.Empty);
            Assert.That(
                @event?.TypeDocumentationId,
                Is.Not.Null.And.Not.Empty);
            Assert.That(@event?.ScalarOperands, Is.Empty);
            Assert.That(
                @event?.ExactExceptionTypeHierarchy,
                Is.Empty);
            Assert.That(
                evidence.Witness?.Detail,
                Is.EqualTo(@event?.MemberDocumentationId));
            Assert.That(json, Does.Not.Contain(expression));
        }
    }

    [Test]
    public void AllocationReplayRejectsResealedInvalidSourceSpans()
    {
        var valid = CreateContractArtifact(ZeroAllocationSplitSource);
        AssertRejected(valid, static (_, _) => 0);
        AssertRejected(valid, static (treeLength, start) =>
            treeLength - start + 1);
        return;

        static void AssertRejected(
            CompilerManifestArtifact valid,
            Func<int, int, int> chooseLength)
        {
            var artifact = CloneArtifact(valid);
            var evidence =
                artifact.Callables.Single().EffectClaims.Single();
            var @event = evidence.Replay!.Events.Single();
            var length = chooseLength(
                artifact.Compilation.SyntaxTrees[0].TextLength,
                @event.SyntaxStart);
            @event.SyntaxLength = length;
            @event.Location.Length = length;
            evidence.Witness!.Location.Length = length;
            CompilerEffectClaimArtifactCodec.Seal(evidence);

            Assert.Throws<JsonException>(
                (Action)(() =>
                    CompilerManifestArtifactJson.Serialize(
                        artifact)));
        }
    }

    [TestCase("line")]
    [TestCase("tree")]
    [TestCase("line-map")]
    public void AllocationReplayRejectsCoordinatedResealedMappedGeometry(
        string mutation)
    {
        var artifact = CreateContractArtifact(ZeroAllocationSplitSource);
        var callable = artifact.Callables.Single();
        var evidence = callable.EffectClaims.Single();
        var authority = callable.EffectAuthorities.Single();
        var @event = evidence.Replay!.Events.Single();
        var authorityEvent = authority.Replay!.Events.Single();

        switch (mutation)
        {
            case "line":
                @event.Location.Line++;
                authorityEvent.Location.Line++;
                evidence.Witness!.Location.Line++;
                break;
            case "tree":
                @event.SourceTreeOrdinal++;
                authorityEvent.SourceTreeOrdinal++;
                break;
            case "line-map":
                @event.SourceLineMapSha256 = new string('0', 64);
                authorityEvent.SourceLineMapSha256 = new string('0', 64);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        CompilerEffectClaimArtifactCodec.Seal(evidence);
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void AllocationReplaySyntaxTreeLineMapIsRejectedAtWireAndHydrationBoundaries()
    {
        var artifact = CreateContractArtifact(ZeroAllocationSplitSource);
        var callable = artifact.Callables.Single();
        var evidence = callable.EffectClaims.Single();
        var authority = callable.EffectAuthorities.Single();
        evidence.Replay!.Events.Single().SyntaxTreeLineMapSha256 =
            new string('0', 64);
        authority.Replay!.Events.Single().SyntaxTreeLineMapSha256 =
            new string('0', 64);
        CompilerEffectClaimArtifactCodec.Seal(evidence);
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void UnmodeledExceptionConstructorCannotFabricateAReplayWitness()
    {
        var artifact = CreateContractArtifact(
            """
            using System;
            using System.Collections.Generic;
            using SharpProof.Attributes;

            internal static class Subject {
                [DoesNotThrow]
                internal static AggregateException Create() =>
                    new AggregateException(
                        (IEnumerable<Exception>)null!);
            }
            """);
        var target = CompilerManifestArtifactJson.DecodeCallables(artifact).Single();
        var evidence = target.EffectClaims.Single();
        var result = EffectClaimResultAssembler.Assemble(target, evidence);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                evidence.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                evidence.Reason,
                Is.EqualTo(WorkerClaimReason.EffectSummaryIncomplete));
            Assert.That(
                evidence.Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary));
            Assert.That(evidence.Evidence, Does.Contain("UnmodeledCall"));
            Assert.That(evidence.Witness, Is.Null);
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(WorkerClaimReason.EffectSummaryIncomplete));
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(result.Model, Is.Empty);
        }
    }

    [Test]
    public void ContractPredicatesAreBoundToCompilerInventory()
    {
        const string source = NonNegativeIdentitySource;
        var swapped = CreateContractArtifact(source);
        var graph = swapped.Callables[0].Graph!;
        var rows = swapped.Callables[0].Clauses;
        (graph.Roots[0], graph.Roots[1]) = (graph.Roots[1], graph.Roots[0]);
        (rows[0].PredicateSha256, rows[1].PredicateSha256) =
            (rows[1].PredicateSha256, rows[0].PredicateSha256);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(swapped)));
    }

    [Test]
    public void AddedPreconditionCannotPassHydration()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """;
        var artifact = CreateContractArtifact(source);
        var callable = artifact.Callables[0];
        var requires = callable.Clauses.Single(
            static row => row.Kind == CompilerContractKind.Requires);
        var originalRoot = callable.Graph!.Roots[requires.Root];
        callable.Graph.Roots = [.. callable.Graph.Roots, originalRoot];
        callable.Clauses = [.. callable.Clauses, new CompilerClauseArtifact {
            Kind = requires.Kind,
            Evidence = requires.Evidence,
            Root = callable.Clauses.Length,
            AssumptionId = requires.AssumptionId,
            PredicateSha256 = requires.PredicateSha256
        }];

        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Manifest.Callables[0].Assumptions.Count(
                static item => item.Kind == WorkerAssumptionKind.Precondition), Is.EqualTo(1));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void ProgramEntryIsCanonicalAndLegacyInstructionOffsetIsRejected()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Choose(bool first, int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    if (first) return value;
                    return value;
                }
            }
        """;
        var artifact = CreateContractArtifact(source);
        var json = CompilerManifestArtifactJson.Serialize(artifact);
        var graph = artifact.Callables[0].Graph!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.Entry, Is.Zero);
            Assert.That(graph.Blocks, Has.Length.GreaterThan(1));
        }
        graph.Entry = 1;
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));

        var withOffset = json.Replace(
            "\"kind\":\"Program\"", "\"kind\":\"Program\",\"startInstruction\":1",
            StringComparison.Ordinal);
        Assert.That(withOffset, Is.Not.EqualTo(json));
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(withOffset)));
    }

    [Test]
    public async Task SpecCallSetAndCompilerCallIdentityFailClosed()
    {
        const string source = EmptyArraySource;
        var valid = CreateContractArtifact(source);
        var body = valid.Callables[0].Body!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(body.Calls.Single().Identity, Is.EqualTo("M:System.Array.Empty``1"));
            Assert.That(body.SpecCalls.Single().WitnessIdentifier, Is.EqualTo("bcl.array.empty"));
        }
        foreach (var descriptors in new Func<CompilerSpecCallArtifact[], CompilerSpecCallArtifact[]>[] {
                     static _ => [],
                     static values => [values[0], values[0]]
                 })
        {
            var artifact = CloneArtifact(valid);
            artifact.Callables[0].Body!.SpecCalls = descriptors(artifact.Callables[0].Body!.SpecCalls);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
        foreach (var identities in new Func<CompilerCallIdentityArtifact[], CompilerCallIdentityArtifact[]>[] {
                     static _ => [], static values => [values[0], values[0]]
                 })
        {
            var artifact = CloneArtifact(valid);
            artifact.Callables[0].Body!.Calls = identities(artifact.Callables[0].Body!.Calls);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }

        var substituted = CloneArtifact(valid);
        substituted.Callables[0].Body!.SpecCalls[0].WitnessIdentifier = "bcl.enumerable.empty";
        var target = CompilerManifestArtifactJson.DecodeCallables(substituted).Single();
        var verifier = new CallableVerifier(new UnexpectedBackend(), WorkerBudgets.DefaultMaximumExpressionDepth);
        var results = await verifier.VerifyAsync(target,
            new MethodResourceBudget(null, WorkerBudgets.DefaultQueryRlimit, WorkerBudgets.DefaultMethodRlimit),
            CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(results.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(results.Single().Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    [Test]
    public void CanonicalVariableEvidenceFailsClosed()
    {
        Action<CompilerCallableArtifact>[] corruptions = [
            value => value.Variables[1].Variable = value.Variables[0].Variable,
            value => value.Variables[0].Ordinal = 1,
            value => value.Variables[0].ModelLabel = "parameter:invented",
            value => (value.Variables[0].Minimum,
                value.Variables[0].Maximum) = (0, 42),
            value => value.Variables[1].Role =
                CompilerVariableRole.Receiver
        ];
        var valid = CreateContractArtifact();
        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            corrupt(artifact.Callables[0]);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void ProgramParameterBindingsFailClosed()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value, bool choose) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return choose ? value : value;
                }
            }
            """;
        Action<CompilerCallableArtifact>[] corruptions = [
            value => value.Body!.ParameterBindings[0].Source = value.Body.ParameterBindings[0].Target,
            value => value.Body!.ParameterBindings[0].Target =
                value.Variables.Single(static item => item.Role == CompilerVariableRole.Result).Variable,
            value => value.Body!.ParameterBindings[1].Source = value.Body.ParameterBindings[0].Source,
            value => (value.Body!.ParameterBindings[0].Target, value.Body.ParameterBindings[1].Target) =
                (value.Body.ParameterBindings[1].Target, value.Body.ParameterBindings[0].Target)
        ];
        var valid = CreateContractArtifact(source);
        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            Assert.That(artifact.Callables[0].Body!.ParameterBindings, Has.Length.EqualTo(2));
            corrupt(artifact.Callables[0]);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void SameTypedParameterBindingSwapFailsClosed()
    {
        const string parameterSource =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Sum(int left, int right) {
                    Contract.Ensures(Contract.Result<int>() == left);
                    return left >= right ? left : right;
                }
            }
            """;
        var valid = CreateContractArtifact(parameterSource);
        var parameterSwap = CloneArtifact(valid);
        var bindings = parameterSwap.Callables[0].Body!.ParameterBindings;
        Assert.That(bindings, Has.Length.EqualTo(2));
        (bindings[0].Target, bindings[1].Target) =
            (bindings[1].Target, bindings[0].Target);
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(parameterSwap)));

        var pairedSwap = CloneArtifact(valid);
        bindings = pairedSwap.Callables[0].Body!.ParameterBindings;
        (bindings[0].Target, bindings[1].Target) =
            (bindings[1].Target, bindings[0].Target);
        (bindings[0].SourceOrdinal, bindings[1].SourceOrdinal) =
            (bindings[1].SourceOrdinal, bindings[0].SourceOrdinal);
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(pairedSwap)));
    }

    [Test]
    public void SameTypedPreStateAssociationSwapFailsClosed()
    {
        const string preStateSource =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Sum(int left, int right) {
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(left));
                    Contract.Ensures(Contract.Old(right) == Contract.Old(right));
                    return left;
                }
            }
            """;
        var valid = CreateContractArtifact(preStateSource);
        var preStateSwap = CloneArtifact(valid);
        var preStates = preStateSwap.Callables[0].Variables
            .Where(static item => item.Role == CompilerVariableRole.PreState)
            .ToArray();
        Assert.That(preStates, Has.Length.EqualTo(2));
        (preStates[0].CurrentStateVariable, preStates[1].CurrentStateVariable) =
            (preStates[1].CurrentStateVariable, preStates[0].CurrentStateVariable);
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(preStateSwap)));

        var pairedSwap = CloneArtifact(valid);
        preStates = pairedSwap.Callables[0].Variables
            .Where(static item => item.Role == CompilerVariableRole.PreState)
            .ToArray();
        (preStates[0].CurrentStateVariable, preStates[1].CurrentStateVariable) =
            (preStates[1].CurrentStateVariable, preStates[0].CurrentStateVariable);
        (preStates[0].SourceOrdinal, preStates[1].SourceOrdinal) =
            (preStates[1].SourceOrdinal, preStates[0].SourceOrdinal);
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(pairedSwap)));
    }

    [Test]
    public void SummaryFreeVariablesAreFreshFromProgramAndCanonicalVariables()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                private static int Identity(int value) => value;

                internal static int Call(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Identity(value);
                }
            }
            """;
        Action<CompilerCallableArtifact>[] corruptions = [
            callable => callable.Body!.SummaryCalls[0].Result =
                callable.Variables.Single(static variable =>
                    variable.Role == CompilerVariableRole.Parameter).Variable,
            callable => callable.Body!.SummaryCalls[0].Result =
                callable.Graph!.Blocks
                    .SelectMany(static block => block.Instructions)
                    .Single(static instruction =>
                        instruction.Kind == IrInstructionKind.Call).A,
            callable => callable.Body!.SummaryCalls[0].ExistentialVariables = [
                callable.Variables.Single(static variable =>
                    variable.Role == CompilerVariableRole.Parameter).Variable
            ]
        ];
        var valid = CreateContractArtifact(source);

        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            Assert.That(
                artifact.Callables[0].Body!.SummaryCalls,
                Has.Length.EqualTo(1));
            corrupt(artifact.Callables[0]);

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void SummaryCallBindsSameTypedArgumentPermutationAndDuplicate()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                private static bool Select(bool first, bool second) => first;

                internal static bool Call(bool left, bool right) {
                    Contract.Ensures(Contract.Result<bool>() == left);
                    return Select(left, right);
                }
            }
            """;
        Action<CompilerCallableArtifact>[] corruptions = [
            callable => {
                var call = FindCall(callable);
                (call.Items[0], call.Items[1]) = (call.Items[1], call.Items[0]);
            },
            callable => {
                var call = FindCall(callable);
                call.Items[1] = call.Items[0];
            }
        ];
        var valid = CreateContractArtifact(source);

        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            corrupt(artifact.Callables[0]);

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void SummaryCallBindsDifferentTypedArgumentPermutation()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                private static int Select(int first, bool second) => first;

                internal static int Call(int value, bool flag) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Select(value, flag);
                }
            }
            """;
        var artifact = CreateContractArtifact(source);
        var call = FindCall(artifact.Callables[0]);
        (call.Items[0], call.Items[1]) = (call.Items[1], call.Items[0]);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
    }

    [Test]
    public void SummaryCallBindsReceiverInstantiation()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal sealed class Box { }
            internal static class Subject {
                private static bool Select(bool value) => value;

                internal static bool Call(Box box, bool value) {
                    Contract.Requires(box != null);
                    Contract.Ensures(Contract.Result<bool>() == value);
                    return Select(value);
                }
            }
            """;
        var artifact = CreateContractArtifact(source);
        var callable = artifact.Callables.Single(static value =>
            value.Body?.SummaryCalls.Length == 1);
        var call = FindCall(callable);
        var boxIndex = Array.FindIndex(callable.Graph!.Variables, static value =>
            value.Name == "parameter:0");
        Assert.That(boxIndex, Is.GreaterThanOrEqualTo(0));
        call.C = Array.FindIndex(callable.Graph.Terms, value =>
            value.Kind == IrTermKind.Variable && value.A == boxIndex);
        Assert.That(call.C, Is.GreaterThanOrEqualTo(0));

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
    }

    [Test]
    public void SummaryCallBindsExistentialRolesAndRequiresDigest()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                private static int Identity(int value) => value;
                private static int Compose(int value) => Identity(Identity(value));

                internal static int Call(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return Compose(value);
                }
            }
            """;
        Action<CompilerCallableArtifact>[] corruptions = [
            callable => {
                var summary = callable.Body!.SummaryCalls.Single();
                Assert.That(summary.ExistentialVariables, Has.Length.EqualTo(2));
                (summary.ExistentialVariables[0], summary.ExistentialVariables[1]) =
                    (summary.ExistentialVariables[1], summary.ExistentialVariables[0]);
            },
            callable => callable.Body!.SummaryCalls.Single().InstantiationSha256 =
                string.Empty
        ];
        var valid = CreateContractArtifact(source);

        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            corrupt(artifact.Callables.Single());

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
    }

    [Test]
    public void SameShapedMemberSubstitutionFailsClosed()
    {
        const string source = EmptyArraySource;
        var artifact = CreateContractArtifact(source);
        var graph = artifact.Callables[0].Graph!;
        var call = graph.Blocks.SelectMany(static block => block.Instructions)
            .Single(static instruction => instruction.Kind == IrInstructionKind.Call);
        var original = graph.Members[call.B];
        graph.Identities = [.. graph.Identities, graph.Identities.Length];
        graph.Members[call.B] = new PortableIrMember
        {
            Identity = graph.Identities.Length - 1,
            DeclaringType = original.DeclaringType,
            Name = original.Name,
            ReturnType = original.ReturnType,
            IsStatic = original.IsStatic,
            ParameterTypes = [.. original.ParameterTypes],
            DocumentationCommentId = "M:System.Linq.Enumerable.Empty``1"
        };

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
    }

    [Test]
    public void ResolverDependentDirectivesFailClosed()
    {
        var parse = new CSharpParseOptions(
            LanguageVersion.CSharp12,
            kind: SourceCodeKind.Script);
        var compilation = CreateCompilation(
            parse,
            "#r \"dependency.dll\"\nclass Subject {}\n",
            includeContractReference: false);
        var discovery = new ClaimManifestBuilder(compilation).Build();

        Assert.Throws<InvalidOperationException>(
            (Action)(() => CompilerManifestArtifactProducer.Create(
                compilation,
                TestContext.CurrentContext.WorkDirectory,
                "net8.0",
                WorkerFeatureSet.All,
                discovery,
                WorkerBudgets.DefaultMaximumExpressionDepth,
                CancellationToken.None)));
    }

    private static CompilerManifestArtifact CreateArtifact(
        CSharpParseOptions? parse = null,
        string source = "internal sealed class Subject {}\n// line two\n// line three\n",
        ImmutableArray<string> specificationPacks = default)
    {
        return CreateArtifactCore(
            parse ?? new CSharpParseOptions(LanguageVersion.CSharp12),
            source,
            includeContractReference: false,
            features: WorkerFeatureSet.All,
            specificationPacks: specificationPacks);
    }

    private static CompilerManifestArtifact CreateArtifactCore(
        CSharpParseOptions parse,
        string source,
        bool includeContractReference,
        WorkerFeatureSet features,
        ImmutableArray<string> specificationPacks = default)
    {
        var compilation = CreateCompilation(
            parse,
            source,
            includeContractReference);
        var discovery = new ClaimManifestBuilder(
            compilation,
            features,
            CancellationToken.None).Build();
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            features,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None,
            specificationPacks: specificationPacks);
    }

    private static PortableIrInstruction FindCall(
        CompilerCallableArtifact callable)
    {
        return callable.Graph!.Blocks
            .SelectMany(static block => block.Instructions)
            .Single(static instruction =>
                instruction.Kind == IrInstructionKind.Call);
    }

    private static CompilerManifestArtifact CreateContractArtifact(string? source = null)
    {
        return CreateArtifactCore(
            new CSharpParseOptions(LanguageVersion.CSharp12),
            source ?? """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            includeContractReference: true,
            features: WorkerFeatureSet.All);
    }

    private static CompilerManifestArtifact CreateFeatureArtifact(
        WorkerFeatureSet features,
        string source)
    {
        return CreateArtifactCore(
            new CSharpParseOptions(LanguageVersion.CSharp12),
            source,
            includeContractReference: true,
            features: features);
    }

    private static CompilerManifestArtifact CreateEffectArtifact()
    {
        return CreateContractArtifact(DoesNotThrowIdentitySource);
    }

    private static CompilerManifestArtifact CreateUnsupportedLoopArtifact()
    {
        return CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    while (value > 0) { value--; }
                    return value;
                }
            }
            """);
    }

    private static CompilerManifestArtifact CloneArtifact(
        CompilerManifestArtifact artifact)
    {
        return CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));
    }

    private static CompilerManifestArtifact CanonicalRoundTrip(
        CompilerManifestArtifact artifact)
    {
        artifact.FeatureScopeSha256 =
            CompilerFeatureScopeFingerprint.ComputeSha256(artifact);
        return CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));
    }

    private static void ReplaceWithLinearBody(
        PortableIrGraph graph,
        int blockCount)
    {
        Assert.That(blockCount, Is.GreaterThan(0));
        var terminal = graph.Blocks[graph.Entry].Instructions[^1];
        Assert.That(terminal.Kind, Is.EqualTo(IrInstructionKind.Return));
        var terminalOperation = graph.Operations[terminal.Operation];
        graph.Operations = [terminalOperation];
        terminal.Operation = 0;
        var blocks = new List<PortableIrBlock>();
        for (var index = 0; index < blockCount; index++)
        {
            var instruction = index == blockCount - 1
                ? terminal
                : new PortableIrInstruction(
                    IrInstructionKind.Goto,
                    terminal.Operation,
                    a: index + 1);
            blocks.Add(new PortableIrBlock(instructions: [instruction]));
        }

        graph.Blocks = [.. blocks];
        graph.Entry = 0;
    }

    private sealed class UnexpectedBackend : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query, CancellationToken cancellationToken)
        {
            throw new AssertionException("A mismatched spec witness reached the backend.");
        }
    }

    private static CompilerAdditionalFileSnapshot AdditionalFile(
        string name,
        char hash)
    {
        return new()
        {
            Path = Path.GetFullPath(Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    name))
                .Replace('\\', '/'),
            Sha256 = new string(hash, 64)
        };
    }

    private static void AddCaseVariantModule(
        CompilerManifestArtifact artifact)
    {
        var manifest = artifact.Compilation.References[0].Modules[0];
        var characters = manifest.Path.ToCharArray();
        var index = Array.FindIndex(characters, char.IsLetter);
        Assert.That(index, Is.GreaterThanOrEqualTo(0));
        characters[index] = char.IsUpper(characters[index])
            ? char.ToLowerInvariant(characters[index])
            : char.ToUpperInvariant(characters[index]);
        var caseVariant = new string(characters);
        Assert.That(caseVariant, Is.Not.EqualTo(manifest.Path));
        Assert.That(caseVariant, Is.EqualTo(manifest.Path).IgnoreCase);
        artifact.Compilation.References[0].Modules = [
            manifest,
            new CompilerReferenceModuleSnapshot
            {
                Name = "zz-linked.netmodule",
                Mvid = Guid.NewGuid().ToString("D"),
                Path = caseVariant,
                Sha256 = new string('a', 64),
                SizeBytes = 1
            }
        ];
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation, []);
    }

    private static CompilerReferenceModuleSnapshot[] CreateModuleRows(
        int count,
        long sizeBytes)
    {
        var root = TestContext.CurrentContext.WorkDirectory;
        return [.. Enumerable.Range(0, count).Select(index => new
            CompilerReferenceModuleSnapshot
            {
                Name = $"module-{index:D5}.netmodule",
                Mvid = Guid.NewGuid().ToString("D"),
                Path = Path.GetFullPath(Path.Combine(
                    root,
                    $"module-{index:D5}.netmodule")),
                Sha256 = new string((char)('a' + index % 6), 64),
                SizeBytes = sizeBytes
            })];
    }

    private static CompilerDiagnosticArtifact Diagnostic(
        string message,
        int length,
        int line,
        int column)
    {
        return new()
        {
            Code = "compiler.TEST",
            Message = message,
            IsSource = true,
            Location = new WorkerSourceLocation
            {
                Path = "same.cs",
                Start = 1,
                Length = length,
                Line = line,
                Column = column
            }
        };
    }

    private static void BindDiagnostics(CompilerManifestArtifact artifact)
    {
        var tree = artifact.Compilation.SyntaxTrees.Single();
        foreach (var diagnostic in artifact.CompilerDiagnostics)
        {
            var mappedLine = diagnostic.Location.Line - 1;
            var mappedColumn = diagnostic.Location.Column - 1;
            var entry = tree.LineMap.LastOrDefault(item =>
                item.MappedLine == mappedLine &&
                item.MappedColumn <= mappedColumn &&
                mappedColumn - item.MappedColumn <= item.SourceLength);
            Assert.That(entry, Is.Not.Null);
            diagnostic.Location.Path = entry!.MappedPath;
            diagnostic.Location.Start = entry.SourceStart +
                mappedColumn - entry.MappedColumn;
            diagnostic.SourceTreeOrdinal = 0;
            diagnostic.SourceTreePath = tree.Path;
            diagnostic.SourceTreeSha256 = tree.Sha256;
            diagnostic.SourceLineMapSha256 = tree.LineMapSha256;
        }
    }

    private static void AssertMalformedAdditionalFiles(
        params CompilerAdditionalFileSnapshot[] files)
    {
        AssertMalformedCapture(compilation =>
        {
            compilation.AdditionalFiles = files;
        });
    }

    private static void AssertMalformedCapture(
        Action<CompilerCompilationSnapshot> corrupt,
        string? source = null)
    {
        var artifact = source is null
            ? CreateArtifact()
            : CreateArtifact(source: source);
        corrupt(artifact.Compilation);
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation, []);
        var json = JsonSerializer.Serialize(
                artifact,
                WorkerProtocolJson.Options) +
            "\n";

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(json)));
    }

    private static void AssertWellFormedCapture(
        CompilerManifestArtifact artifact,
        string? message = null)
    {
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact))),
            message ?? string.Empty);
    }

    private static CSharpCompilation CreateCompilation(
        CSharpParseOptions parse,
        string source,
        bool includeContractReference)
    {
        return CSharpCompilation.Create(
            "CompilerArtifactTest",
            [CSharpSyntaxTree.ParseText(
                source,
                parse,
                Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "Subject.cs"))],
            includeContractReference
                ? TestMetadataReferences.WithSharpProof
                : TestMetadataReferences.CoreLibraryOnly,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }
}
