using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;

namespace SharpProof.Package.Test;

[TestFixture]
[NonParallelizable]
public sealed class HumanReleaseGateScriptTests
{
    private const string ProductCommit =
        "1111111111111111111111111111111111111111";
    private const string BaselineCommit =
        "2222222222222222222222222222222222222222";
    private const string OtherCommit =
        "3333333333333333333333333333333333333333";
    private static readonly string Digest = new('a', 64);
    private static readonly JsonSerializerOptions s_indentedJsonOptions =
        new()
        {
            WriteIndented = true
        };

    [Test]
    public async Task AnnotatedExternalEvidencePassesAndBindsResolvedCommit()
    {
        using var workspace = await EvidenceWorkspace.CreateAsync(
            CreateEvidenceJson(),
            annotatedTag: true);
        var validationPath = Path.Combine(
            workspace.Root,
            "human-validation.json");

        var result = await RunHumanGateAsync(
            workspace.Repository,
            validationPath);

        Assert.That(result.ExitCode, Is.Zero, result.Output);
        using var validation = JsonDocument.Parse(
            await File.ReadAllBytesAsync(validationPath));
        Assert.That(
            validation.RootElement.GetProperty("status").GetString(),
            Is.EqualTo("passed"));
        Assert.That(
            validation.RootElement.GetProperty("productCommit").GetString(),
            Is.EqualTo(ProductCommit));
        Assert.That(
            validation.RootElement.GetProperty("evidenceCommit").GetString(),
            Is.EqualTo(workspace.EvidenceCommit));
        Assert.That(
            validation.RootElement.GetProperty("evidenceTagObject")
                .GetString(),
            Has.Length.EqualTo(40));
        Assert.That(
            validation.RootElement.GetProperty("evidenceDocumentSha256")
                .GetString(),
            Has.Length.EqualTo(64));
    }

    [TestCase("package")]
    [TestCase("runtime")]
    [TestCase("tool")]
    [TestCase("policy")]
    [TestCase("outcomes")]
    [TestCase("result")]
    [TestCase("workflow")]
    [TestCase("workflow-duplicate")]
    public async Task InvalidBoundPilotEvidenceFails(string invalidSection)
    {
        var evidence = JsonNode.Parse(CreateEvidenceJson())!.AsObject();
        CorruptEvidence(evidence, invalidSection);
        using var workspace = await EvidenceWorkspace.CreateAsync(
            evidence.ToJsonString(s_indentedJsonOptions),
            annotatedTag: true);
        var validationPath = Path.Combine(
            workspace.Root,
            "human-validation.json");

        var result = await RunHumanGateAsync(
            workspace.Repository,
            validationPath);

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        Assert.That(File.Exists(validationPath), Is.False);
    }

    [Test]
    public async Task LightweightEvidenceTagIsRejected()
    {
        using var workspace = await EvidenceWorkspace.CreateAsync(
            CreateEvidenceJson(),
            annotatedTag: false);

        var result = await RunHumanGateAsync(
            workspace.Repository,
            Path.Combine(workspace.Root, "human-validation.json"));

        Assert.That(result.ExitCode, Is.Not.Zero, result.Output);
        Assert.That(
            result.Output,
            Does.Contain("must be an annotated tag"));
    }

    [Test]
    public async Task QualificationStatesDoNotClaimSuccessEarly()
    {
        using var workspace = TemporaryWorkspace.Create(
            "SharpProof.ReleaseQualification");
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");

        var running = await RunQualificationAsync(
            qualificationPath,
            "running",
            "v1.0.0-preview.1");
        Assert.That(running.ExitCode, Is.Zero, running.Output);
        using (var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath)))
        {
            Assert.That(
                document.RootElement.GetProperty("status").GetString(),
                Is.EqualTo("running"));
            Assert.That(
                document.RootElement.GetProperty("gates")
                    .GetProperty("acceptance")
                    .GetString(),
                Is.EqualTo("pending"));
        }

        var failed = await RunQualificationAsync(
            qualificationPath,
            "failed",
            "v1.0.0-preview.1",
            "-FailureKind",
            "test-failure");
        Assert.That(failed.ExitCode, Is.Zero, failed.Output);
        using (var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath)))
        {
            Assert.That(
                document.RootElement.GetProperty("status").GetString(),
                Is.EqualTo("failed"));
            Assert.That(
                document.RootElement.GetProperty("gates")
                    .GetProperty("acceptance")
                    .GetString(),
                Is.EqualTo("incomplete"));
        }
    }

    [Test]
    public async Task FinalQualificationRequiresValidatedExternalEvidence()
    {
        using var workspace = TemporaryWorkspace.Create(
            "SharpProof.FinalQualification");
        var qualificationPath = Path.Combine(
            workspace.Root,
            "qualification.json");
        var missingEvidence = await RunQualificationAsync(
            qualificationPath,
            "passed",
            "v1.0.0",
            "-CoverageBaselineCommit",
            BaselineCommit);
        Assert.That(
            missingEvidence.ExitCode,
            Is.Not.Zero,
            missingEvidence.Output);

        var humanValidationPath = Path.Combine(
            workspace.Root,
            "human-validation.json");
        await File.WriteAllTextAsync(
            humanValidationPath,
            JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                status = "passed",
                releaseTag = "v1.0.0",
                productCommit = ProductCommit,
                evidenceRef = "refs/tags/evidence/v1.0.0",
                evidenceTagObject = OtherCommit,
                evidenceCommit = BaselineCommit,
                evidenceDocumentSha256 = Digest
            }));
        var passed = await RunQualificationAsync(
            qualificationPath,
            "passed",
            "v1.0.0",
            "-CoverageBaselineCommit",
            BaselineCommit,
            "-HumanEvidencePath",
            humanValidationPath);
        Assert.That(passed.ExitCode, Is.Zero, passed.Output);
        using var document = JsonDocument.Parse(
            await File.ReadAllBytesAsync(qualificationPath));
        Assert.That(
            document.RootElement.GetProperty("status").GetString(),
            Is.EqualTo("passed"));
        Assert.That(
            document.RootElement.GetProperty("gates")
                .EnumerateObject()
                .Select(property => property.Value.GetString()),
            Is.All.EqualTo("passed"));
        Assert.That(
            document.RootElement.GetProperty("humanEvidence")
                .GetProperty("status")
                .GetString(),
            Is.EqualTo("passed"));
        Assert.That(
            document.RootElement.GetProperty("humanEvidence")
                .GetProperty("documentSha256")
                .GetString(),
            Is.EqualTo(Digest));
    }

    private static void CorruptEvidence(
        JsonObject evidence,
        string invalidSection)
    {
        var pilot = evidence["pilots"]![0]!.AsObject();
        var cycle = pilot["weeklyCycles"]![0]!.AsObject();
        switch (invalidSection)
        {
            case "package":
                pilot["package"]!["artifacts"]![0]!["sha256"] = "invalid";
                break;
            case "runtime":
                pilot["runtime"]!["architecture"] = "arm64";
                break;
            case "tool":
                pilot["tool"]!["productCommit"] = OtherCommit;
                break;
            case "policy":
                pilot["policy"]!["profile"] = "advisory";
                break;
            case "outcomes":
                cycle["outcomes"]!["proven"] = 49;
                cycle["outcomes"]!["unknown"] = 1;
                break;
            case "result":
                cycle["result"]!["runStatus"] = "Failed";
                break;
            case "workflow":
                cycle["workflow"]!["runId"] = 0;
                break;
            case "workflow-duplicate":
                pilot["weeklyCycles"]![1]!["workflow"] =
                    cycle["workflow"]!.DeepClone();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(invalidSection),
                    invalidSection,
                    "Unknown evidence mutation.");
        }
    }

    private static string CreateEvidenceJson()
    {
        var evidence = new
        {
            schemaVersion = 2,
            releaseTag = "v1.0.0",
            productCommit = ProductCommit,
            evidenceRef = "refs/tags/evidence/v1.0.0",
            pilots = new[]
            {
                CreatePilot("pilot-a", 1),
                CreatePilot("pilot-b", 101)
            },
            openDefects = new
            {
                p0 = 0,
                p1 = 0,
                evidenceUrl = "https://example.com/defects"
            },
            soundnessReviews = new[]
            {
                new
                {
                    reviewer = "reviewer-a",
                    independent = true,
                    productCommit = ProductCommit,
                    disposition = "approved",
                    evidenceUrl = "https://example.com/reviews/a"
                },
                new
                {
                    reviewer = "reviewer-b",
                    independent = true,
                    productCommit = ProductCommit,
                    disposition = "approved",
                    evidenceUrl = "https://example.com/reviews/b"
                }
            },
            governance = new
            {
                protectedDefaultBranch = true,
                protectedReleaseTags = true,
                protectedPublishingEnvironments = true,
                requiredChecks = true,
                independentReviewRequired = true,
                evidenceUrl = "https://example.com/governance"
            }
        };
        return JsonSerializer.Serialize(
            evidence,
            s_indentedJsonOptions);
    }

    private static object CreatePilot(string id, int firstRunId)
    {
        return new
        {
            id,
            selectedClaims = 50,
            package = new
            {
                version = "1.0.0",
                releaseManifestSha256 = Digest,
                artifacts = new[]
                {
                    new
                    {
                        id = "SharpProof.Attributes",
                        sha256 = Digest
                    },
                    new
                    {
                        id = "SharpProof",
                        sha256 = Digest
                    },
                    new
                    {
                        id = "SharpProof.Verifier.Win-x64",
                        sha256 = Digest
                    }
                }
            },
            runtime = new
            {
                operatingSystem = "windows",
                architecture = "x64",
                dotnetSdkVersion = "9.0.300",
                dotnetRuntimeVersion = "9.0.0",
                roslynVersion = "4.14.0"
            },
            tool = new
            {
                productCommit = ProductCommit,
                workerVersion = "1.0.0",
                protocolVersion = "9",
                manifestSchemaVersion = 4,
                compilerArtifactSchemaVersion = 8,
                workerBinarySha256 = Digest
            },
            policy = new
            {
                profile = "strict",
                features = "all",
                verifyPolicy = "require-proven",
                assumptionPolicy = "error"
            },
            weeklyCycles = Enumerable.Range(0, 4)
                .Select(index => new
                {
                    weekEnding = new DateOnly(2030, 1, 6)
                        .AddDays(index * 7)
                        .ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture),
                    outcomes = new
                    {
                        selectedClaims = 50,
                        proven = 50,
                        refuted = 0,
                        unknown = 0,
                        assumptions = 0,
                        infrastructureFailures = 0
                    },
                    result = new
                    {
                        format = "SharpProof.WorkerVerifyResponse",
                        runStatus = "Complete",
                        sha256 = Digest,
                        requestHash = Digest
                    },
                    workflow = new
                    {
                        provider = "github-actions",
                        repository = $"owner/{id}",
                        name = "SharpProof strict weekly",
                        runId = firstRunId + index,
                        runAttempt = 1,
                        sourceCommit = OtherCommit,
                        evidenceUrl =
                            $"https://example.com/{id}/runs/{firstRunId + index}"
                    }
                })
                .ToArray()
        };
    }

    private static Task<ProcessResult> RunHumanGateAsync(
        string evidenceRepository,
        string validationPath)
    {
        return RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Test-SharpProofHumanReleaseGates.ps1"),
            "-ExpectedProductCommit",
            ProductCommit,
            "-EvidenceRepository",
            evidenceRepository,
            "-OutputPath",
            validationPath);
    }

    private static Task<ProcessResult> RunQualificationAsync(
        string outputPath,
        string status,
        string tag,
        params string[] additionalArguments)
    {
        var arguments = new List<string>
        {
            "-NoLogo",
            "-NoProfile",
            "-File",
            Path.Combine(
                FindRepositoryRoot(),
                "scripts",
                "Write-SharpProofReleaseQualification.ps1"),
            "-Status",
            status,
            "-Tag",
            tag,
            "-ReleaseCommit",
            ProductCommit,
            "-OutputPath",
            outputPath
        };
        arguments.AddRange(additionalArguments);
        return RunProcessAsync(
            FindRepositoryRoot(),
            "pwsh",
            arguments.ToArray());
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string workingDirectory,
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            (await standardOutput) + Environment.NewLine +
            (await standardError));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(
            typeof(HumanReleaseGateScriptTests).Assembly.Location);
        while (directory != null)
        {
            if (File.Exists(
                    Path.Combine(
                        directory.FullName,
                        "SharpProof.Release.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }
        throw new InvalidOperationException(
            "Repository root was not found.");
    }

    private sealed class EvidenceWorkspace : IDisposable
    {
        private readonly TemporaryWorkspace _temporary;

        private EvidenceWorkspace(
            TemporaryWorkspace temporary,
            string repository,
            string evidenceCommit)
        {
            _temporary = temporary;
            Repository = repository;
            EvidenceCommit = evidenceCommit;
        }

        internal string Root => _temporary.Root;

        internal string Repository
        {
            get;
        }

        internal string EvidenceCommit
        {
            get;
        }

        internal static async Task<EvidenceWorkspace> CreateAsync(
            string evidenceJson,
            bool annotatedTag)
        {
            var temporary = TemporaryWorkspace.Create(
                "SharpProof.HumanEvidenceTests");
            try
            {
                var repository = Path.Combine(
                    temporary.Root,
                    "evidence-repository");
                Directory.CreateDirectory(repository);
                await AssertGitAsync(
                    repository,
                    "init",
                    "--quiet",
                    "--initial-branch=release-evidence");
                await AssertGitAsync(
                    repository,
                    "config",
                    "user.name",
                    "SharpProof Tests");
                await AssertGitAsync(
                    repository,
                    "config",
                    "user.email",
                    "sharpproof-tests@example.com");
                var evidenceDirectory = Path.Combine(
                    repository,
                    "releases");
                Directory.CreateDirectory(evidenceDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(
                        evidenceDirectory,
                        "v1.0.0.json"),
                    evidenceJson);
                await AssertGitAsync(repository, "add", ".");
                await AssertGitAsync(
                    repository,
                    "commit",
                    "--quiet",
                    "-m",
                    "Record external release evidence");
                var commit = await AssertGitAsync(
                    repository,
                    "rev-parse",
                    "HEAD");
                var tagArguments = annotatedTag
                    ? new[]
                    {
                        "tag",
                        "-a",
                        "evidence/v1.0.0",
                        "-m",
                        "SharpProof 1.0 human evidence"
                    }
                    : new[]
                    {
                        "tag",
                        "evidence/v1.0.0"
                    };
                await AssertGitAsync(repository, tagArguments);
                return new EvidenceWorkspace(
                    temporary,
                    repository,
                    commit.Output.Trim());
            }
            catch
            {
                temporary.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            _temporary.Dispose();
        }

        private static async Task<ProcessResult> AssertGitAsync(
            string repository,
            params string[] arguments)
        {
            var result = await RunProcessAsync(
                repository,
                "git",
                arguments);
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            return result;
        }
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private readonly string _expectedParent;

        private TemporaryWorkspace(string root, string expectedParent)
        {
            Root = root;
            _expectedParent = expectedParent;
        }

        internal string Root
        {
            get;
        }

        internal static TemporaryWorkspace Create(string name)
        {
            var parent = Path.GetFullPath(
                Path.Combine(Path.GetTempPath(), name));
            Directory.CreateDirectory(parent);
            var root = Path.Combine(
                parent,
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TemporaryWorkspace(root, parent);
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root);
            var relative = Path.GetRelativePath(_expectedParent, resolved);
            if (Path.IsPathRooted(relative) ||
                relative == "." ||
                relative == ".." ||
                relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                foreach (var file in Directory.EnumerateFiles(
                             resolved,
                             "*",
                             SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(resolved, recursive: true);
            }
        }
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
