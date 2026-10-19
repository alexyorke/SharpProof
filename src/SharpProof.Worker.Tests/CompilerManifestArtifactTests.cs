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
    public void ProjectPublicationBindsACopyWithoutRewritingProducerCompilation()
    {
        var artifact = CreateContractArtifact();
        var compilation = artifact.Compilation;
        var directory = compilation.ProjectDirectory;
        using var project = new ShadowTestProject(artifact);
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(
            project.Artifact, cancellationToken: TestContext.CurrentContext.CancellationToken));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Compilation, Is.SameAs(compilation));
            Assert.That(compilation.ProjectDirectory, Is.EqualTo(directory));
            Assert.That(project.Artifact, Is.Not.SameAs(artifact));
            Assert.That(project.Artifact.Compilation, Is.Not.SameAs(compilation));
            Assert.That(project.Artifact.Compilation.ProjectDirectory,
                Is.EqualTo(Path.GetDirectoryName(project.Request.CompilerManifest.Path)));
            Assert.That(WorkerProtocolJson.ComputeSha256(bytes),
                Is.EqualTo(project.Request.CompilerManifest.Sha256));
        }
    }

    [Test]
    public void SerializationCanonicalizesCopiesWithoutRewritingProducerCollections()
    {
        var artifact = CreateContractArtifact(
            "using SharpProof.Attributes; internal static class Subject { " +
            "internal static int B(int value) { Contract.Requires(value > 0); " +
            "Contract.Ensures(Contract.Result<int>() == value); return value; } " +
            "internal static int A(int value) { Contract.Requires(value > 0); " +
            "Contract.Ensures(Contract.Result<int>() == value); return value; } }");
        var canonical = CompilerManifestArtifactJson.Serialize(artifact, TestContext.CurrentContext.CancellationToken);
        { artifact = artifact with { Callables = artifact.Callables.Reverse().ToImmutableArray() }; }
        artifact.Manifest.Callables = artifact.Manifest.Callables.Reverse().ToImmutableArray();
        artifact.Manifest.Claims = artifact.Manifest.Claims.Reverse().ToImmutableArray();
        foreach (var callable in artifact.Manifest.Callables)
        {
            callable.SelectedFeatures = callable.SelectedFeatures.Reverse().ToImmutableArray();
            callable.SelectionReasons = callable.SelectionReasons.Reverse().ToImmutableArray();
            callable.ClaimIds = callable.ClaimIds.Reverse().ToImmutableArray();
            callable.Assumptions = callable.Assumptions.Reverse().ToImmutableArray();
        }
        var manifest = artifact.Manifest;
        var payloads = artifact.Callables;
        var entries = manifest.Callables;
        var claims = manifest.Claims;
        var collections = entries.Select(static entry =>
            entry is null ? throw new ArgumentNullException(nameof(entry)) :
                (Entry: entry, entry.SelectedFeatures, entry.SelectionReasons, entry.ClaimIds, entry.Assumptions)).ToImmutableArray();
        var before = JsonSerializer.Serialize(artifact, WorkerProtocolJson.SharedOptions);

        var actual = CompilerManifestArtifactJson.Serialize(artifact, TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(actual, Is.EqualTo(canonical));
            Assert.That(JsonSerializer.Serialize(artifact, WorkerProtocolJson.SharedOptions), Is.EqualTo(before));
            Assert.That(artifact.Manifest, Is.SameAs(manifest));
            Assert.That(artifact.Callables.Equals(payloads), Is.True);
            Assert.That(manifest.Callables.Equals(entries), Is.True);
            Assert.That(manifest.Claims.Equals(claims), Is.True);
            foreach (var collection in collections)
            {
                Assert.That(collection.Entry.SelectedFeatures.Equals(collection.SelectedFeatures), Is.True);
                Assert.That(collection.Entry.SelectionReasons.Equals(collection.SelectionReasons), Is.True);
                Assert.That(collection.Entry.ClaimIds.Equals(collection.ClaimIds), Is.True);
                Assert.That(collection.Entry.Assumptions.Equals(collection.Assumptions), Is.True);
            }
        }
    }

    [Test]
    public void CallableDecodingCapturesAuthorityWithoutRewritingCompilation()
    {
        var artifact = CreateContractArtifact();
        var compilation = artifact.Compilation with
        {
            SpecificationPackIds = ["producer-sentinel"],
            SpecificationPackCatalogVersion = -1,
            SpecificationPackCatalogSha256 = "producer-sentinel"
        };
        artifact = artifact with { Compilation = compilation };
        var originalIds = compilation.SpecificationPackIds;

        var prepared = CompilerManifestArtifactJson.DecodeCallables(artifact, TestContext.CurrentContext.CancellationToken).Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(artifact.Compilation, Is.SameAs(compilation));
            Assert.That(compilation.SpecificationPackIds.Equals(originalIds), Is.True);
            Assert.That(compilation.SpecificationPackIds, Has.Length.EqualTo(1));
            Assert.That(compilation.SpecificationPackIds[0], Is.EqualTo("producer-sentinel"));
            Assert.That(compilation.SpecificationPackCatalogVersion, Is.EqualTo(-1));
            Assert.That(compilation.SpecificationPackCatalogSha256, Is.EqualTo("producer-sentinel"));
            Assert.That(prepared.Compilation, Is.Not.SameAs(compilation));
            Assert.That(prepared.Compilation.SpecificationPackIds, Is.EqualTo(artifact.SpecificationPackIds));
            Assert.That(prepared.Compilation.SpecificationPackCatalogVersion, Is.EqualTo(artifact.SpecificationPackCatalogVersion));
            Assert.That(prepared.Compilation.SpecificationPackCatalogSha256, Is.EqualTo(artifact.SpecificationPackCatalogSha256));
        }
    }

    [Test]
    public void DeserializationPreservesHydratedCompilationAuthority()
    {
        var artifact = CreateContractArtifact();
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, TestContext.CurrentContext.CancellationToken);

        var decoded = CompilerManifestArtifactJson.DeserializePrepared(json, out var prepared, TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(decoded.Compilation.SpecificationPackIds, Is.EqualTo(decoded.SpecificationPackIds));
            Assert.That(decoded.Compilation.SpecificationPackCatalogVersion, Is.EqualTo(decoded.SpecificationPackCatalogVersion));
            Assert.That(decoded.Compilation.SpecificationPackCatalogSha256, Is.EqualTo(decoded.SpecificationPackCatalogSha256));
            Assert.That(prepared.Single().Compilation, Is.SameAs(decoded.Compilation));
        }
    }

    [Test]
    public void CompilerManifestCasesUseBoundedParallelism()
    {
        var fixtureAttribute = typeof(CompilerManifestArtifactTests)
            .GetCustomAttributesData()
            .Single(static attribute =>
                attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeType == typeof(ParallelizableAttribute)));
        var workerAttribute = typeof(CompilerManifestArtifactTests).Assembly
            .GetCustomAttributesData()
            .Single(static attribute =>
                attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.AttributeType == typeof(LevelOfParallelismAttribute)));

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
        const string source =
            "internal sealed class Subject { private const string Value = \"" +
            SourceMarker +
            "\"; }\n";
        var artifact = CreateArtifact(parse, source);
        var tree = artifact.Compilation.SyntaxTrees.Single();
        var json = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);

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
                    feature is null ? throw new System.ArgumentNullException(nameof(feature)) : (new KeyValuePair<string, string>(
                        feature.Key,
                        feature.Value))),
                Is.EqualTo(parse.Features));
            Assert.That(
                artifact.Compilation.Options.ResolverPolicy,
                Is.EqualTo(CompilerResolverPolicy.EvidenceOnly));
            Assert.That(json, Does.Not.Contain(SourceMarker));
            Assert.That(json, Does.Not.Contain("\"text\":"));
        }
    }

    [Test]
    public void CompilerIdentityIsProvenanceForSelectedClaimArtifact()
    {
        var artifact = CreateContractArtifact();
        var callable = artifact.Manifest.Callables.Single();
        var claims = artifact.Manifest.Claims
            .Where(claim => claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.CallableId == callable.CallableId)
            .ToImmutableArray();
        Assert.That(claims, Is.Not.Empty);
        artifact = artifact with
        {
            Compilation = artifact.Compilation with
            {
                CompilerMvid = Guid.NewGuid().ToString("D"),
                CSharpCompilerMvid = Guid.NewGuid().ToString("D")
            }
        };

        var roundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken);
        var callables =
            CompilerManifestArtifactJson.DecodeCallables(roundTrip, cancellationToken: TestContext.CurrentContext.CancellationToken);

        var decoded = callables.Single(item =>
            item is null ? throw new ArgumentNullException(nameof(item)) : item.Entry.CallableId == callable.CallableId);
        var postcondition = claims.Single(static claim =>
            claim is null ? throw new ArgumentNullException(nameof(claim)) : claim.Kind == WorkerClaimKind.Postcondition);

        Assert.That(decoded.Entry.ClaimIds, Is.EqualTo(callable.ClaimIds));
        Assert.That(decoded.Total, Is.Not.Null);
        Assert.That(decoded.Total!.Clauses.Any(clause =>
            clause is null ? throw new ArgumentNullException(nameof(clause)) : clause.Kind == CompilerContractKind.Ensures &&
            clause.ClaimId == postcondition.ClaimId), Is.True);
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
        var allowed = ImmutableHashSet.Create<WorkerClaimReason>(
            WorkerClaimReason.UnsupportedCallable,
            WorkerClaimReason.UnsupportedContract,
            WorkerClaimReason.UnsupportedBody,
            WorkerClaimReason.UnsupportedExpression);
        var rejected = Enum.GetValues<WorkerClaimReason>()
            .Where(reason => reason is not (
                WorkerClaimReason.Unspecified or
                WorkerClaimReason.None) &&
                !allowed.Contains(reason))
            .ToImmutableArray();
        Assert.That(rejected, Does.Contain(WorkerClaimReason.MethodTimeout));
        var artifact = CreateUnsupportedLoopArtifact();

        foreach (var reason in allowed)
        {
            { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { FailureReason = reason }) }; }
            Assert.That(
                CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)
                    .Single().FailureReason,
                Is.EqualTo(reason),
                reason.ToString());
        }

        foreach (var reason in rejected)
        {
            { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { FailureReason = reason }) }; }
            Assert.Throws<JsonException>(
                (Action)(() =>
                    CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)),
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
                    callable is null ? throw new System.ArgumentNullException(nameof(callable)) : (callable.FailureReason)),
                Is.All.EqualTo(WorkerClaimReason.UnsupportedCallable));
        }
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));

        { artifact = artifact with { Callables = artifact.Callables.SetItem(0, artifact.Callables[0] with { FailureReason = WorkerClaimReason.UnsupportedBody }) }; }
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    [Platform("Linux")]
    public void CaseDistinctModulePathsRemainDistinct()
    {
        var artifact = CreateArtifact();
        artifact = AddCaseVariantModule(artifact);

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
            TestContext.CurrentContext.CancellationToken);

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
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(
            artifact.Callables.Single(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId.Contains(".B(", StringComparison.Ordinal))).Total?.Graph,
            Is.Not.Null);
    }

    [Test]
    public void CompilerDiagnosticLocationsUseTheSharedOneBasedOrNoneShape()
    {
        var valid = CreateArtifact();
        {
            valid = valid with
            {
                CompilerDiagnostics = [Diagnostic(
            "valid", length: 0, line: 1, column: 1)]
            };
        }
        valid = BindDiagnostics(valid);
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(valid, cancellationToken: TestContext.CurrentContext.CancellationToken)));

        var none = CreateArtifact();
        {
            none = none with
            {
                CompilerDiagnostics = [new CompilerDiagnosticArtifact
        {
            Code = "compiler.NONE",
            Message = "non-source",
            Location = new WorkerSourceLocation()
        }]
            };
        }
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(none, cancellationToken: TestContext.CurrentContext.CancellationToken)));

        foreach (var location in ImmutableArray.Create(new WorkerSourceLocation
        {
            Path = "same.cs",
            Start = 0,
            Length = 0,
            Line = 0,
            Column = 1
        }, new WorkerSourceLocation
        {
            Path = "same.cs",
            Start = 0,
            Length = 0,
            Line = 1,
            Column = 0
        }, new WorkerSourceLocation
        {
            Path = "",
            Start = 0,
            Length = 1,
            Line = 0,
            Column = 0
        }, new WorkerSourceLocation
        {
            Path = "",
            Start = -1,
            Length = 0,
            Line = 0,
            Column = 0
        }))
        {
            var malformed = CreateArtifact();
            {
                malformed = malformed with
                {
                    CompilerDiagnostics = [new CompilerDiagnosticArtifact
            {
                Code = "compiler.BAD",
                Message = "bad geometry",
                Location = location
            }]
                };
            }
            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(malformed, cancellationToken: TestContext.CurrentContext.CancellationToken)));
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
        System.ArgumentNullException.ThrowIfNull(code);
        var artifact = CreateArtifact();
        {
            artifact = artifact with
            {
                CompilerDiagnostics = [Diagnostic(
            "invalid code", length: 1, line: 1, column: 1)]
            };
        }
        artifact = BindDiagnostics(artifact);
        { artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.SetItem(0, artifact.CompilerDiagnostics[0] with { Code = code }) }; }

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void CompilerDiagnosticCodesRejectControlCharacters()
    {
        var artifact = CreateArtifact();
        {
            artifact = artifact with
            {
                CompilerDiagnostics = [Diagnostic(
            "invalid code", length: 1, line: 1, column: 1)]
            };
        }
        artifact = BindDiagnostics(artifact);
        { artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.SetItem(0, artifact.CompilerDiagnostics[0] with { Code = "compiler.CS" + (char)1 }) }; }

        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase("compiler.CS1001")]
    [TestCase("compiler.ERR_Test")]
    [TestCase("compiler.A1_b2")]
    public void CompilerDiagnosticCodesAcceptCanonicalRoslynIdGrammar(
        string code)
    {
        System.ArgumentNullException.ThrowIfNull(code);
        var artifact = CreateArtifact();
        {
            artifact = artifact with
            {
                CompilerDiagnostics = [Diagnostic(
            "canonical code", length: 1, line: 1, column: 1)]
            };
        }
        artifact = BindDiagnostics(artifact);
        { artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.SetItem(0, artifact.CompilerDiagnostics[0] with { Code = code }) }; }

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void RelationalEvidenceSchemaVersionsAreExactPins()
    {
        ImmutableArray<Func<CompilerManifestArtifact, CompilerManifestArtifact>> corruptions =
        [
            artifact => { System.ArgumentNullException.ThrowIfNull(artifact); { artifact = artifact with { RelationalSummarySchemaVersion = 0 }; } return artifact;},
            artifact => { System.ArgumentNullException.ThrowIfNull(artifact); { artifact = artifact with { RelationalSummarySchemaVersion = CompilerRelationalSummaryVersions.Current + 1 }; } return artifact;},
            artifact => { System.ArgumentNullException.ThrowIfNull(artifact); { artifact = artifact with { SpecificationPackSchemaVersion = 0 }; } return artifact;},
            artifact => { System.ArgumentNullException.ThrowIfNull(artifact); { artifact = artifact with { SpecificationPackSchemaVersion = CompilerSpecificationPackVersions.Current + 1 }; } return artifact;}
        ];

        foreach (var corrupt in corruptions)
        {
            var artifact = CreateArtifact();
            artifact = corrupt(artifact);

            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        }
    }

    [Test]
    public void SpecificationPackAuthorityFieldsCannotSilentlyDefaultOnWire()
    {
        var json = CompilerManifestArtifactJson.Serialize(CreateArtifact(), cancellationToken: TestContext.CurrentContext.CancellationToken);
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
            CompilerManifestArtifactJson.Deserialize(withoutCatalogVersion, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    [Platform("Linux")]
    public void AdditionalFilesPermitCaseDistinctPaths()
    {
        var artifact = CreateArtifact();
        artifact = artifact with
        {
            Compilation = artifact.Compilation with
            {
                AdditionalFiles = [
                    AdditionalFile("CASE.input", 'a'),
                    AdditionalFile("case.input", 'b')
                ]
            }
        };

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void CompilerManifestAllowsPayloadsAboveWorkerJsonEnvelope()
    {
        var artifact = CreateArtifact();
        { artifact = artifact with { CompilerDiagnostics = [Diagnostic("x", 1, 1, 1)] }; }
        artifact = BindDiagnostics(artifact);
        var initial = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var padding =
            WorkerProtocolJson.MaximumJsonBytes + 1 -
            Encoding.UTF8.GetByteCount(initial);
        Assert.That(padding, Is.GreaterThan(0));

        { artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.SetItem(0, artifact.CompilerDiagnostics[0] with { Message = artifact.CompilerDiagnostics[0].Message + new string('x', padding) }) }; }

        var json = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CompilerManifestArtifactFile.MaximumBytes,
                Is.GreaterThan(WorkerProtocolJson.MaximumJsonBytes));
            Assert.That(
                Encoding.UTF8.GetByteCount(json),
                Is.GreaterThan(WorkerProtocolJson.MaximumJsonBytes));
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.Deserialize(json, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        }
    }

    [Test]
    public void SerializationEnforcesWorkerInputByteLimit()
    {
        var artifact = CreateArtifact();
        { artifact = artifact with { CompilerDiagnostics = [Diagnostic("x", 1, 1, 1)] }; }
        artifact = BindDiagnostics(artifact);
        var initial = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var padding = CompilerManifestArtifactFile.MaximumBytes -
            Encoding.UTF8.GetByteCount(initial);
        Assert.That(padding, Is.GreaterThan(0));

        { artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.SetItem(0, artifact.CompilerDiagnostics[0] with { Message = artifact.CompilerDiagnostics[0].Message + new string('x', padding) }) }; }
        var exact = CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(
            Encoding.UTF8.GetByteCount(exact),
            Is.EqualTo(CompilerManifestArtifactFile.MaximumBytes));

        { artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.SetItem(0, artifact.CompilerDiagnostics[0] with { Message = artifact.CompilerDiagnostics[0].Message + "x" }) }; }
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
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
            Assert.That(CompilerManifestArtifactJson.DecodeCallables(valid, cancellationToken: TestContext.CurrentContext.CancellationToken).Single()
                .EffectClaims, Has.Length.EqualTo(1));
        }

        ImmutableArray<Func<CompilerCallableArtifact, CompilerCallableArtifact>> corruptions = [
            value => { System.ArgumentNullException.ThrowIfNull(value); { value = value with { EffectClaims = [] }; } return value;},
            value => { System.ArgumentNullException.ThrowIfNull(value); { value = value with { EffectClaims = [value.EffectClaims[0], value.EffectClaims[0]] }; } return value;},
            value => { System.ArgumentNullException.ThrowIfNull(value); { value = value with { EffectClaims = value.EffectClaims.SetItem(0, value.EffectClaims[0] with { ClaimId = "spc1:invented" }) }; } return value;},
            value => { System.ArgumentNullException.ThrowIfNull(value); { value = value with { EffectClaims = value.EffectClaims.SetItem(0, value.EffectClaims[0] with { ContractKind = WorkerEffectContractKind.ZeroAllocations }) }; } return value;},
            value => { System.ArgumentNullException.ThrowIfNull(value); { value = value with { EffectClaims = value.EffectClaims.SetItem(0, value.EffectClaims[0] with { Outcome = WorkerClaimOutcome.Refuted }) }; } return value;},
            value => { System.ArgumentNullException.ThrowIfNull(value); { value = value with { EffectClaims = value.EffectClaims.SetItem(0, value.EffectClaims[0] with { Certainty = WorkerEffectEvidenceCertainty.CompleteMayEffectSummary }) }; } return value;}
        ];
        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            artifact = artifact with { Callables = artifact.Callables.SetItem(0, corrupt(artifact.Callables[0])) };
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        }
    }

    [Test]
    public void AllowedExceptionEvidenceRequiresCanonicalTypeNames()
    {
        var artifact = CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                [AllowedExceptions(typeof(System.ArgumentException),
                    typeof(System.InvalidOperationException))]
                internal static void Target() { }
            }
            """);
        var evidence = artifact.Callables.Single().EffectClaims.Single();
        var allowed = evidence.Constraint.AllowedExceptionTypes;
        Assert.That(allowed, Has.Length.EqualTo(2));
        Assert.That(allowed, Is.EqualTo(
            allowed.OrderBy(static type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type), StringComparer.Ordinal).ToImmutableArray()));
        Assert.DoesNotThrow((Action)(() =>
            CompilerEffectClaimArtifactCodec.Validate(evidence)));

        var constraint = evidence.Constraint;
        {
            evidence = evidence with { Constraint = null! };
            artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { EffectClaims = artifact.Callables.Single().EffectClaims.ReplaceReference(artifact.Callables.Single().EffectClaims.Single(), evidence) }) };
        }
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerEffectClaimArtifactCodec.Validate(evidence)));
        {
            evidence = evidence with { Constraint = constraint };
            artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { EffectClaims = artifact.Callables.Single().EffectClaims.ReplaceReference(artifact.Callables.Single().EffectClaims.Single(), evidence) }) };
        }

        foreach (var invalid in ImmutableArray.Create<ImmutableArray<string>>(
            default,
            ["", allowed[1]],
            [allowed[0], allowed[0]],
            [allowed[1], allowed[0]]))
        {
            {
                evidence = evidence with { Constraint = evidence.Constraint with { AllowedExceptionTypes = invalid } };
                artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { EffectClaims = artifact.Callables.Single().EffectClaims.ReplaceReference(artifact.Callables.Single().EffectClaims.Single(), evidence) }) };
            }
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerEffectClaimArtifactCodec.Validate(evidence)));
        }
    }

    [Test]
    public void LoweredCallableDecoderRejectsMalformedAdmissionEvidence()
    {
        var valid = CreateEffectArtifact();
        Assert.That(CompilerLoweredArtifact.Decode(
            valid.Callables, valid.Manifest, valid.Compilation, cancellationToken: TestContext.CurrentContext.CancellationToken).Length, Is.EqualTo(1));

        foreach (var mutation in ImmutableArray.Create("missing-payload", "missing-compilation", "callable-count", "foreign-id", "claim-id", "reason", "missing-effects"))
        {
            var artifact = CloneArtifact(valid);
            switch (mutation)
            {
                case "missing-payload":
                    { artifact = artifact with { Callables = default }; }
                    break;
                case "missing-compilation":
                    { artifact = artifact with { Compilation = null! }; }
                    break;
                case "callable-count":
                    { artifact = artifact with { Callables = [] }; }
                    break;
                case "foreign-id":
                    { artifact = artifact with { Callables = artifact.Callables.SetItem(0, artifact.Callables[0] with { CallableId = "foreign" }) }; }
                    break;
                case "claim-id":
                    artifact.Manifest.Callables[0].ClaimIds = ["foreign"];
                    break;
                case "reason":
                    { artifact = artifact with { Callables = artifact.Callables.SetItem(0, artifact.Callables[0] with { FailureReason = WorkerClaimReason.MethodTimeout }) }; }
                    break;
                case "missing-effects":
                    { artifact = artifact with { Callables = artifact.Callables.SetItem(0, artifact.Callables[0] with { EffectClaims = default }) }; }
                    break;
            }

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerLoweredArtifact.Decode(
                    artifact.Callables, artifact.Manifest, artifact.Compilation, cancellationToken: TestContext.CurrentContext.CancellationToken)),
                mutation);
        }
    }

    [Test]
    public void LoweredCallableDecoderRejectsDuplicateEffectClaimIds()
    {
        var artifact = CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow, ZeroAllocations]
                internal static void Target() { }
            }
            """);
        var effects = artifact.Callables.Single().EffectClaims;
        Assert.That(effects, Has.Length.EqualTo(2));
        Assert.That(CompilerLoweredArtifact.Decode(
            artifact.Callables, artifact.Manifest, artifact.Compilation, cancellationToken: TestContext.CurrentContext.CancellationToken).Length,
            Is.EqualTo(1));

        {
            effects = effects.SetItem(1, effects[1] with { ClaimId = effects[0].ClaimId });
            artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { EffectClaims = effects }) };
        }
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerLoweredArtifact.Decode(
                artifact.Callables, artifact.Manifest, artifact.Compilation, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void StrictSerializationRejectsUndecodableCallableGraph()
    {
        var artifact = CreateContractArtifact();
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));

        { artifact = artifact with { Callables = artifact.Callables.ReplaceReference(artifact.Callables.Single(), artifact.Callables.Single() with { Total = artifact.Callables.Single().Total! with { Graph = artifact.Callables.Single().Total!.Graph with { Roots = default } } }) }; }
        var failure = Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.That(failure!.Message,
            Is.EqualTo("The compiler manifest callable payload is invalid."));
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
        ImmutableArray<Func<CompilerCallableArtifact, CompilerCallableArtifact>> corruptions = [
            callable => { System.ArgumentNullException.ThrowIfNull(callable); { callable = callable with { Total = callable.Total! with { Result = callable.Total.Parameters[0].Entry } }; } return callable;},
            callable => { System.ArgumentNullException.ThrowIfNull(callable); { callable = callable with { Total = callable.Total! with { Parameters = callable.Total!.Parameters.SetItem(0, callable.Total!.Parameters[0] with { Current = callable.Total.Parameters[0].Entry }) } }; } return callable;},
            callable => { System.ArgumentNullException.ThrowIfNull(callable); { callable = callable with { Total = callable.Total! with { Parameters = callable.Total!.Parameters.SetItem(0, callable.Total!.Parameters[0] with { Old = callable.Total.Parameters[0].Current }) } }; } return callable;}
        ];
        var valid = CreateContractArtifact(source);
        Assert.That(valid.Callables[0].Total, Is.Not.Null);
        foreach (var corrupt in corruptions)
        {
            var artifact = CloneArtifact(valid);
            artifact = artifact with { Callables = artifact.Callables.SetItem(0, corrupt(artifact.Callables[0])) };
            Assert.Throws<InvalidDataException>(new Action(() => CompilerManifestArtifactJson.DecodeCallables(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
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
                TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase("array")]
    [TestCase("entry")]
    [TestCase("manifest")]
    public void ArtifactSerializationRejectsMalformedCallableContainers(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
        var artifact = CreateContractArtifact();
        switch (mutation)
        {
            case "array":
                { artifact = artifact with { Callables = default }; }
                break;
            case "entry":
                { artifact = artifact with { Callables = [null!] }; }
                break;
            case "manifest":
                { artifact = artifact with { Manifest = null! }; }
                break;
        }

        Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void ArtifactSerializationKeepsValidCallableRoundTrip()
    {
        var artifact = CreateContractArtifact();
        var decoded = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken);

        Assert.That(decoded.Callables.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId)),
            Is.EqualTo(artifact.Callables.Select(item => item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.CallableId))));
    }

    [TestCase("features")]
    [TestCase("reasons")]
    [TestCase("unspecified")]
    [TestCase("valid")]
    public void ArtifactSerializationKeepsManifestEnumRejectionBoundary(string mutation)
    {
        System.ArgumentNullException.ThrowIfNull(mutation);
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
            var decoded = CompilerManifestArtifactJson.Deserialize(CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken);
            Assert.That(decoded.Manifest.Callables.Single().CallableId, Is.EqualTo(callable.CallableId));
        }
        else
        {
            Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Validate(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
            Assert.Throws<JsonException>((Action)(() => CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        }
    }

    private static CompilerManifestArtifact CreateArtifact(
        CSharpParseOptions? parse = null,
        string source = "internal sealed class Subject {}\n// line two\n// line three\n",
        ImmutableArray<string> /* SPINT001: all values valid - default denotes absent optional inputs and is handled downstream */ specificationPacks = default)
    {

        System.ArgumentNullException.ThrowIfNull(source);
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
        ImmutableArray<string> /* SPINT001: all values valid - default denotes absent optional inputs and is handled downstream */ specificationPacks = default)
    {

        System.ArgumentNullException.ThrowIfNull(parse);
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = CreateCompilation(
            parse,
            source,
            includeContractReference);
        var discovery = new ClaimManifestBuilder(
            compilation,
            features,
            TestContext.CurrentContext.CancellationToken).Build();
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            features,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            TestContext.CurrentContext.CancellationToken,
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
        System.ArgumentNullException.ThrowIfNull(artifact);
        return CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken);
    }

    private static CompilerAdditionalFileSnapshot AdditionalFile(
        string name,
        char hash)
    {
        System.ArgumentNullException.ThrowIfNull(name);
        return new()
        {
            Path = Path.GetFullPath(Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    name))
                .Replace('\\', '/'),
            Sha256 = new string(hash, 64)
        };
    }

    private static CompilerManifestArtifact AddCaseVariantModule(
        CompilerManifestArtifact artifact)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
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
        var modules = ImmutableArray.Create<CompilerReferenceModuleSnapshot>(
            manifest,
            new CompilerReferenceModuleSnapshot
            {
                Name = "zz-linked.netmodule",
                Mvid = Guid.NewGuid().ToString("D"),
                Path = caseVariant,
                Sha256 = new string('a', 64),
                SizeBytes = 1
            });
        return artifact with
        {
            Compilation = artifact.Compilation with
            {
                References = artifact.Compilation.References.SetItem(0,
                    artifact.Compilation.References[0] with { Modules = modules })
            }
        };
    }

    private static CompilerDiagnosticArtifact Diagnostic(
        string message,
        int length,
        int line,
        int column)
    {
        System.ArgumentNullException.ThrowIfNull(message);
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

    private static CompilerManifestArtifact BindDiagnostics(CompilerManifestArtifact artifact)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
        var tree = artifact.Compilation.SyntaxTrees.Single();
        for (var index = 0; index < artifact.CompilerDiagnostics.Length; index++)
        {
            var diagnostic = artifact.CompilerDiagnostics[index];
            var mappedLine = diagnostic.Location.Line - 1;
            var mappedColumn = diagnostic.Location.Column - 1;
            var entry = tree.LineMap.LastOrDefault(item =>
                item is null ? throw new System.ArgumentNullException(nameof(item)) : (item.MappedLine == mappedLine &&
                item.MappedColumn <= mappedColumn &&
                mappedColumn - item.MappedColumn <= item.SourceLength));
            Assert.That(entry, Is.Not.Null);
            { diagnostic = diagnostic with { Location = diagnostic.Location with { Path = entry!.MappedPath } }; artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.ReplaceAt(index, diagnostic) }; }
            {
                diagnostic = diagnostic with
                {
                    Location = diagnostic.Location with
                    {
                        Start = entry.SourceStart +
                mappedColumn - entry.MappedColumn
                    }
                };
                artifact = artifact with { CompilerDiagnostics = artifact.CompilerDiagnostics.ReplaceAt(index, diagnostic) };
            }
            {
                artifact = artifact with
                {
                    CompilerDiagnostics = artifact.CompilerDiagnostics.SetItem(index, diagnostic with
                    {
                        SourceTreeOrdinal = 0,
                        SourceTreePath = tree.Path,
                        SourceTreeSha256 = tree.Sha256,
                        SourceLineMapSha256 = tree.LineMapSha256
                    })
                };
            }
        }
        return artifact;
    }

    private static void AssertWellFormedCapture(
        CompilerManifestArtifact artifact,
        string? message = null)
    {
        System.ArgumentNullException.ThrowIfNull(artifact);
        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken), cancellationToken: TestContext.CurrentContext.CancellationToken)),
            message ?? string.Empty);
    }

    private static CSharpCompilation CreateCompilation(
        CSharpParseOptions parse,
        string source,
        bool includeContractReference)
    {
        System.ArgumentNullException.ThrowIfNull(parse);
        System.ArgumentNullException.ThrowIfNull(source);
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
