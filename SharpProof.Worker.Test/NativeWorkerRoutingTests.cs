using NUnit.Framework;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeWorkerRoutingTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PublicConstructionUsesTypedProofAndOriginalBodyReplay(bool injectBackend)
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() == unchecked(value + 1));
                    Contract.Ensures(Contract.Result<int>() > value);
                    return unchecked(value + 1);
                }
            }
            """);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var backend = new NativeCallableBackend(new IrSmtBackendOptions(project.Request.Budgets.QueryRlimit));
        using var worker = injectBackend ? new SharpProofWorker(backend) : SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.ClaimResults.Select(result => result.Outcome),
                Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
            Assert.That(response.ClaimResults[1].Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(response.ClaimResults[1].Model, Has.Length.EqualTo(1));
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        }
    }

    [TestCase("Contract.Result<string>() != null", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<string>() == null", WorkerClaimOutcome.Refuted)]
    public async Task NativeConcatModelsItsNonNullResult(string predicate, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject($$"""
            #nullable enable
            using SharpProof.Attributes;
            public static class Subject {
                public static string Target(string? left, string? right) {
                    Contract.Ensures({{predicate}});
                    return string.Concat(left, right);
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected), response.ClaimResults.Single().Reason.ToString());
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task NativeConcatPreservesPartialArgumentNormalCompletion()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static string Target(long divisor) {
                    Contract.Ensures(divisor != 0);
                    return string.Concat(1L / divisor == 0 ? "" : "value", "");
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
        Assert.That(response.ClaimResults.Single().Vacuity, Is.EqualTo(WorkerVacuityKind.None));
    }

    [Test]
    public async Task NativeStringContentEqualityRemainsUnsupported()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static string Target(string value) {
                    Contract.Ensures(Contract.Result<string>() == "value");
                    return string.Concat(value, "");
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [Test]
    public async Task NativeConcatEvaluatesNamedArgumentsInSourceOrder()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static string Target([InRange(0, 0)] int value) {
                    Contract.Ensures(value == 1);
                    return string.Concat(str1: ++value == 1 ? "left" : "", str0: value == 1 ? "right" : "");
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), response.ClaimResults.Single().Reason.ToString());
    }


    [TestCase("sbyte")]
    [TestCase("byte")]
    [TestCase("short")]
    [TestCase("ushort")]
    [TestCase("char")]
    [TestCase("int")]
    [TestCase("uint")]
    [TestCase("long")]
    [TestCase("ulong")]
    [TestCase("bool")]
    [TestCase("string")]
    [TestCase("object")]
    public async Task NativeEmptyArraysHaveExactResultFacets(string elementType)
    {
        using var project = new ShadowTestProject($$"""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static {{elementType}}[] Target() {
                    Contract.Ensures(Contract.Result<{{elementType}}[]>() != null);
                    Contract.Ensures(Contract.Result<{{elementType}}[]>().Length == 0);
                    Contract.Ensures(Contract.Result<{{elementType}}[]>().Length > 0);
                    return Array.Empty<{{elementType}}>();
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Select(claim => claim.Outcome),
            Is.EqualTo(new[] { WorkerClaimOutcome.Proven, WorkerClaimOutcome.Proven, WorkerClaimOutcome.Refuted }));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task NativeEmptyArrayCallsPreserveCachedIdentity()
    {
        using var project = new ShadowTestProject("""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static bool Target() {
                    Contract.Ensures(Contract.Result<bool>());
                    return Array.Empty<int>() == Array.Empty<int>();
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
    }

    [Test]
    public async Task NativeEmptyArrayModelRejectsSourceTypesNamedSystemArray()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            namespace System { public static class Array { public static T[] Empty<T>() { return null; } } }
            public static class Subject {
                public static int[] Target() {
                    Contract.Ensures(Contract.Result<int[]>() != null);
                    return System.Array.Empty<int>();
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        // Generic source expansion abstains; the impersonated type must never
        // acquire the trusted framework model or its non-null proof.
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
    }

    [TestCase("value > 0", "Contract.Result<int>() == value", WorkerClaimOutcome.Proven)]
    [TestCase("false", "Contract.Result<int>() == value", WorkerClaimOutcome.Proven)]
    [TestCase("value > 0", "Contract.Result<int>() > 0", WorkerClaimOutcome.Refuted)]
    [TestCase("false", "false", WorkerClaimOutcome.Refuted)]
    [TestCase("1 / value > 0", "Contract.Result<int>() == value", WorkerClaimOutcome.Proven)]
    public async Task NativeSourceCallsDoNotImportCalleeAssumptions(string assumption, string predicate, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public static class Subject {
                private static int Helper(int value) {
                    Contract.Assume({{assumption}});
                    return value;
                }
                public static int Target(int value) {
                    Contract.Ensures({{predicate}});
                    return Helper(value);
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected));
        Assert.That(response.ClaimResults.Single().Assumptions, Is.Empty);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task NativeSourceCallsPreserveTheCallersOwnAssumption()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                private static int Helper(int value) { Contract.Assume(false); return value; }
                public static int Target(int value) {
                    Contract.Assume(value > 0);
                    Contract.Ensures(Contract.Result<int>() > 0);
                    return Helper(value);
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var claim = response.ClaimResults.Single();
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(claim.Assumptions.Single().Kind, Is.EqualTo(WorkerAssumptionKind.UserAssume));
        Assert.That(claim.Assumptions.Single().Used, Is.True);
    }

    [TestCase(false, WorkerClaimOutcome.Refuted)]
    [TestCase(true, WorkerClaimOutcome.Proven)]
    public async Task NativeSourceCallsPreserveEmittedContractArguments(bool emitted, WorkerClaimOutcome expected)
    {
        var directive = emitted ? "#define SHARPPROOF_CONTRACTS" : "#undef SHARPPROOF_CONTRACTS";
        using var project = new ShadowTestProject($$"""
            {{directive}}
            using SharpProof.Attributes;
            public static class Subject {
                private static int Helper(int value) { Contract.Assume(1 / value > 0); return -1; }
                [return: Positive]
                public static int Target([InRange(0, 0)] int value) { return Helper(value); }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected));
        Assert.That(response.ClaimResults.Single().Vacuity,
            Is.EqualTo(emitted ? WorkerVacuityKind.NoModeledNormalReturn : WorkerVacuityKind.None));
    }

    [TestCase(false, WorkerClaimOutcome.Refuted)]
    [TestCase(true, WorkerClaimOutcome.Proven)]
    public async Task NativeSourceCallsRouteEmittedContractArgumentFaultsToCallerHandlers(bool emitted, WorkerClaimOutcome expected)
    {
        var directive = emitted ? "#define SHARPPROOF_CONTRACTS" : "#undef SHARPPROOF_CONTRACTS";
        using var project = new ShadowTestProject($$"""
            {{directive}}
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                private static int Helper(int value) { Contract.Assume(1 / value > 0); return -1; }
                [return: InRange(7, 7)]
                public static int Target([InRange(0, 0)] int value) {
                    try { return Helper(value); }
                    catch (DivideByZeroException) { return 7; }
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected));
        Assert.That(response.ClaimResults.Single().Vacuity, Is.EqualTo(WorkerVacuityKind.None));
    }

    [TestCase("Contract.Result<int>() >= 0", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<int>() != int.MinValue", WorkerClaimOutcome.Proven)]
    [TestCase("Contract.Result<int>() > 0", WorkerClaimOutcome.Refuted)]
    public async Task NativeMathAbsUsesOnlyNormalCompletion(string predicate, WorkerClaimOutcome expected)
    {
        using var project = new ShadowTestProject($$"""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures({{predicate}});
                    return Math.Abs(value);
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(expected));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task NativeMathAbsOverflowReachesTheCallerCatch()
    {
        using var project = new ShadowTestProject("""
            using System;
            using SharpProof.Attributes;
            public static class Subject {
                public static int Target(int value) {
                    Contract.Requires(value == int.MinValue);
                    Contract.Ensures(Contract.Result<int>() == 7);
                    try { return Math.Abs(value); }
                    catch (OverflowException) { return 7; }
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(response.ClaimResults.Single().Vacuity, Is.Not.EqualTo(WorkerVacuityKind.NoModeledNormalReturn));
    }

    [Test]
    public async Task NativeMathAbsDoesNotTrustASourceTypeWithTheFrameworkName()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            namespace System { public static class Math { public static int Abs(int value) => -1; } }
            public static class Subject {
                public static int Target(int value) {
                    Contract.Ensures(Contract.Result<int>() >= 0);
                    return System.Math.Abs(value);
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
    }

    [Test]
    public async Task NativeFactoryHandlesFullUnsignedDomain()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static ulong Target(ulong value) {
                    Contract.Requires(value == 18446744073709551615UL);
                    Contract.Ensures(Contract.Result<ulong>() == 0UL);
                    return unchecked(value + 1UL);
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [TestCase("value", WorkerClaimOutcome.Proven)]
    [TestCase("0", WorkerClaimOutcome.Refuted)]
    [TestCase("value", WorkerClaimOutcome.Proven, true)]
    [TestCase("0", WorkerClaimOutcome.Refuted, true)]
    public async Task NativeWorkerUsesCompanionClausesWithTheOriginalTargetBody(string returned, WorkerClaimOutcome outcome, bool instance = false)
    {
        var modifier = instance ? "" : "static";
        var receiver = instance ? "Subject receiver, " : "";
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public {{modifier}} class Subject { public {{modifier}} int Target(int value) { return {{returned}}; } }
            [ContractFor(typeof(Subject))] public static class SubjectContracts {
                public static int Target({{receiver}}int contractValue) {
                    Contract.Requires(contractValue > 0);
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(contractValue));
                    return contractValue;
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome));
        Assert.That(response.Manifest.Claims.Single().Evidence, Is.EqualTo(WorkerClaimEvidence.CompanionClause));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [TestCase("int", "Positive", "1", WorkerClaimOutcome.Proven)]
    [TestCase("int", "Positive", "0", WorkerClaimOutcome.Refuted)]
    [TestCase("ulong", "Positive", "18446744073709551615UL", WorkerClaimOutcome.Proven)]
    [TestCase("ulong", "Positive", "0UL", WorkerClaimOutcome.Refuted)]
    [TestCase("sbyte", "InRange(-129, 128)", "-128", WorkerClaimOutcome.Proven)]
    [TestCase("long", "InRange(-9223372036854775808L, 9223372036854775807L)", "-9223372036854775808L", WorkerClaimOutcome.Proven)]
    [TestCase("byte", "InRange(-1, 256)", "255", WorkerClaimOutcome.Proven)]
    [TestCase("byte", "InRange(256, 300)", "255", WorkerClaimOutcome.Refuted)]
    [TestCase("ulong", "InRange(-2, -1)", "0UL", WorkerClaimOutcome.Refuted)]
    [TestCase("string", "NotNull", "value", WorkerClaimOutcome.Proven)]
    [TestCase("string", "NotNull", "null!", WorkerClaimOutcome.Refuted)]
    public async Task NativeWorkerVerifiesTypedReturnAttributes(string type, string attribute, string value, WorkerClaimOutcome outcome)
    {
        var parameters = type == "string" ? "[NotNull] string value" : "";
        var source = $$"""
            using SharpProof.Attributes;
            public static class Subject { [return: {{attribute}}] public static {{type}} Target({{parameters}}) { return {{value}}; } }
            """;
        using var project = new ShadowTestProject(source);
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Not.Null);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(outcome), response.ClaimResults.Single().Reason.ToString());
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [TestCase("int")]
    [TestCase("ulong")]
    [TestCase("int", true)]
    public async Task NativeWorkerCombinesParameterAttributesDirectClausesAndReturnAttributes(string type, bool instance = false)
    {
        var modifier = instance ? "" : "static";
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public {{modifier}} class Subject {
                [return: Positive] public {{modifier}} {{type}} Target([InRange(1, 10)] {{type}} value) {
                    Contract.Ensures(Contract.Result<{{type}}>() == value);
                    return value;
                }
            }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
        Assert.That(response.ClaimResults.Select(result => result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [TestCase("Contract.Result<int>() == receiver.State")]
    [TestCase("Contract.Result<int>() == (receiver == null ? 0 : contractValue)")]
    public async Task ReceiverDependentCompanionClausesRemainUnsupported(string clause)
    {
        using var project = new ShadowTestProject($$"""
            using SharpProof.Attributes;
            public class Subject { public int State; public int Target(int value) { return value; } }
            [ContractFor(typeof(Subject))] public static class SubjectContracts {
                public static int Target(Subject receiver, int contractValue) {
                    Contract.Ensures({{clause}});
                    return contractValue;
                }
            }
            """);
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Null);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task VirtualInstanceBodiesRemainUnsupported()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public class Subject {
                public virtual int Target(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; }
            }
            """);
        Assert.That(project.Snapshot.Callables.Single().Total, Is.Null);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task NativeWorkerRetainsDirectVerifierOutcomesAcrossTheQualifiedUniverse()
    {
        var cases = WorkerVcShadowSourceGateTests.QualificationCases();
        var posts = 0;
        var known = 0;
        foreach (var sourceCase in cases)
        {
            using var project = new ShadowTestProject(WorkerVcShadowSourceGateTests.CreateGateArtifact(sourceCase));
            var expected = new Dictionary<string, WorkerClaimResult>(StringComparer.Ordinal);
            foreach (var preparation in project.Snapshot.Callables)
            {
                await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets, check =>
                    expected[check.ClaimId] = CallableClaimResultAssembler.FromTotal(preparation, check), CancellationToken.None);
            }
            using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True, sourceCase.Name);
            var postIds = response.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition)
                .Select(claim => claim.ClaimId).ToHashSet(StringComparer.Ordinal);
            posts += postIds.Count;
            var actual = response.ClaimResults.Where(result => postIds.Contains(result.ClaimId)).ToDictionary(result => result.ClaimId, StringComparer.Ordinal);
            Assert.That(actual.Keys, Is.EquivalentTo(postIds), sourceCase.Name);
            if (sourceCase.Name == "golden:vc-shadow-unsupported-lock-local")
            {
                Assert.That(actual.Values.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
                Assert.That(actual.Values.Single().Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable));
            }
            foreach (var result in expected.Values.Where(result => result.Outcome is WorkerClaimOutcome.Proven or WorkerClaimOutcome.Refuted))
            {
                known++;
                Assert.That((actual[result.ClaimId].Outcome, actual[result.ClaimId].Vacuity),
                    Is.EqualTo((result.Outcome, result.Vacuity)), sourceCase.Name + ":" + result.ClaimId);
            }
        }
        Assert.That(posts, Is.EqualTo(253));
        // The newly modeled fresh lock receiver exposes synchronization. Its
        // former postcondition proof now abstains; all other known results remain.
        Assert.That(known, Is.EqualTo(237));
        await TestContext.Out.WriteLineAsync($"native-worker universe: fixtures={cases.Length} posts={posts} retained-known={known}");
    }

    [TestCase(1, false)]
    [TestCase(2, false)]
    [TestCase(1, true)]
    public async Task NativeWorkerVerifiesLegacyFailedCallablesAndReusesValidatedCache(int parallelism, bool cacheEnabled)
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject {
                public static ulong A(ulong value) {
                    Contract.Requires(value == 18446744073709551615UL);
                    Contract.Ensures(Contract.Result<ulong>() == 0UL);
                    return unchecked(value + 1UL);
                }
                public static int B(int value) {
                    Contract.Ensures(Contract.Result<int>() == value);
                    return value;
                }
            }
            """, cacheEnabled);
        project.Request.Budgets.MaxParallelism = parallelism;
        Assert.That(project.Snapshot.Callables.Single(callable => callable.Entry.CallableId.Contains(".A(", StringComparison.Ordinal)).IsSuccess, Is.False);
        Assert.That(SharpProofWorker.CountSolverTargets(project.Snapshot.Callables), Is.EqualTo(2));
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        for (var run = 0; run < 2; run++)
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(response.ClaimResults, Has.Length.EqualTo(2));
            Assert.That(response.ClaimResults.Select(result => result.Outcome), Is.All.EqualTo(WorkerClaimOutcome.Proven));
            Assert.That(response.CallableResults.Select(result => result.Coverage), Is.All.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(cacheEnabled
                ? run == 0 ? WorkerCacheStatus.Written : WorkerCacheStatus.Hit : WorkerCacheStatus.Disabled));
        }
    }

    [Test]
    public async Task EffectOnlyEntryWithoutPreconditionsDoesNotConsumeSolverQueries()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { [DoesNotThrow] public static void Target() { } }
            """);
        using var worker = new SharpProofWorker(new UnexpectedBackend(), null);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task ContradictoryNativeEntryMakesEffectProofVacuityExplicit()
    {
        using var project = new ShadowTestProject("""
            using SharpProof.Attributes;
            public static class Subject { [DoesNotThrow] public static void Target() {
                Contract.Requires(false);
                throw null!;
            } }
            """);
        using var worker = SharpProofWorker.CreateNative(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        var result = response.ClaimResults.Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.ContradictoryPreconditions));
        Assert.That(result.ProofCore, Is.Not.Empty);
        Assert.That(result.Assumptions.Single().Used, Is.True);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task MethodTimeoutRetainsEarlierNativeProofAndMarksOnlyPendingClaim()
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        project.Request.Budgets.MethodWallTimeMilliseconds = 1500;
        var factory = project.Snapshot.Callables.Single().Total!.Program.Factory;
        using var worker = new SharpProofWorker(() => new StallAfterQueriesBackend(factory, project.Request.Budgets.QueryRlimit));
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.ClaimResults.Count(result => result.Outcome == WorkerClaimOutcome.Proven), Is.EqualTo(1));
        var pending = response.ClaimResults.Single(result => result.Outcome == WorkerClaimOutcome.Unknown);
        Assert.That(pending.Reason, Is.EqualTo(WorkerClaimReason.MethodTimeout));
        Assert.That(response.CallableResults.Single().Reason, Is.EqualTo(WorkerCallableCoverageReason.MethodTimeout));
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    private sealed class UnexpectedBackend : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        { throw new AssertionException("An unconstrained effect-only entry needs no SMT query."); }
    }

    [TestCase(false)]
    [TestCase(true)]
    [TestCase(false, true)]
    public async Task ProjectInterruptionRetainsEarlierNativeProof(bool projectTimeout, bool separateCallable = false)
    {
        using var project = new ShadowTestProject(separateCallable ? """
            using SharpProof.Attributes;
            public static class Subject {
                public static int A(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; }
                public static int B(int value) { Contract.Ensures(Contract.Result<int>() == value); return value; }
            }
            """ : CompilerTotalCallableArtifactTests.DiamondSource);
        project.Request.Budgets.MaxParallelism = 1;
        using var cancellation = new CancellationTokenSource();
        if (projectTimeout)
        {
            project.Request.Budgets.ProjectWallTimeMilliseconds = 1500;
            project.Request.Budgets.MethodWallTimeMilliseconds = 1500;
        }
        var factory = project.Snapshot.Callables.OrderBy(callable => callable.Entry.CallableId, StringComparer.Ordinal).First().Total!.Program.Factory;
        using var worker = new SharpProofWorker(() => new StallAfterQueriesBackend(factory,
            project.Request.Budgets.QueryRlimit, projectTimeout ? null : cancellation.Cancel));
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, cancellation.Token);
        Assert.That(response.ClaimResults.Count(result => result.Outcome == WorkerClaimOutcome.Proven), Is.EqualTo(1));
        var pending = response.ClaimResults.Single(result => result.Outcome == WorkerClaimOutcome.Unknown);
        if (projectTimeout)
        {
            // Both budgets expire together; either timer may signal first.
            Assert.That(pending.Reason, Is.EqualTo(WorkerClaimReason.ProjectTimeout).Or.EqualTo(WorkerClaimReason.MethodTimeout));
        }
        else
        { Assert.That(pending.Reason, Is.EqualTo(WorkerClaimReason.Canceled)); }
        Assert.That(response.RunStatus, Is.EqualTo(projectTimeout ? WorkerRunStatus.TimedOut : WorkerRunStatus.Canceled));
        if (separateCallable)
        {
            Assert.That(response.CallableResults.First().Coverage, Is.EqualTo(WorkerCallableCoverage.Complete));
            Assert.That(response.CallableResults.Last().Reason, Is.EqualTo(WorkerCallableCoverageReason.Canceled));
        }
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
    }

    [Test]
    public async Task ExplicitProjectBoundaryRetainsNativeProofAndClassifiesPendingClaim()
    {
        using var project = new ShadowTestProject(CompilerTotalCallableArtifactTests.DiamondSource);
        using var boundary = new CancellationTokenSource();
        var target = project.Snapshot.Callables.Single();
        using var backend = new StallAfterQueriesBackend(target.Total!.Program.Factory, project.Request.Budgets.QueryRlimit, boundary.Cancel);
        var result = await CallableVerificationPolicy.VerifyNativeTargetAsync(backend, target, project.Request.Budgets,
            null, project.Request.Budgets.MethodWallTimeMilliseconds, boundary, CancellationToken.None);
        Assert.That(result.Claims.Count(claim => claim.Outcome == WorkerClaimOutcome.Proven), Is.EqualTo(1));
        Assert.That(result.Claims.Single(claim => claim.Outcome == WorkerClaimOutcome.Unknown).Reason, Is.EqualTo(WorkerClaimReason.ProjectTimeout));
        Assert.That(result.Callable.Reason, Is.EqualTo(WorkerCallableCoverageReason.ProjectTimeout));
    }

    private sealed class StallAfterQueriesBackend : ISmtBackend, IDisposable
    {
        private readonly CallableSolverSession _session;
        private readonly Action? _beforeStall;
        private int _queries;
        internal StallAfterQueriesBackend(IrFactory factory, uint queryRlimit, Action? beforeStall = null)
        {
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            _session = new(factory, new IrSmtBackendOptions(queryRlimit));
            _beforeStall = beforeStall;
        }
        public async Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            if (++_queries == 4)
            {
                _beforeStall?.Invoke();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            return await _session.CheckAsync(query, cancellationToken).ConfigureAwait(false);
        }
        public void Dispose()
        {
            _session.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
