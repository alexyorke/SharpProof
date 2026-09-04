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
    [Platform("Linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public void CorpusContainmentRejectsSymlinkTargetsOutsideRoot()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Gates.Test",
            Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Gates.Test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var target = Path.Combine(outside, "license.txt");
        var link = Path.Combine(root, "license.txt");
        try
        {
            File.WriteAllText(target, "outside\n");
            File.CreateSymbolicLink(link, target);

            var exception = Assert.Throws<InvalidDataException>((Action)(() =>
                OpenSourceCorpusCatalog.EnsureContained(root, link)));

            Assert.That(
                exception!.Message,
                Does.Contain("follows a link outside its directory"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
            Directory.Delete(outside, recursive: true);
        }
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

    [Test]
    [Platform("Linux")]
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    public async Task CanceledGitReadTerminatesTheChildProcess()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "SharpProof.Gates.Test",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
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
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void CorpusSnapshotFormatRequiresExactSchemaThreeBytes()
    {
        const string data = "case|Proven|Proven|";
        var canonical = Encoding.UTF8.GetBytes(CorpusSnapshotHeader + data + "\n");
        Assert.That(CorpusSnapshotFormat.Parse(canonical), Is.EqualTo(new[] { data }));
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
                Is.EqualTo(1));
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
            Assert.That(result.SupportedOpenSourceMethodCount, Is.EqualTo(1));
            Assert.That(result.OpenSourceFileCount, Is.EqualTo(87));
            Assert.That(result.SyntheticSeedCount, Is.EqualTo(28));
            Assert.That(result.SupportedCaseCount, Is.EqualTo(163));
            Assert.That(
                result.IntentionallyUnsupportedCaseCount,
                Is.EqualTo(299));
            Assert.That(result.SupportedUnknownCount, Is.Zero);
            Assert.That(result.UnknownCount, Is.EqualTo(289));
            Assert.That(result.SilentUnknownCount, Is.EqualTo(10));
            Assert.That(result.TotalUnknownCount, Is.EqualTo(299));
            Assert.That(
                result.UnknownReasons
                    .ToDictionary(
                        static item => item.Reason,
                        static item => item.Count),
                Is.EquivalentTo(
                    new Dictionary<string, int>(StringComparer.Ordinal)
                    {
                        ["SP0002"] = 27,
                        ["SP0016"] = 18,
                        ["SP0045"] = 27,
                        ["SP0045+SP0046"] = 9,
                        ["SP0046"] = 27,
                        ["SP0047"] = 181,
                        ["silent-unclassified"] = 10
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
                "from Refuted to Proven relative to seed.baseline.",
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
            line.StartsWith("C06.baseline|", StringComparison.Ordinal))
            .Split('|');
        var openSource = lines.Single(static line =>
            line.StartsWith("OSS0001.baseline|", StringComparison.Ordinal))
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
                    "Call to 'Positive' violates precondition 'false'"));
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
