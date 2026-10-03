using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Analyzer;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class CorpusGateTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void NativeContractCoverageUsesCompanionTreeIdentity(bool collidingPaths)
    {
        var options = new Microsoft.CodeAnalysis.CSharp.CSharpParseOptions(Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12);
        var implementation = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("""
            public static class Helper { public static int Target(int value) => value; }
            public static class Caller { public static int Root(int value) => Helper.Target(value); }
            """, options, "Implementation.cs");
        var companion = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText("""
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            [ContractFor(typeof(Helper))]
            public static class HelperContracts {
                public static int Target(int value) { Contract.Requires(value > 0); return value; }
            }
            """, options, collidingPaths ? "Implementation.cs" : "Companion.cs");
        var occupiedSuffix = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(
            "internal class Unused {}", options, "Implementation.cs#1");
        var compilation = AnalyzerGateHost.CreateCompilation("", "CompanionCoverage")
            .RemoveAllSyntaxTrees().AddSyntaxTrees(implementation, companion, occupiedSuffix);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var batch = SharpProof.CompilerArtifact.CompilerTotalCallableLowerer.PrepareShadowCallers(compilation,
            SharpProof.Worker.Protocol.WorkerFeatureSet.All,
            SharpProof.CompilerArtifact.CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), null,
            SharpProof.CompilerArtifact.CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
        Assert.That(batch.Gaps.All(static gap => gap.Reason == "UnsupportedOwnContracts"), Is.True);
        var body = batch.Callers.Single().Body;
        var detached = SharpProof.CompilerArtifact.CompilerTotalCallableArtifactCodec.DecodeShadowBody(body.CallableId,
            SharpProof.CompilerArtifact.CompilerTotalCallableArtifactCodec.Encode(body)!, CancellationToken.None);
        var match = CorpusGate.MatchNativeContractCalls(compilation, [detached.Body]);
        Assert.That(match.Expected, Is.EqualTo(1));
        Assert.That(match.Matched, Is.EqualTo(1));
        Assert.That(match.Failures, Is.Empty);
        var missing = CorpusGate.MatchNativeContractCalls(compilation,
            [detached.Body with { CallPreconditions = [] }]);
        Assert.That(missing.Expected, Is.EqualTo(1));
        Assert.That(missing.Matched, Is.Zero);
        Assert.That(missing.Failures, Has.Some.StartsWith("MissingNativeObligation:"));
        var row = detached.Body.CallPreconditions.Single();
        var factory = detached.Body.Program.Factory;
        var span = factory.GetOperationInfo(row.ClauseSite).SourceSpan!;
        var wrongSite = factory.CreateOperation("coverage-negative",
            new SharpProof.Ir.IrSourceSpan(span.Document, span.Start + 1, span.Length));
        var substituted = CorpusGate.MatchNativeContractCalls(compilation,
            [detached.Body with { CallPreconditions = [row with { ClauseSite = wrongSite }] }]);
        Assert.That(substituted.Expected, Is.EqualTo(1));
        Assert.That(substituted.Matched, Is.Zero);
        Assert.That(substituted.Failures, Has.Some.StartsWith("UnexpectedNativeObligation:"));
        Assert.That(substituted.Failures, Has.Some.StartsWith("MissingNativeObligation:"));
    }

    [Test]
    public async Task NativeShadowCallersQualifyEveryContractCorpusVariantWithOriginalReplay()
    {
        SharpProof.Host.ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var root = RepositoryLayout.FindRoot();
        var snapshotPath = Path.Combine(root, "SharpProof.Gates", "Corpus", "expected.canonical.snapshot");
        var snapshotBefore = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(snapshotPath)));
        var frozen = (await File.ReadAllLinesAsync(snapshotPath)).Where(static line => line.StartsWith('C') && line.Contains('|', StringComparison.Ordinal))
            .ToDictionary(static line => line.Split('|')[0], StringComparer.Ordinal);
        var cases = CorpusCatalog.CreateSyntheticCases().Where(static item => item.Mode == "contracts").ToArray();
        Assert.That(cases, Has.Length.EqualTo(100));
        Assert.That(cases.Select(static item => item.SeedId).Distinct().Count(), Is.EqualTo(10));
        Assert.That(frozen.Keys, Is.EquivalentTo(cases.Select(static item => item.Id)));
        foreach (var seed in cases.GroupBy(static item => item.SeedId, StringComparer.Ordinal))
        {
            Assert.That(seed.Select(static item => item.Variant), Is.EquivalentTo(Enum.GetValues<CorpusVariant>()), seed.Key);
        }
        var rows = new List<(string Seed, string Outcome, string Reason)>();
        foreach (var item in cases)
        {
            var compilation = AnalyzerGateHost.CreateCompilation(item.Source, "ShadowCorpus_" + item.SeedId);
            var census = CorpusGate.CensusContractCalls(compilation);
            Assert.Multiple(new Action(() =>
            {
                Assert.That(census.PublicOwners, Is.EqualTo(1), item.Id);
                Assert.That(census.PotentialCalls, Is.EqualTo(1), item.Id);
                Assert.That(census.BoundRequires, Is.EqualTo(1), item.Id);
                Assert.That(census.Complete, Is.True, item.Id);
                Assert.That(census.Unpublished, Is.True, item.Id);
                Assert.That(census.ManifestUnchanged, Is.True, item.Id);
                Assert.That(census.Failures, Is.Empty, item.Id);
            }));
            var originalManifest = System.Text.Json.JsonSerializer.Serialize(
                new SharpProof.CompilerArtifact.ClaimManifestBuilder(compilation).Build().Manifest);
            var batch = SharpProof.CompilerArtifact.CompilerTotalCallableLowerer.PrepareShadowCallers(compilation,
                SharpProof.Worker.Protocol.WorkerFeatureSet.All,
                SharpProof.CompilerArtifact.CompilerCompilationCapture.CaptureTrees(compilation, CancellationToken.None), null,
                SharpProof.CompilerArtifact.CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None);
            Assert.That(System.Text.Json.JsonSerializer.Serialize(
                new SharpProof.CompilerArtifact.ClaimManifestBuilder(compilation).Build().Manifest),
                Is.EqualTo(originalManifest), item.Id);
            Assert.That(batch.Gaps, Is.Empty, item.Id);
            Assert.That(batch.Callers, Has.Length.EqualTo(1), item.Id);
            var body = batch.Callers.Single().Body;
            Assert.That(body.Clauses, Is.Empty, item.Id);
            Assert.That(body.IsBodyAbstraction, Is.False, item.Id);
            Assert.That(body.ValidEffectClaimIds, Is.Empty, item.Id);
            Assert.That(body.ExceptionConstraints, Is.Empty, item.Id);
            Assert.That(body.CallPreconditions, Has.Length.EqualTo(1), item.Id);
            var encoded = SharpProof.CompilerArtifact.CompilerTotalCallableArtifactCodec.Encode(body)!;
            var detached = SharpProof.CompilerArtifact.CompilerTotalCallableArtifactCodec.DecodeShadowBody(
                body.CallableId, encoded, CancellationToken.None);
            Assert.That(detached.Body.Program.Factory, Is.Not.SameAs(body.Program.Factory), item.Id);
            var coverage = CorpusGate.MatchNativeContractCalls(compilation, [detached.Body]);
            Assert.That(coverage.Expected, Is.EqualTo(1), item.Id);
            Assert.That(coverage.Matched, Is.EqualTo(1), item.Id);
            Assert.That(coverage.Failures, Is.Empty, item.Id);
            var nativeClause = detached.Body.CallPreconditions.Single();
            var marker = detached.Body.Program.Blocks.SelectMany(static block => block.Instructions)
                .Single(instruction => instruction.Id == nativeClause.Instruction);
            var callSpan = detached.Body.Program.Factory.GetOperationInfo(marker.Operation).SourceSpan!;
            var clauseSpan = detached.Body.Program.Factory.GetOperationInfo(nativeClause.ClauseSite).SourceSpan!;
            var nativeKey = new ContractCoverageKey(body.CallableId, nativeClause.CalleeIdentity, nativeClause.ClauseOrdinal,
                new(callSpan.Document, callSpan.Start, callSpan.Length),
                new(clauseSpan.Document, clauseSpan.Start, clauseSpan.Length));
            Assert.That(coverage.Matched, Is.EqualTo(1), nativeKey.ToString());
            var candidate = SharpProof.Worker.PassiveCallableArtifactAdapter.EnrollShadow(detached);
            Assert.That(SharpProof.Worker.PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, item.Id + ":" + reason);
            using var solver = new SharpProof.Worker.PassiveCallableSolver(plan!);
            var result = await solver.VerifyCallPreconditionAsync(0);
            Assert.That(result.Reason, Is.EqualTo(SharpProof.Worker.Protocol.WorkerClaimReason.None), item.Id);
            var outcome = result.Outcome switch
            {
                SharpProof.Verify.ProvenOutcome => "Proven",
                SharpProof.Verify.RefutedOutcome => "Refuted",
                _ => "Unknown"
            };
            rows.Add((item.SeedId, outcome, result.Reason.ToString()));
            var expected = item.SeedId is "C01" or "C03" or "C09" ? "Proven" : "Refuted";
            Assert.That(outcome, Is.EqualTo(expected), item.Id + ":" + result.Reason);
            if (outcome == "Refuted")
            {
                var replay = plan!.ReplayCallPrecondition(0, result.EntryModel, CancellationToken.None);
                Assert.That(replay, Is.Not.Null, item.Id);
                Assert.That(result.CallPreconditionWitness, Is.EqualTo(replay), item.Id);
            }
            var legacy = await CorpusGate.ObserveCaseAsync(item, CancellationToken.None);
            Assert.That(legacy.ToCanonicalLine(), Is.EqualTo(frozen[item.Id]), item.Id + ": legacy changed");
        }
        Assert.That(rows.Count(static row => row.Outcome == "Proven"), Is.EqualTo(30));
        Assert.That(rows.Count(static row => row.Outcome == "Refuted"), Is.EqualTo(70));
        var snapshotAfter = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(snapshotPath)));
        Assert.That(snapshotAfter, Is.EqualTo(snapshotBefore));
    }

    private const string CorpusSnapshotHeader = "# SharpProof analyzer corpus snapshot schema 3\n# case-id|verdict|semantic-outcome|sorted-diagnostics\n# diagnostic=id@effective-severity@normalized-location@base64-invariant-message\n";

    [Test]
    public void OssImporterRejectsMitLicenseWithAppendedRestrictions()
    {
        var repositoryRoot = RepositoryLayout.FindRoot();
        var license = File.ReadAllText(Path.Combine(
            repositoryRoot, "SharpProof.Gates", "Corpus",
            "third-party", "aalhour-C-Sharp-Algorithms-LICENSE.txt"));

        Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusImporter.ValidateReviewedMitLicense(
                license + "\nAdditional restriction: no commercial use.\n")));
    }

    [TestCase("https://github.com/aalhour/C-Sharp-Algorithms.git", "https://github.com/aalhour/C-Sharp-Algorithms")]
    [TestCase("git@github.com:aalhour/C-Sharp-Algorithms.git", "https://github.com/aalhour/C-Sharp-Algorithms")]
    [TestCase("https://github.com/aalhour/C-Sharp-Algorithms.git-mirror", "https://github.com/aalhour/C-Sharp-Algorithms.git-mirror")]
    public void OssImporterRepositoryUrlNormalizationOnlyRemovesTerminalGitSuffix(
        string input,
        string expected)
    {
        Assert.That(
            OpenSourceCorpusImporter.NormalizeRepositoryUrl(input),
            Is.EqualTo(expected));
    }

    [Test]
    public void UnknownReasonRatchetRejectsStaleCeilings()
    {
        var ratchet = new CorpusUnknownReasonRatchet(
            0,
            0,
            1,
            new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["SP0002"] = 1,
                ["SP0047"] = 1
            }.ToImmutableDictionary(StringComparer.Ordinal));
        var actual = ImmutableArray.Create(
            new CorpusUnknownReasonCount("SP0002", 1));
        var failures = ImmutableArray.CreateBuilder<string>();

        CorpusGate.ValidateUnknownReasonRatchet(
            ratchet, actual, 1, 0, 0, failures);

        Assert.That(
            failures,
            Has.Some.Contains("SP0047"));
        Assert.That(failures, Has.Some.Contains("stale ratchet ceiling"));
    }

    [Test]
    public void UnknownReasonRatchetEnforcesSupportAndEveryUnknownCeiling()
    {
        var ratchet = new CorpusUnknownReasonRatchet(5, 3, 2,
            new Dictionary<string, int>(StringComparer.Ordinal) { ["known"] = 1 }
                .ToImmutableDictionary(StringComparer.Ordinal));
        var failures = ImmutableArray.CreateBuilder<string>();
        CorpusGate.ValidateUnknownReasonRatchet(ratchet,
            [new CorpusUnknownReasonCount("known", 2), new CorpusUnknownReasonCount("new", 1)],
            3, 4, 2, failures);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(failures, Has.Count.EqualTo(5));
            Assert.That(failures, Has.Some.Contains("minimum of 5 cases to 4"));
            Assert.That(failures, Has.Some.Contains("minimum of 3 methods to 2"));
            Assert.That(failures, Has.Some.Contains("maximum 2 to 3"));
            Assert.That(failures, Has.Some.Contains("'known' regressed"));
            Assert.That(failures, Has.Some.Contains("new unreviewed Unknown reason 'new'"));
        }
        failures.Clear();
        CorpusGate.ValidateUnknownReasonRatchet(ratchet,
            [new CorpusUnknownReasonCount("known", 1)], 2, 5, 3, failures);
        Assert.That(failures, Is.Empty);
    }

    [TestCase("schema", "Unsupported OSS corpus schema")]
    [TestCase("sources", "at least one upstream source")]
    [TestCase("files", "pinned upstream source files")]
    [TestCase("count", "methods;")]
    [TestCase("source-id", "refers to unknown source")]
    [TestCase("duplicate-file", "Duplicate OSS corpus source file")]
    [TestCase("content-hash", "source file hash does not match")]
    [TestCase("method-id", "contiguous and sorted")]
    [TestCase("method-file", "refers to missing source")]
    [TestCase("range", "invalid line range")]
    [TestCase("location", "Duplicate OSS corpus source location")]
    [TestCase("declaration", "declaration hash does not match")]
    [TestCase("name", "name does not match")]
    [TestCase("mode", "must run in effects mode")]
    [TestCase("support", "explicit support classification")]
    [TestCase("url", "invalid repository URL")]
    [TestCase("commit", "pin a full Git commit")]
    [TestCase("license", "unsupported license")]
    [TestCase("missing-license", "license file is missing")]
    [TestCase("license-hash", "license hash does not match")]
    public void OssCatalogRejectsIndividuallyCorruptedPinnedEvidence(string field, string message)
    {
        var repository = RepositoryLayout.FindRoot();
        var document = OpenSourceCorpusCatalog.Load(repository);
        using var temporary = new TempDirectory("SharpProof.Gates.Test-catalog-");
        var corpus = Path.Combine(temporary.FullName, "SharpProof.Gates", "Corpus");
        Directory.CreateDirectory(corpus);
        foreach (var source in document.Sources)
        {
            var destination = Path.Combine(corpus, source.LicenseFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(repository, "SharpProof.Gates", "Corpus", source.LicenseFile), destination);
        }
        var manifestPath = Path.Combine(corpus, "oss-methods.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(document));
        Assert.That(JsonSerializer.Serialize(OpenSourceCorpusCatalog.Load(temporary.FullName)), Is.EqualTo(JsonSerializer.Serialize(document)));
        var sourceEntry = document.Sources[0];
        var file = document.Files[0];
        var method = document.Methods[0];
        document = field switch
        {
            "schema" => document with { SchemaVersion = 0 },
            "sources" => document with { Sources = [] },
            "files" => document with { Files = [] },
            "count" => document with { Methods = [] },
            "source-id" => document with { Files = document.Files.SetItem(0, file with { SourceId = "missing" }) },
            "duplicate-file" => document with { Files = document.Files.Add(file) },
            "content-hash" => document with { Files = document.Files.SetItem(0, file with { ContentSha256 = new string('0', 64) }) },
            "method-id" => document with { Methods = document.Methods.SetItem(0, method with { Id = "OSS9999" }) },
            "method-file" => document with { Methods = document.Methods.SetItem(0, method with { Path = "missing.cs" }) },
            "range" => document with { Methods = document.Methods.SetItem(0, method with { StartLine = 0 }) },
            "location" => document with { Methods = document.Methods.SetItem(1, method with { Id = document.Methods[1].Id }) },
            "declaration" => document with { Methods = document.Methods.SetItem(0, method with { DeclarationSha256 = new string('0', 64) }) },
            "name" => document with { Methods = document.Methods.SetItem(0, method with { MethodName = "WrongName" }) },
            "mode" => document with { Methods = document.Methods.SetItem(0, method with { Mode = "contracts" }) },
            "support" => document with { Methods = document.Methods.SetItem(0, method with { Support = (CorpusSupport)int.MaxValue }) },
            "url" => document with { Sources = document.Sources.SetItem(0, sourceEntry with { Repository = "http://example.com/source" }) },
            "commit" => document with { Sources = document.Sources.SetItem(0, sourceEntry with { Commit = "short" }) },
            "license" => document with { Sources = document.Sources.SetItem(0, sourceEntry with { LicenseSpdx = "unreviewed" }) },
            "missing-license" => document with { Sources = document.Sources.SetItem(0, sourceEntry with { LicenseFile = "missing.txt" }) },
            "license-hash" => document with { Sources = document.Sources.SetItem(0, sourceEntry with { LicenseSha256 = new string('0', 64) }) },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(document));
        Assert.That(Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusCatalog.Load(temporary.FullName)))!.Message, Does.Contain(message));
    }

    [Test]
    public void CorpusFileCountIncludesSourceIdentity()
    {
        var methods = new[]
        {
            new OpenSourceCorpusMethod("OSS0001", "one", "shared.cs", 1, 1,
                "hash-one", "A", "effects", CorpusVerdict.Proven, CorpusSupport.Supported),
            new OpenSourceCorpusMethod("OSS0002", "two", "shared.cs", 1, 1,
                "hash-two", "B", "effects", CorpusVerdict.Proven, CorpusSupport.Supported)
        };

        Assert.That(OpenSourceCorpusCatalog.CountSourceFiles(methods), Is.EqualTo(2));
    }

    [Test]
    public void CorpusSourceIdsRejectDuplicatesDeterministically()
    {
        var source = new OpenSourceCorpusSource(
            "shared", "https://example.invalid", new string('a', 40),
            "MIT", "LICENSE", new string('b', 64));

        var exception = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusCatalog.ValidateSourceIds([source, source])));

        Assert.That(exception!.Message, Is.EqualTo(
            "Duplicate OSS corpus source ID: shared."));
    }

    [Test]
    public void CorpusSourceIdsRejectEmptyValuesDeterministically()
    {
        var source = new OpenSourceCorpusSource(
            " ", "https://example.invalid", new string('a', 40),
            "MIT", "LICENSE", new string('b', 64));

        var exception = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusCatalog.ValidateSourceIds([source])));

        Assert.That(exception!.Message, Is.EqualTo(
            "OSS corpus source IDs must not be empty."));
    }

    [Test]
    public void CorpusContainmentRejectsLexicalEscapes()
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var root = temporary.FullName;
        var exception = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusCatalog.EnsureContained(
                root,
                Path.Combine(root, "..", "outside.txt"))));

        Assert.That(
            exception!.Message,
            Does.Contain("escaped its directory"));
    }

    [Test]
    [Platform("Linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void CorpusContainmentRejectsSymlinkTargetsOutsideRoot()
    {
        using var outsideTemporary = new TempDirectory("SharpProof.Gates.Test-outside-");
        using var rootTemporary = new TempDirectory("SharpProof.Gates.Test-root-");
        var root = rootTemporary.FullName;
        var outside = outsideTemporary.FullName;
        var target = Path.Combine(outside, "license.txt");
        var link = Path.Combine(root, "license.txt");
        File.WriteAllText(target, "outside\n");
        File.CreateSymbolicLink(link, target);

        var exception = Assert.Throws<InvalidDataException>((Action)(() =>
            OpenSourceCorpusCatalog.EnsureContained(root, link)));

        Assert.That(
            exception!.Message,
            Does.Contain("follows a link outside its directory"));
    }

    [Test]
    public void UnassignedCorpusDiagnosticFailsTheGate()
    {
        var descriptor = new DiagnosticDescriptor(
            "SPTEST",
            "Test diagnostic",
            "Test diagnostic",
            "Test",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
        var diagnostic = Diagnostic.Create(descriptor, Location.None);

        Assert.That(
            (Action)(() =>
                OpenSourceCorpusRunner.RequireCompleteDiagnosticAssignment(
                    [diagnostic],
                    [0])),
            Throws.TypeOf<InvalidDataException>());
        Assert.DoesNotThrow((Action)(() =>
            OpenSourceCorpusRunner.RequireCompleteDiagnosticAssignment(
                [diagnostic],
                [1])));
        Assert.That(
            (Action)(() =>
                OpenSourceCorpusRunner.RequireCompleteDiagnosticAssignment(
                    [diagnostic],
                    [2])),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task CorpusBatchRollsBackACommitFailure()
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var root = temporary.FullName;
        var first = Path.Combine(root, "first.txt");
        var second = Path.Combine(root, "second.txt");
        await File.WriteAllTextAsync(first, "old-first\n");
        await File.WriteAllTextAsync(second, "old-second\n");
        Func<Task> write = () => CorpusFileTransaction.WriteAllAsync(
            root,
            [
                new CorpusFileUpdate(first, "new-first\n"),
                new CorpusFileUpdate(second, "new-second\n")
            ],
            CancellationToken.None,
            index =>
            {
                if (index == 1)
                {
                    throw new IOException("injected failure");
                }
            });
        Assert.ThrowsAsync<IOException>(write);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await File.ReadAllTextAsync(first),
                Is.EqualTo("old-first\n"));
            Assert.That(
                await File.ReadAllTextAsync(second),
                Is.EqualTo("old-second\n"));
            Assert.That(
                File.Exists(Path.Combine(
                    root,
                    ".sharpproof-corpus-transaction.json")),
                Is.False);
        }
    }

    [Test]
    public async Task CorpusCatalogRecoveryRollsBackAnInterruptedBatch()
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var root = temporary.FullName;
        var first = Path.Combine(root, "first.txt");
        var second = Path.Combine(root, "second.txt");
        var firstStage = Path.Combine(root, "first.new");
        var secondStage = Path.Combine(root, "second.new");
        var firstBackup = Path.Combine(root, "first.old");
        var secondBackup = Path.Combine(root, "second.old");
        var marker = Path.Combine(
            root,
            ".sharpproof-corpus-transaction.json");
        await File.WriteAllTextAsync(first, "new-first\n");
        await File.WriteAllTextAsync(second, "old-second\n");
        await File.WriteAllTextAsync(secondStage, "new-second\n");
        await File.WriteAllTextAsync(firstBackup, "old-first\n");
        await File.WriteAllTextAsync(secondBackup, "old-second\n");
        await File.WriteAllTextAsync(
            marker,
            JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                Entries = new[]
                {
                    new
                    {
                        DestinationPath = first,
                        StagedPath = firstStage,
                        BackupPath = firstBackup,
                        DestinationExisted = true
                    },
                    new
                    {
                        DestinationPath = second,
                        StagedPath = secondStage,
                        BackupPath = secondBackup,
                        DestinationExisted = true
                    }
                }
            }));
        CorpusFileTransaction.Recover(root);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                await File.ReadAllTextAsync(first),
                Is.EqualTo("old-first\n"));
            Assert.That(
                await File.ReadAllTextAsync(second),
                Is.EqualTo("old-second\n"));
            Assert.That(File.Exists(marker), Is.False);
            Assert.That(File.Exists(secondStage), Is.False);
            Assert.That(File.Exists(firstBackup), Is.False);
            Assert.That(File.Exists(secondBackup), Is.False);
        }
    }

    [TestCase("destination")]
    [TestCase("staged")]
    [TestCase("backup")]
    [TestCase("symlink")]
    [TestCase("alias")]
    [TestCase("null-entry")]
    [TestCase("null-path")]
    [TestCase("missing-backup")]
    [TestCase("missing-existed")]
    public void CorpusRecoveryPreflightsEveryPathBeforeMutation(string corruption)
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var root = Path.Combine(temporary.FullName, "transaction");
        Directory.CreateDirectory(root);
        var outside = Path.Combine(temporary.FullName, "outside.txt");
        File.WriteAllText(outside, "outside");
        var destination = Path.Combine(root, "first.txt");
        var backup = Path.Combine(root, "first.old");
        var staged = Path.Combine(root, "first.new");
        File.WriteAllText(destination, "published");
        File.WriteAllText(backup, "original");
        File.WriteAllText(staged, "staged");
        var second = new Dictionary<string, object?>
        {
            ["DestinationPath"] = Path.Combine(root, "second.txt"),
            ["StagedPath"] = Path.Combine(root, "second.new"),
            ["BackupPath"] = Path.Combine(root, "second.old"),
            ["DestinationExisted"] = false
        };
        switch (corruption)
        {
            case "destination":
                second["DestinationPath"] = outside;
                break;
            case "staged":
                second["StagedPath"] = outside;
                break;
            case "backup":
                second["BackupPath"] = outside;
                break;
            case "null-path":
                second["StagedPath"] = null;
                break;
            case "missing-backup":
                second["DestinationExisted"] = true;
                break;
            case "missing-existed":
                second.Remove("DestinationExisted");
                break;
            case "alias":
                second["StagedPath"] = Path.Combine(root, ".", "first.txt");
                break;
            case "symlink":
                var link = Path.Combine(root, "link");
                Directory.CreateSymbolicLink(link, temporary.FullName);
                second["StagedPath"] = Path.Combine(link, "outside.txt");
                break;
        }
        var marker = Path.Combine(root, ".sharpproof-corpus-transaction.json");
        var json = JsonSerializer.Serialize(new
        {
            SchemaVersion = 1,
            Entries = new object?[]
            {
                new
                {
                    DestinationPath = destination,
                    StagedPath = staged,
                    BackupPath = backup,
                    DestinationExisted = true
                },
                corruption == "null-entry" ? null : second
            }
        });
        File.WriteAllText(marker, json);
        if (corruption == "missing-existed")
        {
            Assert.Throws<JsonException>((Action)(() => CorpusFileTransaction.Recover(root)));
        }
        else
        {
            Assert.Throws<InvalidDataException>((Action)(() => CorpusFileTransaction.Recover(root)));
        }
        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.ReadAllText(outside), Is.EqualTo("outside"));
            Assert.That(File.ReadAllText(destination), Is.EqualTo("published"));
            Assert.That(File.ReadAllText(backup), Is.EqualTo("original"));
            Assert.That(File.ReadAllText(staged), Is.EqualTo("staged"));
            Assert.That(File.ReadAllText(marker), Is.EqualTo(json));
        }
    }

    [Test]
    public void CorpusStagingFailureCleansTheCurrentEntryBackup()
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var destination = Path.Combine(temporary.FullName, "existing.txt");
        File.WriteAllText(destination, "original");
        Func<Task> write = () => CorpusFileTransaction.WriteAllAsync(
            temporary.FullName, [new CorpusFileUpdate(destination, null!)], CancellationToken.None);
        Assert.ThrowsAsync<ArgumentNullException>(write);
        Assert.That(File.ReadAllText(destination), Is.EqualTo("original"));
        Assert.That(Directory.GetFiles(temporary.FullName), Is.EqualTo(new[] { destination }));
    }

    [Test]
    [Platform("Linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task CanceledGitReadTerminatesTheChildProcess()
    {
        using var temporary = new TempDirectory("SharpProof.Gates.Test-");
        var root = temporary.FullName;
        var executable = Path.Combine(root, "git-probe.sh");
        var pidPath = Path.Combine(root, "child.pid");
        await File.WriteAllTextAsync(
            executable,
            "#!/bin/sh\nprintf '%s\\n' \"$$\" > child.pid\n" +
            "while :; do sleep 1; done\n");
        File.SetUnixFileMode(
            executable,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute);
        var processId = -1;
        try
        {
            using var cancellation = new CancellationTokenSource();
            var read = OpenSourceCorpusImporter.ReadGitAsync(
                root,
                ["status", "--porcelain"],
                cancellation.Token,
                executable);
            for (var attempt = 0;
                 attempt < 100 && !File.Exists(pidPath);
                 attempt++)
            {
                await Task.Delay(10);
            }
            Assert.That(File.Exists(pidPath), Is.True);
            processId = int.Parse(
                await File.ReadAllTextAsync(pidPath),
                System.Globalization.CultureInfo.InvariantCulture);

            await cancellation.CancelAsync();
            var canceled = false;
            try
            {
                _ = await read;
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            for (var attempt = 0;
                 attempt < 100 && ProcessExists(processId);
                 attempt++)
            {
                await Task.Delay(10);
            }
            using (Assert.EnterMultipleScope())
            {
                Assert.That(canceled, Is.True);
                Assert.That(ProcessExists(processId), Is.False);
            }
        }
        finally
        {
            if (processId > 0 && ProcessExists(processId))
            {
                using var process = System.Diagnostics.Process.GetProcessById(
                    processId);
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    [Test]
    public void CorpusSnapshotFormatRequiresExactSchemaThreeBytes()
    {
        const string data = "case|Proven|Proven|";
        var canonical = Encoding.UTF8.GetBytes(CorpusSnapshotHeader + data + "\n");
        string[]? parsed = null;
        try
        {
            parsed = CorpusSnapshotFormat.Parse(canonical);
        }
        catch (InvalidDataException)
        {
            Assert.Fail("The canonical schema-three snapshot must be accepted.");
        }
        Assert.That(parsed, Is.EqualTo(new[] { data }));
        Assert.That(CorpusSnapshotFormat.Render(new[] { data }), Is.EqualTo(CorpusSnapshotHeader + data + "\n"));
        var invalid = new[]
        {
            Encoding.UTF8.GetBytes(data + "\n"),
            Encoding.UTF8.GetBytes(CorpusSnapshotHeader.Split('\n')[0] + "\n" + data + "\n"),
            Encoding.UTF8.GetBytes(CorpusSnapshotHeader + CorpusSnapshotHeader + data + "\n"),
            Encoding.UTF8.GetBytes((CorpusSnapshotHeader + data + "\n").Replace("schema 3", "schema 2", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes((CorpusSnapshotHeader + data + "\n").Replace("schema 3", "schema 999", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes((CorpusSnapshotHeader + data + "\n").Replace("SharpProof", "sharpproof", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes((CorpusSnapshotHeader + data + "\n").Replace("schema 3", "schema  3", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes("# case-id|verdict|semantic-outcome|sorted-diagnostics\n# SharpProof analyzer corpus snapshot schema 3\n# diagnostic=id@effective-severity@normalized-location@base64-invariant-message\n" + data + "\n"),
            Encoding.UTF8.GetBytes(CorpusSnapshotHeader + "# extra\n" + data + "\n"),
            Encoding.UTF8.GetBytes(CorpusSnapshotHeader + "\n" + data + "\n"),
            Encoding.UTF8.GetBytes((CorpusSnapshotHeader + data + "\n").Replace("\n", "\r\n", StringComparison.Ordinal)),
            Encoding.UTF8.GetBytes(CorpusSnapshotHeader + data),
            Encoding.UTF8.GetBytes(CorpusSnapshotHeader + data + "\n\n"),
            new byte[] { 0xEF, 0xBB, 0xBF }.Concat(canonical).ToArray(),
            canonical[..^1].Concat(new byte[] { 0xFF, (byte)'\n' }).ToArray()
        };
        foreach (var bytes in invalid)
        {
            Assert.Throws<InvalidDataException>((Action)(() => CorpusSnapshotFormat.Parse(bytes)));
        }
    }

    [Test]
    public void CorpusSnapshotFormatRequiresCanonicalRowOrdering()
    {
        const string first = "a|Proven|Proven|";
        const string second = "b|Proven|Proven|";

        Assert.That(
            CorpusSnapshotFormat.Parse(Encoding.UTF8.GetBytes(
                CorpusSnapshotHeader + first + "\n" + second + "\n")),
            Is.EqualTo(new[] { first, second }));
        Assert.Throws<InvalidDataException>((Action)(() =>
            CorpusSnapshotFormat.Parse(Encoding.UTF8.GetBytes(
                CorpusSnapshotHeader + second + "\n" + first + "\n"))));
        Assert.Throws<InvalidDataException>((Action)(() =>
            CorpusSnapshotFormat.Render([second, first])));
    }

    [Test]
    public void CorpusSnapshotFormatRequiresCanonicalEnumNames()
    {
        static byte[] Snapshot(string header, string data)
        {
            return Encoding.UTF8.GetBytes(header + data + "\n");
        }
        static void AssertAccepted(string header, string data)
        {
            Assert.That(
                CorpusSnapshotFormat.Render([data]),
                Is.EqualTo(header + data + "\n"));
            Assert.That(
                CorpusSnapshotFormat.Parse(Snapshot(header, data)),
                Is.EqualTo(new[] { data }));
        }

        foreach (var verdict in new[]
                 {
                     "Proven", "Refuted", "Unknown", "SilentUnknown"
                 })
        {
            AssertAccepted(CorpusSnapshotHeader, $"case|{verdict}|Proven|");
        }
        foreach (var semanticOutcome in new[]
                 {
                     "NotApplicable", "Proven", "Suppressed", "Abstained",
                     "Unknown", "Refuted"
                 })
        {
            AssertAccepted(CorpusSnapshotHeader, $"case|Proven|{semanticOutcome}|");
        }

        foreach (var noncanonical in new[]
                 {
                     "case|0|Proven|",
                     "case| Proven|Proven|",
                     "case|Proven |Proven|",
                     "case|Proven|1|",
                     "case|Proven| Proven|",
                     "case|Proven|Proven |"
                 })
        {
            Assert.Throws<InvalidDataException>((Action)(() =>
                CorpusSnapshotFormat.Parse(Snapshot(CorpusSnapshotHeader, noncanonical))));
            Assert.Throws<InvalidDataException>((Action)(() =>
                CorpusSnapshotFormat.Render([noncanonical])));
        }
    }

    private static bool ProcessExists(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(
                processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    [Test]
    public void GeneratorHasDocumentedMetamorphicCoverage()
    {
        var cases = CorpusCatalog.CreateCases();
        var synthetic = cases.Where(static item =>
            item.Origin == CorpusOrigin.SyntheticMetamorphic).ToArray();
        var openSource = cases.Where(static item =>
            item.Origin == CorpusOrigin.OpenSource).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(synthetic, Has.Length.EqualTo(262));
            Assert.That(
                synthetic.Select(static item => item.SeedId).Distinct().Count(),
                Is.EqualTo(28));
            Assert.That(
                synthetic.Select(static item => item.Variant).Distinct(),
                Is.EquivalentTo(Enum.GetValues<CorpusVariant>()));
            Assert.That(
                synthetic.Select(static item => item.Support),
                Has.None.EqualTo(CorpusSupport.Unspecified));
            Assert.That(
                openSource.Length,
                Is.InRange(
                    OpenSourceCorpusCatalog.MinimumMethodCount,
                    OpenSourceCorpusCatalog.MaximumMethodCount));
            Assert.That(
                openSource.Select(static item => item.ProvenanceId)
                    .Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(openSource.Length));
            Assert.That(
                cases.Select(static item => item.Id).Distinct().Count(),
                Is.EqualTo(cases.Length));
        }
    }

    [Test]
    public void OpenSourceManifestHasPinnedLicensedProvenance()
    {
        var root = RepositoryLayout.FindRoot();
        var document = OpenSourceCorpusCatalog.Load(root);
        var selectedFileCount = document.Methods
            .Select(static method => method.Path)
            .Distinct(StringComparer.Ordinal)
            .Count();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(document.SchemaVersion, Is.EqualTo(2));
            Assert.That(document.Sources, Has.Length.EqualTo(1));
            Assert.That(document.Sources[0].Repository, Is.EqualTo(
                "https://github.com/aalhour/C-Sharp-Algorithms"));
            Assert.That(document.Sources[0].Commit, Has.Length.EqualTo(40));
            Assert.That(document.Sources[0].LicenseSpdx, Is.EqualTo("MIT"));
            Assert.That(document.Methods, Has.Length.EqualTo(200));
            Assert.That(
                document.Methods.Count(static method =>
                    method.Support == CorpusSupport.Supported),
                Is.EqualTo(13));
            Assert.That(
                document.Methods.Select(static method => method.Support),
                Has.None.EqualTo(CorpusSupport.Unspecified));
            Assert.That(
                selectedFileCount,
                Is.GreaterThanOrEqualTo(
                    OpenSourceCorpusCatalog.MinimumSourceFileCount));
            Assert.That(
                document.Methods.Select(static method =>
                    method.DeclarationSha256).Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(document.Methods.Length));
        }
    }

    [Test]
    [Category("Corpus")]
    public async Task AnalyzerMatchesCanonicalCorpusAndReplayModes()
    {
        var root = RepositoryLayout.FindRoot();

        var result = await CorpusGate.RunAsync(root);

        Assert.That(
            result.Failures,
            Is.Empty,
            string.Join(Environment.NewLine, result.Failures));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Passed, Is.True);
            Assert.That(result.CaseCount, Is.EqualTo(462));
            Assert.That(result.BaseCaseCount, Is.EqualTo(228));
            Assert.That(result.OpenSourceMethodCount, Is.EqualTo(200));
            Assert.That(result.SupportedOpenSourceMethodCount, Is.EqualTo(13));
            Assert.That(result.OpenSourceFileCount, Is.EqualTo(87));
            Assert.That(result.SyntheticSeedCount, Is.EqualTo(28));
            Assert.That(result.SupportedCaseCount, Is.EqualTo(239));
            Assert.That(
                result.IntentionallyUnsupportedCaseCount,
                Is.EqualTo(223));
            Assert.That(result.SupportedUnknownCount, Is.Zero);
            Assert.That(result.UnknownCount, Is.EqualTo(222));
            Assert.That(result.SilentUnknownCount, Is.EqualTo(1));
            Assert.That(result.TotalUnknownCount, Is.EqualTo(223));
            Assert.That(
                result.UnknownReasons
                    .ToDictionary(
                        static item => item.Reason,
                        static item => item.Count),
                Is.EquivalentTo(
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["SP0002"] = 34,
                        ["SP0016"] = 18,
                        ["SP0045"] = 9,
                        ["SP0046"] = 9,
                        ["SP0047"] = 152,
                        ["silent-unclassified"] = 1
                    }));
            Assert.That(
                result.UnknownRate,
                Is.EqualTo(result.UnknownCount / (double)result.CaseCount));
            Assert.That(
                result.SilentUnknownRate,
                Is.EqualTo(
                    result.SilentUnknownCount / (double)result.CaseCount));
            Assert.That(
                result.TotalUnknownRate,
                Is.EqualTo(
                    result.TotalUnknownCount / (double)result.CaseCount));
            Assert.That(result.CacheReplayCount, Is.GreaterThan(0));
            Assert.That(result.ConcurrentReplayCount, Is.GreaterThan(0));
        }
    }

    [Test]
    public void SupportedUnknownFailsIndependentlyOfExpectedVerdict()
    {
        var item = new CorpusCase(
            "explicit-supported",
            "explicit-supported",
            CorpusVariant.Baseline,
            "effects",
            CorpusVerdict.Unknown,
            CorpusSupport.Supported,
            "public static int Value() => 1;");
        var failures = CorpusGate.ValidateSupportedOutcomes(
            [item],
            [(item.Id, CorpusVerdict.Unknown)]);

        Assert.That(
            failures,
            Is.EqualTo([
                "1 supported corpus cases produced Unknown; supported cases " +
                "must have an accountable Proven or Refuted result."
            ]));
    }

    [Test]
    public void AlphaRenameDoesNotEmitDuplicateEffectSources()
    {
        var cases = CorpusCatalog.CreateSyntheticCases();
        Assert.That(
            cases.Any(item => item.SeedId.StartsWith('E') &&
                item.Variant == CorpusVariant.AlphaRenameContractFormals),
            Is.False);
        Assert.That(
            cases.Any(item => item.SeedId == "C07" &&
                item.Variant == CorpusVariant.AlphaRenameContractFormals),
            Is.True);
    }

    [Test]
    public async Task MetamorphicVariantsMustRetainSeedOutcomeAndDiagnosticClasses()
    {
        var catalog = CorpusCatalog.CreateSyntheticCases();
        var baseline = catalog.Single(static item =>
            item.Id == "C02.baseline") with
        {
            Id = "seed.baseline",
            SeedId = "seed"
        };
        var renamed = catalog.Single(static item =>
            item.Id == "C02.rename") with
        {
            Id = "seed.rename",
            SeedId = "seed"
        };
        var temporary = catalog.Single(static item =>
            item.Id == "C01.temporary") with
        {
            Id = "seed.temporary",
            SeedId = "seed"
        };
        var trivia = catalog.Single(static item =>
            item.Id == "E02.trivia") with
        {
            Id = "seed.trivia",
            SeedId = "seed"
        };
        var cases = new[] { baseline, renamed, temporary, trivia };
        var observations = await Task.WhenAll(cases.Select(item =>
            CorpusGate.ObserveCaseAsync(item, CancellationToken.None)));
        var failures = CorpusGate.ValidateMetamorphicConsistency(
            [.. cases],
            [.. observations]);

        Assert.That(
            failures.ToArray(),
            Is.EqualTo([
                "Metamorphic variant seed.trivia changed semantic outcome " +
                "from Refuted to Unknown relative to seed.baseline.",
                "Metamorphic variant seed.trivia changed diagnostic classes " +
                "from [SP0027@Warning] to [SP0002@Warning] relative to " +
                "seed.baseline.",
                "Metamorphic variant seed.temporary changed semantic outcome " +
                "from Refuted to Unknown relative to seed.baseline.",
                "Metamorphic variant seed.temporary changed diagnostic classes " +
                "from [SP0027@Warning] to [] relative to seed.baseline."
            ]));
    }

    [Test]
    public void SnapshotCapturesSemanticOutcomeAndCanonicalDiagnostics()
    {
        var root = RepositoryLayout.FindRoot();
        var lines = File.ReadAllLines(
            Path.Combine(
                root,
                "SharpProof.Gates",
                "Corpus",
                "expected.canonical.snapshot"));
        var refuted = lines.Single(static line =>
            line.StartsWith("C02.baseline|", StringComparison.Ordinal));
        var refutedParts = refuted.Split('|');
        var diagnosticParts = refutedParts[3].Split('@');
        var message = Encoding.UTF8.GetString(
            Convert.FromBase64String(diagnosticParts[3]));
        var silentUnknown = lines.Single(static line =>
            line.StartsWith("OSS0139.baseline|", StringComparison.Ordinal))
            .Split('|');
        var openSource = lines.Single(static line =>
            line.StartsWith("OSS0002.baseline|", StringComparison.Ordinal))
            .Split('|');

        using (Assert.EnterMultipleScope())
        {
            Assert.That(refutedParts, Has.Length.EqualTo(4));
            Assert.That(refutedParts[1], Is.EqualTo("Refuted"));
            Assert.That(refutedParts[2], Is.EqualTo("Refuted"));
            Assert.That(diagnosticParts, Has.Length.EqualTo(4));
            Assert.That(diagnosticParts[0], Is.EqualTo("SP0027"));
            Assert.That(diagnosticParts[1], Is.EqualTo("Warning"));
            Assert.That(
                diagnosticParts[2],
                Does.StartWith("input.cs:"));
            Assert.That(
                message,
                Is.EqualTo(
                    "Call to 'Positive' violates precondition 'value > 0'"));
            Assert.That(silentUnknown[1], Is.EqualTo("SilentUnknown"));
            Assert.That(silentUnknown[2], Is.EqualTo("Unknown"));
            Assert.That(silentUnknown[3], Is.Empty);
            Assert.That(openSource, Has.Length.EqualTo(4));
            Assert.That(openSource[1], Is.EqualTo("Unknown"));
            Assert.That(openSource[2], Is.EqualTo("Abstained"));
            Assert.That(openSource[3], Does.StartWith("SP0047@Warning@"));
        }
    }
}


[TestFixture]
public sealed class IndependentPinnedRequiresCensusTests
{
    private sealed record Row(string Owner, string Callee, int ClauseOrdinal, int CallStart, int CallLength,
        int ClauseStart, int ClauseLength);

    private static Row[] Census(Microsoft.CodeAnalysis.CSharp.CSharpCompilation compilation, out int declarations, out int declaredRequires)
    {
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        var api = compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract")!;
        Assert.That(api, Is.Not.Null);
        Assert.That(api.Locations.Any(static location => location.IsInSource), Is.False);
        Assert.That(api.ContainingAssembly.Identity.Name, Is.EqualTo(typeof(SharpProof.Attributes.Contract).Assembly.GetName().Name));
        var clauses = new Dictionary<IMethodSymbol, List<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>>(SymbolEqualityComparer.Default);
        var calls = new List<(IMethodSymbol Owner, IMethodSymbol Target, Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax Syntax)>();
        declarations = 0;
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            declarations += root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>().Count();
            foreach (var invocation in root.DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>())
            {
                if (Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetSymbolInfo(model, invocation).Symbol is not IMethodSymbol target)
                { continue; }
                if (model.GetEnclosingSymbol(invocation.SpanStart) is not IMethodSymbol owner)
                { continue; }
                if (SymbolEqualityComparer.Default.Equals(target.ContainingType, api))
                {
                    if (target.Name == "Requires")
                    {
                        Assert.That(target.IsStatic && target.ReturnsVoid && target.Arity == 0 && target.Parameters.Length == 1 &&
                            target.Parameters[0].Type.SpecialType == SpecialType.System_Boolean, Is.True);
                        if (!clauses.TryGetValue(owner.OriginalDefinition, out var owned))
                        { clauses.Add(owner.OriginalDefinition, owned = []); }
                        owned.Add(invocation);
                    }
                    continue;
                }
                calls.Add((owner, target.OriginalDefinition, invocation));
            }
        }
        declaredRequires = clauses.Values.Sum(static owned => owned.Count);
        var rows = new List<Row>();
        foreach (var call in calls)
        {
            if (!clauses.TryGetValue(call.Target, out var required))
            { continue; }
            var ordinal = 0;
            foreach (var clause in required.OrderBy(static clause => clause.SpanStart))
            {
                rows.Add(new(call.Owner.GetDocumentationCommentId()!, call.Target.GetDocumentationCommentId()!, ordinal++,
                    call.Syntax.SpanStart, call.Syntax.Span.Length, clause.SpanStart, clause.Span.Length));
            }
        }
        return [.. rows];
    }

    [Test]
    public async Task AllPinnedSourcesHaveAnIndependentDirectRequiresCensus()
    {
        var root = RepositoryLayout.FindRoot();
        var synthetic = CorpusCatalog.CreateSyntheticCases();
        Assert.That(synthetic, Has.Length.EqualTo(262));
        var declarations = 0;
        var obligations = 0;
        var contractIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in synthetic)
        {
            var compilation = AnalyzerGateHost.CreateCompilation(item.Source, "Independent_" + item.SeedId);
            var rows = Census(compilation, out var count, out var clauseDeclarations);
            declarations += count;
            Assert.That(clauseDeclarations, Is.EqualTo(item.Mode == "contracts" ? 1 : 0), item.Id);
            Assert.That(rows, Has.Length.EqualTo(item.Mode == "contracts" ? 1 : 0), item.Id);
            if (item.Mode != "contracts")
            { continue; }
            Assert.That(contractIds.Add(item.Id), Is.True);
            obligations += rows.Length;
            var row = rows.Single();
            Assert.That(row.ClauseOrdinal, Is.Zero, item.Id);
            var tree = compilation.SyntaxTrees.Single();
            var model = compilation.GetSemanticModel(tree);
            var syntax = (await tree.GetRootAsync()).DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax>()
                .Single(invocation => invocation.SpanStart == row.CallStart && invocation.Span.Length == row.CallLength);
            var owner = (IMethodSymbol)model.GetEnclosingSymbol(syntax.SpanStart)!;
            Assert.That(owner.IsStatic && owner.MethodKind == MethodKind.Ordinary && owner.DeclaredAccessibility == Accessibility.Public,
                Is.True, item.Id);
            Assert.That(syntax.Ancestors().Any(static node => node is Microsoft.CodeAnalysis.CSharp.Syntax.LocalFunctionStatementSyntax or
                Microsoft.CodeAnalysis.CSharp.Syntax.AnonymousFunctionExpressionSyntax), Is.False, item.Id);
            var existing = CorpusGate.CensusContractCalls(compilation);
            Assert.That(existing.PublicOwners, Is.EqualTo(1), item.Id);
            Assert.That(existing.PotentialCalls, Is.EqualTo(rows.Length), item.Id);
            Assert.That(existing.BoundRequires, Is.EqualTo(rows.Length), item.Id);
            Assert.That(existing.Complete && existing.Unpublished && existing.ManifestUnchanged, Is.True, item.Id);
            Assert.That(existing.Failures, Is.Empty, item.Id);
        }
        Assert.That(obligations, Is.EqualTo(100));
        var snapshot = await File.ReadAllLinesAsync(Path.Combine(root, "SharpProof.Gates", "Corpus", "expected.canonical.snapshot"));
        var pinnedContractIds = snapshot.Where(static line => line.StartsWith('C') && line.Contains('|', StringComparison.Ordinal))
            .Select(static line => line.Split('|')[0]).ToArray();
        Assert.That(contractIds, Is.EquivalentTo(pinnedContractIds));
        var document = OpenSourceCorpusCatalog.Load(root);
        Assert.That(document.Methods, Has.Length.EqualTo(200));
        var oss = OpenSourceCorpusRunner.PrepareExceptionProbe(document, CancellationToken.None, purity: true);
        var ossRows = Census(oss, out var ossDeclarations, out var ossDeclaredRequires);
        Assert.That(ossRows, Is.Empty);
        Assert.That(ossDeclaredRequires, Is.Zero);
        await TestContext.Progress.WriteLineAsync($"independent-census synthetic={synthetic.Length} methodDeclarations={declarations} requiresObligations={obligations} " +
            $"ossSelected={document.Methods.Length} ossTrees={oss.SyntaxTrees.Length} ossMethodDeclarations={ossDeclarations} ossRequiresObligations={ossRows.Length}");
    }
}
