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
    public void FailedCallablesCannotRetainExecutablePayloadAcrossWireBoundary()
    {
        var artifact = CreateContractArtifact();
        artifact.Callables.Single().FailureReason =
            WorkerClaimReason.UnsupportedBody;

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
            artifact.Callables.Single(item => item.CallableId.Contains(".B(", StringComparison.Ordinal)).Graph,
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
    public void HonestEffectAuthorityPreservesWorkerResultClassification()
    {
        var artifact = CreateContractArtifact(ZeroAllocationInlineSource);
        var target = CompilerManifestArtifactJson.DecodeCallables(artifact).Single();
        var result = EffectClaimResultAssembler.Assemble(
            target, target.EffectClaims.Single(),
            CallableEntryFeasibility.Feasible, CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(result.EffectWitness, Is.Not.Null);
        }
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

        var target = CompilerManifestArtifactJson.DecodeCallables(artifact)
            .Single();
        var hydrated = target.EffectClaims.Single();
        var result = EffectClaimResultAssembler.Assemble(
            target, hydrated, CallableEntryFeasibility.Feasible,
            CancellationToken.None);

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
        const string source =
            """
            using System;
            using System.Collections.Generic;
            using SharpProof.Attributes;
            internal static class Subject {
                [DoesNotThrow]
                internal static IEnumerable<int> Values() {
                    yield return 1;
                    throw new InvalidOperationException();
                }
            }
            """;
        CompilerManifestArtifact? artifact = null;
        try
        {
            artifact = CreateContractArtifact(source);
        }
        catch (JsonException)
        {
            // Unsupported bodies must still produce a valid, unavailable claim.
        }
        Assert.That(artifact, Is.Not.Null,
            "Unsupported effect evidence must remain a valid compiler artifact.");
        var evidence = artifact!.Callables.Single().EffectClaims.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(evidence.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(evidence.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedContract));
            Assert.That(evidence.Certainty, Is.EqualTo(WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(evidence.Witness, Is.Null);
            Assert.That(evidence.Replay, Is.Null);
        }
    }

    [Test]
    public void SupportedExplicitThrowIncludesReplayEvidence()
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
                @event?.Kind,
                Is.EqualTo(
                    CompilerEffectReplayEventKind
                        .ManagedObjectAllocation));
            Assert.That(@event?.Ordinal, Is.Zero);
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
        var result = EffectClaimResultAssembler.Assemble(
            target, evidence, CallableEntryFeasibility.Feasible,
            CancellationToken.None);

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
    public void SpecCallSetAndCompilerCallIdentityFailClosed()
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
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(substituted));
        Assert.Throws<InvalidDataException>(new Action(() => ArtifactValidator.Decode(bytes)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SpecCatalogWitnessAndMemoryEffectFailClosedAtWorkerBoundary(bool corruptMemoryEffect)
    {
        var artifact = CreateContractArtifact(EmptyArraySource);
        var descriptor = artifact.Callables.Single().Body!.SpecCalls.Single();
        if (corruptMemoryEffect)
        { descriptor.ConsumesMemoryHavoc = !descriptor.ConsumesMemoryHavoc; }
        else
        { descriptor.WitnessIdentifier = "unknown.spec.witness"; }
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.SerializeProducerValidated(artifact));
        Assert.Throws<InvalidDataException>(new Action(() => ArtifactValidator.Decode(bytes)));
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
    public void CurrentStateVariableAcceptsOnlyCanonicalMinusOneSentinel()
    {
        var valid = CreateContractArtifact();
        var parameter = valid.Callables.Single().Variables
            .Single(static item => item.Role == CompilerVariableRole.Parameter);
        Assert.That(parameter.CurrentStateVariable, Is.EqualTo(-1));

        var validJson = CompilerManifestArtifactJson.Serialize(valid);
        var validRoundTrip = CompilerManifestArtifactJson.Deserialize(validJson);
        var decoded = CompilerManifestArtifactJson.DecodeCallables(validRoundTrip).Single();
        Assert.That(decoded.Variables
            .Single(static item => item.Role == CompilerVariableRole.Parameter)
            .CurrentStateVariable, Is.Null);

        foreach (var invalidSentinel in new[] { -2, int.MinValue })
        {
            var resealed = CloneArtifact(valid);
            resealed.Callables.Single().Variables
                .Single(static item => item.Role == CompilerVariableRole.Parameter)
                .CurrentStateVariable = invalidSentinel;
            var resealedJson = CompilerManifestArtifactJson.SerializeValidated(resealed);

            Assert.Throws<JsonException>((Action)(() =>
                CompilerManifestArtifactJson.Deserialize(resealedJson)),
                $"currentStateVariable={invalidSentinel} must be rejected");
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
