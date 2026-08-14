using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class WorkerTests
{
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

    private static readonly string[] ReplayedAllocationWitnessKinds = [
        "managed-allocation",
        "managed-array-allocation"
    ];

    private static bool TryCreateFileSymbolicLink(string link, string target)
    {
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or
            PlatformNotSupportedException)
        {
            return false;
        }
    }

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
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
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
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(factoryCalls, Is.EqualTo(1));
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(CacheFiles(project), Is.Empty);
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
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Effect));
            Assert.That(claim.EffectContractKind,
                Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
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
        var result = response.ClaimResults.Single();
        var usedPreconditions = result.Assumptions.Where(
            static assumption =>
                assumption.Kind ==
                WorkerAssumptionKind.Precondition);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                result.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.VacuousEntry));
            Assert.That(
                result.Vacuity,
                Is.EqualTo(
                    WorkerVacuityKind
                        .ContradictoryPreconditions));
            Assert.That(result.ProofCore, Is.Not.Empty);
            Assert.That(usedPreconditions, Is.Not.Empty);
            Assert.That(
                usedPreconditions.All(
                    static assumption => assumption.Used),
                Is.True);
            Assert.That(
                first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Disabled));
            Assert.That(
                response.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Disabled));
            Assert.That(CacheFiles(project), Is.Empty);
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
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(
                result.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(WorkerClaimReason.ResourceLimit));
            Assert.That(
                result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(
                result.Vacuity,
                Is.EqualTo(WorkerVacuityKind.None));
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
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Reason),
                Is.All.EqualTo(
                    WorkerClaimReason.UnsupportedContract));
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
    public async Task EffectOnlyClaimRemainsAccountableWhileMixedRequiresFailsClosed()
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
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
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
            Assert.That(
                throwing.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
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
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task AllowedExceptionsRemainVisibleWhenMixedRequiresBodyIsUnsupported()
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
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
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
            Assert.That(
                maybeNull.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                maybeNull.Reason,
                Is.EqualTo(
                    WorkerClaimReason.EffectContractNotEstablished));
            Assert.That(
                maybeNull.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(maybeNull.EffectWitness, Is.Null);
            Assert.That(
                requiredNonNull.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                requiredNonNull.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(
                response.CallableResults.Single(result =>
                    result.CallableId.Contains(
                        ".MaybeNull(",
                        StringComparison.Ordinal)).Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(
                response.CallableResults.Single(result =>
                    result.CallableId.Contains(
                        ".RequiredNonNull(",
                        StringComparison.Ordinal)).Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
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
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(result.Model, Is.Empty);
            Assert.That(response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(response.CallableResults.Single().Reason,
                Is.EqualTo(
                    WorkerCallableCoverageReason.SemanticUnknown));
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
    public async Task ConditionalEffectViolationRemainsTypedUnknown()
    {
        using var project = TestProject.Create(
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
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new CountingBackend(BackendCheckResult.Unsatisfiable([])));

        var response = await worker.VerifyAsync(request);
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(
                    WorkerClaimReason.EffectContractNotEstablished));
            Assert.That(
                result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.CompleteMayEffectSummary));
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(
                response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task UnprovenInitializationAndExceptionConstructionDoNotRefute()
    {
        using var project = TestProject.Create(
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
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new CountingBackend(BackendCheckResult.Unsatisfiable([])));

        var response = await worker.VerifyAsync(request);

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
    public async Task DirectWriteAndCapabilityClaimsFailClosedWithoutReplayTraces()
    {
        using var project = TestProject.Create(
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
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new CountingBackend(BackendCheckResult.Unsatisfiable([])));

        var response = await worker.VerifyAsync(request);
        var results = response.ClaimResults.OrderBy(result =>
            response.Manifest.Claims.Single(claim =>
                claim.ClaimId == result.ClaimId).EffectContractKind).ToArray();
        var responseJson = WorkerProtocolJson.SerializeResponse(response);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                results.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown),
                responseJson);
            Assert.That(
                results.Select(static result => result.EffectCertainty),
                Is.All.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(
                results.Select(static result => result.Reason),
                Is.All.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(
                results.Select(static result => result.EffectWitness),
                Is.All.Null);
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(WorkerCallableCoverage.Incomplete));
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
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
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.EffectCertainty,
                Is.EqualTo(
                    WorkerEffectEvidenceCertainty.TrustedCompleteBoundary));
            Assert.That(result.Assumptions.Select(static item => item.Kind),
                Does.Contain(WorkerAssumptionKind.TrustedBoundary));
            Assert.That(response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task MixedPostconditionAndEffectClaimsAreReturnedInManifestOrder()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [DoesNotThrow]
                [return: Positive]
                public static int One() => 1;
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

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
            Assert.That(backend.CallCount, Is.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task InvalidCompilerElidedClauseDoesNotPoisonCompanionProof()
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
                Is.EqualTo(WorkerClaimEvidence.CompanionClause));
            Assert.That(response.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven),
                response.ClaimResults.Single().Reason.ToString());
            Assert.That(response.CallableResults.Single().Coverage,
                Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task ProvenEffectEvidenceNeverEntersTheSemanticCache()
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
                Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(second.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(second.Manifest.Hash, Is.EqualTo(first.Manifest.Hash));
            Assert.That(second.ClaimResults.Single().ClaimId,
                Is.EqualTo(first.ClaimResults.Single().ClaimId));
            Assert.That(second.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(second.ClaimResults.Single().EffectCertainty,
                Is.EqualTo(first.ClaimResults.Single().EffectCertainty));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(CacheFiles(project), Is.Empty);
        }
    }

    [Test]
    public async Task CompilerAllocationViolationsAreReplayedAndNotCached()
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
                Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(
                second.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Miss));
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
            Assert.That(CacheFiles(project), Is.Empty);
            Assert.That(WorkerProtocolJson.Validate(second).IsValid, Is.True);
        }
    }

    [Test]
    public async Task UnsupportedSelectedEffectCallablesRemainTypedUnknown()
    {
        using var project = TestProject.Create(
            """
            using System;
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Subject {
                [ZeroAllocations]
                public static object Generic<T>() =>
                    new object();

                [ZeroAllocations]
                public static async Task<object> Async() {
                    await Task.Yield();
                    return new object();
                }

                [ZeroAllocations]
                public static object DelegateCall(
                    Func<object> factory) =>
                    new object();
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new CountingBackend(
                BackendCheckResult.Unsatisfiable([])));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ClaimResults, Has.Length.EqualTo(3));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Reason),
                Is.All.EqualTo(
                    WorkerClaimReason.UnsupportedCallable));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.EffectCertainty),
                Is.All.EqualTo(
                    WorkerEffectEvidenceCertainty.Unavailable));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.EffectWitness),
                Is.All.Null);
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(
                    WorkerCallableCoverage.Incomplete));
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Reason),
                Is.All.EqualTo(
                    WorkerCallableCoverageReason
                        .UnsupportedCallable));
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
        }
    }

    [Test]
    public async Task UnsupportedSelectedContractCallablesRemainTypedUnknown()
    {
        using var project = TestProject.Create(
            """
            using System.Threading.Tasks;
            using SharpProof.Attributes;

            public static class Subject {
                public static int Generic<T>() {
                    Contract.Ensures(
                        Contract.Result<int>() == 1);
                    return 1;
                }

                public static async Task<int> Async() {
                    Contract.Ensures(true);
                    await Task.Yield();
                    return 1;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(
            new CountingBackend(
                BackendCheckResult.Unsatisfiable([])));

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults.Select(static result =>
                    result.Reason),
                Is.All.EqualTo(
                    WorkerClaimReason.UnsupportedCallable));
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Coverage),
                Is.All.EqualTo(
                    WorkerCallableCoverage.Incomplete));
            Assert.That(
                response.CallableResults.Select(static result =>
                    result.Reason),
                Is.All.EqualTo(
                    WorkerCallableCoverageReason
                        .UnsupportedCallable));
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(
                WorkerProtocolJson.Validate(response).IsValid,
                Is.True);
        }
    }

    [Test]
    public async Task CompilerEffectWitnessTamperingCannotBypassReplay()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                [ZeroAllocations]
                public static object Allocate() => new object();
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var artifact = CompilerManifestArtifactJson.Deserialize(
            await File.ReadAllTextAsync(
                request.CompilerManifest.Path));
        var evidence = artifact.Callables.Single()
            .EffectClaims.Single();
        evidence.Witness!.Effects = WorkerEffectSet.Throws;
        CompilerEffectClaimArtifactCodec.Seal(evidence);
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            CompilerManifestArtifactJson.Serialize(artifact));
        await File.WriteAllBytesAsync(
            request.CompilerManifest.Path,
            bytes);
        request.CompilerManifest.Sha256 =
            WorkerProtocolJson.ComputeSha256(bytes);
        using var worker = new SharpProofWorker(
            new CountingBackend(
                BackendCheckResult.Unsatisfiable([])));

        var response = await worker.VerifyAsync(request);
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(
                    WorkerRunFailureReason.CounterexampleReplayFailed));
            Assert.That(
                result.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleReplayFailed));
            Assert.That(result.EffectWitness, Is.Null);
            Assert.That(
                response.Summary.CacheStatus,
                Is.Not.EqualTo(WorkerCacheStatus.Written));
        }
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

    [Test]
    public async Task NullCompilerDiagnosticsAreTypedAsManifestInvalid()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var json = await File.ReadAllTextAsync(request.CompilerManifest.Path);
        json = json.Replace(
            "\"compilerDiagnostics\":[]",
            "\"compilerDiagnostics\":null",
            StringComparison.Ordinal);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        await File.WriteAllBytesAsync(request.CompilerManifest.Path, bytes);
        request.CompilerManifest.Sha256 = WorkerProtocolJson.ComputeSha256(bytes);
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
        artifact.CompilationSha256 =
            CompilationFingerprint.ComputeSha256(artifact.Compilation, []);
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            CompilerManifestArtifactJson.Serialize(artifact));
        await File.WriteAllBytesAsync(request.CompilerManifest.Path, bytes);
        request.CompilerManifest.Sha256 =
            WorkerProtocolJson.ComputeSha256(bytes);
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
        {
            Interlocked.Increment(ref factoryCalls);
            return new CountingBackend(BackendCheckResult.Unsatisfiable([]));
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
            Assert.That(CacheFiles(project), Is.Empty);
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
        using (var first = new SharpProofWorker(
                   new SpuriousModelBackend()))
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
    public async Task ClosedArtifactRecordsCompilerSemanticOptions()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(
            cacheEnabled: false,
            parseOptions: CreateParseOptions(LanguageVersion.CSharp13),
            compilationOptions: CreateRoslynOptions(
                outputKind: OutputKind.ConsoleApplication,
                optimizationLevel: OptimizationLevel.Debug,
                checkOverflow: true,
                allowUnsafe: true,
                platform: Platform.X64,
                nullableContextOptions: NullableContextOptions.Warnings,
                deterministic: false));
        var snapshot = await WorkerInputSnapshot.LoadAsync(
            request,
            WorkerCacheIdentity.Current,
            CancellationToken.None);

        var parse = snapshot.CompilerManifest.Compilation.SyntaxTrees.Single();
        var options = snapshot.CompilerManifest.Compilation.Options;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                parse.LanguageVersion,
                Is.EqualTo(LanguageVersion.CSharp13.ToString()));
            Assert.That(
                options.NullableContext,
                Is.EqualTo(CompilerNullableContext.Warnings));
            Assert.That(
                options.OptimizationLevel,
                Is.EqualTo(CompilerOptimizationLevel.Debug));
            Assert.That(options.CheckOverflow, Is.True);
            Assert.That(options.AllowUnsafe, Is.True);
            Assert.That(options.Deterministic, Is.False);
            Assert.That(
                options.OutputKind,
                Is.EqualTo(CompilerOutputKind.ConsoleApplication));
            Assert.That(
                options.Platform,
                Is.EqualTo(CompilerPlatform.X64));
        }
    }

    [Test]
    public async Task EverySemanticCompilationOptionInvalidatesTheCache()
    {
        using var project = TestProject.Create(RefutationSource);
        var requests = new List<WorkerVerifyRequest>();
        Add();
        Add(targetFramework: "net8.0-linux");
        Add(parseOptions: CreateParseOptions(LanguageVersion.CSharp11));
        Add(compilationOptions: CreateRoslynOptions(
            nullableContextOptions: NullableContextOptions.Warnings));
        Add(compilationOptions: CreateRoslynOptions(
            optimizationLevel: OptimizationLevel.Debug));
        Add(compilationOptions: CreateRoslynOptions(checkOverflow: true));
        Add(compilationOptions: CreateRoslynOptions(allowUnsafe: true));
        Add(compilationOptions: CreateRoslynOptions(deterministic: false));
        Add(compilationOptions: CreateRoslynOptions(
            outputKind: OutputKind.NetModule));
        Add(compilationOptions: CreateRoslynOptions(platform: Platform.X64));
        Add(parseOptions: CreateParseOptions(
            preprocessorSymbols: ["EXTRA"]));
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var hashes = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            var response = await worker.VerifyAsync(request);
            Assert.That(response.Errors, Is.Empty);
            Assert.That(hashes.Add(response.InputHash), Is.True);
        }

        Assert.That(backend.CallCount, Is.EqualTo(requests.Count));
        Assert.That(
            Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json"),
            Has.Length.EqualTo(requests.Count));

        void Add(
            CSharpParseOptions? parseOptions = null,
            CSharpCompilationOptions? compilationOptions = null,
            string targetFramework = "net8.0")
        {
            requests.Add(project.CreateRequest(
                cacheEnabled: true,
                parseOptions,
                compilationOptions,
                targetFramework));
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

        var baseline = await WorkerInputSnapshot.LoadAsync(
            request,
            baselineIdentity,
            CancellationToken.None);
        var tool = await WorkerInputSnapshot.LoadAsync(
            request,
            changedTool,
            CancellationToken.None);
        var binary = await WorkerInputSnapshot.LoadAsync(
            request,
            changedBinary,
            CancellationToken.None);
        var specs = await WorkerInputSnapshot.LoadAsync(
            request,
            changedSpecs,
            CancellationToken.None);
        var specContent = await WorkerInputSnapshot.LoadAsync(
            request,
            changedSpecContent,
            CancellationToken.None);
        var artifactBytes = await File.ReadAllBytesAsync(
            request.CompilerManifest.Path);
        var sharedHash = CompilerArtifactInputHash.Compute(
            request, artifactBytes, baselineIdentity.ToolIdentity,
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
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
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
        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
        Assert.That(
            response.ClaimResults[0].Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(backend.CallCount, Is.EqualTo(1));
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

        Assert.That(response.Errors, Is.Empty);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(cases.Length));

        var runtimeRequest = project.CreateRequest(
            cacheEnabled: false,
            parseOptions: CreateParseOptions(preprocessorSymbols: []));
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

        var loadContext = new System.Runtime.Loader.AssemblyLoadContext(
            "SharpProof.Worker.Test.RuntimeContractOracle",
            isCollectible: true);
        loadContext.Resolving += ResolveRuntimeContractAssembly;
        try
        {
            image.Position = 0;
            var assembly = loadContext.LoadFromStream(image);
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
        }
        finally
        {
            loadContext.Resolving -= ResolveRuntimeContractAssembly;
            loadContext.Unload();
        }
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
            Is.EqualTo(["domain:parameter:0"]));
        Assert.That(
            response.ClaimResults
                .Where(record => !GetCallableId(response, record).Contains(
                    ".Int64Identity(",
                    StringComparison.Ordinal))
                .SelectMany(static record => record.ProofCore),
            Is.All.EqualTo("domain:parameter:0"));
    }

    [Test]
    public async Task SourceDomainAssumptionsUseLoweredEvidence()
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
        var backend = new CapturingBackend(
            BackendCheckResult.Unsatisfiable([0]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var query = backend.Query;
        Assert.That(query.Assumptions, Has.Length.EqualTo(1));
        Assert.That(
            query.Assumptions[0].Justification,
            Is.TypeOf<LoweredJustification>());
        Assert.That(
            response.ClaimResults.Single().ProofCore,
            Is.EqualTo(["domain:parameter:0"]));
    }

    [Test]
    public async Task ProofCoreMarksOnlyTheUserAssumptionItUses()
    {
        using var project = TestProject.Create(
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
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var snapshot = await WorkerInputSnapshot.LoadAsync(
            request,
            WorkerCacheIdentity.Current,
            CancellationToken.None);
        var expectedUsedId = snapshot.CompilerManifest.Callables.Single()
            .Clauses.First(static clause =>
                clause.Kind == CompilerContractKind.Assume)
            .AssumptionId;
        var backend = new CapturingBackend(
            BackendCheckResult.Unsatisfiable([0]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        var record = response.ClaimResults.Single();
        Assert.That(
            record.Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven),
            record.Reason.ToString());
        Assert.That(
            backend.Query.Assumptions[0].Justification,
            Is.TypeOf<UserAssumedJustification>());
        var userAssumptions = record.Assumptions
            .Where(static evidence =>
                evidence.Kind == WorkerAssumptionKind.UserAssume).ToArray();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                userAssumptions.Count(static evidence => evidence.Used),
                Is.EqualTo(1));
            Assert.That(
                userAssumptions.Single(static evidence => evidence.Used).Id,
                Is.EqualTo(expectedUsedId));
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
        var result = response.ClaimResults.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Vacuity,
                Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
            Assert.That(first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(response.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Miss));
            Assert.That(CacheFiles(project), Is.Empty);
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
                Is.EqualTo(WorkerVacuityKind.None));
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
    public async Task DirectMathAbsReturnIsProvenFromItsApiSpec()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Absolute(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= 0);
                    return Math.Abs(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                record.ProofCore,
                Is.EqualTo(["spec:bcl.math.abs.int32"]));
            Assert.That(record.Model, Is.Empty);
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
                public static int AbsoluteBranch(long divisor) {
                    Contract.Ensures(divisor != 0);
                    return Math.Abs(1L / divisor == 0 ? 1 : -1);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.None));
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
                Is.EqualTo(["spec:bcl.string.concat.string-string"]));
            Assert.That(empty, Has.Length.EqualTo(2));
            foreach (var record in empty)
            {
                Assert.That(
                    record.ProofCore,
                    Is.EqualTo(["spec:bcl.array.empty"]));
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(record.ProofCore, Is.Empty);
        }
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(record.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedExpression));
            Assert.That(record.ProofCore, Is.Empty);
        }
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
                private static void TouchAmbient() => s_ambient++;

                public static int[] Unsafe() {
                    Contract.Ensures(
                        Contract.Result<int[]>() != null);
                    var result = Array.Empty<int>();
                    TouchAmbient();
                    return result;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(record.ProofCore, Is.Empty);
        }
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.None));
        }
    }

    [Test]
    public async Task LoopsAbstainWhileDirectAcyclicSourceCallsAreProven()
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
            Assert.That(loop.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                loop.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(call.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(call.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                call.ProofCore.Any(static item => item.StartsWith(
                    "source-summary:",
                    StringComparison.Ordinal)),
                Is.True);
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CompilerImplementationIlSummaryLowerer.IsCandidate(
                    compilation,
                    external),
                Is.True);
            Assert.That(
                external.MetadataToken & unchecked((int)0xff000000),
                Is.EqualTo(0x06000000));
        }
        var discovery = new ClaimManifestBuilder(compilation).Build();
        var target = discovery.Targets.Values.Single(candidate =>
            candidate.Method.MetadataName == "Call");
        var lowerer = new CompilerCallableLowerer(
            compilation,
            new IrFactory());
        var preparation = lowerer.Prepare(target);
        Assert.That(
            preparation.IsSuccess,
            Is.True,
            lowerer.LastImplementationIlAbstention.ToString());
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                result.ProofCore.Any(static item => item.StartsWith(
                    "il-summary:",
                    StringComparison.Ordinal)),
                Is.True);
        }
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
        var summary = artifact.Callables.Single()
            .Body!.SummaryCalls.Single();
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var result = response.ClaimResults.Single();
        var implementationEvidence = summary.DependencyEvidence
            .SingleOrDefault(item =>
                item.Origin == CompilerSummaryOrigin.ImplementationIl);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(summary.Origin, Is.EqualTo(CompilerSummaryOrigin.Source));
            Assert.That(
                summary.DependencyEvidence.Select(static item => item.Origin),
                Does.Contain(CompilerSummaryOrigin.ImplementationIl));
            Assert.That(
                implementationEvidence?.EvidenceSha256.Length ?? 0,
                Is.EqualTo(64));
            Assert.That(
                result.ProofCore.Any(static item => item.StartsWith(
                    "source-summary:",
                    StringComparison.Ordinal)),
                Is.True);
        }

        implementationEvidence!.EvidenceSha256 = new string('b', 64);
        Assert.That(
            (Action)(() => CompilerManifestArtifactJson.DecodeCallables(
                artifact)),
            Throws.TypeOf<InvalidDataException>());
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
                    result.ProofCore.Any(static item => item.StartsWith(
                        "il-summary:",
                        StringComparison.Ordinal))));
        }
    }

    [Test]
    public void ImplementationIlScalarOpcodeMatrixBuildsExactSummaries()
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
        var unsupported = new Dictionary<string, CompilerImplementationIlAbstentionReason>(
            StringComparer.Ordinal)
        {
            ["VerifyLongAdd"] = CompilerImplementationIlAbstentionReason.UnsupportedIl,
            ["VerifyNeg64"] = CompilerImplementationIlAbstentionReason.UnsupportedIl,
            ["VerifyInadmissibleCall"] =
                CompilerImplementationIlAbstentionReason.InadmissibleCallTarget
        };
        var successful = 0;

        foreach (var signature in signatures)
        {
            var targetName = "Verify" + signature.Name;
            var lowerer = new CompilerCallableLowerer(compilation, new IrFactory());
            var preparation = lowerer.Prepare(targets[targetName]);
            if (unsupported.TryGetValue(targetName, out var expectedReason))
            {
                Assert.That(preparation.IsSuccess, Is.False, targetName);
                Assert.That(
                    lowerer.LastImplementationIlAbstention,
                    Is.EqualTo(expectedReason),
                    targetName);
                continue;
            }

            Assert.That(
                preparation.IsSuccess,
                Is.True,
                targetName + ": " +
                lowerer.LastImplementationIlAbstention);
            var summary = preparation.Body!.SummaryCalls.Values.Single();
            using (Assert.EnterMultipleScope())
            {
                Assert.That(
                    summary.Origin,
                    Is.EqualTo(CompilerSummaryOrigin.ImplementationIl),
                    targetName);
                Assert.That(
                    summary.EvidenceSha256,
                    Does.Match("^[0-9a-f]{64}$"),
                    targetName);
                Assert.That(
                    summary.EvidenceIdentity,
                    Is.Empty,
                    targetName);
            }
            successful++;
        }

        Assert.That(successful, Is.EqualTo(signatures.Length - unsupported.Count));
    }

    [Test]
    public void DebugImplementationIlCoversLocalAndWideOperandForms()
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
                     "VerifyManyLocals",
                     "VerifyManyParameters"
                 })
        {
            var lowerer = new CompilerCallableLowerer(compilation, new IrFactory());
            var preparation = lowerer.Prepare(targets[targetName]);
            Assert.That(
                preparation.IsSuccess,
                Is.True,
                targetName + ": " + lowerer.LastImplementationIlAbstention);
            Assert.That(
                preparation.Body!.SummaryCalls.Values.Single().Origin,
                Is.EqualTo(CompilerSummaryOrigin.ImplementationIl),
                targetName);
        }
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
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.ClaimResults.Select(static result => result.Outcome),
                Is.All.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Is.All.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(
                response.ClaimResults.SelectMany(
                    static result => result.ProofCore),
                Is.Empty);
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
        var result = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                result.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(result.ProofCore, Is.Empty);
        }
    }

    [Test]
    public async Task AuditedSpecificationPackRequiresExplicitOptIn()
    {
        using var project = TestProject.Create(
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
            """);
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
        var summaryArtifact = withArtifact.Callables.Single()
            .Body!.SummaryCalls.Single();
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
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(enabled.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(enabled.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(
                enabled.ProofCore.Any(static item => item.StartsWith(
                    "spec-pack:dotnet.scalar@1:",
                    StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                summaryArtifact.Origin,
                Is.EqualTo(CompilerSummaryOrigin.SpecificationPack));
            Assert.That(
                summaryArtifact.EvidenceIdentity,
                Is.EqualTo("dotnet.scalar@1"));
            Assert.That(
                summaryArtifact.EvidenceSha256,
                Has.Length.EqualTo(64));
        }

        summaryArtifact.EvidenceIdentity = string.Empty;
        Assert.That(
            (Action)(() => CompilerManifestArtifactJson.DecodeCallables(
                withArtifact)),
            Throws.TypeOf<InvalidDataException>());
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            Assert.That(record.ProofCore, Is.Empty);
        }
    }

    [Test]
    public async Task SpecModeledCallProducesTypedNonfatalUnreplayableCounterexample()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Absolute(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= value);
                    return Math.Abs(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = SharpProofWorker.Create(request.Budgets);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(record.ProofCore, Is.Empty);
            Assert.That(record.Model, Is.Empty);
        }
    }

    [Test]
    public async Task RelationalSummaryCallProducesTypedNonfatalUnreplayableCounterexample()
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(
                    WorkerClaimReason.CounterexampleNotReplayable));
            Assert.That(record.ProofCore, Is.Empty);
            Assert.That(record.Model, Is.Empty);
        }
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
                    var ignored = Math.Abs(value);
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

        var result = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    [Test]
    public async Task WorkerProductPathInstantiatesApiSpecPostconditions()
    {
        using var project = TestProject.Create(
            """
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Absolute(int value) {
                    Contract.Ensures(
                        Contract.Result<int>() >= 0);
                    return Math.Abs(value);
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new CapturingBackend(
            BackendCheckResult.Unsatisfiable([0]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        var query = backend.Query;
        var specAssumption = query.Assumptions.Single(assumption =>
            assumption.Justification is SpecJustification);
        var predicate = specAssumption.Predicate as IrBinaryTerm;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(
                response.ClaimResults.Single().ProofCore,
                Is.EqualTo(["spec:bcl.math.abs.int32"]));
            Assert.That(predicate, Is.Not.Null);
            Assert.That(
                predicate!.Operator,
                Is.EqualTo(IrBinaryOperator.GreaterThanOrEqual));
            Assert.That(predicate.Left, Is.TypeOf<IrVariableTerm>());
            Assert.That(
                predicate.Right,
                Is.TypeOf<IrIntegerTerm>()
                    .And.Property(nameof(IrIntegerTerm.Value)).EqualTo(0));
        }
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                record.Model.Single(value =>
                    value.Variable == "parameter:0").Value,
                Is.EqualTo(byte.MaxValue.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)));
        }
    }

    [Test]
    public async Task WidthSensitiveArithmeticAndConversionsAbstain()
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
        Assert.That(
            response.ClaimResults.Select(static record => record.Outcome),
            Is.All.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(
            response.ClaimResults
                .Where(record => GetCallableId(response, record).Contains(
                    "Contract(",
                    StringComparison.Ordinal))
                .Select(static record => record.Reason),
            Is.All.EqualTo(
                WorkerClaimReason.UnsupportedExpression));
        Assert.That(
            response.ClaimResults
                .Where(record => GetCallableId(response, record).Contains(
                    "Body(",
                    StringComparison.Ordinal))
                .Select(static record => record.Reason),
            Is.All.EqualTo(WorkerClaimReason.UnsupportedBody));
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
            response.ClaimResults.SelectMany(static record => record.ProofCore),
            Does.Contain("body:normal-completion"));
        Assert.That(
            response.ClaimResults.Select(static record => record.Vacuity),
            Is.All.EqualTo(WorkerVacuityKind.NoModeledNormalReturn));
    }

    [Test]
    public async Task UnusedAssignmentDefinednessConstrainsNormalCompletion()
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(record.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(record.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(record.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            Assert.That(record.ProofCore, Does.Contain("body:normal-completion"));
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
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.RunStatus,
                Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(
                    WorkerClaimReason.PostconditionMayBeUndefined));
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

        Assert.That(response.Errors, Is.Empty);
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        }
    }

    [Test]
    public async Task NullableStringProofsAbstainWithoutNullTagEncoding()
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
                WorkerClaimReason.UnsupportedExpression));
    }

    [Test]
    public async Task UnsupportedBodyAndDeepPostconditionAbstain()
    {
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                private sealed class Reader {
                    internal long Read(long value) => value;
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
            """);
        var request = project.CreateRequest(
            cacheEnabled: false,
            maximumExpressionDepth: 3);
        using var worker = new SharpProofWorker(new CountingBackend(
            BackendCheckResult.Unsatisfiable([])));
        var response = await worker.VerifyAsync(request);

        Assert.That(
            response.ClaimResults.Select(static record => record.Reason),
            Is.EquivalentTo(new[] {
                WorkerClaimReason.DeepPostcondition,
                WorkerClaimReason.UnsupportedBody
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

        Assert.That(response.Errors, Is.Empty);
        var record = response.ClaimResults.Single();
        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                record.Outcome,
                Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(
                record.Reason,
                Is.EqualTo(WorkerClaimReason.UnsupportedContract));
        }
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

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(response.ClaimResults, Has.Length.EqualTo(1));
            Assert.That(response.ClaimResults[0].Outcome,
                Is.EqualTo(WorkerClaimOutcome.Proven));
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
        var backend = new SpuriousModelBackend();
        using var firstWorker = new SharpProofWorker(backend);
        var first = await firstWorker.VerifyAsync(enabled);
        var second = await firstWorker.VerifyAsync(enabled);
        Assert.That(backend.CallCount, Is.EqualTo(1));
        AssertSemanticallyEquivalent(first, second);

        var disabled = project.CreateRequest(cacheEnabled: false);
        var disabledBackend = new SpuriousModelBackend();
        using var disabledWorker = new SharpProofWorker(disabledBackend);
        var withoutCache = await disabledWorker.VerifyAsync(disabled);
        AssertSemanticallyEquivalent(first, withoutCache);
    }

    [Test]
    public async Task RequireProvenDoesNotReuseTheAdvisorySemanticCache()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);

        request.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
        request.AssumptionPolicy = WorkerAssumptionPolicy.Error;
        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(second.InputHash, Is.EqualTo(first.InputHash));
            Assert.That(second.RequestHash, Is.Not.EqualTo(first.RequestHash));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
        }
    }

    [Test]
    public async Task UnknownOutcomesNeverEnterTheCache()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new CountingBackend(
            BackendCheckResult.Unknown(
                BackendFailureReason.ResourceLimit));
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(
            first.ClaimResults.Single().Outcome,
            Is.EqualTo(WorkerClaimOutcome.Unknown));
        AssertSemanticallyEquivalent(first, second);
        Assert.That(
            Directory.Exists(project.CacheDirectory)
                ? Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json")
                : [],
            Is.Empty);
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
                Is.EqualTo(claimReason));
        }
    }

    [Test]
    public async Task UnexpectedBackendExceptionBecomesTypedInfrastructureFailure()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        using var worker = new SharpProofWorker(new ThrowingBackend());

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

    [Test]
    public async Task FatalClaimTakesPrecedenceOverAnotherCallableTimeout()
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
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 30;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        using var worker = new SharpProofWorker(
            new UnavailableThenDelayingBackend());

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Failed));
            Assert.That(
                response.FailureReason,
                Is.EqualTo(WorkerRunFailureReason.BackendUnavailable));
            Assert.That(
                response.ClaimResults.Select(static result => result.Reason),
                Does.Contain(WorkerClaimReason.BackendUnavailable)
                    .And.Contain(WorkerClaimReason.MethodTimeout));
        }
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
        var factory = new IrFactory();
        ImmutableArray<CompilerCallablePreparation> targets = [
            new CompilerCallablePreparation(
                factory,
                manifest.Callables[0],
                [new CompilerPreparedClause(
                    CompilerContractKind.Ensures,
                    factory.Boolean(false),
                    CompilerContractEvidence.CompilerBoundInvocation,
                    "claim",
                    null)],
                [],
                WorkerClaimReason.None,
                CompilerPreparedBody.Trivial())
        ];
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
                manifest,
                targets),
            Is.False);
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                "not-a-sha-256-hash",
                manifest,
                targets),
            Is.False);
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                null!,
                targets),
            Is.False);

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Refuted;
        response.Summary.OutcomeCounts[0].Outcome =
            WorkerClaimOutcome.Refuted;
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest,
                targets),
            Is.True);

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Unknown;
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest,
                targets),
            Is.False);

        response.ClaimResults[0].Outcome = WorkerClaimOutcome.Proven;
        response.ClaimResults[0].Reason =
            WorkerClaimReason.InfrastructureFailure;
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                response.InputHash,
                manifest,
                targets),
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
                manifest,
                targets),
            Is.False);
        Assert.That(
            VerificationCache.IsCacheable(
                response,
                new string('b', 64),
                manifest,
                targets),
            Is.False);
        response.Errors = [];
        response.ClaimResults[0].Assumptions = [];
        Assert.That(VerificationCache.IsCacheable(
            response,
            response.InputHash,
            manifest,
            targets), Is.False);

        response.ClaimResults = [null!];
        Assert.That(VerificationCache.IsCacheable(
            response,
            response.InputHash,
            manifest,
            targets), Is.False);
    }

    [Test]
    public async Task CorruptCacheFailsClosedAndRecomputes()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var cacheFile = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        await File.WriteAllTextAsync(cacheFile, "{corrupt");
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(2));
        AssertSemanticallyEquivalent(first, second);
    }

    [Test]
    public async Task PreviousReplayCacheSchemaMissesAndRecomputes()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
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

        Assert.That(backend.CallCount, Is.EqualTo(2));
        AssertSemanticallyEquivalent(first, second);
    }

    [Test]
    public async Task CacheEvictionHonorsTheConfiguredByteBound()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.MaximumBytes = 1;
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(
                Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json"),
                Is.Empty);
        }
    }

    [Test]
    public async Task CacheHitEnforcesALoweredByteBound()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        var cacheFile = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        request.Cache.MaximumBytes = new FileInfo(cacheFile).Length - 1;

        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(
                Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json"),
                Is.Empty);
        }
    }

    [Test]
    public async Task CacheHitEvictsOlderEntriesUnderTheActiveByteBound()
    {
        using var project = TestProject.Create(RefutationSource);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var firstRequest = project.CreateRequest(
            cacheEnabled: true,
            targetFramework: "net8.0-linux");
        var secondRequest = project.CreateRequest(
            cacheEnabled: true,
            targetFramework: "net9.0-linux");
        await worker.VerifyAsync(firstRequest);
        var oldest = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        await worker.VerifyAsync(secondRequest);
        var files = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json");
        Assert.That(files, Has.Length.EqualTo(2));
        var newest = files.Single(path => !string.Equals(
            path,
            oldest,
            StringComparison.Ordinal));
        File.SetLastWriteTimeUtc(oldest, DateTime.UtcNow.AddMinutes(-1));
        File.SetLastWriteTimeUtc(newest, DateTime.UtcNow);
        secondRequest.Cache.MaximumBytes = new FileInfo(newest).Length;

        var hit = await worker.VerifyAsync(secondRequest);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(hit.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(
                Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json"),
                Is.EqualTo(new[] { newest }));
        }
    }

    [Test]
    public async Task CanceledCachePublicationCannotBecomeALaterHit()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        using var cancellation = new CancellationTokenSource();
        try
        {
            VerificationCache.PathValidationOverride = (_, path) =>
            {
                if (path.EndsWith(
                        ".sharp-proof-cache.json",
                        StringComparison.Ordinal) &&
                    File.Exists(path))
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }
            };
            var canceled = await worker.VerifyAsync(request, cancellation.Token);
            Assert.That(canceled.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
            Assert.That(
                Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json"),
                Is.Empty);
        }
        finally
        {
            VerificationCache.PathValidationOverride = null;
        }

        var recomputed = await worker.VerifyAsync(request);
        var cached = await worker.VerifyAsync(request);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(recomputed.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(cached.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(backend.CallCount, Is.EqualTo(2));
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
    public async Task CacheEvictionPreservesUnownedSuffixMatches()
    {
        using var project = TestProject.Create(RefutationSource);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var firstRequest = project.CreateRequest(cacheEnabled: true);
        var first = await worker.VerifyAsync(firstRequest);
        Assert.That(
            first.Summary.CacheStatus,
            Is.EqualTo(WorkerCacheStatus.Written));
        var owned = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        var maximumBytes = new FileInfo(owned).Length + 16;
        File.Delete(owned);

        Directory.CreateDirectory(project.CacheDirectory);
        var unowned = new[]
        {
            Path.Combine(
                project.CacheDirectory,
                "important.sharp-proof-cache.json"),
            Path.Combine(
                project.CacheDirectory,
                new string('A', 64) + ".sharp-proof-cache.json")
        };
        foreach (var path in unowned)
        {
            await File.WriteAllBytesAsync(
                path,
                new byte[maximumBytes]);
        }

        var request = project.CreateRequest(
            cacheEnabled: true,
            targetFramework: "net8.0-linux");
        request.Cache.MaximumBytes = maximumBytes;
        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(unowned.Select(File.Exists), Is.All.True);
        }
    }

    [Test]
    public async Task CacheDirectoryLockMakesReadMissAndWriteUnavailable()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));

        using var heldLock = new FileStream(
            Path.Combine(project.CacheDirectory, ".sharp-proof-cache.lock"),
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None);
        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
        }
    }

    [Test]
    public async Task ReparsePointCacheEntryFailsClosedWithoutTouchingTarget()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));

        var cacheFile = Directory.GetFiles(
            project.CacheDirectory,
            "*.sharp-proof-cache.json").Single();
        var external = Path.Combine(project.DirectoryPath, "external-cache.json");
        const string externalContents = "external cache target";
        await File.WriteAllTextAsync(external, externalContents);
        File.Delete(cacheFile);
        if (!TryCreateFileSymbolicLink(cacheFile, external))
        {
            Assert.Ignore("The host does not permit symbolic-link creation.");
        }

        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(await File.ReadAllTextAsync(external), Is.EqualTo(externalContents));
        }
    }

    [Test]
    public async Task ReparsePointCacheLockFailsClosedWithoutTouchingTarget()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        Assert.That(first.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Written));

        var lockPath = Path.Combine(
            project.CacheDirectory,
            ".sharp-proof-cache.lock");
        var external = Path.Combine(project.DirectoryPath, "external-lock");
        const string externalContents = "external lock target";
        await File.WriteAllTextAsync(external, externalContents);
        File.Delete(lockPath);
        if (!TryCreateFileSymbolicLink(lockPath, external))
        {
            Assert.Ignore("The host does not permit symbolic-link creation.");
        }

        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(second.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(await File.ReadAllTextAsync(external), Is.EqualTo(externalContents));
        }
    }

    [Test]
    public async Task ReparsePointEvictionEntryFailsClosedWithoutDeletion()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.MaximumBytes = 1;
        var external = Path.Combine(project.DirectoryPath, "external-eviction.json");
        const string externalContents = "external eviction target";
        await File.WriteAllTextAsync(external, externalContents);
        Directory.CreateDirectory(project.CacheDirectory);
        var cacheFile = Path.Combine(
            project.CacheDirectory,
            new string('b', 64) + ".sharp-proof-cache.json");
        if (!TryCreateFileSymbolicLink(cacheFile, external))
        {
            Assert.Ignore("The host does not permit symbolic-link creation.");
        }

        using var worker = new SharpProofWorker(new SpuriousModelBackend());
        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
            Assert.That(File.Exists(cacheFile), Is.True);
            Assert.That(await File.ReadAllTextAsync(external), Is.EqualTo(externalContents));
            Assert.That(
                Directory.GetFiles(
                    project.CacheDirectory,
                    "*.sharp-proof-cache.json"),
                Is.EqualTo(new[] { cacheFile }));
        }
    }

    [Test]
    public async Task ReparsePointCacheDirectoryFailsClosedBeforeChildAccess()
    {
        using var project = TestProject.Create(RefutationSource);
        var realDirectory = Path.Combine(project.DirectoryPath, "real-cache");
        var aliasDirectory = Path.Combine(project.DirectoryPath, "cache-alias");
        Directory.CreateDirectory(realDirectory);
        if (!TryCreateDirectorySymbolicLink(aliasDirectory, realDirectory))
        {
            Assert.Ignore("The host does not permit symbolic-link creation.");
        }

        var request = project.CreateRequest(cacheEnabled: true);
        request.Cache.Directory = aliasDirectory;
        using var worker = new SharpProofWorker(new SpuriousModelBackend());
        var response = await worker.VerifyAsync(request);

        Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Unavailable));
        Assert.That(Directory.GetFiles(realDirectory), Is.Empty);
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
        using var project = TestProject.Create(
            """
            using SharpProof.Attributes;
            public static class Subject {
                public static long Broken(long value) {
                    Contract.Ensures(Contract.Result<long>() > value);
                    return value;
                }
            }
            """);
        var request = project.CreateRequest(cacheEnabled: true);
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request);
        var cached = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                response.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
            Assert.That(
                response.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                cached.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Hit));
            Assert.That(
                Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json"),
                Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task RehashedCacheCannotUpgradeARefutationToProven()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        await RewriteCachedClaimAsync(
            project,
            claim =>
            {
                claim["outcome"] = nameof(WorkerClaimOutcome.Proven);
                claim["model"] = new JsonArray();
            });

        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(
                second.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                second.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
        }
    }

    [Test]
    public async Task RehashedCacheSealedForDifferentManifestMissesAndRecomputes()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        await RewriteCachedPayloadAsync(
            project,
            payload => payload["manifestHash"] = new string('c', 64));

        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(
                second.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                second.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
        }
    }

    [Test]
    public async Task RehashedCacheWithInvalidScalarModelMissesAndRecomputes()
    {
        using var project = TestProject.Create(RefutationSource);
        var request = project.CreateRequest(cacheEnabled: true);
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);
        await RewriteCachedClaimAsync(
            project,
            claim => claim["model"]![0]!["value"] = "not-an-integer");

        var second = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                first.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(backend.CallCount, Is.EqualTo(2));
            Assert.That(
                second.Summary.CacheStatus,
                Is.EqualTo(WorkerCacheStatus.Written));
            Assert.That(
                second.ClaimResults.Single().Outcome,
                Is.EqualTo(WorkerClaimOutcome.Refuted));
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

        Assert.That(
            response.ClaimResults.Single().Outcome,
            Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(
            response.ClaimResults.Single().Reason,
            Is.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task MethodRlimitIsCumulativeAcrossCallableQueries()
    {
        using var project = TestProject.Create(
            MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 6;
        request.Budgets.MethodRlimit = 12;
        var backend = new ResourceCountingBackend(
            resourceCost: 6,
            BackendCheckResult.Unsatisfiable([]));
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
        Assert.That(backend.CallCount, Is.EqualTo(2));
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
            }
            """);
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
    public async Task BackendFactoryCannotReuseAnInstanceAcrossSolverLanes()
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
        Assert.That(response.FailureReason, Is.EqualTo(WorkerRunFailureReason.BackendUnavailable));
        Assert.That(response.ClaimResults.Select(static result => result.Reason),
            Is.All.EqualTo(WorkerClaimReason.BackendUnavailable));
        Assert.That(backend.CallCount, Is.Zero);
    }

    [Test]
    public async Task MethodTimeoutRetiresAndRecreatesTheInterruptedSolverLane()
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
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 30;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
            Interlocked.Increment(ref factoryCalls) == 1
                ? new DelayingBackend()
                : new CountingBackend(BackendCheckResult.Unsatisfiable([])));

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
            }
            """);
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
        WorkerClaimReason.InfrastructureFailure, 1)]
    public async Task InvalidRenewalStateFailsClosedWithTypedEvidence(
        string scenario,
        WorkerRunFailureReason expectedFailure,
        WorkerClaimReason expectedClaimReason,
        int expectedFactoryCalls)
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
            }
            """);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MaxParallelism = 1;
        request.Budgets.MethodWallTimeMilliseconds = 30;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        var factoryCalls = 0;
        ISmtBackend? original = null;
        using var worker = new SharpProofWorker(() =>
        {
            factoryCalls++;
            if (factoryCalls == 1)
            {
                original = scenario == "dispose"
                    ? new ThrowingDisposeDelayingBackend()
                    : new DelayingBackend();
                return original;
            }

            return scenario switch
            {
                "null" => null!,
                "reuse" => original!,
                _ => new CountingBackend(
                    BackendCheckResult.Unsatisfiable([]))
            };
        });

        var response = await worker.VerifyAsync(request);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(factoryCalls, Is.EqualTo(expectedFactoryCalls));
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
    public async Task FactorylessTimeoutClassifiesEveryUnclaimedTargetAsTimedOut()
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
            }
            """);
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
        request.Budgets.MethodWallTimeMilliseconds = 30;
        request.Budgets.ProjectWallTimeMilliseconds = 1_000;
        var factoryCalls = 0;
        using var worker = new SharpProofWorker(() =>
            Interlocked.Increment(ref factoryCalls) == 1
                ? new DelayingBackend()
                : new ProofThenCounterexampleBackend());

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
                : new CountingBackend(
                    BackendCheckResult.Unsatisfiable([])));

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
                    WorkerClaimOutcome.Proven
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
            response.ClaimResults[0].Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(
            response.ClaimResults.Skip(1).Select(static record => record.Reason),
            Is.All.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task InjectedBuiltInBackendStillChargesTheMethodRlimit()
    {
        using var project = TestProject.Create(MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MethodRlimit = request.Budgets.QueryRlimit;
        using var backend = new SharpProof.Smt.IrSmtBackend(
            new SharpProof.Smt.IrSmtBackendOptions(
                request.Budgets.QueryRlimit));
        using var worker = new SharpProofWorker(
            backend,
            readConsumedResourceCount: null);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Errors, Is.Empty);
        Assert.That(
            response.ClaimResults[0].Outcome,
            Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(
            response.ClaimResults.Skip(1).Select(static record => record.Reason),
            Is.All.EqualTo(WorkerClaimReason.ResourceLimit));
    }

    [Test]
    public async Task UnmeteredBackendReservesThePerQueryRlimit()
    {
        using var project = TestProject.Create(MultipleEnsuresSource);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.QueryRlimit = 6;
        request.Budgets.MethodRlimit = 12;
        var backend = new CountingBackend(
            BackendCheckResult.Unsatisfiable([]));
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
        var backend = new SpuriousModelBackend();
        using var worker = new SharpProofWorker(backend);
        var first = await worker.VerifyAsync(request);

        request.Budgets.MethodRlimit--;
        var second = await worker.VerifyAsync(request);

        Assert.That(backend.CallCount, Is.EqualTo(2));
        Assert.That(second.InputHash, Is.Not.EqualTo(first.InputHash));
        Assert.That(
            Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json"),
            Has.Length.EqualTo(2));
    }

    [Test]
    public async Task CallerCancellationPreservesManifestAndIsNotCached()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: true);
        request.Budgets.MethodWallTimeMilliseconds = 5_000;
        using var worker = new SharpProofWorker(new DelayingBackend());
        using var cancellation = new CancellationTokenSource(50);

        var response = await worker.VerifyAsync(request, cancellation.Token);

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
            Directory.Exists(project.CacheDirectory)
                ? Directory.GetFiles(project.CacheDirectory, "*.sharp-proof-cache.json")
                : [],
            Is.Empty);
    }

    [Test]
    public async Task PreCanceledRunLoadsTheAuthoritativeManifestWithoutStartingProofWork()
    {
        using var project = TestProject.Create(TautologySource);
        var request = project.CreateRequest(cacheEnabled: false);
        var backend = new CountingBackend(BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var response = await worker.VerifyAsync(request, cancellation.Token);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
            Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
            Assert.That(response.ClaimResults.Single().Reason, Is.EqualTo(WorkerClaimReason.Canceled));
            Assert.That(backend.CallCount, Is.Zero);
            Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        }
    }

    [Test]
    public async Task ProjectBoundaryPermitsWorkThatFinishesBeforeItsDeadline()
    {
        var sources = Enumerable.Range(0, 512)
            .Select(index => ($"Padding{index}.cs", $"internal sealed class Padding{index} {{ }}"))
            .Prepend(("Subject.cs", TautologySource))
            .ToArray();
        using var project = TestProject.Create(sources);
        var request = project.CreateRequest(cacheEnabled: false);
        request.Budgets.MethodWallTimeMilliseconds = 1;
        request.Budgets.ProjectWallTimeMilliseconds = 1;
        var backend = new CountingBackend(BackendCheckResult.Unsatisfiable([]));
        using var worker = new SharpProofWorker(backend);

        var response = await worker.VerifyAsync(request);

        Assert.That(response.Manifest.Claims, Has.Length.EqualTo(1));
        Assert.That(WorkerProtocolJson.Validate(response).IsValid, Is.True);
        var reason = response.ClaimResults.Single().Reason;
        if (response.RunStatus == WorkerRunStatus.TimedOut)
        {
            Assert.That(reason, Is.EqualTo(WorkerClaimReason.ProjectTimeout));
            Assert.That(
                backend.CallCount,
                Is.LessThanOrEqualTo(1),
                "The project deadline may expire immediately before or " +
                "after the single backend call completes.");
        }
        else
        {
            Assert.That(response.RunStatus, Is.EqualTo(WorkerRunStatus.Complete));
            Assert.That(reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(backend.CallCount, Is.EqualTo(1));
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
            FindRepositoryRoot(),
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

    private static async Task RewriteCachedClaimAsync(
        TestProject project,
        Action<JsonObject> mutate)
    {
        await RewriteCachedPayloadAsync(
            project,
            payload => mutate(payload["claimResults"]![0]!.AsObject()));
    }

    private static async Task RewriteCachedPayloadAsync(
        TestProject project,
        Action<JsonObject> mutate)
    {
        var path = CacheFiles(project).Single();
        var envelope = JsonNode.Parse(
            await File.ReadAllTextAsync(path))!.AsObject();
        var payload = JsonNode.Parse(
            envelope["payload"]!.GetValue<string>())!.AsObject();
        mutate(payload);
        var payloadJson = payload.ToJsonString(
            WorkerProtocolJson.Options);
        envelope["payload"] = payloadJson;
        envelope["payloadHash"] =
            WorkerProtocolJson.ComputeSha256(
                System.Text.Encoding.UTF8.GetBytes(payloadJson));
        await File.WriteAllTextAsync(
            path,
            envelope.ToJsonString(WorkerProtocolJson.Options));
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

    private static System.Reflection.Assembly?
        ResolveRuntimeContractAssembly(
            System.Runtime.Loader.AssemblyLoadContext context,
            System.Reflection.AssemblyName requestedName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate =>
                System.Reflection.AssemblyName.ReferenceMatchesDefinition(
                    candidate.GetName(),
                    requestedName));
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

    private sealed class CapturingBackend(BackendCheckResult result)
        : ISmtBackend
    {
        private readonly BackendCheckResult _result = result;
        private VerificationQuery? _query;

        internal VerificationQuery Query =>
            _query ?? throw new InvalidOperationException(
                "The backend has not received a query.");

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _query = query;
            return Task.FromResult(_result);
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

    private sealed class ProofThenCounterexampleBackend : ISmtBackend
    {
        private int _calls;

        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return Task.FromResult(
                    BackendCheckResult.Unsatisfiable([]));
            }

            var assignments = query.ModelVariables.Select(variable =>
                KeyValuePair.Create(
                    variable,
                    query.Factory.CreateIntegerValue(0)));
            return Task.FromResult(
                BackendCheckResult.Satisfiable(
                    new BackendModel(assignments)));
        }
    }

    private sealed class ThrowingBackend : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException(
                "Injected unexpected backend failure.");
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
        internal CoordinatedBackend(ConcurrentLaneState state)
        {
            _state = state;
            state.CreatedBackend();
        }
        public Task<BackendCheckResult> CheckAsync(
            VerificationQuery query, CancellationToken cancellationToken)
        {
            return _state.CheckAsync(cancellationToken);
        }

        public void Dispose()
        {
            _state.DisposedBackend();
        }
    }

    private sealed class UnavailableThenDelayingBackend : ISmtBackend
    {
        private int _calls;

        public async Task<BackendCheckResult> CheckAsync(
            VerificationQuery query,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return BackendCheckResult.Unknown(
                    BackendFailureReason.Unavailable);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return BackendCheckResult.Unknown(
                BackendFailureReason.InfrastructureFailure);
        }
    }

    private sealed class ResourceCountingBackend(
        long resourceCost,
        BackendCheckResult result) : ISmtBackend
    {
        private readonly long _resourceCost = resourceCost;
        private readonly BackendCheckResult _result = result;
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
            return Task.FromResult(_result);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(
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

    private sealed class TestProject : IDisposable
    {
        private readonly List<string> _additionalReferencePaths = [];
        private bool _useNetCoreReferencePack;

        private TestProject(string directory, string[] sourcePaths)
        {
            DirectoryPath = directory;
            SourcePaths = sourcePaths;
            CacheDirectory = Path.Combine(directory, "cache");
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
            var directory = Path.Combine(
                Path.GetTempPath(),
                "SharpProof.Worker.Test",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var sourcePaths = sources.Select(source =>
            {
                var sourcePath = Path.Combine(directory, source.FileName);
                File.WriteAllText(
                    sourcePath,
                    source.Source,
                    new System.Text.UTF8Encoding(false));
                return sourcePath;
            }).ToArray();
            return new TestProject(directory, sourcePaths);
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
                    Sha256 = string.Concat(
                        System.Security.Cryptography.SHA256.HashData(bytes)
                            .Select(static value => value.ToString(
                                "x2",
                                System.Globalization.CultureInfo
                                    .InvariantCulture)))
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

        internal void AddImplementationReference(
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
                GetReferences().Select(static referencePath =>
                    MetadataReference.CreateFromFile(referencePath)),
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
        }

        public void Dispose()
        {
            var resolved = Path.GetFullPath(DirectoryPath);
            var expectedRoot = Path.GetFullPath(
                Path.Combine(
                    Path.GetTempPath(),
                    "SharpProof.Worker.Test"));
            if (!resolved.StartsWith(
                    expectedRoot + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Refusing to remove an unexpected test directory.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }

        private static string[] GetReferences()
        {
            var trusted = ((string)AppContext.GetData(
                    "TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator);
            var names = new HashSet<string>(
                RequiredReferenceFileNames,
                StringComparer.OrdinalIgnoreCase);
            return [.. trusted
                .Where(path => names.Contains(Path.GetFileName(path)))
                .Append(typeof(Contract).Assembly.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static path => path, StringComparer.Ordinal)];
        }

        private static string[] GetNetCoreReferencePack()
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
                .OrderBy(static path => path, StringComparer.Ordinal)];
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
                    ? GetNetCoreReferencePack()
                    : GetReferences())
                .Concat(_additionalReferencePaths)
                .Select(static path =>
                    MetadataReference.CreateFromFile(path));
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
