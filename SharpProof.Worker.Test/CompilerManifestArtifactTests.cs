using System.Text;
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
public sealed class CompilerManifestArtifactTests
{
    private const string SourceMarker =
        "sharp-proof-source-must-not-be-embedded";

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

        foreach (var reason in allowed)
        {
            var artifact = CreateUnsupportedLoopArtifact();
            artifact.Callables.Single().FailureReason = reason;
            Assert.That(
                CompilerManifestArtifactJson.DecodeCallables(artifact)
                    .Single().FailureReason,
                Is.EqualTo(reason),
                reason.ToString());
        }

        foreach (var reason in rejected)
        {
            var artifact = CreateUnsupportedLoopArtifact();
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

        Assert.DoesNotThrow((Action)(() =>
            CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact))));
    }

    [Test]
    [Platform("Linux")]
    public void CapturePreservesBackslashFilenameCharacters()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.BackslashPath." + Guid.NewGuid().ToString("N"));
        var projectDirectory = Path.Combine(root, "literal\\backslash");
        Directory.CreateDirectory(projectDirectory);
        try
        {
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
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void NullableSchemaShapesFailWithJsonException()
    {
        var previousSchema = CreateArtifact();
        previousSchema.SchemaVersion = 11;
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
                    (1, "a", 1, 2),
                    (1, "a", 2, 1),
                    (1, "b", 1, 1),
                    (2, "a", 1, 1)
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
        artifact.Compilation.AssemblyName = "x";
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation,
            artifact.CompilerDiagnostics);
        var initial = CompilerManifestArtifactJson.Serialize(artifact);
        var padding = CompilerManifestArtifactFile.MaximumBytes -
            Encoding.UTF8.GetByteCount(initial);
        Assert.That(padding, Is.GreaterThan(0));

        artifact.Compilation.AssemblyName += new string('x', padding);
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation,
            artifact.CompilerDiagnostics);
        var exact = CompilerManifestArtifactJson.Serialize(artifact);
        Assert.That(
            Encoding.UTF8.GetByteCount(exact),
            Is.EqualTo(CompilerManifestArtifactFile.MaximumBytes));

        artifact.Compilation.AssemblyName += "x";
        artifact.CompilationSha256 = CompilationFingerprint.ComputeSha256(
            artifact.Compilation,
            artifact.CompilerDiagnostics);
        Assert.Throws<JsonException>((Action)(() =>
            CompilerManifestArtifactJson.Serialize(artifact)));
    }

    [Test]
    public void MalformedLoweredCallableFailsDuringHydration()
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
        var roundTrip = CompilerManifestArtifactJson.Deserialize(
            CompilerManifestArtifactJson.Serialize(artifact));

        Assert.Throws<InvalidDataException>(
            (Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(roundTrip)));
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

        var resealed = CanonicalRoundTrip(artifact);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(resealed)));
    }

    [TestCase(64, false)]
    [TestCase(65, true)]
    public void ReachableBlockLimitIsExactDuringCanonicalHydration(
        int blockCount,
        bool malformed)
    {
        var artifact = CreateContractArtifact();
        ReplaceWithLinearBody(artifact.Callables[0].Graph!, blockCount);
        var resealed = CanonicalRoundTrip(artifact);

        if (malformed)
        {
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(resealed)));
        }
        else
        {
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(resealed)));
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

        var resealed = CanonicalRoundTrip(artifact);

        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(resealed)));
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
        var missing = CreateContractArtifact();
        var missingReturn = missing.Callables[0].Graph!.Blocks[0]
            .Instructions[^1];
        Assert.That(missingReturn.Kind, Is.EqualTo(IrInstructionKind.Return));
        missingReturn.A = -1;

        var wrongType = CreateContractArtifact();
        var wrongGraph = wrongType.Callables[0].Graph!;
        var wrongReturn = wrongGraph.Blocks[0]
            .Instructions[^1];
        wrongReturn.A = wrongGraph.Roots[0];

        var honest = CanonicalRoundTrip(CreateContractArtifact());

        using (Assert.EnterMultipleScope())
        {
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(
                    CanonicalRoundTrip(missing))));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(
                    CanonicalRoundTrip(wrongType))));
            Assert.DoesNotThrow((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(honest)));
        }
    }

    [Test]
    public void EnsuresRowsExactlyMatchManifestClaimIdentityAndEvidence()
    {
        const string source =
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
            var artifact = CreateContractArtifact(source);
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
            var artifact = CreateEffectArtifact();
            corrupt(artifact.Callables[0]);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
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
            Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(
            evidence.Reason,
            Is.EqualTo(
                WorkerClaimReason.CounterexampleNotReplayable));
        Assert.That(
            evidence.Certainty,
            Is.EqualTo(
                WorkerEffectEvidenceCertainty.Unavailable));
        Assert.That(evidence.Witness, Is.Null);
        Assert.That(evidence.Replay, Is.Null);
        Assert.That(
            CompilerManifestArtifactJson.DecodeCallables(artifact)
                .Single().EffectClaims.Single().Reason,
            Is.EqualTo(
                WorkerClaimReason.CounterexampleNotReplayable));
    }

    [Test]
    public void AllocationEffectReplayRoundTripsCompilerEvidence()
    {
        const string expression = "new object()";
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                [ZeroAllocations]
                internal static object Allocate() =>
                    new object();
            }
            """;
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
        AssertRejected(static (_, _) => 0);
        AssertRejected(static (treeLength, start) =>
            treeLength - start + 1);
        return;

        static void AssertRejected(
            Func<int, int, int> chooseLength)
        {
            var artifact = CreateContractArtifact(
                """
                using SharpProof.Attributes;
                internal static class Subject {
                    [ZeroAllocations]
                    internal static object Allocate() =>
                        new object();
                }
                """);
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
        const string source =
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
        var graph = artifact.Callables[0].Graph!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(graph.Entry, Is.Zero);
            Assert.That(graph.Blocks, Has.Length.GreaterThan(1));
        }
        graph.Entry = 1;
        Assert.Throws<InvalidDataException>((Action)(() =>
            CompilerManifestArtifactJson.DecodeCallables(artifact)));

        var json = CompilerManifestArtifactJson.Serialize(CreateContractArtifact(source));
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
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int[] Empty() {
                    Contract.Ensures(Contract.Result<int[]>() != null);
                    return System.Array.Empty<int>();
                }
            }
            """;
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
            var artifact = CreateContractArtifact(source);
            artifact.Callables[0].Body!.SpecCalls = descriptors(artifact.Callables[0].Body!.SpecCalls);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
        foreach (var identities in new Func<CompilerCallIdentityArtifact[], CompilerCallIdentityArtifact[]>[] {
                     static _ => [], static values => [values[0], values[0]]
                 })
        {
            var artifact = CreateContractArtifact(source);
            artifact.Callables[0].Body!.Calls = identities(artifact.Callables[0].Body!.Calls);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }

        var substituted = CreateContractArtifact(source);
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
        foreach (var corrupt in corruptions)
        {
            var artifact = CreateContractArtifact();
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
        foreach (var corrupt in corruptions)
        {
            var artifact = CreateContractArtifact(source);
            Assert.That(artifact.Callables[0].Body!.ParameterBindings, Has.Length.EqualTo(2));
            corrupt(artifact.Callables[0]);
            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(artifact)));
        }
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

        foreach (var corrupt in corruptions)
        {
            var artifact = CreateContractArtifact(source);
            Assert.That(
                artifact.Callables[0].Body!.SummaryCalls,
                Has.Length.EqualTo(1));
            corrupt(artifact.Callables[0]);
            var resealed = CompilerManifestArtifactJson.Deserialize(
                CompilerManifestArtifactJson.Serialize(artifact));

            Assert.Throws<InvalidDataException>((Action)(() =>
                CompilerManifestArtifactJson.DecodeCallables(resealed)));
        }
    }

    [Test]
    public void SameShapedMemberSubstitutionFailsClosed()
    {
        const string source =
            """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int[] Empty() {
                    Contract.Ensures(Contract.Result<int[]>() != null);
                    return System.Array.Empty<int>();
                }
            }
            """;
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
        string source = "internal sealed class Subject {}\n")
    {
        var compilation = CreateCompilation(
            parse ?? new CSharpParseOptions(LanguageVersion.CSharp12),
            source,
            includeContractReference: false);
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
    }

    private static CompilerManifestArtifact CreateContractArtifact(string? source = null)
    {
        var parse = new CSharpParseOptions(
            LanguageVersion.CSharp12);
        var compilation = CreateCompilation(
            parse,
            source ?? """
            using SharpProof.Attributes;
            internal static class Subject {
                internal static int Identity(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """,
            includeContractReference: true);
        var discovery = new ClaimManifestBuilder(
            compilation,
            WorkerFeatureSet.All,
            CancellationToken.None).Build();
        return CompilerManifestArtifactProducer.Create(
            compilation,
            TestContext.CurrentContext.WorkDirectory,
            "net8.0",
            WorkerFeatureSet.All,
            discovery,
            WorkerBudgets.DefaultMaximumExpressionDepth,
            CancellationToken.None);
    }

    private static CompilerManifestArtifact CreateEffectArtifact()
    {
        return CreateContractArtifact(
            """
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow]
                internal static int Identity(int value) => value;
            }
            """);
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

    private static CompilerManifestArtifact CanonicalRoundTrip(
        CompilerManifestArtifact artifact)
    {
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

    private static void AssertMalformedAdditionalFiles(
        params CompilerAdditionalFileSnapshot[] files)
    {
        var artifact = CreateArtifact();
        artifact.Compilation.AdditionalFiles = files;
        artifact.CompilationSha256 =
            CompilationFingerprint.ComputeSha256(artifact.Compilation, []);
        var json = JsonSerializer.Serialize(
                artifact,
                WorkerProtocolJson.Options) +
            "\n";

        Assert.Throws<JsonException>(
            (Action)(() =>
                CompilerManifestArtifactJson.Deserialize(json)));
    }

    private static CSharpCompilation CreateCompilation(
        CSharpParseOptions parse,
        string source,
        bool includeContractReference)
    {
        var paths = ((string)AppContext.GetData(
                "TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(path =>
                includeContractReference ||
                string.Equals(
                    path,
                    typeof(object).Assembly.Location,
                    StringComparison.OrdinalIgnoreCase))
            .Append(includeContractReference
                ? typeof(Contract).Assembly.Location
                : typeof(object).Assembly.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return CSharpCompilation.Create(
            "CompilerArtifactTest",
            [CSharpSyntaxTree.ParseText(
                source,
                parse,
                Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "Subject.cs"))],
            paths.Select(static path =>
                MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
    }
}
