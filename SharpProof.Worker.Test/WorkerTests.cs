using System.Collections.Immutable;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerTests
{
    [Test]
    public async Task PreparedArtifactRejectsChangedDigestAndRebindsCurrentBudgets()
    {
        using var project = TestProject.Create(BoundedIdentitySubjectSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, CancellationToken.None);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request, snapshot, CancellationToken.None);

        request.CompilerManifest.Sha256 = new string('f', 64);
        var mismatched = await worker.VerifyAsync(request, snapshot, CancellationToken.None);
        Assert.That(mismatched.FailureReason, Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));

        request.CompilerManifest.Sha256 = snapshot.ArtifactDigest;
        request.Budgets.MethodRlimit++;
        File.Delete(request.CompilerManifest.Path);
        var changedBudget = await worker.VerifyAsync(request, snapshot, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(changedBudget.RunStatus, Is.EqualTo(WorkerRunStatus.Complete),
                string.Join("; ", changedBudget.Errors.Select(static error => error.Code + ": " + error.Message)));
            Assert.That(changedBudget.InputHash, Is.Not.EqualTo(first.InputHash));
            Assert.That(changedBudget.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(changedBudget.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        }
    }

    [TestCase("[ZeroAllocations] public static object Allocate() => new object();")]
    [TestCase("[DoesNotThrow] public static int Identity(int value) => value;")]
    [TestCase("public static int Identity(int value) { Contract.Ensures(Contract.Result<int>() == value); while (value > 0) { value--; } return value; }")]
    [TestCase("public static int Identity(int value) => value;")]
    public async Task CompleteResponsesReuseCacheAcrossEffectUnknownAndEmptyClaims(string member)
    {
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject { {{member}} }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var first = await worker.VerifyAsync(request);
        var second = await worker.VerifyAsync(request);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(second.ClaimResults.Select(static claim => (claim.Outcome, claim.Reason)),
                Is.EqualTo(first.ClaimResults.Select(static claim => (claim.Outcome, claim.Reason))));
            Assert.That(WorkerProtocolJson.ValidateForRequest(second,
                WorkerProtocolJson.ComputeRequestHash(request), second.InputHash,
                second.Manifest, request, second.Summary.Versions).IsValid, Is.True);
        }
    }

    private const string BoundedIdentitySubjectSource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Identity(long value) {
                Contract.Assume(value >= 0);
                Contract.Assume(value <= 10);
                Contract.Ensures(Contract.Result<long>() >= 0);
                return value;
            }
        }
        """;

    private const string MaximumSubjectSource =
        """
        using System;
        using SharpProof.Attributes;
        public static class Subject {
            public static int Maximum(int left, int right) {
                Contract.Ensures(
                    Contract.Result<int>() ==
                    (left >= right ? left : right));
                return Math.Max(left, right);
            }
        }
        """;

    private const string ConcurrentSubjectsSource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long A(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            }
            public static long B(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            }
        }
        """;

    private static readonly string[] InvalidBudgetErrorCodes = [
        "protocol.unsupported",
        "budgets.rlimit",
        "budgets.method_rlimit",
        "budgets.parallelism",
        "budgets.expression_depth",
        "budgets.wall_order",
        "cache.maximum_bytes"
    ];

    private static readonly string[] RequiredReferenceFileNames = [
        "System.Private.CoreLib.dll",
        "System.Linq.dll",
        "System.Runtime.dll",
        "netstandard.dll"
    ];

    private static readonly ImmutableArray<string>
        ReplayedAllocationWitnessKinds = AllocationWitnessKinds.Managed;

    [Test]
    public void ProtocolValidationClosesVersionAndBudgetBounds()
    {
        var request = new WorkerVerifyRequest
        {
            ProtocolVersion = "unsupported",
            CompilerManifest = new WorkerFileReference
            {
                Path = "compiler-manifest.json",
                Sha256 = new string('a', 64)
            },
            Budgets = new WorkerBudgets
            {
                QueryRlimit = 0,
                MethodRlimit = 0,
                MaxParallelism = 5,
                MaximumExpressionDepth = 300,
                MethodWallTimeMilliseconds = 20,
                ProjectWallTimeMilliseconds = 10
            },
            Cache = new WorkerCacheOptions
            {
                MaximumBytes = WorkerCacheOptions.DefaultMaximumBytes + 1
            }
        };
        var validation = WorkerProtocolJson.Validate(request);
        Assert.That(validation.IsValid, Is.False);
        Assert.That(
            validation.Errors.Select(static error => error.Code),
            Is.SupersetOf(InvalidBudgetErrorCodes));
        Assert.Throws<JsonException>((Action)(() =>
            WorkerProtocolJson.DeserializeRequest(
                """{"protocolVersion":"1","unknown":true}""")));

        request.ProtocolVersion = WorkerProtocolVersions.Current;
        request.Budgets.QueryRlimit = 2;
        request.Budgets.MethodRlimit = 1;
        validation = WorkerProtocolJson.Validate(request);
        Assert.That(
            validation.Errors.Select(static error => error.Code),
            Does.Contain("budgets.rlimit_order"));
    }

    [Test]
    public void ProtocolDefaultsFailClosedWithoutCompilerManifest()
    {
        var request = new WorkerVerifyRequest();

        var validation = WorkerProtocolJson.Validate(request);

        Assert.That(validation.IsValid, Is.False);
        Assert.That(
            validation.Errors.Select(static error => error.Code),
            Does.Contain("project.compiler_manifest"));
    }

    [Test]
    public async Task InvalidRequestStillProducesAWellFormedFailedResponse()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 0;
        using var worker = new SharpProofWorker(new CountingBackend(
            BackendCheckResult.Unsatisfiable([])));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.InvalidRequest));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task BuiltInWorkerRejectsRequestWithDifferentQueryRlimit()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        request.Budgets.QueryRlimit++;

        var response = await worker.VerifyAsync(request);

        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
        Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.InvalidRequest));
        Assert.That(response.Errors.Select(static error => error.Code),
            Does.Contain("budgets.query_rlimit_mismatch"));
    }

    [Test]
    public async Task ClosedCompilerManifestDoesNotRereadChangedSourceFiles()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var authoritative = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(request.CompilerManifest.Path));
        await File.WriteAllTextAsync(
            project.SourcePaths.Single(),
            TautologySource.Replace(
                "return value;", "return 00000;",
                StringComparison.Ordinal));
        using var backend = new CountingNativeBackend();
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return backend;
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(
                WorkerProtocolJson.ManifestsEqual(
                    response.Manifest, authoritative.Manifest),
                Is.True);
            Assert.That(
                response.CallableResults.All(static result =>
                    result.Coverage == WorkerCallableCoverage.Complete &&
                    result.Reason == WorkerCallableCoverageReason.None),
                Is.True);
            Assert.That(
                response.ClaimResults.All(static result =>
                    result.Outcome == WorkerClaimOutcome.Proven &&
                    result.Reason == WorkerClaimReason.None),
                Is.True);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(backend.CallCount, Is.EqualTo(3));
            Assert.That(CacheFiles(project), Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task EffectOnlyClaimUsesSealedCompilerEvidenceWithoutSmtQuery()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static int Identity(int value) => value;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);
        var claim = response.Manifest.Claims.Single();
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(claim.EffectContractKind,
                Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
            Assert.That(result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(response.Summary.Versions.WorkerBinarySha256,
                Does.Match("^[0-9a-f]{64}$"));
            Assert.That(response.Summary.Versions.ApiSpecContentSha256,
                Does.Match("^[0-9a-f]{64}$"));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task EffectClaimCanBeProvenVacuouslyOnlyFromContradictoryEntry()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [EffectContract(
                    SharpProofEffect.None,
                    Complete = true)]
                public static int Impossible(
                    [Positive, InRange(-2, -1)] int value) =>
                    value;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        using var worker = SharpProofWorker.Create(request.Budgets);

        var first = await worker.VerifyAsync(request);
        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven,
            expectedVacuity: WorkerVacuityKind.ContradictoryPreconditions);
        var usedPreconditions = result.Assumptions.Where(
            static assumption =>
                assumption.Kind ==
                WorkerAssumptionKind.Precondition);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.VacuousEntry));
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(usedPreconditions, Is.Not.Empty);
            Assert.That(
                usedPreconditions.All(
                    static assumption => assumption.Used),
                Is.True);
            Assert.That(
                first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                response.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(CacheFiles(project), Has.Length.EqualTo(1));
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
        }
    }

    [Test]
    public async Task LiteralEffectVacuityMarksOnlyItsContradictoryPreconditionUsed()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static int Impossible(int value) {
                    Contract.Requires(false);
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);
        var result = response.ClaimResults.Single();
        var preconditions = result.Assumptions.Where(
            static assumption =>
                assumption.Kind ==
                WorkerAssumptionKind.Precondition).ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Vacuity,
                Is.EqualTo(
                    WorkerVacuityKind.ContradictoryPreconditions));
            Assert.That(preconditions, Has.Length.EqualTo(2));
            Assert.That(
                preconditions.Count(
                    static assumption => assumption.Used),
                Is.EqualTo(1));
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
        }
    }

    [Test]
    public async Task UnknownEntryFeasibilityKeepsEffectClaimUnknown()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static int Positive(int value) {
                    Contract.Requires(value > 0);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new CountingBackend(
            BackendCheckResult.Unknown(
                BackendFailureReason.ResourceLimit));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.ResourceLimit,
            WorkerVacuityKind.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(
                result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(
                response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
        }
    }

    [Test]
    public async Task InvalidEffectClaimsCannotBecomeVacuouslyProven()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;

            public static class Subject {
                [AllowedCapabilities(
                    (SharpProofCapability)(1 << 30))]
                public static void Contradictory() {
                    Contract.Requires(false);
                }

                [AllowedCapabilities(
                    (SharpProofCapability)(1 << 30))]
                public static void UnknownEntry(int value) {
                    Contract.Requires(value > 0);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new CountingBackend(
            BackendCheckResult.Unknown(
                BackendFailureReason.ResourceLimit));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Reason),
                Is.All.EqualTo(
                    WorkerClaimReason.UnsupportedContract),
                string.Join(", ", response.Errors.Select(static error =>
                    error.Code)));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.EffectCertainty),
                Is.All.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Vacuity),
                Is.All.EqualTo(WorkerVacuityKind.None));
            Assert.That(
                response.ClaimResults.SelectMany(static result =>
                    result.ProofCore),
                Is.Empty);
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(
                    WorkerCallableCoverage.Incomplete));
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
        }
    }

    [Test]
    public async Task EffectContractsAreDecidedNatively()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static object AllocateOnly() => new object();

                [EffectContract(
                    SharpProofEffect.Throws,
                    ThrownExceptions = new[] { typeof(Exception) },
                    Complete = true)]
                public static void ThrowExisting(Exception exception) {
                    Contract.Requires(exception != null);
                    throw exception;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);
        Assert.That(
            response.ClaimResults,
            Has.Length.EqualTo(2),
            string.Join(
                Environment.NewLine,
                response.Manifest.Claims.Select(static claim =>
                    claim.CallableId + " / " +
                    claim.Kind + " / " +
                    claim.Evidence)));
        var allocation = response.ClaimResults.Single(result =>
            GetCallableId(response, result).Contains(
                ".AllocateOnly",
                StringComparison.Ordinal));
        var throwing = response.ClaimResults.Single(result =>
            GetCallableId(response, result).Contains(
                ".ThrowExisting(",
                StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                allocation.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                allocation.Reason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                allocation.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(
                allocation.EffectWitness?.Kind,
                Is.EqualTo("managed-allocation"));
            Assert.That(
                allocation.EffectWitness?.Effects,
                Is.EqualTo(WorkerEffectSet.Allocates));
            Assert.That(
                throwing.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            // A null exception throws a runtime NullReferenceException, which
            // allocates; Allocates is not declared.
            Assert.That(
                throwing.Reason,
                Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(
                throwing.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(
                throwing.ProofCore,
                Is.Empty);
            Assert.That(
                response.CallableResults.Single(result =>
                    result.CallableId.Contains(
                        ".AllocateOnly",
                        StringComparison.Ordinal)).Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(
                response.CallableResults.Single(result =>
                    result.CallableId.Contains(
                        ".ThrowExisting(",
                        StringComparison.Ordinal)).Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task AllowedExceptionsAreDecidedNatively()
    {
        using var project = TestProject.Create(
            """
            #nullable enable
            using System;
            using SharpProof.Attributes;

            public static class Subject {
                [AllowedExceptions(typeof(InvalidOperationException))]
                public static void MaybeNull(
                    InvalidOperationException? exception) =>
                    throw exception;

                [AllowedExceptions(typeof(InvalidOperationException))]
                public static void RequiredNonNull(
                    InvalidOperationException? exception) {
                    Contract.Requires(exception != null);
                    throw exception;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);
        var maybeNull = response.ClaimResults.Single(result =>
            GetCallableId(response, result).Contains(
                ".MaybeNull(",
                StringComparison.Ordinal));
        var requiredNonNull = response.ClaimResults.Single(result =>
            GetCallableId(response, result).Contains(
                ".RequiredNonNull(",
                StringComparison.Ordinal));

        using (Assert.EnterMultipleScope())
        {
            // `throw null` raises NullReferenceException, which is not allowed.
            Assert.That(maybeNull.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(maybeNull.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(maybeNull.EffectWitness?.Detail, Is.EqualTo("System.NullReferenceException"));
            Assert.That(requiredNonNull.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(requiredNonNull.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(requiredNonNull.ProofCore, Has.One.StartsWith("native-effect:"));
            Assert.That(response.CallableResults.Select(static result => result.Coverage), Is.All.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task UnsupportedThrowEffectViolationRemainsTypedUnknown()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static void Throw() => throw new InvalidOperationException();
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Refuted,
            WorkerClaimReason.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(
                result.EffectWitness?.Kind,
                Is.EqualTo("explicit-throw"));
            Assert.That(
                result.EffectWitness?.Effects,
                Is.EqualTo(WorkerEffectSet.Throws));
            Assert.That(
                result.EffectWitness?.ExactExceptionTypeHierarchy,
                Is.Not.Empty);
            Assert.That(result.Model, Is.Empty);
            Assert.That(response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(response.CallableResults.Single().Reason,
                Is.EqualTo(WorkerCallableCoverageReason.None));
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task ConditionalThrowRefutesWithAConcreteEntry()
    {
        var response = await RunAsync(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static void MaybeThrow(bool condition) {
                    if (condition)
                        throw new InvalidOperationException();
                }
            }
            """,
            cacheEnabled: false);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Refuted,
            WorkerClaimReason.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.EffectCertainty, Is.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(result.EffectWitness?.Kind, Is.EqualTo("explicit-throw"));
            Assert.That(result.EffectWitness?.Detail, Is.EqualTo("System.InvalidOperationException"));
            Assert.That(response.CallableResults.Single().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task UnprovenInitializationAndExceptionConstructionDoNotRefute()
    {
        var response = await RunAsync(
            """
            using System;
            using SharpProof.Attributes;

            public sealed class UserException : Exception {
                public UserException() =>
                    throw new InvalidOperationException();
            }

            public static class StaticSubject {
                private static int _value;
                static StaticSubject() =>
                    throw new InvalidOperationException();

                [EnforcePure]
                public static void Write() => _value = 1;
            }

            public static class ExceptionSubject {
                [AllowedExceptions(typeof(UserException))]
                public static void Throw() =>
                    throw new UserException();
            }
            """,
            cacheEnabled: false);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.EffectWitness),
                Is.All.Null);
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task DirectWritesRefuteNativelyAndCapabilitiesReplay()
    {
        var response = await RunAsync(
            """
            using SharpProof.Attributes;
            public sealed class Subject {
                private int _value;

                [EnforcePure]
                public void Write() => _value = 1;

                [AllowedCapabilities(SharpProofCapability.None)]
                public static void Synchronize() {
                    lock (new object()) {
                    }
                }
            }
            """,
            cacheEnabled: false);
        var results = response.ClaimResults.OrderBy(result =>
            response.Manifest.Claims.Single(claim =>
                claim.ClaimId == result.ClaimId).EffectContractKind).ToArray();
        var responseJson = WorkerProtocolJson.SerializeResponse(response);

        using (Assert.EnterMultipleScope())
        {
            // Z3 refutes purity at the field store; the capability claim keeps
            // its replayed compiler violation.
            Assert.That(results.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted), responseJson);
            Assert.That(results.Select(static result => result.EffectCertainty),
                Is.All.EqualTo(WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(results[0].EffectWitness?.Kind, Is.EqualTo("nonlocal-write"));
            Assert.That(results[0].EffectWitness?.Effects, Is.EqualTo(WorkerEffectSet.WritesReceiverState));
            Assert.That(results[1].EffectWitness?.Kind, Is.EqualTo("synchronization-lock"));
            Assert.That(results[1].EffectWitness?.Capabilities, Is.EqualTo(WorkerEffectCapabilitySet.Synchronization));
            Assert.That(response.CallableResults.Select(static result => result.Coverage),
                Is.All.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task FirstStatementDirectEventsAreReplayedWithTrailingStatements()
    {
        async Task<WorkerVerifyResponse> AnalyzeMember(string member)
        {
            var source = $$"""
                using System;
                using SharpProof.Attributes;
                public static class Subject {
                    {{member}}
                }
                """;
            using var project = TestProject.Create(source);
            var sourceErrors = project.CreateCompilation().GetDiagnostics()
                .Where(diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(static diagnostic => diagnostic.ToString())
                .ToArray();
            Assert.That(
                sourceErrors,
                Is.Empty,
                string.Join(Environment.NewLine, sourceErrors));
            using var worker = new SharpProofWorker(new CountingBackend(
                BackendCheckResult.Unsatisfiable([])));
            return await worker.VerifyAsync(
                project.CreateRequest(cacheEnabled: false));
        }

        WorkerClaimResult ClaimFor(
            WorkerVerifyResponse response,
            string methodName)
        {
            var claim = response.Manifest.Claims.Single(candidate =>
                candidate.CallableId.Contains(
                    "." + methodName,
                    StringComparison.Ordinal));
            return response.ClaimResults.Single(result =>
                result.ClaimId == claim.ClaimId);
        }

        void AssertOutcome(
            WorkerVerifyResponse response,
            string methodName,
            WorkerClaimOutcome expected)
        {
            var responseJson = WorkerProtocolJson.SerializeResponse(response);
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True,
                responseJson);
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete),
                responseJson);
            Assert.That(
                ClaimFor(response, methodName).Outcome,
                Is.EqualTo(expected),
                responseJson);
        }

        var lockObject = await AnalyzeMember(
            """
            [AllowedCapabilities(SharpProofCapability.None)]
            public static void LockObjectThenReturn() {
                lock (new object()) { }
                return;
            }
            """);
        var lockType = await AnalyzeMember(
            """
            [AllowedCapabilities(SharpProofCapability.None)]
            public static void LockTypeThenContinue() {
                lock (typeof(Subject)) { }
                int marker = 0;
                marker++;
            }
            """);
        var allocateObject = await AnalyzeMember(
            """
            [ZeroAllocations]
            public static void AllocateObjectThenReturn() {
                new object();
                return;
            }
            """);
        var lockArray = await AnalyzeMember(
            """
            [ZeroAllocations]
            public static void LockArrayThenReturn() {
                lock (new object[1]) { }
                return;
            }
            """);
        var returnArray = await AnalyzeMember(
            """
            [ZeroAllocations]
            public static object[] ReturnArrayThenUnreachableStatement() {
                return new object[1];
                int marker = 0;
            }
            """);
        var throwResponse = await AnalyzeMember(
            """
            [DoesNotThrow]
            public static void ThrowThenContinue() {
                throw new InvalidOperationException();
                int marker = 0;
            }
            """);
        var conditional = await AnalyzeMember(
            """
            [AllowedCapabilities(SharpProofCapability.None)]
            public static void ConditionalLock(bool condition) {
                if (condition) {
                    lock (new object()) { }
                }
                return;
            }
            """);
        var laterAllocation = await AnalyzeMember(
            """
            [ZeroAllocations]
            public static void AllocationAfterFirstStatement() {
                int marker = 0;
                new object();
            }
            """);
        var expressionBody = await AnalyzeMember(
            """
            [ZeroAllocations]
            public static object AllocateExpressionBody() => new object();
            """);

        using (Assert.EnterMultipleScope())
        {
            AssertOutcome(lockObject, "LockObjectThenReturn", WorkerClaimOutcome.Refuted);
            // A typeof receiver has no concrete value in the IR.
            AssertOutcome(lockType, "LockTypeThenContinue", WorkerClaimOutcome.Unknown);
            AssertOutcome(allocateObject, "AllocateObjectThenReturn", WorkerClaimOutcome.Refuted);
            AssertOutcome(lockArray, "LockArrayThenReturn", WorkerClaimOutcome.Refuted);
            AssertOutcome(returnArray, "ReturnArrayThenUnreachableStatement", WorkerClaimOutcome.Refuted);
            AssertOutcome(throwResponse, "ThrowThenContinue", WorkerClaimOutcome.Refuted);
            AssertOutcome(expressionBody, "AllocateExpressionBody", WorkerClaimOutcome.Refuted);
            Assert.That(
                ClaimFor(lockObject, "LockObjectThenReturn").EffectWitness?.Kind,
                Is.EqualTo("synchronization-lock"));
            Assert.That(
                ClaimFor(allocateObject, "AllocateObjectThenReturn").EffectWitness?.Kind,
                Is.EqualTo("managed-allocation"));
            Assert.That(
                ClaimFor(lockArray, "LockArrayThenReturn").EffectWitness?.Kind,
                Is.EqualTo("managed-array-allocation"));
            Assert.That(
                ClaimFor(returnArray, "ReturnArrayThenUnreachableStatement").EffectWitness?.Kind,
                Is.EqualTo("managed-array-allocation"));
            Assert.That(
                ClaimFor(throwResponse, "ThrowThenContinue").EffectWitness?.Kind,
                Is.EqualTo("explicit-throw"));
            Assert.That(
                ClaimFor(conditional, "ConditionalLock").Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                ClaimFor(laterAllocation, "AllocationAfterFirstStatement").Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
        }
    }

    [Test]
    public async Task TrustedCompleteExternEffectContractIsProven()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class NativeSubject {
                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(SharpProofEffect.None, Complete = true)]
                public static extern int Read();
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.TrustedCompleteBoundary));
            Assert.That(result.Assumptions.Select(static item => item.Kind),
                Does.Contain(WorkerAssumptionKind.TrustedBoundary));
            Assert.That(result.Assumptions.Single(static item =>
                    item.Kind == WorkerAssumptionKind.TrustedBoundary).Used,
                Is.True);
            Assert.That(response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    // A trusted complete boundary establishes every other effect claim its
    // contract satisfies; one it does not satisfy stays unknown.
    [TestCase("SharpProofEffect.None", WorkerClaimOutcome.Proven)]
    [TestCase("SharpProofEffect.Throws | SharpProofEffect.Allocates, ThrownExceptions = new[] { typeof(System.InvalidOperationException) }",
        WorkerClaimOutcome.Unknown)]
    public async Task TrustedCompleteExternContractDecidesOtherEffectClaims(string effects, WorkerClaimOutcome outcome)
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class NativeSubject {
                [SharpProofTrusted("Reviewed native implementation.")]
                [EffectContract(
            """ + effects + """
            , Complete = true)]
                [DoesNotThrow, ZeroAllocations]
                public static extern int Read();
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(new CountingBackend(BackendCheckResult.Unsatisfiable([])));
        var response = await worker.VerifyAsync(request);
        var effectClaims = response.Manifest.Claims.Where(static claim => claim.Kind == WorkerClaimKind.Effect)
            .Select(claim => response.ClaimResults.Single(result => result.ClaimId == claim.ClaimId)).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(effectClaims, Has.Length.EqualTo(3));
            Assert.That(effectClaims.Count(static result => result.Outcome == WorkerClaimOutcome.Proven),
                Is.EqualTo(outcome == WorkerClaimOutcome.Proven ? 3 : 1));
            Assert.That(effectClaims.Select(static result => result.EffectCertainty),
                Has.All.EqualTo(WorkerEffectEvidenceCertainty.TrustedCompleteBoundary));
        }
    }

    [Test]
    public async Task MixedPostconditionAndEffectClaimsAreReturnedInManifestOrder()
    {
        using var backend = new CountingNativeBackend();
        var response = await RunAsync(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                [return: Positive]
                public static int One() => 1;
            }
            """,
            cacheEnabled: false,
            backend: backend);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Manifest.Claims.Select(static claim => claim.Kind),
                Is.EqualTo([
                    WorkerClaimKind.Postcondition,
                    WorkerClaimKind.Effect
                ]));
            Assert.That(response.ClaimResults.Select(static result => result.ClaimId),
                Is.EqualTo(response.Manifest.Claims.Select(static claim => claim.ClaimId)));
            Assert.That(response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(backend.CallCount, Is.GreaterThan(0));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task InvalidCompilerElidedClauseCannotBeHiddenByCompanionProof()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static bool UnsupportedAndThrowing() =>
                    throw new System.InvalidOperationException();

                public static int Identity(int value) {
                    if (value >= 0) {
                        Contract.Ensures(UnsupportedAndThrowing());
                    }
                    return value;
                }
            }

            [ContractFor(typeof(Subject))]
            public static class SubjectContracts {
                public static int Identity(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() == value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(response.Manifest.Claims[0].Evidence,
                Is.EqualTo(WorkerClaimEvidence.DirectClause));
            Assert.That(response.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(response.ClaimResults.Single().Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedContract),
                string.Join(", ", response.Errors.Select(static error =>
                    error.Code)));
            Assert.That(response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(response.CallableResults.Single().Reason,
                Is.EqualTo(WorkerCallableCoverageReason.UnsupportedContract));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task ProvenEffectEvidenceIsReusedFromTheSemanticCache()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [ZeroAllocations]
                public static int Identity(int value) => value;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var first = await worker.VerifyAsync(request);
        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(second.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(second.Manifest.Hash, Is.EqualTo(first.Manifest.Hash));
            Assert.That(second.ClaimResults.Single().ClaimId,
                Is.EqualTo(first.ClaimResults.Single().ClaimId));
            Assert.That(second.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(second.ClaimResults.Single().EffectCertainty,
                Is.EqualTo(first.ClaimResults.Single().EffectCertainty));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(CacheFiles(project), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task CompilerAllocationViolationsAreReplayedAndCached()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [ZeroAllocations]
                public static object AllocateObject() => new object();

                [ZeroAllocations]
                public static object[] AllocateArray() => new object[1];
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var first = await worker.VerifyAsync(request);
        var second = await worker.VerifyAsync(request);
        var results = second.ClaimResults
            .OrderBy(
                static result =>
                    result.EffectWitness?.Kind ?? string.Empty,
                StringComparer.Ordinal)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                second.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(
                results.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                results.Select(static result => result.EffectCertainty),
                Is.All.EqualTo(
                    WorkerEffectEvidenceCertainty.DefiniteViolation));
            Assert.That(
                results.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                results.Select(static result =>
                    result.EffectWitness!.Kind),
                Is.EqualTo(ReplayedAllocationWitnessKinds));
            Assert.That(
                results.Select(static result =>
                    result.EffectWitness!.Effects),
                Is.All.EqualTo(WorkerEffectSet.Allocates));
            Assert.That(
                second.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(
                second.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(CacheFiles(project), Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
        }
    }

    [Test]
    public async Task NativeAllocationClaimsCoverGenericAndDelegateCallables()
    {
        var response = await RunAsync(
            WorkerTestSources.UnsupportedEffectCallables,
            cacheEnabled: false);

        using (Assert.EnterMultipleScope())
        {
            // Async bodies stay unsupported; the others allocate `new object()`.
            Assert.That(response.ClaimResults, Has.Length.EqualTo(3));
            foreach (var result in response.ClaimResults)
            {
                var asynchronous = GetCallableId(response, result).Contains(".Async", StringComparison.Ordinal);
                Assert.That(result.Outcome, Is.EqualTo(asynchronous ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Refuted));
                Assert.That(result.Reason, Is.EqualTo(asynchronous ? WorkerClaimReason.UnsupportedCallable : WorkerClaimReason.None));
                Assert.That(result.EffectWitness?.Kind, Is.EqualTo(asynchronous ? null : "managed-allocation"));
            }
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task OriginalGenericDefinitionsProveWithoutAdmittingAsyncBodies()
    {
        var response = await RunAsync(WorkerTestSources.UnsupportedContractCallables, cacheEnabled: false);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        var generic = response.ClaimResults.Single(result => GetCallableId(response, result).Contains(".Generic", StringComparison.Ordinal));
        var asynchronous = response.ClaimResults.Single(result => GetCallableId(response, result).Contains(".Async", StringComparison.Ordinal));
        Assert.That(generic.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(generic.Reason, Is.EqualTo(WorkerClaimReason.None));
        Assert.That(asynchronous.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(asynchronous.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedCallable));
        Assert.That(response.CallableResults.Single(result => result.CallableId.Contains(".Generic", StringComparison.Ordinal)).Coverage,
            Is.EqualTo(WorkerCallableCoverage.Complete));
        Assert.That(response.CallableResults.Single(result => result.CallableId.Contains(".Async", StringComparison.Ordinal)).Coverage,
            Is.EqualTo(WorkerCallableCoverage.Incomplete));
        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
        Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
    }

    [Test]
    public async Task CompilationFailurePreservesAuthoritativeClaims()
    {
        using var project = TestProject.Create(
            TautologySource + "\nMissingType invalid;\n");
        var request = project.CreateRequest(cacheEnabled: true);
        var authoritative = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(request.CompilerManifest.Path));
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new CountingBackend(
                BackendCheckResult.Unsatisfiable([]));
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.CompilationFailure));
            Assert.That(WorkerProtocolJson.ManifestsEqual(
                response.Manifest, authoritative.Manifest), Is.True);
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
            Assert.That(response.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(CacheFiles(project), Is.Empty);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task InvalidCompilerManifestDigestIsTypedAndStopsBeforeWork()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        await File.AppendAllTextAsync(request.CompilerManifest.Path, " ");
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new CountingBackend(
                BackendCheckResult.Unsatisfiable([]));
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
            Assert.That(
                response.Errors.Single().Code,
                Is.EqualTo("compiler_manifest.invalid"));
            Assert.That(response.Manifest.Claims, Is.Empty);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(CacheFiles(project), Is.Empty);
        }
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public async Task NullOrBlankLoweredCallableIdsAreTypedAsManifestInvalid(
        string? callableId)
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var json = await File.ReadAllTextAsync(
            request.CompilerManifest.Path);
        var root = JsonNode.Parse(json)!.AsObject();
        var loweredCallables = root["callables"]!.AsArray();
        Assert.That(loweredCallables, Is.Not.Empty);
        loweredCallables[0]!.AsObject()["callableId"] = callableId;
        json = root.ToJsonString(WorkerProtocolJson.Options) + "\n";
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(request.CompilerManifest.Path, bytes);
        request.CompilerManifest.Sha256 =
            WorkerProtocolJson.ComputeSha256(bytes);

        var deserializeException = CaptureJson(() =>
            CompilerManifestArtifactJson.Deserialize(json));
        var snapshotException = CaptureIOException(() => WorkerInputSnapshot.Load(
            request,
            WorkerCacheIdentity.Current,
            CancellationToken.None));
        using var worker = new SharpProofWorker(
            () => throw new AssertionException(
                "An invalid manifest must fail before backend creation."));
        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                deserializeException,
                Is.TypeOf<JsonException>());
            Assert.That(snapshotException, Is.TypeOf<IOException>());
            Assert.That(
                snapshotException!.Message,
                Is.EqualTo(WorkerInputSnapshot.ManifestInvalid));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
            Assert.That(
                response.Errors.Single().Code,
                Is.EqualTo("compiler_manifest.invalid"));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }

        static JsonException? CaptureJson(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (JsonException exception)
            {
                return exception;
            }
        }

        static IOException? CaptureIOException(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (IOException exception)
            {
                return exception;
            }
        }
    }

    [TestCase(nameof(CompilerDiagnosticArtifact.SourceTreePath))]
    [TestCase(nameof(CompilerDiagnosticArtifact.SourceTreeSha256))]
    [TestCase(nameof(CompilerDiagnosticArtifact.SourceLineMapSha256))]
    public async Task NullNonsourceDiagnosticBindingsAreTypedAsManifestInvalid(
        string binding)
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var diagnostic = new CompilerDiagnosticArtifact
        {
            Code = "compiler.CS0001",
            Message = "malformed non-source diagnostic",
            Location = new WorkerSourceLocation()
        };
        switch (binding)
        {
            case nameof(CompilerDiagnosticArtifact.SourceTreePath):
                diagnostic.SourceTreePath = null!;
                break;
            case nameof(CompilerDiagnosticArtifact.SourceTreeSha256):
                diagnostic.SourceTreeSha256 = null!;
                break;
            case nameof(CompilerDiagnosticArtifact.SourceLineMapSha256):
                diagnostic.SourceLineMapSha256 = null!;
                break;
            default:
                throw new AssertionException(
                    "Unknown diagnostic binding field: " + binding);
        }

        var json = await File.ReadAllTextAsync(request.CompilerManifest.Path);
        json = json.Replace(
            "\"compilerDiagnostics\":[]",
            "\"compilerDiagnostics\":" + JsonSerializer.Serialize(
                new[] { diagnostic },
                WorkerProtocolJson.Options),
            StringComparison.Ordinal);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(request.CompilerManifest.Path, bytes);
        request.CompilerManifest.Sha256 =
            WorkerProtocolJson.ComputeSha256(bytes);
        using var worker = new SharpProofWorker(
            () => throw new AssertionException(
                "An invalid manifest must fail before backend creation."));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
            Assert.That(
                response.Errors.Single().Code,
                Is.EqualTo("compiler_manifest.invalid"));
        }
    }

    [Test]
    public async Task OversizedCompilerManifestIsTypedAndStopsBeforeWork()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using (var stream = File.OpenWrite(request.CompilerManifest.Path))
        {
            stream.SetLength(CompilerManifestArtifactFile.MaximumBytes + 1L);
        }

        using var worker = new SharpProofWorker(
            () => throw new AssertionException(
                "An oversized manifest must fail before backend creation."));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.CompilerManifestMismatch));
            Assert.That(
                response.Errors.Single().Code,
                Is.EqualTo("compiler_manifest.invalid"));
            Assert.That(response.Manifest.Claims, Is.Empty);
        }
    }

    [Test]
    public async Task CompilerVersionIsProvenanceRatherThanARuntimeGate()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(request.CompilerManifest.Path));
        artifact.Compilation.CompilerVersion = "0.0.0.0";
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            CompilerManifestArtifactJson.Serialize(artifact));
        await File.WriteAllBytesAsync(request.CompilerManifest.Path, bytes);
        request.CompilerManifest.Sha256 =
            WorkerProtocolJson.ComputeSha256(bytes);
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new CountingNativeBackend();
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(
                response.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(CacheFiles(project), Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task BackendLoadFailurePreservesManifestAndTypedClaims()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = new SharpProofWorker(
            () => throw new DllNotFoundException("test backend"));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.BackendUnavailable));
            Assert.That(
                response.Errors.Single().Code,
                Is.EqualTo("backend.unavailable"));
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(
                response.ClaimResults.Single().Reason,
                Is.EqualTo(WorkerClaimReason.BackendUnavailable));
            Assert.That(CacheFiles(project), Is.Empty);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task BackendFactoryProgrammingFailureIsInfrastructureFailure()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = new SharpProofWorker(
            () => throw new InvalidOperationException(
                "test lane construction failure"));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.InfrastructureFailure));
            Assert.That(
                response.Errors.Single().Code,
                Is.EqualTo("worker.infrastructure"));
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(
                response.CallableResults.Single().Reason,
                Is.EqualTo(
                    WorkerCallableCoverageReason.InfrastructureFailure));
            Assert.That(
                response.ClaimResults.Single().Reason,
                Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
            Assert.That(CacheFiles(project), Is.Empty);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task UnavailableCompilerManifestIsTypedAndStopsBeforeWork()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        File.Delete(request.CompilerManifest.Path);
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new CountingBackend(
                BackendCheckResult.Unsatisfiable([]));
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.InputUnavailable));
            Assert.That(
                response.Errors.Single().Code,
                Is.EqualTo("compiler_manifest.unavailable"));
            Assert.That(response.Manifest.Claims, Is.Empty);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
            Assert.That(factoryCalls, Is.Zero);
            Assert.That(CacheFiles(project), Is.Empty);
        }
    }

    [Test]
    public async Task CacheHitDoesNotConstructTheBackend()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var firstBackend = new CountingNativeBackend();
        using (var first = new SharpProofWorker(firstBackend))
        {
            Assert.That(
                (await first.VerifyAsync(request)).Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
        }

        var factoryCalls = 0;
        using var second = new SharpProofWorker(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new CountingBackend(
                BackendCheckResult.Unsatisfiable([]));
        });

        var response = await second.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(factoryCalls, Is.Zero);
        }
    }

    [Test]
    public async Task ToolAndApiSpecIdentitiesInvalidateTheInputHash()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var baselineIdentity = WorkerCacheIdentity.Current;
        var changedTool = new WorkerCacheIdentity(
            baselineIdentity.ToolIdentity,
            baselineIdentity.ToolVersion + ".changed",
            baselineIdentity.WorkerBinarySha256,
            baselineIdentity.ApiSpecIdentity,
            baselineIdentity.ApiSpecVersion,
            baselineIdentity.ApiSpecContentSha256);
        var changedBinary = new WorkerCacheIdentity(
            baselineIdentity.ToolIdentity,
            baselineIdentity.ToolVersion,
            DifferentHash(baselineIdentity.WorkerBinarySha256),
            baselineIdentity.ApiSpecIdentity,
            baselineIdentity.ApiSpecVersion,
            baselineIdentity.ApiSpecContentSha256);
        var changedSpecs = new WorkerCacheIdentity(
            baselineIdentity.ToolIdentity,
            baselineIdentity.ToolVersion,
            baselineIdentity.WorkerBinarySha256,
            baselineIdentity.ApiSpecIdentity,
            baselineIdentity.ApiSpecVersion + ".changed",
            baselineIdentity.ApiSpecContentSha256);
        var changedSpecContent = new WorkerCacheIdentity(
            baselineIdentity.ToolIdentity,
            baselineIdentity.ToolVersion,
            baselineIdentity.WorkerBinarySha256,
            baselineIdentity.ApiSpecIdentity,
            baselineIdentity.ApiSpecVersion,
            DifferentHash(baselineIdentity.ApiSpecContentSha256));

        var baseline = WorkerInputSnapshot.Load(
            request,
            baselineIdentity,
            CancellationToken.None);
        var tool = WorkerInputSnapshot.Load(
            request,
            changedTool,
            CancellationToken.None);
        var binary = WorkerInputSnapshot.Load(
            request,
            changedBinary,
            CancellationToken.None);
        var specs = WorkerInputSnapshot.Load(
            request,
            changedSpecs,
            CancellationToken.None);
        var specContent = WorkerInputSnapshot.Load(
            request,
            changedSpecContent,
            CancellationToken.None);
        var artifactBytes = await File.ReadAllBytesAsync(
            request.CompilerManifest.Path);
        var sharedHash = CompilerArtifactInputHash.Compute(
            request, ArtifactDigest.Compute(artifactBytes), baselineIdentity.ToolIdentity,
            baselineIdentity.ToolVersion, baselineIdentity.WorkerBinarySha256,
            baselineIdentity.ApiSpecIdentity, baselineIdentity.ApiSpecVersion,
            baselineIdentity.ApiSpecContentSha256);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                baselineIdentity.ToolIdentity,
                Is.EqualTo(WorkerCacheIdentity.CurrentToolIdentity));
            Assert.That(baselineIdentity.ToolVersion, Is.Not.Empty);
            Assert.That(
                WorkerProtocolJson.IsSha256(baselineIdentity.WorkerBinarySha256),
                Is.True);
            Assert.That(
                baselineIdentity.ApiSpecIdentity,
                Is.EqualTo(
                    SharpProof.Specs.ApiSpecTable.DefaultTableIdentity));
            Assert.That(
                baselineIdentity.ApiSpecVersion,
                Is.EqualTo(
                    SharpProof.Specs.ApiSpecTable.DefaultTableVersion));
            Assert.That(
                baselineIdentity.ApiSpecContentSha256,
                Is.EqualTo(SharpProof.Specs.ApiSpecTable.Default.ContentSha256));
            Assert.That(baseline.InputHash, Is.EqualTo(sharedHash));
            Assert.That(tool.InputHash, Is.Not.EqualTo(baseline.InputHash));
            Assert.That(binary.InputHash, Is.Not.EqualTo(baseline.InputHash));
            Assert.That(specs.InputHash, Is.Not.EqualTo(baseline.InputHash));
            Assert.That(specContent.InputHash, Is.Not.EqualTo(baseline.InputHash));
        }

        static string DifferentHash(string value)
        {
            return (value[0] == '0' ? "1" : "0") + value[1..];
        }
    }

    [Test]
    public async Task CompilerArtifactInputsProduceDeterministicProofs()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long ZBroken(long value) {
                    Contract.Ensures(Contract.Result<long>() > value);
                    return value;
                }
                public static long AIdentity(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var firstWorker = SharpProofWorker.Create(request.Budgets);
        using var secondWorker = SharpProofWorker.Create(request.Budgets);
        var first = await firstWorker.VerifyAsync(request);
        var second = await secondWorker.VerifyAsync(request);

        Assert.That(first.Errors, Is.Empty);
        Assert.That(first.ClaimResults.Length, Is.EqualTo(2));
        Assert.That(
            first.ClaimResults.Select(static record => record.Outcome),
            Is.EquivalentTo(new[] {
                WorkerClaimOutcome.Proven,
                WorkerClaimOutcome.Refuted
            }));
        Assert.That(
            first.ClaimResults.Select(record =>
                GetCallableId(first, record)),
            Is.Ordered);
        AssertSemanticallyEquivalent(first, second);
    }

    [Test]
    public async Task PartialMethodDiscoveryUsesOnlyTheImplementation()
    {
        using var project = TestProject.Create(
            (
                "Definition.cs",
                """
                public static partial class Subject {
                    public static partial long Identity(long value);
                }
                """),
            (
                "Implementation.cs",
                """
                using SharpProof.Attributes;
                public static partial class Subject {
                    public static partial long Identity(long value) {
                        Contract.Ensures(
                            Contract.Result<long>() == value);
                        return value;
                    }
                }
                """));
        var request = project.CreateRequest(cacheEnabled: false);
        var compilation = project.CreateCompilation();
        using var backend = new CountingNativeBackend();
        var target = new ClaimManifestBuilder(compilation)
            .Build()
            .Targets
            .Values
            .Single(candidate => candidate.Method.Name == "Identity");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(target.Method.PartialDefinitionPart, Is.Not.Null);
            Assert.That(target.Method.PartialImplementationPart, Is.Null);
            Assert.That(
                Path.GetFileName(target.Declaration!.SyntaxTree.FilePath),
                Is.EqualTo("Implementation.cs"));
        }

        using var worker = new SharpProofWorker(backend);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.Errors, Is.Empty,
            string.Join(", ", response.Errors.Select(static error =>
                error.Code)));
        Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
        Assert.That(
            response.ClaimResults[0].Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(backend.CallCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task GeneratedContractVerdictsMatchConcreteRuntime()
    {
        var cases = CreateRuntimeContractCases(seed: 23063, count: 24);
        using var project = TestProject.Create(
            CreateRuntimeContractSource(cases));
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty,
            string.Join(", ", response.Errors.Select(static error =>
                error.Code)));
        Assert.That(response.ClaimResults, Has.Length.EqualTo(cases.Length));

        var runtimeCompilation = project.CreateCompilation(
            CreateParseOptions(preprocessorSymbols: []));
        using var image = new MemoryStream();
        var emit = runtimeCompilation.Emit(image);
        Assert.That(
            emit.Success,
            Is.True,
            string.Join(
                Environment.NewLine,
                emit.Diagnostics.Select(static diagnostic =>
                    diagnostic.ToString())));

        RuntimeAssemblyTestHost.WithRuntimeAssembly(
            "SharpProof.Worker.Test.RuntimeContractOracle",
            image,
            assembly =>
        {
            var fixture = assembly.GetType(
                    "RuntimeContractOracle",
                    throwOnError: true)!;
            foreach (var item in cases)
            {
                var record = response.ClaimResults.Single(candidate =>
                    GetCallableId(response, candidate).Contains(
                        "." + item.MethodName + "(",
                        StringComparison.Ordinal));
                Assert.That(
                    record.Outcome,
                    Is.EqualTo(item.ExpectedStatus),
                    item.MethodName);

                var method = fixture.GetMethod(
                        item.MethodName,
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.Static) ??
                    throw new InvalidOperationException(
                        $"Runtime method '{item.MethodName}' is missing.");
                var runtimeWitnesses = 0;
                foreach (var input in item.Inputs)
                {
                    if (!item.Requires(input))
                    {
                        continue;
                    }

                    var result = (long)method.Invoke(null, [input])!;
                    var holds = item.Ensures(input, result);
                    if (!holds)
                    {
                        runtimeWitnesses++;
                    }

                    if (item.ExpectedStatus ==
                        WorkerClaimOutcome.Proven)
                    {
                        Assert.That(
                            holds,
                            Is.True,
                            $"{item.MethodName}({input})");
                    }
                }
                if (item.ExpectedStatus ==
                    WorkerClaimOutcome.Refuted)
                {
                    Assert.That(
                        runtimeWitnesses,
                        Is.GreaterThan(0),
                        item.MethodName);
                }
            }
        });
    }

    [Test]
    public async Task NarrowIntegralSourceDomainsAreHygienicAndExact()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static sbyte SByteIdentity(sbyte value) {
                    Contract.Ensures(
                        Contract.Result<sbyte>() >= sbyte.MinValue &&
                        Contract.Result<sbyte>() <= sbyte.MaxValue);
                    return value;
                }
                public static byte ByteIdentity(byte value) {
                    Contract.Ensures(
                        Contract.Result<byte>() >= byte.MinValue &&
                        Contract.Result<byte>() <= byte.MaxValue);
                    return value;
                }
                public static short Int16Identity(short value) {
                    Contract.Ensures(
                        Contract.Result<short>() >= short.MinValue &&
                        Contract.Result<short>() <= short.MaxValue);
                    return value;
                }
                public static ushort UInt16Identity(ushort value) {
                    Contract.Ensures(
                        Contract.Result<ushort>() >= ushort.MinValue &&
                        Contract.Result<ushort>() <= ushort.MaxValue);
                    return value;
                }
                public static char CharIdentity(char value) {
                    Contract.Ensures(
                        Contract.Result<char>() >= char.MinValue &&
                        Contract.Result<char>() <= char.MaxValue);
                    return value;
                }
                public static int Id(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return value;
                }
                public static uint UInt32Identity(uint value) {
                    Contract.Ensures(
                        Contract.Result<uint>() >= uint.MinValue &&
                        Contract.Result<uint>() <= uint.MaxValue);
                    return value;
                }
                public static long Int64Identity(long value) {
                    Contract.Ensures(
                        Contract.Result<long>() >= long.MinValue &&
                        Contract.Result<long>() <= long.MaxValue);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(8));
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Proven));
        var intIdentity = response.ClaimResults.Single(record =>
            GetCallableId(response, record).Contains(
                ".Id(",
                StringComparison.Ordinal));
        Assert.That(
            intIdentity.ProofCore,
            Is.Empty);
        Assert.That(
            response.ClaimResults
                .Where(record => !GetCallableId(response, record).Contains(
                    ".Int64Identity(",
                    StringComparison.Ordinal))
                .SelectMany(static record => record.ProofCore),
            Is.Empty);
    }

    [Test]
    public async Task SourceDomainsUseNativeBitvectorTypes()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Id(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        Assert.That(backend.Query.Factory.Semantics, Is.EqualTo(IrExecutionSemantics.Total));
        Assert.That(response.ClaimResults.Single().ProofCore, Is.Empty);
    }
    [Test]
    public async Task NativeProofCoreMarksItsBodyAssumptionsUsed()
    {
        using var project = TestProject.Create(BoundedIdentitySubjectSource);
        var request = project.CreateRequest(cacheEnabled: false);
        var snapshot = WorkerInputSnapshot.Load(
            request,
            WorkerCacheIdentity.Current,
            CancellationToken.None);
        var expectedUsedIds = snapshot.CompilerManifest.Callables.Single()
            .Clauses.Where(static clause =>
                clause.Kind == CompilerContractKind.Assume)
            .Select(static clause => clause.AssumptionId).ToArray();
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        var record = response.ClaimResults.Single();
        Assert.That(
            record.Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven),
            record.Reason.ToString());
        Assert.That(
            backend.Query.Factory.Semantics,
            Is.EqualTo(IrExecutionSemantics.Total));
        var userAssumptions = record.Assumptions
            .Where(static evidence =>
                evidence.Kind == WorkerAssumptionKind.UserAssume).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                userAssumptions.Count(static evidence => evidence.Used),
                Is.EqualTo(2));
            Assert.That(
                userAssumptions.Where(static evidence => evidence.Used).Select(static evidence => evidence.Id),
                Is.EquivalentTo(expectedUsedIds));
        }
    }

    [Test]
    public async Task ContradictoryLiteralPreconditionIsExplicitVacuityEvidence()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Impossible() {
                    Contract.Requires(false);
                    Contract.Ensures(false);
                    return 0;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var first = await worker.VerifyAsync(request);
        var response = await worker.VerifyAsync(request);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven,
            expectedVacuity: WorkerVacuityKind.ContradictoryPreconditions);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(response.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(CacheFiles(project), Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task EntryDomainsAndClosedAttributesProduceExplicitPreconditionVacuity()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static byte ByteImpossible(byte value) {
                    Contract.Requires(value > 300);
                    Contract.Ensures(false);
                    return value;
                }

                public static uint UIntImpossible(uint value) {
                    Contract.Requires(value > 5000000000L);
                    Contract.Ensures(false);
                    return value;
                }

                public static int PositiveImpossible(
                    [Positive] int value) {
                    Contract.Requires(value <= 0);
                    Contract.Ensures(false);
                    return value;
                }

                public static int RangeImpossible(
                    [InRange(5, 10)] int value) {
                    Contract.Requires(value < 5);
                    Contract.Ensures(false);
                    return value;
                }

                public static int AssumeOnly(int value) {
                    Contract.Assume(false);
                    Contract.Ensures(false);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);
        var contradictory = new[]
        {
            Result("ByteImpossible"),
            Result("UIntImpossible"),
            Result("PositiveImpossible"),
            Result("RangeImpossible")
        };
        var assumeOnly = Result("AssumeOnly");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.ClaimResults, Has.Length.EqualTo(5));
            Assert.That(
                contradictory.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                contradictory.Select(static result => result.Vacuity),
                Is.All.EqualTo(
                    WorkerVacuityKind.ContradictoryPreconditions));
            Assert.That(
                assumeOnly.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                assumeOnly.Vacuity,
                Is.EqualTo(WorkerVacuityKind.NoModeledNormalReturn));
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
        }

        WorkerClaimResult Result(string methodName)
        {
            return response.ClaimResults.Single(result =>
                GetCallableId(response, result).Contains(
                    "." + methodName + "(",
                    StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task MathAbsPostconditionsApplyOnlyToNormalCompletion()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int NonNegative(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= 0);
                    return Math.Abs(value);
                }
                public static int Positive(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() > 0);
                    return Math.Abs(value);
                }
                public static int NotMinimum(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() != int.MinValue);
                    return Math.Abs(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(3));
        WorkerClaimResult For(string methodName)
        {
            var matching = response.ClaimResults.Where(result =>
                    GetCallableId(response, result).Contains(
                        "." + methodName + "(",
                        StringComparison.Ordinal))
                .ToArray();
            Assert.That(
                matching,
                Has.Length.EqualTo(1),
                string.Join(", ", response.ClaimResults.Select(result =>
                    GetCallableId(response, result))));
            return matching[0];
        }

        var nonNegative = For("NonNegative");
        var positive = For("Positive");
        var notMinimum = For("NotMinimum");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                nonNegative.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven),
                nonNegative.Reason.ToString());
            Assert.That(
                positive.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted),
                positive.Reason.ToString());
            Assert.That(
                notMinimum.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven),
                notMinimum.Reason.ToString());
        }
        if (positive.Outcome == WorkerClaimOutcome.Refuted)
        {
            Assert.That(
                positive.Model.Single(value => value.Variable == "parameter:0").Value,
                Is.EqualTo("0"));
        }
    }

    [Test]
    public async Task CfgLoweredPartialSpecArgumentPreservesNormalCompletion()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static string ConcatBranch(long divisor) {
                    Contract.Ensures(divisor != 0);
                    return string.Concat(
                        1L / divisor == 0 ? "" : "value",
                        "");
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven,
            WorkerClaimReason.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(record.Model, Is.Empty);
        }
    }

    [Test]
    public async Task SpecResultFacetsProveConcatAndArrayEmptyContracts()
    {
        using var project = TestProject.Create(
            """
            #nullable enable
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static string Concat(string? left, string? right) {
                    Contract.Ensures(
                        Contract.Result<string>() != null);
                    return string.Concat(left, right);
                }

                public static int[] Empty() {
                    Contract.Ensures(
                        Contract.Result<int[]>() != null);
                    Contract.Ensures(
                        Contract.Result<int[]>().Length == 0);
                    var result = Array.Empty<int>();
                    return result;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.ClaimResults,
            Has.Length.EqualTo(3),
            string.Join(
                Environment.NewLine,
                response.ClaimResults.Select(record =>
                    GetCallableId(response, record) + " / " +
                    GetClaim(response, record).Ordinal + " / " +
                    record.Outcome + " / " +
                    record.Reason)));
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Proven));
        var concat = response.ClaimResults.Single(record =>
            GetCallableId(response, record).Contains(
                ".Concat(",
                StringComparison.Ordinal));
        var empty = response.ClaimResults
            .Where(record => GetCallableId(response, record).Contains(
                ".Empty",
                StringComparison.Ordinal))
            .ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                concat.ProofCore,
                Is.Empty);
            Assert.That(empty, Has.Length.EqualTo(2));
            foreach (var record in empty)
            {
                Assert.That(
                    record.ProofCore,
                    Is.Empty);
            }
        }
    }

    [Test]
    public async Task EnumerableCardinalityIsNotTreatedAsArrayCardinality()
    {
        using var project = TestProject.Create(
            """
            #nullable enable
            using System.Collections.Generic;
            using System.Linq;
            using SharpProof.Attributes;
            public static class Subject {
                public static IEnumerable<int> Empty() {
                    Contract.Ensures(
                        Contract.Result<IEnumerable<int>>() != null);
                    return Enumerable.Empty<int>();
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(
            response.Errors,
            Is.Empty,
            string.Join(
                Environment.NewLine,
                response.Errors.Select(error =>
                    error.Code + ": " + error.Message)));
        // Enumerable.Empty is an opaque call: its result is unknown.
        var record = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.CounterexampleNotReplayable);
        Assert.That(record.ProofCore, Is.Empty);
    }

    [Test]
    public async Task ArrayReferenceEqualityIsNotStructuralSequenceEquality()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static void Invalid(
                    [NotNull] int[] left,
                    [NotNull] int[] right) {
                    Contract.Requires(left.Length == 1);
                    Contract.Requires(right.Length == 1);
                    Contract.Ensures(
                        left == right || left[0] != right[0]);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Refuted,
            WorkerClaimReason.None);
        Assert.That(record.ProofCore, Is.Empty);
    }

    [Test]
    public async Task ArraySummaryDoesNotAuthorizeALaterImpureCallHavoc()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                private static int s_ambient;
                private static void TouchAmbient(ref int[] value) {
                    s_ambient++;
                    value = null;
                }

                public static int[] Unsafe() {
                    Contract.Ensures(
                        Contract.Result<int[]>() != null);
                    var result = Array.Empty<int>();
                    TouchAmbient(ref result);
                    return result;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.UnsupportedBody);
        Assert.That(record.ProofCore, Is.Empty);
    }

    [Test]
    public async Task AcyclicCfgLocalsBranchesAndMultipleReturnsAreProven()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool ThroughLocals(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    var local = value;
                    local = !!local;
                    return local;
                }

                public static bool Choose(
                    bool chooseLeft,
                    bool left,
                    bool right) {
                    Contract.Ensures(
                        Contract.Result<bool>() ==
                        (chooseLeft ? left : right));
                    if (chooseLeft) {
                        return left;
                    }
                    return right;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(
            response.ClaimResults.Select(static record => record.Reason),
            Is.All.EqualTo(WorkerClaimReason.None));
    }

    [Test]
    public async Task OldUsesEntryStateBeforeParameterMutation()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Flip(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() !=
                        Contract.Old(value));
                    value = !value;
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        _ = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven,
            WorkerClaimReason.None);
    }

    [Test]
    public async Task ReducibleLoopsAndDirectAcyclicSourceCallsAreProven()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static bool Read(bool value) => value;
                private static bool ReadAgain(bool value) => Read(value);

                public static bool Loop(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == false);
                    while (value) {
                        value = false;
                    }
                    return value;
                }

                public static bool Call(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    return ReadAgain(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        var loop = response.ClaimResults.Single(record =>
            GetCallableId(response, record).Contains(
                ".Loop(",
                StringComparison.Ordinal));
        var call = response.ClaimResults.Single(record =>
            GetCallableId(response, record).Contains(
                ".Call(",
                StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loop.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                loop.Reason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(call.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(call.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                call.ProofCore.Any(static item => item.StartsWith(
                    "source-summary:",
                    StringComparison.Ordinal)),
                Is.False);
        }
    }

    [Test]
    public async Task ExactImplementationIlSummaryProvesAnExternalCallChain()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Call(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    return ExternalImplementation.ReadAgain(value);
                }
            }
            """);
        project.AddImplementationReference(
            """
            public static class ExternalImplementation {
                private static bool Read(bool value) => value;
                public static bool ReadAgain(bool value) => Read(value);
            }
            """);
        var compilation = project.CreateCompilation();
        var external = compilation.GetTypeByMetadataName(
                "ExternalImplementation")!
            .GetMembers("ReadAgain")
            .OfType<IMethodSymbol>()
            .Single();
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single(candidate =>
            candidate.Method.MetadataName == "Call");
        var lowerer = new CompilerCallableLowerer(
            compilation,
            new IrFactory());
        var preparation = lowerer.Prepare(target);
        Assert.That(
            preparation.Total,
            Is.Not.Null);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven);
        Assert.That(
            result.ProofCore.Any(static item => item.StartsWith(
                "il-summary:",
                StringComparison.Ordinal)),
            Is.False);
    }

    [Test]
    public async Task ImplementationIlRemainderExcludesSignedOverflowInput()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int RemainderRelation(int left, int right) {
                    Contract.Requires(right != 0);
                    Contract.Ensures(left != int.MinValue || right != -1);
                    return ExternalRemainder.Remainder(left, right);
                }

                public static int RemainderOverflowVacuous(int left) {
                    Contract.Requires(left == int.MinValue);
                    Contract.Ensures(Contract.Result<int>() != 0);
                    return ExternalRemainder.Remainder(left, -1);
                }

                public static int RemainderNormalResult() {
                    Contract.Ensures(Contract.Result<int>() == 0);
                    return ExternalRemainder.Remainder(7, -1);
                }

                public static int DivisionControl(int left, int right) {
                    Contract.Requires(right != 0);
                    Contract.Ensures(left != int.MinValue || right != -1);
                    return ExternalRemainder.Divide(left, right);
                }

                public static long LongRemainderControl(long left, long right) {
                    Contract.Requires(right != 0);
                    Contract.Ensures(left != long.MinValue || right != -1);
                    return ExternalRemainder.LongRemainder(left, right);
                }
            }
            """);
        project.AddImplementationReference(
            """
            public static class ExternalRemainder {
                public static int Remainder(int left, int right) => left % right;
                public static int Divide(int left, int right) => left / right;
                public static long LongRemainder(long left, long right) =>
                    left % right;
            }
            """);
        var compilation = project.CreateCompilation();
        var subject = compilation.GetTypeByMetadataName("Subject")!;
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        foreach (var methodName in new[]
                 {
                     "RemainderRelation",
                     "RemainderOverflowVacuous",
                     "RemainderNormalResult",
                     "DivisionControl",
                     "LongRemainderControl"
                 })
        {
            var method = subject.GetMembers(methodName)
                .OfType<IMethodSymbol>()
                .Single();
            var callableId = DocumentationCommentId.CreateDeclarationId(method);
            Assert.That(callableId, Is.Not.Null, methodName);
            var claim = response.Manifest.Claims.Single(candidate =>
                candidate.CallableId == callableId);
            var result = response.ClaimResults.Single(candidate =>
                candidate.ClaimId == claim.ClaimId);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    result.Outcome,
                    Is.EqualTo(WorkerClaimOutcome.Proven),
                    methodName + ": " + result.Reason);
                Assert.That(
                    result.ProofCore.Any(static item => item.StartsWith(
                        "il-summary:",
                        StringComparison.Ordinal)),
                    Is.False,
                    methodName + " retained a legacy implementation-IL summary.");
            }
        }
    }

    [Test]
    public void ImplementationIlRejectsStackDepthBeyondDeclaredMaximum()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Verify(int left, int right) {
                    Contract.Ensures(true);
                    return ExternalStackDepth.Add(left, right);
                }
            }
            """);
        var implementationPath = project.AddImplementationReference(
            """
            public static class ExternalStackDepth
            {
                public static int Add(int left, int right)
                {
                    int result = left + right;
                    return result;
                }
            }
            """,
            OptimizationLevel.Debug);
        SetDeclaredMaxStack(
            implementationPath,
            "Add",
            declaredMaxStack: 1);
        var compilation = project.CreateCompilation();
        var target = new ClaimManifestBuilder(compilation).Build().Targets.Values.Single();
        var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(target);
        Assert.That(preparation.Total, Is.Not.Null);
        Assert.That(preparation.Total!.IsBodyAbstraction, Is.True);
    }

    [Test]
    public void ImplementationIlRejectsOversizedLocalSignatureBeforeMaterialization()
    {
        var localCount =
            129;
        var localDeclarations = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, localCount).Select(static index =>
                $"int local{index};"));
        using var project = TestProject.Create(
            "public static class Subject { }");
        var implementationPath = project.AddImplementationReference(
            $$"""
            public static class ExternalLocalBudget
            {
                public static int Read(int value)
                {
                    {{localDeclarations}}
                    return value;
                }
            }
            """,
            OptimizationLevel.Debug);
        using (var stream = File.OpenRead(implementationPath))
        using (var image = new PEReader(stream))
        {
            var metadata = image.GetMetadataReader();
            var methodHandle = metadata.MethodDefinitions.Single(handle =>
                string.Equals(
                    metadata.GetString(
                        metadata.GetMethodDefinition(handle).Name),
                    "Read",
                    StringComparison.Ordinal));
            var definition = metadata.GetMethodDefinition(methodHandle);
            var body = image.GetMethodBody(
                definition.RelativeVirtualAddress);
            var signature = metadata.GetStandaloneSignature(
                body.LocalSignature);
            var signatureReader = metadata.GetBlobReader(
                signature.Signature);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(body.GetILContent(), Has.Length.LessThan(32));
                Assert.That(
                    signatureReader.ReadSignatureHeader().Kind,
                    Is.EqualTo(SignatureKind.LocalVariables));
                Assert.That(
                    signatureReader.ReadCompressedInteger(),
                    Is.GreaterThan(
                        128));
            }
        }

        var compilation = project.CreateCompilation();
        var method = compilation.GetTypeByMetadataName(
                "ExternalLocalBudget")!
            .GetMembers("Read")
            .OfType<IMethodSymbol>()
            .Single();
        var provider = new CompilerTotalIlBodyProvider(compilation, null);
        Assert.That(provider.Resolve(method, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task MixedSourceAndImplementationSummariesSealDependencyEvidence()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static bool Local(bool value) =>
                    Inner(value);

                private static bool Inner(bool value) =>
                    ExternalMixed.Read(value);

                public static bool Call(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    return Local(value);
                }
            }
            """);
        project.AddImplementationReference(
            """
            public static class ExternalMixed {
                public static bool Read(bool value) => value;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(request.CompilerManifest.Path));
        var manifestJson = await File.ReadAllTextAsync(request.CompilerManifest.Path);
        var canonicalJson = CompilerManifestArtifactJson.Serialize(artifact);
        var roundTrip = CompilerManifestArtifactJson.Deserialize(canonicalJson);
        Assert.That(canonicalJson, Is.EqualTo(manifestJson));
        Assert.That(roundTrip.Callables.Single().Total, Is.Not.Null);
        Assert.That(roundTrip.Callables.Single().Body, Is.Null);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.ProofCore.Any(static item => item.StartsWith("source-summary:", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public async Task ImplementationIlBranchesAndInt32WrappingRemainExact()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Select(
                    bool chooseLeft,
                    int left,
                    int right) {
                    Contract.Ensures(
                        Contract.Result<int>() ==
                        (chooseLeft ? left : right));
                    return ExternalScalar.Select(
                        chooseLeft,
                        left,
                        right);
                }

                public static int Increment(int value) {
                    Contract.Ensures(
                        value != int.MaxValue ||
                        Contract.Result<int>() == int.MinValue);
                    return ExternalScalar.Increment(value);
                }
            }
            """);
        project.AddImplementationReference(
            """
            public static class ExternalScalar {
                public static int Select(
                    bool chooseLeft,
                    int left,
                    int right) => chooseLeft ? left : right;

                public static int Increment(int value) =>
                    unchecked(value + 1);
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                response.ClaimResults,
                Has.All.Matches<WorkerClaimResult>(result =>
                    !result.ProofCore.Any(static item => item.StartsWith(
                        "il-summary:",
                        StringComparison.Ordinal))));
        }
    }

    [TestCase("&", "&&")]
    [TestCase("|", "||")]
    [TestCase("^", "!=")]
    public async Task ImplementationIlBooleanOperatorsProveAndRefuteTruthTables(string bodyOperator, string contractOperator)
    {
        using var project = TestProject.Create($$"""
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Correct(bool left, bool right) {
                    Contract.Ensures(Contract.Result<bool>() == (left {{contractOperator}} right));
                    return ExternalBoolean.Apply(left, right);
                }
                public static bool Incorrect(bool left, bool right) {
                    Contract.Ensures(Contract.Result<bool>() != (left {{contractOperator}} right));
                    return ExternalBoolean.Apply(left, right);
                }
            }
            """);
        project.AddImplementationReference($$"""
            public static class ExternalBoolean {
                public static bool Apply(bool left, bool right) => left {{bodyOperator}} right;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        foreach (var result in response.ClaimResults)
        {
            var correct = GetCallableId(response, result).Contains(".Correct(", StringComparison.Ordinal);
            Assert.That(result.Outcome, Is.EqualTo(correct ? WorkerClaimOutcome.Proven : WorkerClaimOutcome.Refuted), result.Reason.ToString());
        }
    }

    [Test]
    public void ImplementationIlScalarOpcodeMatrixUsesTotalBodies()
    {
        var signatures = new (string ReturnType, string Name, string Parameters,
            string Arguments)[]
        {
            ("int", "MinusOne", "", ""),
            ("int", "Small", "", ""),
            ("int", "Wide", "", ""),
            ("long", "Int64Literal", "", ""),
            ("bool", "False", "", ""),
            ("int", "Sub", "int a, int b", "a, b"),
            ("int", "Mul", "int a, int b", "a, b"),
            ("int", "Div", "int a, int b", "a, b"),
            ("int", "Rem", "int a, int b", "a, b"),
            ("int", "CheckedAdd", "int a, int b", "a, b"),
            ("long", "CheckedLongAdd", "long a, long b", "a, b"),
            ("long", "LongAdd", "long a, long b", "a, b"),
            ("int", "Neg32", "int value", "value"),
            ("long", "Neg64", "long value", "value"),
            ("bool", "And", "bool a, bool b", "a, b"),
            ("bool", "Or", "bool a, bool b", "a, b"),
            ("bool", "Xor", "bool a, bool b", "a, b"),
            ("bool", "Eq", "int a, int b", "a, b"),
            ("bool", "Gt", "int a, int b", "a, b"),
            ("bool", "Lt", "int a, int b", "a, b"),
            ("bool", "BoolLiteralRight", "bool value", "value"),
            ("bool", "BoolLiteralLeft", "bool value", "value"),
            ("int", "EqBranch", "int a, int b, int yes, int no", "a, b, yes, no"),
            ("int", "NeBranch", "int a, int b, int yes, int no", "a, b, yes, no"),
            ("int", "LtBranch", "int a, int b, int yes, int no", "a, b, yes, no"),
            ("int", "LeBranch", "int a, int b, int yes, int no", "a, b, yes, no"),
            ("int", "GtBranch", "int a, int b, int yes, int no", "a, b, yes, no"),
            ("int", "GeBranch", "int a, int b, int yes, int no", "a, b, yes, no"),
            ("int", "IntegerCondition", "int value, int yes, int no", "value, yes, no"),
            ("int", "PopCall", "int value", "value"),
            ("int", "NestedCall", "int value", "value"),
            ("int", "InadmissibleCall", "int value", "value")
        };
        var subjectMethods = string.Join(
            Environment.NewLine,
            signatures.Select(static signature =>
                $$"""
                public static {{signature.ReturnType}} Verify{{signature.Name}}(
                    {{signature.Parameters}}) {
                    Contract.Ensures(true);
                    return ExternalIlMatrix.{{signature.Name}}(
                        {{signature.Arguments}});
                }
                """));
        using var project = TestProject.Create(
            "using SharpProof.Attributes; public static class Subject {" +
            subjectMethods +
            "}");
        project.AddImplementationReference(
            """
            public static class ExternalIlMatrix
            {
                public static int MinusOne() => -1;
                public static int Small() => 42;
                public static int Wide() => 1000;
                public static long Int64Literal() => 0x123456789L;
                public static bool False() => false;
                public static int Sub(int a, int b) => unchecked(a - b);
                public static int Mul(int a, int b) => unchecked(a * b);
                public static int Div(int a, int b) => a / b;
                public static int Rem(int a, int b) => a % b;
                public static int CheckedAdd(int a, int b) => checked(a + b);
                public static long CheckedLongAdd(long a, long b) =>
                    checked(a + b);
                public static long LongAdd(long a, long b) =>
                    unchecked(a + b);
                public static int Neg32(int value) => unchecked(-value);
                public static long Neg64(long value) => unchecked(-value);
                public static bool And(bool a, bool b) => a & b;
                public static bool Or(bool a, bool b) => a | b;
                public static bool Xor(bool a, bool b) => a ^ b;
                public static bool Eq(int a, int b) => a == b;
                public static bool Gt(int a, int b) => a > b;
                public static bool Lt(int a, int b) => a < b;
                public static bool BoolLiteralRight(bool value) =>
                    value == false;
                public static bool BoolLiteralLeft(bool value) =>
                    false == value;
                public static int EqBranch(
                    int a, int b, int yes, int no) =>
                    a == b ? yes : no;
                public static int NeBranch(
                    int a, int b, int yes, int no) =>
                    a != b ? yes : no;
                public static int LtBranch(
                    int a, int b, int yes, int no) =>
                    a < b ? yes : no;
                public static int LeBranch(
                    int a, int b, int yes, int no) =>
                    a <= b ? yes : no;
                public static int GtBranch(
                    int a, int b, int yes, int no) =>
                    a > b ? yes : no;
                public static int GeBranch(
                    int a, int b, int yes, int no) =>
                    a >= b ? yes : no;
                public static int IntegerCondition(
                    int value, int yes, int no) =>
                    value != 0 ? yes : no;

                private static int Identity(int value) => value;

                public static int PopCall(int value)
                {
                    Identity(value);
                    return value;
                }

                private static string Text() => "text";

                public static int InadmissibleCall(int value)
                {
                    _ = Text();
                    return value;
                }

                private static class Nested
                {
                    internal static int Identity(int value) => value;
                }

                public static int NestedCall(int value) =>
                    Nested.Identity(value);
            }
            """);
        var compilation = project.CreateCompilation();
        var targets = new ClaimManifestBuilder(compilation).Build().Targets.Values
            .ToDictionary(static target => target.Method.Name, StringComparer.Ordinal);
        foreach (var signature in signatures)
        {
            var targetName = "Verify" + signature.Name;
            var preparation = new CompilerCallableLowerer(compilation, new IrFactory()).Prepare(targets[targetName]);
            Assert.That(preparation.Total, Is.Not.Null, targetName);
            Assert.That(preparation.Total!.IsBodyAbstraction || preparation.Total.Program.Blocks
                .SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Any(),
                Is.EqualTo(targetName == "VerifyInadmissibleCall"), targetName);
        }
    }

    [Test]
    public void DebugImplementationIlCoversLocalFormsAndBoundsWideSignatures()
    {
        var localDeclarations = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 257).Select(static index =>
                index == 0
                    ? "int local0 = value;"
                    : $"int local{index} = local{index - 1};"));
        var parameters = string.Join(
            ", ",
            Enumerable.Range(0, 257).Select(static index =>
                $"int value{index}"));
        var arguments = string.Join(
            ", ",
            Enumerable.Range(0, 257).Select(static index =>
                index.ToString(CultureInfo.InvariantCulture)));
        using var project = TestProject.Create(
            $$"""
            using SharpProof.Attributes;
            public static class Subject
            {
                public static long VerifyRoundTrip(
                    bool flag,
                    int value,
                    long wide)
                {
                    Contract.Ensures(true);
                    return ExternalDebugLocals.RoundTrip(flag, value, wide);
                }

                public static int VerifyAssignmentValue(int value)
                {
                    Contract.Ensures(true);
                    return ExternalDebugLocals.AssignmentValue(value);
                }

                public static int VerifyManyLocals(int value)
                {
                    Contract.Ensures(true);
                    return ExternalDebugLocals.ManyLocals(value);
                }

                public static int VerifyManyParameters()
                {
                    Contract.Ensures(true);
                    return ExternalDebugLocals.ManyParameters({{arguments}});
                }
            }
            """);
        project.AddImplementationReference(
            $$"""
            public static class ExternalDebugLocals
            {
                public static long RoundTrip(
                    bool flag,
                    int value,
                    long wide)
                {
                    bool local0 = flag;
                    int local1 = value;
                    long local2 = wide;
                    int local3 = local1;
                    long local4 = local2;
                    if (local0)
                    {
                        local3 = local1;
                        local4 = local2;
                    }

                    return local4;
                }

                public static int AssignmentValue(int value)
                {
                    int local;
                    return local = value;
                }

                public static int ManyLocals(int value)
                {
                    {{localDeclarations}}
                    return local256;
                }

                public static int ManyParameters({{parameters}}) => value256;
            }
            """,
            OptimizationLevel.Debug);
        var compilation = project.CreateCompilation();
        var targets = new ClaimManifestBuilder(compilation).Build().Targets.Values
            .ToDictionary(static target => target.Method.Name, StringComparer.Ordinal);

        foreach (var targetName in new[]
                 {
                     "VerifyRoundTrip",
                     "VerifyAssignmentValue",
                     "VerifyManyLocals"
                 })
        {
            var lowerer = new CompilerCallableLowerer(compilation, new IrFactory());
            var preparation = lowerer.Prepare(targets[targetName]);
            Assert.That(preparation.Total, Is.Not.Null, targetName);
            Assert.That(preparation.Total!.IsBodyAbstraction || preparation.Total.Program.Blocks
                .SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Any(),
                Is.EqualTo(targetName == "VerifyManyLocals"), targetName);
        }

        var wideLowerer = new CompilerCallableLowerer(
            compilation,
            new IrFactory());
        var widePreparation = wideLowerer.Prepare(
            targets["VerifyManyParameters"]);
        Assert.That(
            widePreparation.Total!.IsBodyAbstraction || widePreparation.Total.Program.Blocks
                .SelectMany(block => block.Instructions).OfType<IrCallInstruction>().Any(),
            Is.True);
    }

    [Test]
    public async Task CyclicImplementationIlAbstainsWithoutTrustingTheBody()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Loop(bool value) {
                    Contract.Ensures(Contract.Result<bool>() == value);
                    return ExternalCycles.Loop(value);
                }

                public static bool Recurse(bool value) {
                    Contract.Ensures(Contract.Result<bool>() == value);
                    return ExternalCycles.Recurse(value);
                }
            }
            """);
        project.AddImplementationReference(
            """
            public static class ExternalCycles {
                public static bool Loop(bool value) {
                    while (value) { }
                    return value;
                }

                public static bool Recurse(bool value) =>
                    value || Recurse(value);
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        var loop = response.ClaimResults.Single(record =>
            GetCallableId(response, record).Contains(".Loop(", StringComparison.Ordinal));
        var recursion = response.ClaimResults.Single(record =>
            GetCallableId(response, record).Contains(".Recurse(", StringComparison.Ordinal));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(loop.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(loop.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(recursion.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(recursion.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(recursion.ProofCore, Is.Empty);
        }
    }

    [Test]
    public async Task ReferenceAssemblyIsNotImplementationProofAuthority()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Call(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() == value);
                    return ReferenceOnly.Read(value);
                }
            }
            """);
        project.AddImplementationReference(
            """
            using System.Runtime.CompilerServices;
            [assembly: ReferenceAssembly]
            public static class ReferenceOnly {
                public static bool Read(bool value) => value;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var result = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.CounterexampleNotReplayable);
        Assert.That(result.ProofCore, Is.Empty);
    }

    [Test]
    public async Task AuditedSpecificationPackRequiresExplicitOptIn()
    {
        using var project = TestProject.Create(MaximumSubjectSource);
        project.UseNetCoreReferencePack();
        var withoutRequest = project.CreateRequest(cacheEnabled: false);
        using var withoutPackWorker = SharpProofWorker.Create(
            withoutRequest.Budgets);
        var withoutPack = await withoutPackWorker.VerifyAsync(
            withoutRequest);

        var withRequest = project.CreateRequest(
            cacheEnabled: false,
            specificationPacks: ["dotnet.scalar"]);
        var withArtifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(withRequest.CompilerManifest.Path));
        var totalArtifact = withArtifact.Callables.Single().Total;
        using var withPackWorker = SharpProofWorker.Create(
            withRequest.Budgets);
        var withPack = await withPackWorker.VerifyAsync(
            withRequest);

        Assert.That(withoutPack.Errors, Is.Empty);
        Assert.That(withPack.Errors, Is.Empty);
        var disabled = withoutPack.ClaimResults.Single();
        var enabled = withPack.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                disabled.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                disabled.Reason,
                Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(enabled.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(enabled.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                enabled.ProofCore.Any(static item => item.StartsWith(
                    "spec-pack:dotnet.scalar@1:",
                    StringComparison.Ordinal)),
                Is.False);
            Assert.That(totalArtifact, Is.Not.Null);
            Assert.That(withArtifact.SpecificationPackIds, Has.Length.EqualTo(1));
            Assert.That(withArtifact.SpecificationPackIds.Single(), Is.EqualTo("dotnet.scalar"));
        }

        withArtifact.Callables.Single().Total!.Graph.Semantics = IrExecutionSemantics.Legacy;
        Assert.That(
            new Action(() => CompilerManifestArtifactJson.DecodeCallables(withArtifact)),
            Throws.TypeOf<InvalidDataException>());
    }

    [TestCase("Contract.Result<int>() == (left >= right ? left : right)", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<int>() >= left", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<int>() >= right", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<int>() == left", WorkerClaimOutcome.Refuted)]
    public async Task NativeSpecificationPackRequiresExplicitOptIn(string predicate, WorkerClaimOutcome expected)
    {
        using var project = TestProject.Create($$"""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int left, int right) {
                    Contract.Ensures({{predicate}});
                    return Math.Max(left, right);
                }
            }
            """);
        project.UseNetCoreReferencePack();
        var disabledRequest = project.CreateRequest(cacheEnabled: false);
        using var disabledWorker = SharpProofWorker.CreateNative(disabledRequest.Budgets);
        var disabled = await disabledWorker.VerifyAsync(disabledRequest);
        Assert.That(disabled.Errors, Is.Empty);
        Assert.That(disabled.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(disabled.ClaimResults.Single().ProofCore, Is.Empty);

        var enabledRequest = project.CreateRequest(cacheEnabled: false, specificationPacks: ["dotnet.scalar"]);
        using var enabledWorker = SharpProofWorker.CreateNative(enabledRequest.Budgets);
        var enabled = await enabledWorker.VerifyAsync(enabledRequest);
        Assert.That(enabled.Errors, Is.Empty);
        Assert.That(enabled.ClaimResults.Single().Outcome, Is.EqualTo(expected));
        Assert.That(enabled.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.None));
    }

    [Test]
    public async Task NativeSpecificationPackPreservesNamedArgumentEvaluationOrder()
    {
        using var project = TestProject.Create("""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value == 0);
                    Contract.Ensures(Contract.Result<int>() == 2 && value == 2);
                    return Math.Max(val2: ++value, val1: value *= 2);
                }
            }
            """);
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, specificationPacks: ["dotnet.scalar"]);
        using var worker = SharpProofWorker.CreateNative(request.Budgets);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task NativeSpecificationPackDoesNotTrustAnImpersonatedFrameworkType()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            namespace System { public static class Math { public static int Max(int left, int right) => -1; } }
            public static class Subject {
                public static int Target(int left, int right) {
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return System.Math.Max(left, right);
                }
            }
            """);
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, specificationPacks: ["dotnet.scalar"]);
        using var worker = SharpProofWorker.CreateNative(request.Budgets);
        var response = await worker.VerifyAsync(request);
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public void UnknownSpecificationPackFailsClosed()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Identity(bool value) {
                    Contract.Ensures(Contract.Result<bool>() == value);
                    return value;
                }
            }
            """);

        Assert.That(
            (Action)(() => project.CreateRequest(
                    cacheEnabled: false,
                    specificationPacks: ["missing-pack"])),
            Throws.InvalidOperationException.With.Message.Contains(
                "Unknown SharpProof specification pack"));
    }

    [Test]
    public async Task NestedSameShapeCallsRemainBoundToCompilerIdentity()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Nested(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= 0);
                    return Math.Abs(Math.Sign(value));
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        // Math.Sign is opaque; the modeled Math.Abs alone proves the result.
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven, WorkerClaimReason.None);
    }

    [Test]
    public async Task SourceCallProducesReplayedCounterexample()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private static int Identity(int value) => value;

                public static int Call(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() > value);
                    return Identity(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Refuted,
            WorkerClaimReason.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(record.ProofCore, Is.Empty);
            Assert.That(record.Model, Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task ElidedContractCallsKeepRelationalSummariesComposable()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;

            public static class Subject
            {
                private static int Plain(int value) => value;

                private static int WithRequires(int value)
                {
                    Contract.Requires(value >= 0);
                    return value;
                }

                private static int WithEnsures(int value)
                {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }

                private static int WithAssume(int value)
                {
                    Contract.Assume(value >= 0);
                    return value;
                }

                private static int RequiresPositive(int value)
                {
                    Contract.Requires(value > 0);
                    return value;
                }

                public static int CallPlain(int value)
                {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return Plain(value);
                }

                public static int CallRequires(int value)
                {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return WithRequires(value);
                }

                public static int CallEnsures(int value)
                {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return WithEnsures(value);
                }

                public static int CallAssume(int value)
                {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return WithAssume(value);
                }

                public static int InvalidCalleePrecondition(int value)
                {
                    Contract.Requires(value >= 0);
                    Contract.Ensures(Contract.Result<int>() > 0);
                    return RequiresPositive(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(6));
        WorkerClaimResult ResultFor(string methodName)
        {
            return response.ClaimResults.Single(result =>
                GetCallableId(response, result).Contains(
                    methodName,
                    StringComparison.Ordinal));
        }

        foreach (var methodName in new[]
                 {
                     "CallPlain",
                     "CallRequires",
                     "CallEnsures",
                     "CallAssume"
                 })
        {
            Assert.That(
                ResultFor(methodName).Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven),
                $"{methodName} did not compose its relational summary.");
        }
        Assert.That(
            ResultFor("WithEnsures").Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(
            ResultFor("InvalidCalleePrecondition").Outcome,
            Is.Not.EqualTo(WorkerClaimOutcome.Proven),
            "A callee Requires clause must not become an assumption for the caller.");
    }

    [Test]
    public async Task WholeBodyReplayCoversTrivialStateAndAnUnreachedSpecCall()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static void Trivial() {
                    Contract.Ensures(false);
                }

                public static int UnusedInput(int value) {
                    Contract.Ensures(false);
                    return 0;
                }

                public static bool Mutate(bool value) {
                    Contract.Ensures(
                        Contract.Result<bool>() ==
                        Contract.Old(value));
                    value = !value;
                    return value;
                }

                public static int AvoidCall(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() > 0);
                    if (value == 0) {
                        return 0;
                    }
                    var ignored = string.Concat("", "");
                    return 1;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(4));
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.None));
            Assert.That(
                response.ClaimResults.SelectMany(static result => result.Model)
                    .Any(static value => value.Variable.StartsWith(
                        "variable:", StringComparison.Ordinal)),
                Is.False);
        }
    }

    [Test]
    public async Task ConstructorInitializersCannotProduceAFalseRefutation()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public sealed class Subject {
                private readonly int value = Throw();

                public Subject() {
                    Contract.Ensures(false);
                }

                private static int Throw() =>
                    throw new InvalidOperationException();
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        _ = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.UnsupportedBody);
    }

    [Test]
    public async Task WorkerProductPathUsesNativeApiSpecResultEvidence()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static string Concat(string left, string right) {
                    Contract.Ensures(
                        Contract.Result<string>() != null);
                    return string.Concat(left, right);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        Assert.That(backend.Query.Factory.Semantics, Is.EqualTo(IrExecutionSemantics.Total));
        Assert.That(response.ClaimResults.Single().ProofCore,
            Does.Not.Contain("spec:bcl.string.concat.string-string"));
    }
    [Test]
    public async Task NarrowIntegralCounterexampleStaysInsideSourceDomain()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static byte NotAlwaysBelowMaximum(byte value) {
                    Contract.Ensures(
                        Contract.Result<byte>() < byte.MaxValue);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Refuted);
        Assert.That(
            record.Model.Single(value =>
                value.Variable == "parameter:0").Value,
            Is.EqualTo(byte.MaxValue.ToString(
                System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public async Task WidthSensitiveArithmeticAndConversionsUseTypedSemantics()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int UncheckedContract(int value) {
                    Contract.Ensures(
                        unchecked(Contract.Result<int>() + 1) >
                        Contract.Result<int>());
                    return value;
                }
                public static int CheckedContract(int value) {
                    Contract.Ensures(
                        checked(Contract.Result<int>() + 1) >
                        Contract.Result<int>());
                    return value;
                }
                public static int UncheckedBody(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return unchecked((int)value);
                }
                public static int CheckedBody(long value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= int.MinValue);
                    return checked((int)value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(4));
        foreach (var record in response.ClaimResults)
        {
            var callableId = GetCallableId(response, record);
            if (callableId.Contains(".CheckedContract(", StringComparison.Ordinal))
            {
                Assert.That(record.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
                Assert.That(record.Reason, Is.EqualTo(WorkerClaimReason.PostconditionMayBeUndefined));
            }
            else if (callableId.Contains(".UncheckedContract(", StringComparison.Ordinal))
            {
                Assert.That(record.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
                Assert.That(record.Reason, Is.EqualTo(WorkerClaimReason.None));
            }
            else
            {
                Assert.That(record.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), callableId);
                Assert.That(record.Reason, Is.EqualTo(WorkerClaimReason.None), callableId);
            }
        }
    }

    [Test]
    public async Task BodyNormalCompletionConstrainsPartialCorrectness()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long DivideOverflow(long value) {
                    Contract.Requires(value == long.MinValue);
                    Contract.Ensures(false);
                    return value / -1L;
                }

                public static long DivideByZero(long value) {
                    Contract.Ensures(false);
                    return value / 0L;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Proven),
            string.Join(
                Environment.NewLine,
                response.ClaimResults.Select(record =>
                    GetCallableId(response, record) + ": " +
                    record.Outcome + " / " +
                    record.Reason)));
        Assert.That(
            response.ClaimResults.Select(static record => record.ProofCore),
            Has.All.Matches<string[]>(core => core.Any(static item => item.StartsWith("normal-completion:", StringComparison.Ordinal))));
        Assert.That(
            response.ClaimResults.Select(static record => record.Vacuity),
            Is.All.EqualTo(WorkerVacuityKind.NoModeledNormalReturn));
    }

    [Test]
    public async Task UnusedAssignmentFaultsConstrainNormalCompletion()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Divide(long divisor) {
                    Contract.Ensures(divisor != 0L);
                    var unused = 1L / divisor;
                    return 7L;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Proven,
            WorkerClaimReason.None,
            WorkerVacuityKind.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(record.ProofCore, Has.Some.StartsWith("edge:"));
            Assert.That(record.Model, Is.Empty);
        }
    }

    [Test]
    public async Task UndefinedPostconditionProducesTypedNonfatalUnknown()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Zero(long divisor) {
                    Contract.Ensures(
                        Contract.Result<long>() / divisor == 0L);
                    return 0L;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        _ = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.PostconditionMayBeUndefined);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
        }
    }

    [Test]
    public async Task MismatchedResultTypeAbstains()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static int Id(int value) {
                    Contract.Ensures(
                        checked(Contract.Result<long>() + 1L) >
                        Contract.Result<long>());
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty,
            string.Join(", ", response.Errors.Select(static error =>
                error.Code)));
        _ = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.UnsupportedContract);
    }

    [Test]
    public async Task StringConcatEqualityAbstainsWithoutContentEncoding()
    {
        using var project = TestProject.Create(
            """
            #nullable enable
            using SharpProof.Attributes;
            public static class Subject {
                public static string? ResultIntrinsic(string? value) {
                    Contract.Ensures(
                        Contract.Result<string?>() + "" ==
                        Contract.Result<string?>());
                    return value;
                }
                public static string? DirectParameter(string? value) {
                    Contract.Ensures(value + "" == value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(
            response.ClaimResults.Select(static record => record.Reason),
            Is.All.EqualTo(
                WorkerClaimReason.UnsupportedBody));
    }

    [Test]
    public async Task NativeDepthAndUnsupportedInstanceCallAbstain()
    {
        var response = await RunAsync(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private class Reader {
                    internal virtual long Read(long value) => value;
                }
                public static long Unsupported(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return new Reader().Read(value);
                }
                public static long Deep(long value) {
                    Contract.Ensures(
                        value > 0 && value > 1 && value > 2 && value > 3);
                    return value;
                }
            }
            """,
            cacheEnabled: false,
            maximumExpressionDepth: 3);

        Assert.That(
            response.ClaimResults.Select(static record => record.Reason),
            Is.EquivalentTo(new[] {
                WorkerClaimReason.DeepPostcondition,
                WorkerClaimReason.CounterexampleNotReplayable
            }));
        Assert.That(
            response.ClaimResults.All(static record =>
                record.Outcome == WorkerClaimOutcome.Unknown),
            Is.True);
    }

    [Test]
    public async Task TrailingAssumeCannotBecomeAnEntryAssumption()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Invalid() {
                    Contract.Ensures(Contract.Result<long>() > 0);
                    return -1;
                    Contract.Assume(false);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty,
            string.Join(", ", response.Errors.Select(static error =>
                error.Code)));
        _ = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.UnsupportedContract);
    }

    [Test]
    public async Task EffectOnlySelectionProducesAccountableProvenClaim()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                public static int Value() => 1;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        _ = AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.Manifest.Claims[0].Kind,
                Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(response.CallableResults, Has.Length.EqualTo(1));
            Assert.That(
                response.CallableResults[0].Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(
                response.CallableResults[0].Reason,
                Is.EqualTo(WorkerCallableCoverageReason.None));
        }
    }

    [Test]
    public async Task CacheOnOffOutputsMatchAndTerminalOutcomesAreReused()
    {
        using var project = TestProject.Create(RefutationSource);
        var enabled = project.CreateRequest(cacheEnabled: true);
        using var backend = new CountingNativeBackend();
        using var firstWorker = new SharpProofWorker(backend);
        var first = await firstWorker.VerifyAsync(enabled);
        var firstQueries = backend.CallCount;
        Assert.That(firstQueries, Is.GreaterThan(0));
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);
        var second = await firstWorker.VerifyAsync(enabled);
        Assert.That(backend.CallCount, Is.EqualTo(firstQueries));
        AssertSemanticallyEquivalent(first, second);

        var disabled = project.CreateRequest(cacheEnabled: false);
        using var disabledBackend = new CountingNativeBackend();
        using var disabledWorker = new SharpProofWorker(disabledBackend);
        var withoutCache = await disabledWorker.VerifyAsync(disabled);
        AssertSemanticallyEquivalent(first, withoutCache);
    }

    [Test]
    public async Task CompleteSemanticUnknownOutcomesAreReusedFromTheCache()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unknown(
                BackendFailureReason.ResourceLimit));
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(1));
        Assert.That(
            first.ClaimResults.Single().Outcome,
            Is.EqualTo(WorkerClaimOutcome.Unknown));
        AssertSemanticallyEquivalent(first, second);
        Assert.That(
            CacheFiles(project),
            Has.Length.EqualTo(1));
    }

    [TestCase(
        BackendFailureReason.Unavailable,
        WorkerClaimReason.BackendUnavailable,
        WorkerRunFailureReason.BackendUnavailable)]
    [TestCase(
        BackendFailureReason.InfrastructureFailure,
        WorkerClaimReason.InfrastructureFailure,
        WorkerRunFailureReason.InfrastructureFailure)]
    [TestCase(
        BackendFailureReason.MalformedResult,
        WorkerClaimReason.MalformedBackendResult,
        WorkerRunFailureReason.MalformedResult)]
    public async Task FatalBackendFailuresFailTheRun(
        BackendFailureReason backendReason,
        WorkerClaimReason claimReason,
        WorkerRunFailureReason runReason)
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new CountingBackend(BackendCheckResult.Unknown(backendReason)));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.FailureReason, Is.EqualTo(runReason));
            Assert.That(
                response.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults.Single().Reason,
                Is.EqualTo(claimReason),
                string.Join(", ", response.Errors.Select(static error =>
                    error.Code)));
        }
    }

    [Test]
    public async Task SolverIncompletenessLeavesTheRunCompleteWithAnUnknownClaim()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new CountingBackend(BackendCheckResult.Unknown(
                BackendFailureReason.Incomplete)));

        var response = await worker.VerifyAsync(request);
        var claim = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
            Assert.That(
                response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(
                response.CallableResults.Single().Reason,
                Is.EqualTo(WorkerCallableCoverageReason.SemanticUnknown));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task UnexpectedBackendExceptionBecomesTypedInfrastructureFailure()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new ThrowingBackend("Injected unexpected backend failure."));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.InfrastructureFailure));
            Assert.That(
                response.CallableResults.Single().Reason,
                Is.EqualTo(
                    WorkerCallableCoverageReason.InfrastructureFailure));
            Assert.That(
                response.ClaimResults.Single().Reason,
                Is.EqualTo(WorkerClaimReason.InfrastructureFailure));
        }
    }

    [Test]
    public async Task UnexpectedCounterexampleReplayFailureStillFailsTheRun()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(new SpuriousModelBackend());

        var response = await worker.VerifyAsync(request);

        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
        Assert.That(response.FailureReason,
            Is.EqualTo(WorkerRunFailureReason.CounterexampleReplayFailed));
        Assert.That(response.ClaimResults.Single().Reason,
            Is.EqualTo(WorkerClaimReason.CounterexampleReplayFailed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void FatalClaimTakesPrecedenceOverAnotherCallableTimeout(bool reverseRecords)
    {
        using var project = TestProject.Create(ConcurrentSubjectsSource);
        var request = project.CreateRequest(cacheEnabled: false);
        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, CancellationToken.None);
        var assembled = AssembleFatalAndTimedOut(request, snapshot, reverseRecords);
        var response = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.SerializeResponse(assembled))!;
        var validation = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(request),
            snapshot.InputHash, snapshot.CompilerManifest.Manifest, request, Launcher.Program.ExpectedVersions());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(validation.IsValid, Is.True, string.Join(';', validation.Errors.Select(error => error.Code)));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.BackendUnavailable));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.EqualTo((WorkerClaimReason[])[WorkerClaimReason.BackendUnavailable, WorkerClaimReason.MethodTimeout]));
            Assert.That(response.CallableResults.Select(static result => result.Reason),
                Is.EqualTo((WorkerCallableCoverageReason[])[WorkerCallableCoverageReason.InfrastructureFailure,
                    WorkerCallableCoverageReason.MethodTimeout]));
            Assert.That(response.ClaimResults.Select(static result => result.ClaimId),
                Is.EqualTo(snapshot.CompilerManifest.Manifest.Claims.Select(static claim => claim.ClaimId)));
            Assert.That(VerificationCache.IsCacheable(response, snapshot.InputHash, snapshot.CompilerManifest.Manifest), Is.False);
        }
    }

    internal static WorkerVerifyResponse AssembleFatalAndTimedOut(
        WorkerVerifyRequest request, WorkerInputSnapshot snapshot, bool reverseRecords)
    {
        var manifest = snapshot.CompilerManifest.Manifest;
        Assert.That(manifest.Callables, Has.Length.EqualTo(2));
        var callables = manifest.Callables.Select((callable, index) => new WorkerCallableResult
        {
            CallableId = callable.CallableId,
            Coverage = WorkerCallableCoverage.Incomplete,
            Reason = index == 0 ? WorkerCallableCoverageReason.InfrastructureFailure : WorkerCallableCoverageReason.MethodTimeout,
            Assumptions = callable.Assumptions
        }).ToArray();
        var claims = manifest.Callables.SelectMany((callable, index) => callable.ClaimIds.Select(claimId => new WorkerClaimResult
        {
            ClaimId = claimId,
            Outcome = WorkerClaimOutcome.Unknown,
            Reason = index == 0 ? WorkerClaimReason.BackendUnavailable : WorkerClaimReason.MethodTimeout,
            Assumptions = callable.Assumptions
        })).ToArray();
        if (reverseRecords)
        {
            Array.Reverse(callables);
            Array.Reverse(claims);
        }
        var run = WorkerResultAssembler.Classify(callables, claims);
        return WorkerResultAssembler.Create(snapshot.InputHash, manifest, run.Status, run.Failure, callables, claims,
            request.Budgets, WorkerCacheStatus.Disabled, 0, requestHash: WorkerProtocolJson.ComputeRequestHash(request),
            versions: Launcher.Program.ExpectedVersions());
    }

    [Test]
    public void CacheableResponseRequiresValidatedTerminalRecords()
    {
        const string callableId = "M:Subject.M";
        var manifest = new WorkerClaimManifest
        {
            Callables = [new WorkerCallableManifestEntry {
                CallableId = callableId,
                SelectedFeatures = [WorkerSelectedFeature.Contracts],
                SelectionReasons = [
                    WorkerSelectionReason.DiscoveredPostcondition
                ],
                Location = TestLocation(),
                ClaimIds = ["claim"],
                Assumptions = [new WorkerAssumptionEvidence {
                    Id = "spa1:cache",
                    Kind = WorkerAssumptionKind.UserAssume
                }]
            }],
            Claims = [new WorkerClaimManifestEntry {
                ClaimId = "claim",
                CallableId = callableId,
                Kind = WorkerClaimKind.Postcondition,
                Evidence = WorkerClaimEvidence.DirectClause,
                Location = TestLocation()
            }]
        };
        WorkerProtocolJson.SealManifest(manifest);
        var response = WorkerResultAssembler.Create(
            new string('a', 64),
            manifest,
            WorkerRunStatus.Complete,
            WorkerRunFailureReason.None,
            [new WorkerCallableResult {
                CallableId = callableId,
                Coverage = WorkerCallableCoverage.Complete,
                Reason = WorkerCallableCoverageReason.None,
                Assumptions = manifest.Callables[0].Assumptions
            }],
            [new WorkerClaimResult {
                ClaimId = "claim",
                Outcome = WorkerClaimOutcome.Proven,
                Reason = WorkerClaimReason.None,
                Assumptions = manifest.Callables[0].Assumptions
            }],
            new WorkerBudgets(),
            WorkerCacheStatus.Miss,
            0);

        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest),
            Is.True);
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                "not-a-sha-256-hash",
                manifest),
            Is.False);
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                null!),
            Is.False);

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Refuted;
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest),
            Is.True);

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Unknown;
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest),
            Is.False);

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Proven;
        response.ClaimResults[0].Reason =
            WorkerClaimReason.InfrastructureFailure;
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest),
            Is.False);

        response.ClaimResults[0].Reason = WorkerClaimReason.None;
        response.Errors = [
            new WorkerProtocolError {
                Code = "worker.error",
                Message = "Not cacheable."
            }
        ];
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest),
            Is.False);
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                new string('b', 64),
                manifest),
            Is.False);
        response.Errors = [];
        response.ClaimResults[0].Assumptions = [];
        Assert.That(VerificationCache.IsCacheable(
            response,
            response.InputHash,
            manifest), Is.False);

        response.ClaimResults = [null!];
        Assert.That(VerificationCache.IsCacheable(
            response,
            response.InputHash,
            manifest), Is.False);
    }

    [Test]
    public async Task CorruptCacheFailsClosedAndRecomputes()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var firstQueries = backend.CallCount;
        Assert.That(firstQueries, Is.GreaterThan(0));
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);
        var cacheFile = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        await File.WriteAllTextAsync(cacheFile, "{corrupt");
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(firstQueries * 2));
        AssertSemanticallyEquivalent(first, second);
    }

    [Test]
    public async Task PreviousReplayCacheSchemaMissesAndRecomputes()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var firstQueries = backend.CallCount;
        Assert.That(firstQueries, Is.GreaterThan(0));
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);
        var cacheFile = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        var current = "\"schemaVersion\":" +
            WorkerCacheVersions.Current.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        var stale = "\"schemaVersion\":" +
            (WorkerCacheVersions.Current - 1).ToString(
                System.Globalization.CultureInfo.InvariantCulture);
        var envelope = await File.ReadAllTextAsync(cacheFile);
        Assert.That(envelope, Does.Contain(current));
        await File.WriteAllTextAsync(
            cacheFile,
            envelope.Replace(current, stale, StringComparison.Ordinal));

        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(firstQueries * 2));
        AssertSemanticallyEquivalent(first, second);
    }

    [Test]
    public async Task CacheEvictionHonorsTheConfiguredByteBound()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.MaximumBytes = 1;
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var firstQueries = backend.CallCount;
        Assert.That(firstQueries, Is.GreaterThan(0));
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);
        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(firstQueries * 2));
            Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(
                CacheFiles(project),
                Is.Empty);
        }
    }

    [Test]
    public async Task CacheEvictionPreservesUnrelatedJsonFiles()
    {
        using var project = TestProject.Create(RefutationSource);
        Directory.CreateDirectory(project.CacheDirectory);
        var unrelatedPath = Path.Combine(project.CacheDirectory, "unrelated.json");
        await File.WriteAllTextAsync(unrelatedPath, "not a cache entry");

        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.MaximumBytes = 1;
        using var worker = new SharpProofWorker(new SpuriousModelBackend());
        await worker.VerifyAsync(request);

        Assert.That(File.Exists(unrelatedPath), Is.True);
    }

    [Test]
    public async Task CacheEvictionLeavesLegacyJsonFilesUntouched()
    {
        using var project = TestProject.Create(RefutationSource);
        Directory.CreateDirectory(project.CacheDirectory);
        var legacyPath = Path.Combine(
            project.CacheDirectory,
            new string('a', 64) + ".json");
        const string legacyContents = "legacy cache entry";
        await File.WriteAllTextAsync(legacyPath, legacyContents);

        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.MaximumBytes = 1;
        using var worker = new SharpProofWorker(new SpuriousModelBackend());
        await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(File.Exists(legacyPath), Is.True);
            Assert.That(
                await File.ReadAllTextAsync(legacyPath),
                Is.EqualTo(legacyContents));
            Assert.That(
                Directory.GetFiles(
                    project.CacheDirectory,
                    "*.sharp-proof-cache.json"),
                Is.Empty);
        }
    }

    [Test]
    public async Task ReplayValidatedRefutationIsCacheable()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        var cached = await worker.VerifyAsync(request);

        _ = AssertClaimVerdict(response, WorkerClaimOutcome.Refuted);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                cached.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(
                CacheFiles(project),
                Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task TinyRlimitProducesResourceAbstention()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Bounded(long value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(Contract.Result<long>() > 0);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 1;
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);

        _ = AssertClaimVerdict(
            response,
            WorkerClaimOutcome.Unknown,
            WorkerClaimReason.ResourceLimit);
    }

    [Test]
    public async Task MethodRlimitIsCumulativeAcrossCallableQueries()
    {
        using var project = TestProject.Create(
            MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 6;
        request.Budgets.MethodRlimit = 24;
        using var backend = new ResourceCountingBackend(resourceCost: 6);
        using var worker = new SharpProofWorker(
            backend,
            () => backend.ConsumedResourceCount);
        var response = await worker.VerifyAsync(request);
        WorkerClaimOutcome[] expectedStatuses = [
            WorkerClaimOutcome.Proven,
            WorkerClaimOutcome.Proven,
            WorkerClaimOutcome.Unknown
        ];

        Assert.That(response.Errors, Is.Empty);
        Assert.That(backend.CallCount, Is.EqualTo(4));
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.EqualTo(expectedStatuses));
        Assert.That(
            response.ClaimResults[2].Reason,
            Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task BackendFactoryCreatesIsolatedConcurrentSolverLanes()
    {
        using var project = TestProject.Create(ConcurrentSubjectsSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 2;
        var coordination = new ConcurrentLaneState(expectedLanes: 2);
        using var worker = new SharpProofWorker(
            () => new CoordinatedBackend(coordination));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(coordination.Created, Is.EqualTo(2));
            Assert.That(coordination.MaximumActive, Is.EqualTo(2));
            Assert.That(coordination.Disposed, Is.EqualTo(2));
            var callableIds = response.CallableResults.Select(static result => result.CallableId).ToArray();
            Assert.That(callableIds, Is.EqualTo(callableIds.OrderBy(static value => value, StringComparer.Ordinal)));
        }
    }

    [Test]
    public async Task ThrowingBackendCleanupDoesNotReplaceCompletedOutcome()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        ThrowingDisposeBackend? backend = null;
        using var worker = new SharpProofWorker(() =>
        {
            backend = new ThrowingDisposeBackend();
            return backend;
        });

        var response = await worker.VerifyAsync(request);

        _ = AssertClaimVerdict(response, WorkerClaimOutcome.Proven);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(backend!.DisposeCalls, Is.EqualTo(1));
            Assert.That(backend.NativeDisposed, Is.True);
        }
    }

    [Test]
    public async Task PartialBackendSetupCleanupPreservesTypedFailureAndDisposesEveryLane()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long A(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
                public static long B(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
                public static long C(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 3;
        var factoryCalls = 0;
        var created = new List<ThrowingDisposeBackend>();
        using var worker = new SharpProofWorker(() =>
        {
            factoryCalls++;
            if (factoryCalls == 3)
            {
                throw new DllNotFoundException("third backend unavailable");
            }

            var backend = new ThrowingDisposeBackend(
                BackendCheckResult.Unsatisfiable([]));
            created.Add(backend);
            return backend;
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(3));
            Assert.That(created, Has.Count.EqualTo(2));
            Assert.That(
                created.Select(static backend => backend.DisposeCalls),
                Is.All.EqualTo(1));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.BackendUnavailable));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.BackendUnavailable));
        }
    }

    [Test]
    public async Task BackendFactoryReuseIsInfrastructureFailure()
    {
        using var project = TestProject.Create(
            TautologySource + "\n" + TautologySource
                .Replace("using SharpProof.Attributes;\n", string.Empty, StringComparison.Ordinal)
                .Replace("Subject", "Second", StringComparison.Ordinal));
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 2;
        var backend = new CountingBackend(BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(() => backend);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
        Assert.That(
            response.FailureReason,
            Is.EqualTo(WorkerRunFailureReason.InfrastructureFailure));
        Assert.That(response.ClaimResults.Select(static result => result.Reason),
            Is.All.EqualTo(WorkerClaimReason.InfrastructureFailure));
        Assert.That(response.Errors.Single().Code, Is.EqualTo("worker.infrastructure"));
        Assert.That(backend.CallCount, Is.Zero);
    }

    [Test]
    public async Task InjectedBackendSerializesConcurrentWorkerRuns()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new ConcurrentRunBackend();
        using var worker = new SharpProofWorker(backend);

        var first = worker.VerifyAsync(request);
        await backend.FirstEntered.WaitAsync(TimeSpan.FromSeconds(10));
        var second = worker.VerifyAsync(request);
        var secondEnteredBeforeRelease = ReferenceEquals(
            await Task.WhenAny(
                backend.SecondEntered,
                Task.Delay(TimeSpan.FromSeconds(1))),
            backend.SecondEntered);
        backend.ReleaseFirst();
        var responses = await Task.WhenAll(first, second);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(secondEnteredBeforeRelease, Is.False);
            Assert.That(backend.MaximumActive, Is.EqualTo(1));
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(
                responses.Select(static response => response.RunStatus),
                Is.All.EqualTo(WorkerRunStatus.Complete));
        }
    }

    [Test]
    public async Task MethodTimeoutRetiresAndRecreatesTheInterruptedSolverLane()
    {
        using var project = TestProject.Create(ConcurrentSubjectsSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 200;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
            Interlocked.Increment(ref factoryCalls) == 1
                ? new DelayingBackend()
                : new CountingNativeBackend());

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(2));
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.EqualTo((WorkerClaimOutcome[])[
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimOutcome.Proven
                ]));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.EqualTo((WorkerClaimReason[])[
                    WorkerClaimReason.MethodTimeout,
                    WorkerClaimReason.None
                ]));
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RenewalFailurePreservesTimeoutAndClassifiesUnclaimedWork(
        bool backendUnavailable)
    {
        using var project = TestProject.Create(ConcurrentSubjectsSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 30;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            if (Interlocked.Increment(ref factoryCalls) == 1)
            {
                return new DelayingBackend();
            }

            throw backendUnavailable
                ? new DllNotFoundException("replacement z3 missing")
                : new InvalidOperationException("replacement creation failed");
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(2));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(backendUnavailable
                    ? WorkerRunFailureReason.BackendUnavailable
                    : WorkerRunFailureReason.InfrastructureFailure));
            Assert.That(
                response.CallableResults.Select(static result => result.Reason),
                Is.EqualTo((WorkerCallableCoverageReason[])[
                    WorkerCallableCoverageReason.MethodTimeout,
                    WorkerCallableCoverageReason.InfrastructureFailure
                ]));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.EqualTo((WorkerClaimReason[])[
                    WorkerClaimReason.MethodTimeout,
                    backendUnavailable
                        ? WorkerClaimReason.BackendUnavailable
                        : WorkerClaimReason.InfrastructureFailure
                ]));
            Assert.That(
                WorkerProtocolJson.ValidateForRequest(
                    response,
                    WorkerProtocolJson.ComputeRequestHash(request),
                    response.InputHash,
                    response.Manifest,
                    request,
                    new WorkerVersionSummary
                    {
                        WorkerVersion = WorkerCacheIdentity.Current.ToolVersion,
                        ApiSpecVersion = WorkerCacheIdentity.Current.ApiSpecVersion,
                        WorkerBinarySha256 =
                            WorkerCacheIdentity.Current.WorkerBinarySha256,
                        ApiSpecContentSha256 =
                            WorkerCacheIdentity.Current.ApiSpecContentSha256
                    }).IsValid,
                Is.True);
        }
    }

    [TestCase("null", WorkerRunFailureReason.InfrastructureFailure,
        WorkerClaimReason.InfrastructureFailure, 2)]
    [TestCase("reuse", WorkerRunFailureReason.BackendUnavailable,
        WorkerClaimReason.BackendUnavailable, 2)]
    [TestCase("dispose", WorkerRunFailureReason.InfrastructureFailure,
        WorkerClaimReason.InfrastructureFailure, 2)]
    public async Task InvalidRenewalStateFailsClosedWithTypedEvidence(
        string scenario,
        WorkerRunFailureReason expectedFailure,
        WorkerClaimReason expectedClaimReason,
        int expectedFactoryCalls)
    {
        using var project = TestProject.Create(ConcurrentSubjectsSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 30;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        var factoryCalls = 0;
        ISmtBackend? original = null;
        OwnershipBackend? replacement = null;
        using var worker = new SharpProofWorker(() =>
        {
            factoryCalls++;
            if (factoryCalls == 1)
            {
                original = scenario == "dispose"
                    ? new ThrowingDisposeDelayingBackend()
                    : new OwnershipBackend(delay: true);
                return original;
            }

            return scenario switch
            {
                "null" => null!,
                "reuse" => original!,
                _ => replacement = new OwnershipBackend(delay: false)
            };
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(expectedFactoryCalls));
            Assert.That(replacement?.DisposeCount ?? 0, Is.EqualTo(scenario == "dispose" ? 1 : 0));
            if (original is OwnershipBackend owned)
            {
                Assert.That(owned.DisposeCount, Is.EqualTo(1));
            }
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(response.FailureReason, Is.EqualTo(expectedFailure));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.EqualTo((WorkerClaimReason[])[
                    WorkerClaimReason.MethodTimeout,
                    expectedClaimReason
                ]));
        }
    }

    [Test]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000",
        Justification = "The worker factory owns these backends; the test asserts their exact disposal counts.")]
    public async Task RenewalDoesNotDisposeBackendOwnedByAnotherActiveLane()
    {
        using var project = TestProject.Create("""
            using SharpProof.Attributes;
            public static class Subject {
                public static long A(long x) { Contract.Ensures(Contract.Result<long>() == x); return x; }
                public static long B(long x) { Contract.Ensures(Contract.Result<long>() == x); return x; }
                public static long C(long x) { Contract.Ensures(Contract.Result<long>() == x); return x; }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 2;
        request.Budgets.MethodWallTimeMilliseconds = 100;
        request.Budgets.ProjectWallTimeMilliseconds = 2_000;
        var first = new OwnershipBackend(delay: true);
        var second = new OwnershipBackend(delay: false) { Hold = true };
        var replacementRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                return first;
            }
            if (call > 2)
            {
                replacementRequested.TrySetResult();
            }
            return second;
        });
        var verification = worker.VerifyAsync(request);
        try
        {
            await second.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await replacementRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            Assert.That(second.DisposeCount, Is.Zero, "The replacement is still owned by the active second lane.");
        }
        finally
        {
            second.Release.TrySetResult();
            await verification;
        }
        Assert.That(second.DisposedDuringCheck, Is.False);
        Assert.That(second.DisposeCount, Is.EqualTo(1));
        Assert.That(first.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task FactorylessTimeoutClassifiesEveryUnclaimedTargetAsTimedOut()
    {
        using var project = TestProject.Create(ConcurrentSubjectsSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 30;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        using var worker = new SharpProofWorker(new DelayingBackend());

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
            Assert.That(
                response.CallableResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerCallableCoverageReason.MethodTimeout));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.MethodTimeout));
        }
    }

    [Test]
    public async Task RenewedLaneCanProveAndReplayARefutationAfterCancellation()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long A(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
                public static long B(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
                public static long C() {
                    Contract.Ensures(Contract.Result<long>() == 0);
                    return 1;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 200;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
            Interlocked.Increment(ref factoryCalls) == 1
                ? new DelayingBackend()
                : new CountingNativeBackend());

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(2));
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.EqualTo((WorkerClaimOutcome[])[
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimOutcome.Proven,
                    WorkerClaimOutcome.Refuted
                ]));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.EqualTo((WorkerClaimReason[])[
                    WorkerClaimReason.MethodTimeout,
                    WorkerClaimReason.None,
                    WorkerClaimReason.None
                ]));
        }
    }

    [Test]
    public async Task ConcurrentTimedOutLanesAreIndependentlyRenewed()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long A(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
                public static long B(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
                public static long C(long value) {
                    Contract.Ensures(Contract.Result<long>() == value);
                    return value;
                }
                public static long D() {
                    Contract.Ensures(Contract.Result<long>() == 0);
                    return 1;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 2;
        request.Budgets.MethodWallTimeMilliseconds = 200;
        request.Budgets.ProjectWallTimeMilliseconds = 5_000;
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
            Interlocked.Increment(ref factoryCalls) <= 2
                ? new DelayingBackend()
                : new CountingNativeBackend());

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(4));
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.EqualTo((WorkerClaimOutcome[])[
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimOutcome.Unknown,
                    WorkerClaimOutcome.Proven,
                    WorkerClaimOutcome.Refuted
                ]));
            Assert.That(
                response.ClaimResults.Take(2)
                    .Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.MethodTimeout));
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
        }
    }

    [Test]
    public async Task BuiltInBackendChargesTheMethodRlimit()
    {
        using var project = TestProject.Create(MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MethodRlimit = request.Budgets.QueryRlimit;
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(
            response.ClaimResults.Select(static record => record.Reason),
            Is.All.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task InjectedBuiltInBackendStillChargesTheMethodRlimit()
    {
        using var project = TestProject.Create(MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MethodRlimit = request.Budgets.QueryRlimit;
        using var backend = new NativeCallableBackend(
            new SharpProof.Smt.IrSmtBackendOptions(
                request.Budgets.QueryRlimit));
        using var worker = new SharpProofWorker(
            backend,
            readConsumedResourceCount: null);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(
            response.ClaimResults.Select(static record => record.Reason),
            Is.All.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task UnmeteredBackendReservesThePerQueryRlimit()
    {
        using var project = TestProject.Create(MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 6;
        request.Budgets.MethodRlimit = 12;
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);
        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(
            response.ClaimResults[2].Reason,
            Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task MethodRlimitParticipatesInCacheIdentity()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        using var backend = new CountingNativeBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var firstQueries = backend.CallCount;
        Assert.That(firstQueries, Is.GreaterThan(0));
        AssertClaimVerdict(first, WorkerClaimOutcome.Refuted);

        request.Budgets.MethodRlimit--;
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(firstQueries * 2));
        Assert.That(second.InputHash, Is.Not.EqualTo(first.InputHash));
        Assert.That(
            CacheFiles(project),
            Has.Length.EqualTo(2));
    }

    [Test]
    public async Task CallerCancellationPreservesManifestAndIsNotCached()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Budgets.MethodWallTimeMilliseconds = 5_000;
        var backendStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker = new SharpProofWorker(
            new SignalingDelayingBackend(backendStarted));
        using var cancellation = new CancellationTokenSource();

        var verification = worker.VerifyAsync(request, cancellation.Token);
        await backendStarted.Task;
        await cancellation.CancelAsync();
        var response = await verification;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Canceled));
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(
                response.ClaimResults[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults[0].Reason,
                Is.EqualTo(WorkerClaimReason.Canceled));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
        Assert.That(
            CacheFiles(project),
            Is.Empty);
    }

    [Test]
    public async Task PreCanceledRunDoesNotLoadManifestOrStartProofWork()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        File.Delete(request.CompilerManifest.Path);
        var backend = new CountingBackend(BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var response = await worker.VerifyAsync(request, cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
            Assert.That(response.Manifest.Claims, Is.Empty);
            Assert.That(response.ClaimResults, Is.Empty);
            Assert.That(
                response.Errors.Select(static error => error.Code),
                Does.Contain("worker.canceled"));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task PreparedArtifactTimeCountsAgainstTheProjectDeadline()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MethodWallTimeMilliseconds = 1000;
        request.Budgets.ProjectWallTimeMilliseconds = 1000;
        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, CancellationToken.None);
        var backend = new CountingBackend(BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        var expiredStart = System.Diagnostics.Stopwatch.GetTimestamp() -
            2 * System.Diagnostics.Stopwatch.Frequency;

        var response = await worker.VerifyAsync(request, snapshot, CancellationToken.None, expiredStart);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(response.Summary.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(2000));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task ProjectBoundaryStopsBeforeSolving()
    {
        var sources = Enumerable.Range(0, 512)
            .Select(index => ($"Padding{index}.cs", $"using SharpProof.Attributes; internal static class Padding{index} {{ internal static int Identity(int value) {{ Contract.Ensures(Contract.Result<int>() == value); return value; }} }}"))
            .Prepend(("Subject.cs", TautologySource))
            .ToArray();
        using var project = TestProject.Create(sources);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MethodWallTimeMilliseconds = 1;
        request.Budgets.ProjectWallTimeMilliseconds = 1;
        var backend = new CountingBackend(BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task MethodAndProjectWallBoundariesBecomeUnknown()
    {
        using var methodProject = TestProject.Create(TautologySource);
        var methodRequest = methodProject.CreateRequest(cacheEnabled: false);
        methodRequest.Budgets.MethodWallTimeMilliseconds = 30;
        methodRequest.Budgets.ProjectWallTimeMilliseconds = 1_000;
        using (var worker = new SharpProofWorker(new DelayingBackend()))
        {
            var response = await worker.VerifyAsync(methodRequest);
            Assert.That(
                response.ClaimResults.Single().Reason,
                Is.EqualTo(WorkerClaimReason.MethodTimeout));
        }

        var projectSources = Enumerable.Range(0, 8)
            .Select(index => (
                $"Subject{index}.cs",
                TautologySource.Replace(
                    "Subject",
                    $"Subject{index}",
                    StringComparison.Ordinal)))
            .ToArray();
        using var projectProject = TestProject.Create(projectSources);
        var projectRequest = projectProject.CreateRequest(cacheEnabled: false);
        projectRequest.Budgets.MethodWallTimeMilliseconds = 40;
        projectRequest.Budgets.ProjectWallTimeMilliseconds = 100;
        projectRequest.Budgets.MaxParallelism = 1;
        using var projectWorker = new SharpProofWorker(
            static () => new DelayingBackend());
        var projectResponse = await projectWorker.VerifyAsync(projectRequest);
        Assert.That(
            projectResponse.ClaimResults,
            Has.Some.Property(nameof(WorkerClaimResult.Reason))
                .EqualTo(WorkerClaimReason.ProjectTimeout),
            WorkerProtocolJson.SerializeResponse(projectResponse));
    }

    [Test]
    public void DefaultsExposeLogicalAndWallClockBudgets()
    {
        var budgets = new WorkerBudgets();
        Assert.That(
            budgets.QueryRlimit,
            Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
        Assert.That(
            budgets.MethodRlimit,
            Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
        Assert.That(budgets.MaxParallelism, Is.EqualTo(4));
        Assert.That(
            new SharpProof.Smt.IrSmtBackendOptions(17).QueryRlimit,
            Is.EqualTo(17));
    }

    [Test]
    public void AcceptanceContractMatchesWorkerDefaults()
    {
        var contractPath = Path.Combine(
            TestRepository.FindRoot(),
            "eng",
            "acceptance",
            "contract.json");
        using var document = JsonDocument.Parse(
            File.ReadAllText(contractPath));
        var root = document.RootElement;
        var worker = root.GetProperty("worker");
        var cache = root.GetProperty("cache");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                worker.GetProperty("protocolVersion").GetInt32(),
                Is.EqualTo(int.Parse(
                    WorkerProtocolVersions.Current,
                    System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(
                worker.GetProperty("maximumParallelism").GetInt32(),
                Is.EqualTo(WorkerBudgets.MaximumParallelism));
            Assert.That(
                worker.GetProperty("maximumExpressionDepth").GetInt32(),
                Is.EqualTo(WorkerBudgets.DefaultMaximumExpressionDepth));
            Assert.That(
                worker.GetProperty("queryRlimit").GetUInt32(),
                Is.EqualTo(WorkerBudgets.DefaultQueryRlimit));
            Assert.That(
                worker.GetProperty("methodRlimit").GetUInt32(),
                Is.EqualTo(WorkerBudgets.DefaultMethodRlimit));
            Assert.That(
                worker.GetProperty("maximumMethodWallSeconds").GetInt32() *
                1_000,
                Is.EqualTo(
                    WorkerBudgets.DefaultMethodWallTimeMilliseconds));
            Assert.That(
                worker.GetProperty("maximumProjectWallSeconds").GetInt32() *
                1_000,
                Is.EqualTo(
                    WorkerBudgets.DefaultProjectWallTimeMilliseconds));
            Assert.That(
                worker.GetProperty("forcedTerminationMilliseconds")
                    .GetInt32(),
                Is.EqualTo(
                    WorkerLauncherDefaults.TerminationGraceMilliseconds));
            Assert.That(
                cache.GetProperty("schemaVersion").GetInt32(),
                Is.EqualTo(WorkerCacheVersions.Current));
            Assert.That(
                cache.GetProperty("maximumMiB").GetInt64() * 1024 * 1024,
                Is.EqualTo(WorkerCacheOptions.DefaultMaximumBytes));
            Assert.That(
                cache.GetProperty("enabledByDefault").GetBoolean(),
                Is.True);
        }
    }

    private static async Task<WorkerVerifyResponse> RunAsync(
        string source,
        bool cacheEnabled,
        ISmtBackend? backend = null,
        int maximumExpressionDepth =
            WorkerBudgets.DefaultMaximumExpressionDepth)
    {
        using var project = TestProject.Create(source);
        var request = project.CreateRequest(
            cacheEnabled,
            maximumExpressionDepth: maximumExpressionDepth);
        using var defaultBackend = backend == null ? new CountingNativeBackend() : null;
        using var worker = new SharpProofWorker(backend ?? defaultBackend!);
        return await worker.VerifyAsync(request);
    }

    private static WorkerClaimManifestEntry GetClaim(
        WorkerVerifyResponse response,
        WorkerClaimResult result)
    {
        return response.Manifest.Claims.Single(claim =>
            string.Equals(
                claim.ClaimId,
                result.ClaimId,
                StringComparison.Ordinal));
    }

    private static WorkerClaimResult AssertClaimVerdict(
        WorkerVerifyResponse response,
        WorkerClaimOutcome expectedOutcome,
        WorkerClaimReason? expectedReason = null,
        WorkerVacuityKind? expectedVacuity = null)
    {
        var result = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(expectedOutcome));
            if (expectedReason is { } reason)
            {
                Assert.That(result.Reason, Is.EqualTo(reason));
            }
            if (expectedVacuity is { } vacuity)
            {
                Assert.That(result.Vacuity, Is.EqualTo(vacuity));
            }
        }
        return result;
    }

    private static string GetCallableId(
        WorkerVerifyResponse response,
        WorkerClaimResult result)
    {
        return GetClaim(response, result).CallableId;
    }

    private static string[] CacheFiles(TestProject project)
    {
        return Directory.Exists(project.CacheDirectory)
            ? Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json")
            : [];
    }

    private static WorkerSourceLocation TestLocation()
    {
        return new()
        {
            Path = "input.cs",
            Length = 1,
            Line = 1,
            Column = 1
        };
    }

    private static void AssertSemanticallyEquivalent(
        WorkerVerifyResponse expected,
        WorkerVerifyResponse actual)
    {
        WorkerProtocolJson.Canonicalize(expected);
        WorkerProtocolJson.Canonicalize(actual);
        Assert.That(
            SemanticJson(actual),
            Is.EqualTo(SemanticJson(expected)));

        static string SemanticJson(WorkerVerifyResponse response)
        {
            return JsonSerializer.Serialize(
                new
                {
                    response.ProtocolVersion,
                    response.InputHash,
                    response.Manifest,
                    response.RunStatus,
                    response.FailureReason,
                    response.CallableResults,
                    response.ClaimResults,
                    response.Errors
                },
                WorkerProtocolJson.Options);
        }
    }

    private static RuntimeContractCase[] CreateRuntimeContractCases(
        int seed,
        int count)
    {
        var random = new Random(seed);
        var result = new RuntimeContractCase[count];
        for (var index = 0; index < count; index++)
        {
            var boundary = random.Next(-50, 51);
            var inputs = new[] {
                -100L,
                -1L,
                0L,
                1L,
                100L,
                boundary - 1L,
                boundary,
                boundary + 1L,
                random.Next(-100, 101),
                random.Next(-100, 101)
            };
            var name = "M" + index.ToString(
                "D2",
                System.Globalization.CultureInfo.InvariantCulture);
            var boundaryLiteral = boundary.ToString(
                System.Globalization.CultureInfo.InvariantCulture) + "L";
            result[index] = (index % 8) switch
            {
                0 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() == value",
                    static _ => true,
                    static (value, actual) => actual == value,
                    WorkerClaimOutcome.Proven,
                    inputs),
                1 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() <= value",
                    static _ => true,
                    static (value, actual) => actual <= value,
                    WorkerClaimOutcome.Proven,
                    inputs),
                2 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() >= value",
                    static _ => true,
                    static (value, actual) => actual >= value,
                    WorkerClaimOutcome.Proven,
                    inputs),
                3 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() > value",
                    static _ => true,
                    static (value, actual) => actual > value,
                    WorkerClaimOutcome.Refuted,
                    inputs),
                4 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() < value",
                    static _ => true,
                    static (value, actual) => actual < value,
                    WorkerClaimOutcome.Refuted,
                    inputs),
                5 => new RuntimeContractCase(
                    name,
                    null,
                    "Contract.Result<long>() != value",
                    static _ => true,
                    static (value, actual) => actual != value,
                    WorkerClaimOutcome.Refuted,
                    inputs),
                6 => new RuntimeContractCase(
                    name,
                    "value > " + boundaryLiteral,
                    "Contract.Result<long>() > " + boundaryLiteral,
                    value => value > boundary,
                    (_, actual) => actual > boundary,
                    WorkerClaimOutcome.Proven,
                    inputs),
                _ => new RuntimeContractCase(
                    name,
                    "value < " + boundaryLiteral,
                    "Contract.Result<long>() < " + boundaryLiteral,
                    value => value < boundary,
                    (_, actual) => actual < boundary,
                    WorkerClaimOutcome.Proven,
                    inputs)
            };
        }
        return result;
    }

    private static string CreateRuntimeContractSource(
        IEnumerable<RuntimeContractCase> cases)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine("using SharpProof.Attributes;");
        builder.AppendLine("public static class RuntimeContractOracle {");
        foreach (var item in cases)
        {
            builder.Append("    public static long ")
                .Append(item.MethodName)
                .AppendLine("(long value) {");
            if (item.RequiresSource != null)
            {
                builder.Append("        Contract.Requires(")
                    .Append(item.RequiresSource)
                    .AppendLine(");");
            }

            builder.Append("        Contract.Ensures(")
                .Append(item.EnsuresSource)
                .AppendLine(");");
            builder.AppendLine("        return value;");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private sealed record RuntimeContractCase(
        string MethodName,
        string? RequiresSource,
        string EnsuresSource,
        Func<long, bool> Requires,
        Func<long, long, bool> Ensures,
        WorkerClaimOutcome ExpectedStatus,
        long[] Inputs);

    private const string TautologySource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Proof(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                return value;
            }
        }
        """;

    private const string RefutationSource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Broken(long value) {
                Contract.Ensures(Contract.Result<long>() > value);
                return value;
            }
        }
        """;

    private const string MultipleEnsuresSource =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static long Identity(long value) {
                Contract.Ensures(Contract.Result<long>() == value);
                Contract.Ensures(Contract.Result<long>() <= value);
                Contract.Ensures(Contract.Result<long>() >= value);
                return value;
            }
        }
        """;

    private sealed class CountingBackend(BackendCheckResult result)
        : ISmtBackend
    {
        private readonly BackendCheckResult _result = result;
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(_result);
        }
    }

    private sealed class OwnershipBackend(bool delay) : ISmtBackend, IDisposable
    {
        private int _disposals;
        internal int DisposeCount => Volatile.Read(ref _disposals);
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Hold { get; init; }
        internal bool DisposedDuringCheck { get; private set; }
        private int _active;

        public async Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _active);
            Started.TrySetResult();
            try
            {
                if (Hold)
                {
                    await Release.Task;
                }
                if (delay)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                return BackendCheckResult.Unsatisfiable([]);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        public void Dispose()
        {
            DisposedDuringCheck |= Volatile.Read(ref _active) != 0;
            Interlocked.Increment(ref _disposals);
        }
    }

    private sealed class DelayingBackend : ISmtBackend
    {
        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return BackendCheckResult.Unknown(
                BackendFailureReason.InfrastructureFailure);
        }
    }

    private sealed class SignalingDelayingBackend(
        TaskCompletionSource started) : ISmtBackend
    {
        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return BackendCheckResult.Unknown(
                BackendFailureReason.InfrastructureFailure);
        }
    }

    private sealed class ThrowingDisposeDelayingBackend :
        ISmtBackend,
        IDisposable
    {
        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return BackendCheckResult.Unknown(
                BackendFailureReason.InfrastructureFailure);
        }

        public void Dispose()
        {
            throw new InvalidOperationException("backend disposal failed");
        }
    }

    private sealed class CountingNativeBackend : ISmtBackend, IDisposable
    {
        private readonly NativeCallableBackend _backend;
        private int _callCount;
        private VerificationQuery? _query;

        internal CountingNativeBackend()
        {
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            _backend = new NativeCallableBackend(new IrSmtBackendOptions(WorkerBudgets.DefaultQueryRlimit));
        }

        internal int CallCount => Volatile.Read(ref _callCount);

        internal VerificationQuery Query => _query ?? throw new InvalidOperationException("No native query was submitted.");

        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            _query = query;
            Interlocked.Increment(ref _callCount);
            return _backend.CheckAsync(query, cancellationToken);
        }

        public void Dispose()
        {
            _backend.Dispose();
        }
    }

    private sealed class SpuriousModelBackend : ISmtBackend
    {
        private int _callCount;

        internal int CallCount => Volatile.Read(ref _callCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            var assignments = query.ModelVariables.Select(variable =>
                KeyValuePair.Create(variable,
                    query.Factory.GetVariableInfo(variable).Type == query.Factory.BooleanType
                        ? query.Factory.CreateBooleanValue(false)
                        : query.Factory.CreateIntegerValue(0)));
            return Task.FromResult(BackendCheckResult.Satisfiable(
                new BackendModel(assignments)));
        }
    }

    private sealed class ConcurrentLaneState(int expectedLanes)
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _allActive =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        internal int Created
        {
            get; private set;
        }
        internal int Disposed
        {
            get; private set;
        }
        internal int MaximumActive
        {
            get; private set;
        }
        internal void CreatedBackend()
        {
            lock (_gate)
            {
                Created++;
            }
        }
        internal void DisposedBackend()
        {
            lock (_gate)
            {
                Disposed++;
            }
        }
        internal async Task<BackendCheckResult> CheckAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                _active++;
                MaximumActive = Math.Max(MaximumActive, _active);
                if (_active == expectedLanes)
                {
                    _allActive.TrySetResult();
                }
            }
            try
            {
                await _allActive.Task.WaitAsync(cancellationToken);
                return BackendCheckResult.Unsatisfiable([]);
            }
            finally
            {
                lock (_gate)
                {
                    _active--;
                }
            }
        }
    }

    private sealed class CoordinatedBackend : ISmtBackend, IDisposable
    {
        private readonly ConcurrentLaneState _state;
        private readonly CountingNativeBackend _backend = new();
        internal CoordinatedBackend(ConcurrentLaneState state)
        {
            _state = state;
            state.CreatedBackend();
        }
        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query, CancellationToken cancellationToken)
        {
            await _state.CheckAsync(cancellationToken);
            return await _backend.CheckAsync(query, cancellationToken);
        }

        public void Dispose()
        {
            _backend.Dispose();
            _state.DisposedBackend();
        }
    }

    private sealed class ThrowingDisposeBackend(BackendCheckResult? result = null) :
        ISmtBackend,
        IDisposable
    {
        private readonly BackendCheckResult? _result = result;
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2213",
            Justification = "Dispose releases this backend before deliberately throwing the cleanup failure tested by this fixture.")]
        private readonly CountingNativeBackend? _backend = result == null ? new() : null;
        internal bool NativeDisposed { get; private set; }

        internal int DisposeCalls
        {
            get; private set;
        }

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _backend?.CheckAsync(query, cancellationToken) ?? Task.FromResult(_result!);
        }

        public void Dispose()
        {
            DisposeCalls++;
            _backend?.Dispose();
            NativeDisposed = _backend != null;
            throw new InvalidOperationException("backend disposal failed");
        }
    }

    private sealed class ConcurrentRunBackend : ISmtBackend
    {
        private readonly object _gate = new();
        private readonly TaskCompletionSource _firstEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _secondEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;

        internal int CallCount
        {
            get; private set;
        }

        internal int MaximumActive
        {
            get; private set;
        }

        internal Task FirstEntered => _firstEntered.Task;
        internal Task SecondEntered => _secondEntered.Task;

        internal void ReleaseFirst()
        {
            _releaseFirst.TrySetResult();
        }

        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            int call;
            lock (_gate)
            {
                call = ++CallCount;
                _active++;
                MaximumActive = Math.Max(MaximumActive, _active);
            }

            try
            {
                if (call == 1)
                {
                    _firstEntered.TrySetResult();
                    await _releaseFirst.Task.WaitAsync(cancellationToken);
                }
                else
                {
                    _secondEntered.TrySetResult();
                }

                return BackendCheckResult.Unknown(BackendFailureReason.ResourceLimit);
            }
            finally
            {
                lock (_gate)
                {
                    _active--;
                }
            }
        }
    }

    private sealed class ResourceCountingBackend(long resourceCost) : ISmtBackend, IDisposable
    {
        private readonly long _resourceCost = resourceCost;
        private readonly CountingNativeBackend _backend = new();
        private int _callCount;
        private long _consumedResourceCount;

        internal int CallCount => Volatile.Read(ref _callCount);
        internal long ConsumedResourceCount =>
            Interlocked.Read(ref _consumedResourceCount);

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _callCount);
            Interlocked.Add(ref _consumedResourceCount, _resourceCost);
            return _backend.CheckAsync(query, cancellationToken);
        }

        public void Dispose()
        { _backend.Dispose(); }
    }

    private static void SetDeclaredMaxStack(
        string path,
        string methodName,
        ushort declaredMaxStack)
    {
        var bytes = File.ReadAllBytes(path);
        int methodBodyOffset;
        using (var stream = new MemoryStream(bytes, writable: false))
        using (var image = new PEReader(stream))
        {
            var metadata = image.GetMetadataReader();
            var methodHandle = metadata.MethodDefinitions
                .Where(handle => string.Equals(
                    metadata.GetString(
                        metadata.GetMethodDefinition(handle).Name),
                    methodName,
                    StringComparison.Ordinal))
                .Single();
            var definition = metadata.GetMethodDefinition(methodHandle);
            var body = image.GetMethodBody(
                definition.RelativeVirtualAddress);
            Assert.That(
                body.MaxStack,
                Is.GreaterThan(declaredMaxStack));

            var sectionIndex = image.PEHeaders.GetContainingSectionIndex(
                definition.RelativeVirtualAddress);
            Assert.That(sectionIndex, Is.GreaterThanOrEqualTo(0));
            var section = image.PEHeaders.SectionHeaders[sectionIndex];
            methodBodyOffset = checked(
                definition.RelativeVirtualAddress -
                section.VirtualAddress +
                section.PointerToRawData);
            Assert.That(
                bytes[methodBodyOffset] & 0x03,
                Is.EqualTo(0x03),
                "The malformed-IL fixture requires a fat method header.");
        }

        bytes[methodBodyOffset + 2] = (byte)declaredMaxStack;
        bytes[methodBodyOffset + 3] = (byte)(declaredMaxStack >> 8);
        File.WriteAllBytes(path, bytes);
    }

    private sealed class TestProject : IDisposable
    {
        private static readonly ImmutableArray<MetadataReference>
            DefaultReferences = TestMetadataReferences.ForFileNames(
                RequiredReferenceFileNames,
                sort: true);
        private static readonly ImmutableArray<MetadataReference>
            NetCoreReferencePack = CreateNetCoreReferencePack();
        private readonly TempDirectory _temporary;
        private readonly List<string> _additionalReferencePaths = [];
        private bool _useNetCoreReferencePack;

        private TestProject(TempDirectory temporary, string[] sourcePaths)
        {
            _temporary = temporary;
            DirectoryPath = temporary.FullName;
            SourcePaths = sourcePaths;
            CacheDirectory = Path.Combine(DirectoryPath, "cache");
        }

        internal string DirectoryPath
        {
            get;
        }
        internal string[] SourcePaths
        {
            get;
        }
        internal string CacheDirectory
        {
            get;
        }

        internal static TestProject Create(string source)
        {
            return Create(("Subject.cs", source));
        }

        internal static TestProject Create(
            params (string FileName, string Source)[] sources)
        {
            var temporary = TempDirectory.CreateOwned(
                "SharpProof.Worker.Test",
                string.Empty,
                "Refusing to remove an unexpected worker-test directory.");
            try
            {
                var sourcePaths = sources.Select(source =>
                {
                    var sourcePath = Path.Combine(
                        temporary.FullName,
                        source.FileName);
                    File.WriteAllText(
                        sourcePath,
                        source.Source,
                        new System.Text.UTF8Encoding(false));
                    return sourcePath;
                }).ToArray();
                return new TestProject(temporary, sourcePaths);
            }
            catch
            {
                temporary.Dispose();
                throw;
            }
        }

        internal void UseNetCoreReferencePack()
        {
            _useNetCoreReferencePack = true;
        }

        internal WorkerVerifyRequest CreateRequest(
            bool cacheEnabled,
            CSharpParseOptions? parseOptions = null,
            CSharpCompilationOptions? compilationOptions = null,
            string targetFramework = "net8.0",
            WorkerFeatureSet features = WorkerFeatureSet.All,
            int maximumExpressionDepth =
                WorkerBudgets.DefaultMaximumExpressionDepth,
            ImmutableArray<string> specificationPacks = default)
        {
            var compilation = CreateCompilation(
                parseOptions, compilationOptions);
            var discovery = new ClaimManifestBuilder(
                compilation, features).Build();
            var artifact = CompilerManifestArtifactProducer.Create(
                compilation,
                DirectoryPath,
                targetFramework,
                features,
                discovery,
                maximumExpressionDepth,
                CancellationToken.None,
                specificationPacks: specificationPacks);
            var bytes = System.Text.Encoding.UTF8.GetBytes(
                CompilerManifestArtifactJson.Serialize(artifact));
            var path = Path.Combine(
                DirectoryPath,
                "compiler-manifest-" + Guid.NewGuid().ToString("N") +
                ".json");
            File.WriteAllBytes(path, bytes);
            return new WorkerVerifyRequest
            {
                CompilerManifest = new WorkerFileReference
                {
                    Path = Path.GetFullPath(path),
                    Sha256 = WorkerProtocolJson.ComputeSha256(bytes)
                },
                Cache = new WorkerCacheOptions
                {
                    Enabled = cacheEnabled,
                    Directory = CacheDirectory
                },
                Budgets = new WorkerBudgets
                {
                    MaximumExpressionDepth = maximumExpressionDepth
                }
            };
        }

        internal string AddImplementationReference(
            string source,
            OptimizationLevel optimizationLevel = OptimizationLevel.Release)
        {
            var path = Path.Combine(
                DirectoryPath,
                "implementation-" + Guid.NewGuid().ToString("N") +
                ".dll");
            var syntax = CSharpSyntaxTree.ParseText(
                source,
                CreateParseOptions(),
                path + ".cs");
            var compilation = CSharpCompilation.Create(
                "Implementation" + Guid.NewGuid().ToString("N"),
                [syntax],
                DefaultReferences,
                CreateRoslynOptions().WithOptimizationLevel(
                    optimizationLevel));
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            var emit = compilation.Emit(stream);
            Assert.That(
                emit.Success,
                Is.True,
                string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Select(static diagnostic =>
                        diagnostic.ToString())));
            _additionalReferencePaths.Add(path);
            return path;
        }

        public void Dispose()
        {
            _temporary.Dispose();
        }

        private static ImmutableArray<MetadataReference>
            CreateNetCoreReferencePack()
        {
            var runtimeDirectory = Path.GetDirectoryName(
                typeof(object).Assembly.Location) ??
                throw new InvalidOperationException(
                    "The .NET runtime directory is unavailable.");
            var dotnetRoot = Path.GetFullPath(Path.Combine(
                runtimeDirectory,
                "..",
                "..",
                ".."));
            var packRoot = Path.Combine(
                dotnetRoot,
                "packs",
                "Microsoft.NETCore.App.Ref");
            var version = Directory.GetDirectories(packRoot)
                .Select(Path.GetFileName)
                .Where(static value => value != null)
                .Select(static value => Version.Parse(value!))
                .Where(value => value.Major == Environment.Version.Major)
                .OrderByDescending(static value => value)
                .First();
            var references = Directory.GetFiles(
                Path.Combine(
                    packRoot,
                    version.ToString(),
                    "ref",
                    "net" + version.Major.ToString(
                        System.Globalization.CultureInfo.InvariantCulture) +
                    ".0"),
                "*.dll",
                SearchOption.TopDirectoryOnly);
            return [.. references
                .Append(typeof(Contract).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .Select(static path => MetadataReference.CreateFromFile(path))];
        }

        internal CSharpCompilation CreateCompilation(
            CSharpParseOptions? parseOptions = null,
            CSharpCompilationOptions? compilationOptions = null)
        {
            var effectiveParseOptions =
                parseOptions ?? CreateParseOptions();
            var syntaxTrees = SourcePaths.Select(path =>
                CSharpSyntaxTree.ParseText(
                    SourceText.From(
                        File.ReadAllText(path),
                        System.Text.Encoding.UTF8,
                        SourceHashAlgorithm.Sha256),
                    effectiveParseOptions,
                    path));
            var references = (_useNetCoreReferencePack
                    ? NetCoreReferencePack
                    : DefaultReferences)
                .AddRange(_additionalReferencePaths.Select(static path =>
                    MetadataReference.CreateFromFile(path)));
            return CSharpCompilation.Create(
                "WorkerTest",
                syntaxTrees,
                references,
                compilationOptions ?? CreateRoslynOptions());
        }
    }

    private static CSharpParseOptions CreateParseOptions(
        LanguageVersion languageVersion = LanguageVersion.CSharp12,
        IEnumerable<string>? preprocessorSymbols = null)
    {
        return new(
            languageVersion,
            preprocessorSymbols: preprocessorSymbols ?? []);
    }

    private static CSharpCompilationOptions CreateRoslynOptions(
        OutputKind outputKind = OutputKind.DynamicallyLinkedLibrary,
        OptimizationLevel optimizationLevel = OptimizationLevel.Release,
        bool checkOverflow = false,
        bool allowUnsafe = false,
        Platform platform = Platform.AnyCpu,
        NullableContextOptions nullableContextOptions =
            NullableContextOptions.Enable,
        bool deterministic = true)
    {
        return new(
            outputKind,
            optimizationLevel: optimizationLevel,
            checkOverflow: checkOverflow,
            allowUnsafe: allowUnsafe,
            platform: platform,
            nullableContextOptions: nullableContextOptions,
            deterministic: deterministic,
            concurrentBuild: false);
    }
}
