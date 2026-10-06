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

    private const string DoesNotThrowIdentitySource =
        """
        using SharpProof.Attributes;
        internal static class Subject {
            [DoesNotThrow]
            internal static int Identity(int value) => value;
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
    public void CompilerIdentityIsProvenanceRatherThanWorkerGate()
    {
        var artifact = CreateArtifact();
        artifact.Compilation.CompilerMvid =
            Guid.NewGuid().ToString("D");
        artifact.Compilation.CSharpCompilerMvid =
            Guid.NewGuid().ToString("D");

        var roundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));
        var callables =
            CompilerManifestArtifactJson.DecodeCallables(roundTrip);

        Assert.That(callables, Is.Empty);
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
    public void HelperUsedByAnEarlierContractKeepsLaterCallableDecodable()
    {
        var artifact = CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class SharedMember {
                private static long Helper(long value) => value;

                public static long A(long value) {
                    Contract.Requires(Helper(value) > 0);
                    Contract.Ensures(Contract.Result<long>() > 0);
                    return value;
                }

                public static long B(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return Helper(value);
                }

                public static long C(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return Helper(value);
                }
            }
            """);

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));
        Assert.That(
            artifact.Callables.Single(item => item.CallableId.Contains(".B(", StringComparison.Ordinal)).Total?.Graph,
            Is.Not.Null);
    }

    [Test]
    public void CompilerDiagnosticLocationsUseTheSharedOneBasedOrNoneShape()
    {
        var valid = CreateArtifact();
        valid.CompilerDiagnostics = [Diagnostic(
            "valid", length: 0, line: 1, column: 1)];
        BindDiagnostics(valid);
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(valid)));

        var none = CreateArtifact();
        none.CompilerDiagnostics = [new CompilerDiagnosticArtifact
        {
            Code = "compiler.NONE",
            Message = "non-source",
            Location = new WorkerSourceLocation()
        }];
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
    public void SpecificationPackAuthorityFieldsCannotSilentlyDefaultOnWire()
    {
        var json = CompilerManifestArtifactJson.Serialize(CreateArtifact());
        var catalogVersionProperty =
            "\"specificationPackCatalogVersion\":" +
            CompilerSpecificationPackCatalogVersions.Current.ToString(
                CultureInfo.InvariantCulture) + ",";
        Assert.That(json, Does.Contain(catalogVersionProperty));
        var withoutCatalogVersion = json.Replace(
            catalogVersionProperty,
            string.Empty,
            StringComparison.Ordinal);

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(withoutCatalogVersion)));
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

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact))));
    }

    [Test]
    public void CompilerManifestAllowsPayloadsAboveWorkerJsonEnvelope()
    {
        var artifact = CreateArtifact();
        artifact.CompilerDiagnostics = [Diagnostic("x", 1, 1, 1)];
        BindDiagnostics(artifact);
        var initial = CompilerManifestArtifactJson.Serialize(artifact);
        var padding =
            WorkerProtocolJson.MaximumJsonBytes + 1 -
            Encoding.UTF8.GetByteCount(initial);
        Assert.That(padding, Is.GreaterThan(0));

        artifact.CompilerDiagnostics[0].Message += new string('x', padding);

        var json = CompilerManifestArtifactJson.Serialize(artifact);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CompilerManifestArtifactFile.MaximumBytes,
                Is.GreaterThan(WorkerProtocolJson.MaximumJsonBytes));
            Assert.That(
                Encoding.UTF8.GetByteCount(json),
                Is.GreaterThan(WorkerProtocolJson.MaximumJsonBytes));
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.Deserialize(json)));
        }
    }

    [Test]
    public void SerializationEnforcesWorkerInputByteLimit()
    {
        var artifact = CreateArtifact();
        artifact.CompilerDiagnostics = [Diagnostic("x", 1, 1, 1)];
        BindDiagnostics(artifact);
        var initial = CompilerManifestArtifactJson.Serialize(artifact);
        var padding = CompilerManifestArtifactFile.MaximumBytes -
            Encoding.UTF8.GetByteCount(initial);
        Assert.That(padding, Is.GreaterThan(0));

        artifact.CompilerDiagnostics[0].Message += new string('x', padding);
        var exact = CompilerManifestArtifactJson.Serialize(artifact);
        Assert.That(
            Encoding.UTF8.GetByteCount(exact),
            Is.EqualTo(CompilerManifestArtifactFile.MaximumBytes));

        artifact.CompilerDiagnostics[0].Message += "x";
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
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
            // The compiler only declares the claim; Z3 decides it.
            Assert.That(evidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(evidence.Certainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.IncompleteMayEffectSummary));
            Assert.That(CompilerManifestArtifactJson.DecodeCallables(valid).Single()
                .EffectClaims, Has.Length.EqualTo(1));
        }

        Action<CompilerCallableArtifact>[] corruptions = [
            value => value.EffectClaims = [],
            value => value.EffectClaims = [value.EffectClaims[0], value.EffectClaims[0]],
            value => value.EffectClaims[0].ClaimId = "spc1:invented",
            value => value.EffectClaims[0].ContractKind =
                WorkerEffectContractKind.ZeroAllocations,
            value => value.EffectClaims[0].Outcome = WorkerClaimOutcome.Refuted,
            value => value.EffectClaims[0].Certainty =
                WorkerEffectEvidenceCertainty.CompleteMayEffectSummary
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
    public void TotalCallCanonicalVariablesRemainDistinct()
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
            callable => callable.Total!.Result = callable.Total.Parameters[0].Entry,
            callable => callable.Total!.Parameters[0].Current = callable.Total.Parameters[0].Entry,
            callable => callable.Total!.Parameters[0].Old = callable.Total.Parameters[0].Current
        ];
        var valid = CreateContractArtifact(source);
        Assert.That(valid.Callables[0].Total, Is.Not.Null);
        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            corrupt(artifact.Callables[0]);
            Assert.Throws<InvalidDataException>(new Action(() => CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
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

    [TestCase("array")]
    [TestCase("entry")]
    [TestCase("manifest")]
    public void ArtifactSerializationRejectsMalformedCallableContainers(string mutation)
    {
        var artifact = CreateContractArtifact();
        switch (mutation)
        {
            case "array":
                artifact.Callables = null!;
                break;
            case "entry":
                artifact.Callables = [null!];
                break;
            case "manifest":
                artifact.Manifest = null!;
                break;
        }

        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void ArtifactSerializationKeepsValidCallableRoundTrip()
    {
        var artifact = CreateContractArtifact();
        var decoded = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));

        Assert.That(decoded.Callables.Select(item => item.CallableId),
            Is.EqualTo(artifact.Callables.Select(item => item.CallableId)));
    }

    [TestCase("features")]
    [TestCase("reasons")]
    [TestCase("unspecified")]
    [TestCase("valid")]
    public void ArtifactSerializationKeepsManifestEnumRejectionBoundary(string mutation)
    {
        var artifact = CreateContractArtifact();
        var callable = artifact.Manifest.Callables.Single();
        switch (mutation)
        {
            case "features":
                callable.SelectedFeatures = [.. callable.SelectedFeatures, (WorkerSelectedFeature)int.MaxValue];
                break;
            case "reasons":
                callable.SelectionReasons = [.. callable.SelectionReasons, (WorkerSelectionReason)int.MaxValue];
                break;
            case "unspecified":
                callable.SelectedFeatures = [.. callable.SelectedFeatures, WorkerSelectedFeature.Unspecified];
                break;
        }
        if (mutation == "valid")
        {
            var decoded = CompilerManifestArtifactJson.Deserialize(CompilerManifestArtifactJson.Serialize(artifact));
            Assert.That(decoded.Manifest.Callables.Single().CallableId, Is.EqualTo(callable.CallableId));
        }
        else
        {
            Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Validate(artifact)));
            Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Serialize(artifact)));
        }
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
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
