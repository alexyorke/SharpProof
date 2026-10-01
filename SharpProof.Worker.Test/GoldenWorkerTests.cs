using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;
using Program = SharpProof.Worker.Launcher.Program;

namespace SharpProof.Worker.Test;

[TestFixture]
[NonParallelizable]
public sealed class GoldenWorkerTests
{
    public static IEnumerable<string> Cases()
    {
        return GoldenTest.Cases("worker");
    }

    [TestCaseSource(nameof(Cases))]
    public async Task WorkerOutcomesMatchGolden(string caseName)
    {
        var fixture = GoldenTest.Load("worker", caseName);
        const string prefix = "// golden-scenario: ";
        var first = fixture.Source.Split('\n')[0];
        Assert.That(first, Does.StartWith(prefix));
        var scenario = first[prefix.Length..];
        var actual = scenario == "native-infrastructure" ? await NativeInfrastructure()
            : scenario == "native-cancellation" ? await NativeCancellation()
            : scenario == "native-resource" ? await NativeResource()
            : scenario == "passive-vc" ? await PassiveVc(fixture.Source)
            : scenario == "passive-ownership" ? PassiveOwnership()
            : scenario == "total-artifact" ? await TotalArtifact(fixture.Source)
            : scenario == "total-claim-results" ? await TotalClaimResults(fixture.Source)
            : scenario == "artifact-passive-enrollment" ? await ArtifactPassiveEnrollment(fixture.Source)
            : scenario == "vc-shadow" ? await VcShadow(fixture.Source)
            : scenario == "vc-shadow-reference" ? await VcShadow(fixture.Source, stableClaimOrder: true)
            : scenario == "typed-il-shadow" ? await TypedIlGolden(fixture.Source, artifact => VcShadow(artifact))
            : scenario == "typed-il-artifact" ? await TypedIlGolden(fixture.Source, artifact => TotalArtifact(artifact))
            : scenario == "vc-loop-prologue-reentry" ? VcLoopPrologueReentry(fixture.Source)
            : scenario == "vc-exception-multi-entry" ? await VcExceptionMultiEntry()
            : scenario == "vc-assume-placement" ? VcAssumePlacement(fixture.Source)
            : scenario == "vc-shadow-meter-boundary" ? await VcShadowMeterBoundary(fixture.Source)
            : scenario == "vc-shadow-completed-boundary" ? await VcShadowCompletedBoundary(fixture.Source)
            : scenario == "model-boolean" ? await BooleanModels()
            : scenario.StartsWith("model-", StringComparison.Ordinal) ? await TypedModel(scenario)
            : scenario.StartsWith("replay-", StringComparison.Ordinal) ? await Replay(scenario) : await Verify(fixture, scenario);
        GoldenTest.Compare(fixture, actual);
    }

    private static async Task<string> TotalClaimResults(string source)
    {
        using var project = new ShadowTestProject(source);
        var claims = new Dictionary<string, WorkerClaimResult>(StringComparer.Ordinal);
        foreach (var preparation in project.Snapshot.Callables)
        {
            await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
                check => claims[check.ClaimId] = CallableClaimResultAssembler.FromTotal(preparation, check), CancellationToken.None);
        }
        var callables = project.Snapshot.Callables.Select(preparation =>
        {
            var reason = WorkerResultAssembler.ProjectCallableReasons(preparation.Entry.ClaimIds.Select(id => claims[id])).Reason;
            return new WorkerCallableResult
            {
                CallableId = preparation.Entry.CallableId,
                Coverage = reason == WorkerCallableCoverageReason.None ? WorkerCallableCoverage.Complete : WorkerCallableCoverage.Incomplete,
                Reason = reason,
                Assumptions = preparation.Entry.Assumptions
            };
        }).ToArray();
        Assert.That(WorkerResultAssembler.TryProjectRunState(callables, claims.Values, [], out var runStatus, out var failureReason), Is.True);
        var response = WorkerResultAssembler.Create(project.Bind().InputHash, project.Snapshot.CompilerManifest.Manifest,
            runStatus, failureReason, callables, claims.Values, project.Request.Budgets, WorkerCacheStatus.Disabled, 0);
        var json = WorkerProtocolJson.SerializeResponse(response);
        var validation = WorkerProtocolJson.Validate(response, response.InputHash, response.Manifest);
        Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error.Code)));
        var output = new StringBuilder();
        output.AppendLine("candidate-authority: Total IR (legacy worker routing retained)");
        output.AppendLine("run: " + response.RunStatus);
        output.AppendLine("wire-valid: True");
        output.AppendLine("serialized: " + (json.Length > 0));
        foreach (var claim in response.Manifest.Claims.OrderBy(claim => claim.CallableId, StringComparer.Ordinal).ThenBy(claim => claim.Ordinal))
        {
            var result = claims[claim.ClaimId];
            output.AppendLine("claim: " + claim.CallableId + "#" + claim.Ordinal);
            output.AppendLine("  outcome: " + result.Outcome);
            output.AppendLine("  reason: " + result.Reason);
            output.AppendLine("  vacuity: " + result.Vacuity);
            foreach (var model in result.Model)
            { output.AppendLine("  model: " + model.Variable + " " + model.Kind + " " + model.Value); }
        }
        return output.ToString();
    }

    private static string VcLoopPrologueReentry(string source)
    {
        var output = new StringBuilder();
        output.AppendLine("source-contract: finite direct prologue before scalar loop");
        output.AppendLine("payload: canonical re-encoding with original ID/site/predicate");
        output.AppendLine("body-to-assumption-rejected: " + WorkerVcLoopTests.PrologueReentryRejected(false, source));
        output.AppendLine("body-to-initialization-rejected: " + WorkerVcLoopTests.PrologueReentryRejected(true, source));
        return output.ToString();
    }

    private static async Task<string> VcExceptionMultiEntry()
    {
        var candidate = PassiveExceptionLoopTests.MultipleEntryCandidate();
        var execution = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var feasibility = await solver.VerifyFeasibilityAsync();
        var ensures = await solver.VerifyEnsuresAsync(0);
        var output = new StringBuilder();
        output.AppendLine("original: E(true) -> A -> B(true) -> R");
        output.AppendLine("original-return: " + execution.ReturnValue!.IntegerNumericValue);
        output.AppendLine("normal-completion: " + feasibility.Kind);
        output.AppendLine("false-ensures: " + ensures.Outcome!.GetType().Name);
        output.AppendLine("replay: unchanged owned original");
        output.AppendLine("projection-canonical: " + ensures.EntryModel.IsEmpty);
        return output.ToString();
    }

    private static string VcAssumePlacement(string source)
    {
        var output = new StringBuilder();
        output.AppendLine("source-contract: direct prologue");
        output.AppendLine("payload: canonical re-encoding with original ID/site/predicate");
        output.AppendLine("after-current-write-rejected: " + WorkerVcSourceAssumeTests.RelocatedAssumptionRejected(source, unreachable: false));
        output.AppendLine("unreachable-point-rejected: " + WorkerVcSourceAssumeTests.RelocatedAssumptionRejected(source, unreachable: true));
        return output.ToString();
    }

    private static async Task<string> VcShadowMeterBoundary(string source)
    {
        var report = await WorkerVcShadowTests.MeterRefusal(source);
        var row = report.Rows.Single();
        var output = new StringBuilder();
        output.AppendLine("authority: legacy");
        output.AppendLine("budget: actual native meter");
        output.AppendLine("entry-and-normal: " + row.Feasibility);
        output.AppendLine("enrolled: " + row.Enrolled);
        output.AppendLine("checked: " + row.Checked);
        output.AppendLine("reason: " + row.NewReason);
        output.AppendLine("old-outcome: " + row.OldOutcome);
        output.AppendLine("new-outcome: " + row.NewOutcome);
        output.AppendLine("unchecked: " + report.Unchecked);
        output.AppendLine("coverageComplete: " + report.CoverageComplete);
        return output.ToString();
    }

    private static async Task<string> VcShadowCompletedBoundary(string source)
    {
        var result = await WorkerVcShadowTests.CompletedBoundary(source, projectTimeout: false);
        var output = new StringBuilder();
        output.AppendLine("authority: legacy");
        output.AppendLine("boundary: caller canceled after completed observation");
        output.AppendLine("completed-run: " + result.Completed.RunStatus);
        output.AppendLine("observed-checks: " + result.Report.Checked);
        output.AppendLine("cache: " + result.Completed.Summary.CacheStatus);
        output.AppendLine("next-run: " + result.Next.RunStatus);
        output.AppendLine("next-failure: " + result.Next.FailureReason);
        output.AppendLine("same-backend-checks: " + result.Checks);
        return output.ToString();
    }

    private static async Task<string> VcShadow(string source, bool stableClaimOrder = false)
    {
        using var project = new ShadowTestProject(source, cacheEnabled: true);
        return await VcShadow(project, stableClaimOrder);
    }

    private static async Task<string> VcShadow(CompilerManifestArtifact artifact)
    {
        using var project = new ShadowTestProject(artifact, cacheEnabled: true);
        return await VcShadow(project, stableClaimOrder: true);
    }

    private static async Task<string> TypedIlGolden(string source, Func<CompilerManifestArtifact, Task<string>> render)
    {
        const string marker = "// metadata-library";
        var boundary = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(boundary, Is.GreaterThan(0));
        using var subject = new MetadataTestSubject(source[(boundary + marker.Length)..], source[..boundary]);
        return await render(subject.CreateArtifact());
    }

    private static async Task<string> VcShadow(ShadowTestProject project, bool stableClaimOrder = false)
    {
        using var environment = new ShadowEnvironment("shadow");
        using var worker = project.CreateLegacyWorker();
        using var errors = new StringWriter(CultureInfo.InvariantCulture);
        var previous = Console.Error;
        var output = new StringBuilder();
        try
        {
            Console.SetError(errors);
            foreach (var run in new[] { "normal", "cache-hit" })
            {
                var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
                var line = errors.ToString().TrimEnd('\r', '\n');
                Assert.That(line, Does.StartWith(WorkerVcShadowReport.Prefix));
                Assert.That(line.Count(character => character == '\n'), Is.Zero);
                using var report = JsonDocument.Parse(line[WorkerVcShadowReport.Prefix.Length..]);
                var json = report.RootElement;
                Assert.That(json.GetProperty("inputHash").GetString(), Is.EqualTo(response.InputHash));
                Assert.That(json.GetProperty("requestHash").GetString(), Is.EqualTo(response.RequestHash));
                Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
                output.AppendLine("run: " + run);
                output.AppendLine("authority: " + json.GetProperty("authority").GetString());
                output.AppendLine("request-bindings: True");
                output.AppendLine("cache: " + response.Summary.CacheStatus);
                foreach (var property in new[] { "postconditions", "enrolled", "checked", "unchecked", "unenrolled", "unknown", "oldProven", "newProven", "oldVacuous", "newVacuous", "oldConditional", "newConditional", "soundnessDisagreements", "precisionGains", "precisionLosses" })
                { output.AppendLine(property + ": " + json.GetProperty(property).GetInt32()); }
                output.AppendLine("coverageComplete: " + json.GetProperty("coverageComplete").GetBoolean());
                var rows = json.GetProperty("rows").EnumerateArray().ToArray();
                if (stableClaimOrder)
                {
                    // Metadata fixtures use captured temporary document paths. Their claim
                    // hashes vary, so render original callable/ordinal instead of hash order.
                    var claims = response.Manifest.Claims.ToDictionary(claim => claim.ClaimId, StringComparer.Ordinal);
                    rows = [.. rows.OrderBy(row => claims[row.GetProperty("claimId").GetString()!].CallableId, StringComparer.Ordinal)
                        .ThenBy(row => claims[row.GetProperty("claimId").GetString()!].Ordinal)];
                }
                foreach (var row in rows)
                {
                    var claim = response.Manifest.Claims.Single(claim => claim.ClaimId == row.GetProperty("claimId").GetString());
                    output.AppendLine("row: " + claim.CallableId + "#" + claim.Ordinal);
                    foreach (var property in new[] { "oldOutcome", "oldVacuity", "totalPresent", "enrolled", "checked", "newOutcome", "newReason", "newVacuity", "feasibility", "oldConditional", "newConditional" })
                    { output.AppendLine("  " + property + ": " + row.GetProperty(property)); }
                }
                errors.GetStringBuilder().Clear();
            }
        }
        finally
        { Console.SetError(previous); }
        return output.ToString();
    }

    private static async Task<string> TotalArtifact(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        return await TotalArtifact(artifact);
    }

    private static async Task<string> TotalArtifact(CompilerManifestArtifact artifact)
    {
        var row = artifact.Callables.Single();
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        var preparation = prepared.Single();
        var total = preparation.Total!;
        var type = total.Program.Factory.GetTypeInfo(total.Program.Factory.GetVariableInfo(total.Parameters[0].Entry).Type);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var proven = await solver.VerifyEnsuresAsync(0);
        var output = new StringBuilder();
        output.AppendLine("schema: " + artifact.SchemaVersion);
        output.AppendLine("authority: legacy");
        output.AppendLine("legacy-success: " + preparation.IsSuccess);
        output.AppendLine("total-present: " + (row.Total != null));
        output.AppendLine("integer-width: " + type.Width);
        output.AppendLine("unsigned: " + !type.Signed);
        output.AppendLine("roots-interleaved: " + row.Total!.Clauses.Select((clause, ordinal) => clause.ValueRoot == ordinal * 2 && clause.SafeRoot == ordinal * 2 + 1).All(value => value));
        output.AppendLine("same-claim-ids: " + total.Clauses.Where(clause => clause.Kind == CompilerContractKind.Ensures).Select(clause => clause.ClaimId).SequenceEqual(artifact.Manifest.Claims.Select(claim => claim.ClaimId)));
        output.AppendLine("full-ulong-wrap: " + proven.Outcome!.GetType().Name);
        return output.ToString();
    }

    private static async Task<string> ArtifactPassiveEnrollment(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
        var candidate = PassiveCallableArtifactAdapter.Enroll(prepared.Single())!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var proven = await solver.VerifyEnsuresAsync(0);
        var refuted = await solver.VerifyEnsuresAsync(1);
        var output = new StringBuilder();
        output.AppendLine("authority: legacy");
        output.AppendLine("candidate-source: decoded immutable IR");
        output.AppendLine("diamond-old: " + proven.Outcome!.GetType().Name);
        output.AppendLine("mutated-equality: " + refuted.Outcome!.GetType().Name);
        output.AppendLine("unused-entry-present: " + refuted.EntryModel.ContainsKey(candidate.Parameters[2].Entry));
        output.AppendLine("projection-canonical: " + refuted.EntryModel.Keys.ToHashSet().SetEquals(candidate.Parameters.Select(parameter => parameter.Entry)));
        return output.ToString();
    }

    private static async Task<string> PassiveVc(string source)
    {
        var subject = PassiveSourceSubject.Create(source);
        var candidate = subject.Enroll()!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var proven = await solver.VerifyEnsuresAsync(0);
        var refuted = await solver.VerifyEnsuresAsync(1);
        var output = new StringBuilder();
        output.AppendLine("authority: passive candidate");
        output.AppendLine("diamond-old: " + proven.Outcome!.GetType().Name);
        output.AppendLine("mutated-equality: " + refuted.Outcome!.GetType().Name);
        output.AppendLine("unused-entry-present: " + refuted.EntryModel.ContainsKey(candidate.Parameters[2].Entry));
        output.AppendLine("projection-canonical: " + refuted.EntryModel.Keys.ToHashSet().SetEquals(candidate.Parameters.Select(parameter => parameter.Entry)));
        return output.ToString();
    }

    private static string PassiveOwnership()
    {
        var output = new StringBuilder();
        foreach (var alias in new[] { "old-current", "old-entry", "cross-input", "result" })
        {
            var subject = new PassiveCallableVcTests.ScalarSubject();
            subject.Builder.Return(subject.Builder.CreateBlock(), subject.Site, subject.Factory.Integer(0));
            var first = alias switch
            {
                "old-current" => subject.Parameter with { Old = subject.Parameter.Current },
                "old-entry" => subject.Parameter with { Old = subject.Parameter.Entry },
                _ => subject.Parameter
            };
            ImmutableArray<PassiveParameterBinding> parameters = [first];
            if (alias == "cross-input")
            {
                parameters = parameters.Add(new(subject.Parameter.Current,
                    subject.Factory.CreateVariable("other-current", subject.Factory.IntegerType),
                    subject.Factory.CreateVariable("other-old", subject.Factory.IntegerType)));
            }
            var rejected = false;
            try
            {
                _ = new PassiveCallableCandidate("aliases", subject.Builder.Build(), parameters,
                    alias == "result" ? subject.Parameter.Current : subject.Result, [], []);
            }
            catch (ArgumentException)
            { rejected = true; }
            output.AppendLine("alias " + alias + " rejected: " + rejected);
        }
        var old = new PassiveCallableVcTests.ScalarSubject();
        old.Builder.Return(old.Builder.CreateBlock(), old.Site, old.Factory.Variable(old.Parameter.Old));
        var ensures = old.Factory.Binary(IrBinaryOperator.Equal, old.Factory.Variable(old.Result), old.Factory.Variable(old.Parameter.Entry));
        Assert.That(PassiveCallableVcBuilder.TryBuild(old.Candidate(ensures), out _, out var oldReason), Is.False);
        output.AppendLine("uninitialized-body-old: " + oldReason);
        var straight = PassiveCallableVcTests.StraightLineCandidate(80, 32);
        Assert.That(PassiveCallableVcBuilder.TryBuild(straight, out var plan, out _), Is.True);
        var query = plan!.EnsuresQuery(0);
        output.AppendLine("straight-line-linear-bound: " + (query.Assumptions.Length < 4 * (80 + 32) && query.ModelVariables.Length < 4 * (80 + 32)));
        var oversized = PassiveCallableVcTests.StraightLineCandidate(PassiveCallableVcBuilder.MaximumSteps + 1, 0);
        Assert.That(PassiveCallableVcBuilder.TryBuild(oversized, out _, out var limitReason), Is.False);
        output.AppendLine("over-limit: " + limitReason);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var cancellationObserved = false;
        try
        { PassiveCallableVcBuilder.TryBuild(straight, out _, out _, canceled.Token); }
        catch (OperationCanceledException)
        { cancellationObserved = true; }
        output.AppendLine("construction-canceled: " + cancellationObserved);
        return output.ToString();
    }

    private static async Task<string> Verify(GoldenCase fixture, string scenario)
    {
        using var directory = new TempDirectory("sharpproof-golden-worker-");
        var sourcePath = Path.Combine(directory.FullName, Path.GetFileName(fixture.RelativePath));
        var compilation = TestCompilation.Create("GoldenWorker", OutputKind.DynamicallyLinkedLibrary,
            [(sourcePath, fixture.Source)]);
        TestCompilation.AssertNoErrors(compilation);
        var discovery = new ClaimManifestBuilder(compilation, WorkerFeatureSet.All).Build();
        var artifact = CompilerManifestArtifactProducer.Create(compilation, directory.FullName, "net9.0",
            WorkerFeatureSet.All, discovery, WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var bytes = Encoding.UTF8.GetBytes(CompilerManifestArtifactJson.Serialize(artifact));
        var path = Path.Combine(directory.FullName, "artifact.json");
        await File.WriteAllBytesAsync(path, bytes);
        var request = new WorkerVerifyRequest
        {
            CompilerManifest = new WorkerFileReference { Path = path, Sha256 = WorkerProtocolJson.ComputeSha256(bytes) },
            Cache = new WorkerCacheOptions { Enabled = false },
            Budgets = new WorkerBudgets { ProjectWallTimeMilliseconds = 30000, MethodWallTimeMilliseconds = 10000, MaxParallelism = 1 }
        };
        WorkerVerifyResponse response;
        int? exit = null;
        bool? leaseBlocked = null;
        bool? bothPublicationsValid = null;
        if (scenario == "publication-timeout")
        {
            var stableRequest = Path.Combine(directory.FullName, "published-request.json");
            var stableResult = Path.Combine(directory.FullName, "published-result.json");
            var stableManifest = Path.Combine(directory.FullName, "published-manifest.json");
            var held = await PublicationLease.AcquireAsync([stableRequest, stableResult, stableManifest], CancellationToken.None);
            var verified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var publishing = Program.RunMain(["verify", "--request", Path.Combine(directory.FullName, "private-request.json"),
                "--result", Path.Combine(directory.FullName, "private-result.json"), "--compiler-manifest", path,
                "--verify-policy", "advisory", "--assumption-policy", "allow", "--cache-enabled", "false",
                "--project-wall-ms", "1", "--method-wall-ms", "1", "--termination-grace-ms", "1",
                "--publish-request", stableRequest, "--publish-result", stableResult,
                "--publish-compiler-manifest", stableManifest], async (invocation, token) =>
            {
                using var worker = SharpProofWorker.Create(invocation.Budgets);
                var expired = Stopwatch.GetTimestamp() - Stopwatch.Frequency;
                var interrupted = await worker.VerifyAsync(invocation, null, token, expired);
                Assert.That(interrupted.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
                verified.SetResult();
                return interrupted;
            });
            var waited = false;
            try
            {
                await verified.Task.WaitAsync(TimeSpan.FromSeconds(10));
                waited = await Task.WhenAny(publishing, Task.Delay(100)) != publishing;
            }
            finally
            {
                held.Dispose();
            }
            var timeoutExit = await publishing;
            Assert.That(waited, Is.True, "Publication must retain its own bounded reserve after the analysis budget expires.");
            Assert.That(timeoutExit, Is.Zero);
            var timeoutRequest = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(stableRequest))!;
            var timeoutResponse = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(stableResult))!;
            var inputHash = Program.ComputeExpectedInputHash(timeoutRequest, bytes);
            var timeoutBinding = WorkerProtocolJson.ValidateForRequest(timeoutResponse, WorkerProtocolJson.ComputeRequestHash(timeoutRequest),
                inputHash, artifact.Manifest, timeoutRequest, Program.ExpectedVersions());
            Assert.That(timeoutBinding.IsValid, Is.True);
            Assert.That(timeoutResponse.RunStatus, Is.EqualTo(WorkerRunStatus.TimedOut));
            Assert.That(timeoutResponse.FailureReason, Is.EqualTo(WorkerRunFailureReason.None));
            Assert.That(await File.ReadAllBytesAsync(stableManifest), Is.EqualTo(bytes));
            return "analysis-budget-ms: 1\npublication-waited: True\nexit: 0\nrun: TimedOut/None\nrequest-bound: True\ninput-preserved: True\n";
        }
        if (scenario == "publication-runtime")
        {
            var runtime = Path.Combine(directory.FullName, "runtime");
            Directory.CreateDirectory(runtime);
            var workerPath = Path.Combine(runtime, "SharpProof.Worker.dll");
            File.Copy(typeof(SharpProofWorker).Assembly.Location, workerPath);
            var companionPath = Path.ChangeExtension(workerPath, ".deps.json");
            await File.WriteAllTextAsync(companionPath, "Preserved declared runtime companion.");
            var workerBytes = await File.ReadAllBytesAsync(workerPath);
            var companionBytes = await File.ReadAllBytesAsync(companionPath);
            var runtimeOutput = new StringBuilder();
            foreach (var name in new[] { "direct", "leaf", "worker-leaf", "executing" })
            {
                var protectedWorker = name == "executing" ? typeof(Program).Assembly.Location : workerPath;
                var protectedCompanion = name == "executing" ? Path.ChangeExtension(protectedWorker, ".deps.json") : companionPath;
                var protectedWorkerBytes = name == "executing" ? await File.ReadAllBytesAsync(protectedWorker) : workerBytes;
                var protectedCompanionBytes = name == "executing" ? await File.ReadAllBytesAsync(protectedCompanion) : companionBytes;
                var declaredWorker = workerPath;
                var destination = protectedCompanion;
                if (name == "leaf")
                {
                    destination = Path.Combine(directory.FullName, "companion-alias.json");
                    File.CreateSymbolicLink(destination, companionPath);
                }
                if (name == "worker-leaf")
                {
                    var aliasDirectory = Path.Combine(directory.FullName, "worker-alias");
                    Directory.CreateDirectory(aliasDirectory);
                    declaredWorker = Path.Combine(aliasDirectory, "worker.dll");
                    File.CreateSymbolicLink(declaredWorker, workerPath);
                }
                var entered = false;
                using var errors = new StringWriter(CultureInfo.InvariantCulture);
                var previousError = Console.Error;
                int runtimeExit;
                try
                {
                    Console.SetError(errors);
                    var arguments = new List<string> { "verify",
                        "--request", Path.Combine(directory.FullName, name + "-request.json"),
                        "--result", Path.Combine(directory.FullName, name + "-result.json"),
                        "--compiler-manifest", path, "--verify-policy", "advisory", "--assumption-policy", "allow",
                        "--cache-enabled", "false", "--publish-request", Path.Combine(directory.FullName, name + "-published-request.json"),
                        "--publish-result", destination, "--publish-compiler-manifest", Path.Combine(directory.FullName, name + "-published-manifest.json") };
                    if (name != "executing")
                    {
                        arguments.AddRange(["--worker", declaredWorker]);
                    }
                    runtimeExit = await Program.RunMain(arguments.ToArray(),
                        async (invocation, token) =>
                        {
                            entered = true;
                            using var worker = SharpProofWorker.Create(invocation.Budgets);
                            return await worker.VerifyAsync(invocation, token);
                        });
                }
                finally
                {
                    Console.SetError(previousError);
                }
                Assert.That(runtimeExit, Is.EqualTo(2));
                Assert.That(errors.ToString(), Does.Contain("SharpProof launcher input is invalid: ArgumentException"));
                Assert.That(entered, Is.False);
                Assert.That(await File.ReadAllBytesAsync(protectedWorker), Is.EqualTo(protectedWorkerBytes));
                Assert.That(await File.ReadAllBytesAsync(protectedCompanion), Is.EqualTo(protectedCompanionBytes));
                runtimeOutput.AppendLine("alias: " + name);
                runtimeOutput.AppendLine("exit: " + runtimeExit);
                runtimeOutput.AppendLine("worker-executed: " + entered);
                runtimeOutput.AppendLine("worker-preserved: True");
                runtimeOutput.AppendLine("companion-preserved: True");
            }
            return runtimeOutput.ToString();
        }
        if (scenario == "publication-phases")
        {
            var phaseOutput = new StringBuilder();
            foreach (var invalidInput in new[] { true, false })
            {
                var name = invalidInput ? "invalid" : "blocked";
                var privateRequest = Path.Combine(directory.FullName, name + "-request.json");
                var privateResult = Path.Combine(directory.FullName, name + "-result.json");
                var stableResult = Path.Combine(directory.FullName, name + "-published-result.json");
                var stableManifest = invalidInput ? path : Path.Combine(directory.FullName, "blocked-manifest.json");
                if (!invalidInput)
                {
                    Directory.CreateDirectory(stableManifest);
                }
                using var errors = new StringWriter(CultureInfo.InvariantCulture);
                var previousError = Console.Error;
                int phaseExit;
                try
                {
                    Console.SetError(errors);
                    phaseExit = await Program.RunMain(["verify", "--request", privateRequest, "--result", privateResult,
                        "--compiler-manifest", path, "--verify-policy", "advisory", "--assumption-policy", "allow",
                        "--cache-enabled", "false", "--publish-request", Path.Combine(directory.FullName, name + "-published-request.json"),
                        "--publish-result", stableResult, "--publish-compiler-manifest", stableManifest], async (invocation, token) =>
                        {
                            using var worker = SharpProofWorker.Create(invocation.Budgets);
                            return await worker.VerifyAsync(invocation, token);
                        });
                }
                finally
                {
                    Console.SetError(previousError);
                }
                var message = invalidInput ? "SharpProof launcher input is invalid: ArgumentException"
                    : "SharpProof worker result could not be published.";
                Assert.That(phaseExit, Is.EqualTo(invalidInput ? 2 : 3));
                Assert.That(errors.ToString(), Does.Contain(message));
                Assert.That(File.Exists(stableResult), Is.False);
                Assert.That(await File.ReadAllBytesAsync(path), Is.EqualTo(bytes));
                phaseOutput.AppendLine("phase: " + (invalidInput ? "input" : "publication"));
                phaseOutput.AppendLine("exit: " + phaseExit);
                phaseOutput.AppendLine("message: " + message);
                phaseOutput.AppendLine("stable-result-absent: True");
                phaseOutput.AppendLine("input-preserved: True");
            }
            return phaseOutput.ToString();
        }
        if (scenario == "fatal-timeout-precedence")
        {
            var input = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, CancellationToken.None);
            var mixedOutput = new StringBuilder();
            foreach (var reverseRecords in new[] { false, true })
            {
                var mixed = WorkerTests.AssembleFatalAndTimedOut(request, input, reverseRecords);
                mixed = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.SerializeResponse(mixed))!;
                var bound = WorkerProtocolJson.ValidateForRequest(mixed, WorkerProtocolJson.ComputeRequestHash(request),
                    input.InputHash, artifact.Manifest, request, Program.ExpectedVersions());
                Assert.That(bound.IsValid, Is.True, string.Join(';', bound.Errors.Select(error => error.Code)));
                mixedOutput.AppendLine("record-order: " + (reverseRecords ? "timeout-first" : "fatal-first"));
                mixedOutput.AppendLine(CultureInfo.InvariantCulture, $"run: {mixed.RunStatus}/{mixed.FailureReason}");
                mixedOutput.AppendLine(CultureInfo.InvariantCulture, $"request-bound: {bound.IsValid}");
                mixedOutput.AppendLine(CultureInfo.InvariantCulture, $"cacheable: {VerificationCache.IsCacheable(mixed, input.InputHash, artifact.Manifest)}");
                foreach (var claim in mixed.ClaimResults)
                {
                    mixedOutput.AppendLine(CultureInfo.InvariantCulture, $"claim: {claim.Outcome}/{claim.Reason}");
                }
            }
            return mixedOutput.ToString();
        }
        if (scenario is "identity" or "publication-private-binding")
        {
            using var worker = SharpProofWorker.Create(request.Budgets);
            response = await worker.VerifyAsync(request);
        }
        else if (scenario == "publication")
        {
            var stableRequest = Path.Combine(directory.FullName, "published-request.json");
            var stableResult = Path.Combine(directory.FullName, "published-result.json");
            var stableManifest = Path.Combine(directory.FullName, "published-manifest.json");
            var stableSarif = Path.Combine(directory.FullName, "published.sarif");
            var held = await PublicationLease.AcquireAsync([stableRequest, stableResult, stableManifest, stableSarif], CancellationToken.None);
            var verified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var first = Publish("first", verified);
            try
            {
                await verified.Task.WaitAsync(TimeSpan.FromSeconds(10));
                leaseBlocked = !first.IsCompleted && !File.Exists(stableResult);
            }
            finally
            {
                held.Dispose();
            }
            var firstExit = await first;
            var secondExit = await Publish("second", null);
            bothPublicationsValid = firstExit == 0 && secondExit == 0;
            exit = secondExit;
            request = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(stableRequest))!;
            response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(stableResult))!;
            Assert.That(request.CompilerManifest.Path, Is.EqualTo(stableManifest));
            Assert.That(await File.ReadAllBytesAsync(stableManifest), Is.EqualTo(bytes));

            Task<int> Publish(string name, TaskCompletionSource? signal)
            {
                return Program.RunMain(["verify", "--request", Path.Combine(directory.FullName, name + "-request.json"),
                    "--result", Path.Combine(directory.FullName, name + "-result.json"), "--compiler-manifest", path,
                    "--verify-policy", "advisory", "--assumption-policy", "allow", "--cache-enabled", "false",
                    "--publish-request", stableRequest, "--publish-result", stableResult,
                    "--publish-compiler-manifest", stableManifest, "--publish-sarif", stableSarif], async (invocation, token) =>
                {
                    using var worker = SharpProofWorker.Create(invocation.Budgets);
                    var verifiedResponse = await worker.VerifyAsync(invocation, token);
                    signal?.SetResult();
                    return verifiedResponse;
                });
            }
        }
        else
        {
            Assert.That(scenario, Is.AnyOf("empty-timeout", "empty-canceled", "promotion"));
            var resultPath = Path.Combine(directory.FullName, "result.json");
            var requestPath = Path.Combine(directory.FullName, "request.json");
            string[] arguments = ["verify", "--worker", typeof(SharpProofWorker).Assembly.Location,
                "--request", requestPath, "--result", resultPath, "--compiler-manifest", path,
                "--verify-policy", scenario == "empty-timeout" ? "require-proven" : "advisory",
                "--assumption-policy", "allow", "--cache-enabled", "false"];
            exit = await Program.RunMain(arguments, async (invocation, _) =>
            {
                if (scenario == "promotion")
                {
                    return WorkerResultAssembler.Create(WorkerResultAssembler.EmptyInputHash,
                        WorkerResultAssembler.EmptyManifest(), WorkerRunStatus.TimedOut, WorkerRunFailureReason.None,
                        [], [], invocation.Budgets, WorkerCacheStatus.Disabled, 0,
                        [new WorkerProtocolError { Code = WorkerProtocolErrorCodes.WorkerTimeout, Message = "Original timeout context." }],
                        WorkerProtocolJson.ComputeRequestHash(invocation), Program.ExpectedVersions());
                }
                using var worker = SharpProofWorker.Create(invocation.Budgets);
                using var cancellation = new CancellationTokenSource();
                var prepared = scenario == "empty-canceled"
                    ? WorkerInputSnapshot.Load(invocation, WorkerCacheIdentity.Current, CancellationToken.None) : null;
                if (scenario == "empty-canceled")
                {
                    await cancellation.CancelAsync();
                }
                return await worker.VerifyAsync(invocation, prepared, cancellation.Token, scenario == "empty-timeout"
                    ? Stopwatch.GetTimestamp() - (invocation.Budgets.ProjectWallTimeMilliseconds / 1000 + 1) * Stopwatch.Frequency
                    : null);
            });
            request = WorkerProtocolJson.DeserializeRequest(await File.ReadAllTextAsync(requestPath))!;
            response = WorkerProtocolJson.DeserializeResponse(await File.ReadAllTextAsync(resultPath))!;
        }
        var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, CancellationToken.None);
        var binding = WorkerProtocolJson.ValidateForRequest(response, WorkerProtocolJson.ComputeRequestHash(request),
            snapshot.InputHash, artifact.Manifest, request, Program.ExpectedVersions());
        Assert.That(binding.IsValid, Is.True, string.Join(';', binding.Errors.Select(error => error.Code)));
        Assert.That(response.RunStatus, Is.EqualTo(scenario is "identity" or "publication" or "publication-private-binding" ? WorkerRunStatus.Complete
            : scenario == "empty-canceled" ? WorkerRunStatus.Canceled : WorkerRunStatus.TimedOut));
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"run: {response.RunStatus}/{response.FailureReason}");
        output.AppendLine(CultureInfo.InvariantCulture, $"request-bound: {binding.IsValid}");
        output.AppendLine(CultureInfo.InvariantCulture, $"cacheable: {VerificationCache.IsCacheable(response, snapshot.InputHash, artifact.Manifest)}");
        output.AppendLine(CultureInfo.InvariantCulture, $"claims: {response.ClaimResults.Length}");
        if (scenario == "publication-private-binding")
        {
            var substitutedRequest = WorkerProtocolJson.DeserializeRequest(WorkerProtocolJson.SerializeRequest(request))!;
            substitutedRequest.VerifyPolicy = WorkerVerifyPolicy.RequireProven;
            var substitutedResponse = WorkerProtocolJson.DeserializeResponse(WorkerProtocolJson.SerializeResponse(response))!;
            substitutedResponse.RequestHash = WorkerProtocolJson.ComputeRequestHash(substitutedRequest);
            var preparedBinding = WorkerProtocolJson.ValidateForRequest(substitutedResponse, WorkerProtocolJson.ComputeRequestHash(request),
                snapshot.InputHash, artifact.Manifest, request, Program.ExpectedVersions());
            output.AppendLine(CultureInfo.InvariantCulture, $"prepared-request-bound: {preparedBinding.IsValid}");
            output.AppendLine(CultureInfo.InvariantCulture, $"publication-allowed: {preparedBinding.IsValid}");
        }
        if (leaseBlocked != null)
        {
            output.AppendLine(CultureInfo.InvariantCulture, $"lease-blocked: {leaseBlocked}");
            output.AppendLine(CultureInfo.InvariantCulture, $"both-publications-valid: {bothPublicationsValid}");
        }
        foreach (var claim in response.ClaimResults)
        {
            var declaration = artifact.Manifest.Claims.Single(item => item.ClaimId == claim.ClaimId);
            output.AppendLine(CultureInfo.InvariantCulture, $"claim: {declaration.Kind}[{declaration.Ordinal}] {claim.Outcome}/{claim.Reason} vacuity={claim.Vacuity}");
        }
        foreach (var error in response.Errors.OrderBy(error => error.Code, StringComparer.Ordinal))
        {
            output.AppendLine("error: " + error.Code);
            if (scenario == "promotion")
            {
                output.AppendLine("message: " + error.Message);
            }
        }
        if (exit != null)
        {
            output.AppendLine("exit: " + exit);
        }
        return output.ToString();
    }

    private static async Task<string> Replay(string scenario)
    {
        Assert.That(scenario, Is.AnyOf("replay-postcondition-approximation", "replay-guard-approximation",
            "replay-spec-result-unbound", "replay-spec-result-substitution", "replay-input-bound", "replay-input-no-provider",
            "replay-guard-unresolved"));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = factory.CreateVariable("entry", factory.IntegerType);
        var body = factory.CreateVariable("body", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Havoc(block, factory.CreateOperation(), IrHavocKind.Variables,
            scenario is "replay-input-bound" or "replay-input-no-provider" ? IrHavocOrigin.Input
                : scenario.StartsWith("replay-spec", StringComparison.Ordinal) ? IrHavocOrigin.SpecResult : IrHavocOrigin.Approximation, body);
        builder.Return(block, factory.CreateOperation());
        var comparison = factory.Binary(IrBinaryOperator.GreaterThanOrEqual, factory.Variable(parameter), factory.Integer(0));
        var context = new CallableReplayContext(builder.Build(), false,
            scenario == "replay-spec-result-unbound" ? ImmutableDictionary<IrVarId, IrVarId>.Empty
                : ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(body, parameter),
            ImmutableDictionary<IrVarId, IrVarId?>.Empty, [], scenario == "replay-guard-approximation" ? factory.Boolean(false) : comparison,
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty, 100, [],
            postconditionGuard: scenario == "replay-guard-approximation" ? comparison
                : scenario == "replay-guard-unresolved" ? factory.Variable(factory.CreateVariable("unresolved", factory.BooleanType))
                : factory.Boolean(true),
            replayOptions: scenario == "replay-input-no-provider" ? null
                : new IrProgramReplayOptions(_ => factory.CreateIntegerValue(scenario == "replay-input-bound" ? 2 : -1)));
        var query = new VerificationQuery(factory, [], new Goal(factory, factory.Boolean(false),
            ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [parameter]);
        var outcome = await new ProofKernel(new EntryModelBackend(parameter, factory.CreateIntegerValue(-1)))
            .VerifyCallableAsync(query, context);
        return outcome switch
        {
            UnknownOutcome unknown => "outcome: Unknown\nreason: " + unknown.Reason,
            RefutedOutcome => "outcome: Refuted\nvalidated-entry: -1",
            _ => throw new InvalidOperationException("Unexpected replay outcome.")
        };
    }

    private static async Task<string> BooleanModels()
    {
        var output = new StringBuilder();
        foreach (var semantics in new[] { IrExecutionSemantics.Legacy, IrExecutionSemantics.Total })
        {
            var factory = new IrFactory(semantics);
            var parameter = factory.CreateVariable("entry", factory.BooleanType);
            ISmtBackend backend = semantics == IrExecutionSemantics.Total
                ? new CallableSolverSession(factory, new IrSmtBackendOptions()) : new IrSmtBackend();
            using var lifetime = (IDisposable)backend;
            foreach (var expected in new[] { false, true })
            {
                var bound = factory.Binary(IrBinaryOperator.Equal, factory.Variable(parameter), factory.Boolean(expected));
                var query = new VerificationQuery(factory,
                    [new Assumption(factory, bound, new LoweredJustification(factory.CreateOperation()))],
                    new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [parameter]);
                var outcome = await new ProofKernel(backend).VerifyAsync(query);
                Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
                var value = ((RefutedOutcome)outcome).Model.Assignments[parameter];
                Assert.That(value.Boolean, Is.EqualTo(expected));
                output.AppendLine(CultureInfo.InvariantCulture, $"model: {semantics} {value.Boolean} outcome=Refuted");
            }
        }
        return output.ToString();
    }

    private static async Task<string> TypedModel(string scenario)
    {
        var isSigned = scenario[6] == 's';
        Assert.That(scenario[6], Is.AnyOf('s', 'u'));
        var width = int.Parse(scenario[7..], CultureInfo.InvariantCulture);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(width, isSigned);
        var bits = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
        var parameter = factory.CreateVariable("entry", type);
        var body = factory.CreateVariable("body", type);
        var result = factory.CreateVariable("result", type);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Input, body);
        builder.Return(entry, factory.CreateOperation(), factory.Variable(body));
        var context = new CallableReplayContext(builder.Build(), false,
            ImmutableDictionary<IrVarId, IrVarId>.Empty.Add(body, parameter),
            ImmutableDictionary<IrVarId, IrVarId?>.Empty, [result],
            factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(result), factory.Variable(parameter)),
            ImmutableDictionary<IrVarId, (BigInteger, BigInteger)>.Empty.Add(parameter,
                isSigned ? (-(BigInteger.One << (width - 1)), (BigInteger.One << (width - 1)) - 1)
                    : (BigInteger.Zero, new BigInteger(bits))), 100, [],
            postconditionGuard: factory.Boolean(true), replayOptions: null);
        var bound = factory.Binary(IrBinaryOperator.Equal, factory.Variable(parameter), factory.IntegerBits(type, bits));
        var query = new VerificationQuery(factory,
            [new Assumption(factory, bound, new LoweredJustification(factory.CreateOperation()))],
            new Goal(factory, factory.Boolean(false), ProofDiagnosticKind.Postcondition, new SourceLocationId(0)), [parameter]);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyCallableAsync(query, context);
        Assert.That(outcome, Is.TypeOf<RefutedOutcome>());
        var value = ((RefutedOutcome)outcome).Model.Assignments[parameter];
        Assert.That(value.Type, Is.EqualTo(type));
        Assert.That(value.IntegerBits, Is.EqualTo(bits));
        return string.Create(CultureInfo.InvariantCulture,
            $"outcome: Refuted\nwidth: {width}\nsigned: {isSigned}\nraw-bits: {value.IntegerBits}\nnumeric-value: {value.IntegerNumericValue}");
    }

    private static async Task<string> NativeCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var runner = new SmtNativeRunner(static () => new Microsoft.Z3.Context(), retireAfterFailure: true);
        long published = 0;
        long nativeCost = 0;
        var canceled = false;
        try
        {
            await runner.CheckAsync(() =>
            {
                var meter = new SmtQueryResourceMeter(1_000_000, cancellation.Token);
                try
                {
                    using var solver = runner.Context.MkSolver();
                    var before = IrSmtBackend.ReadResourceCount(solver);
                    Assert.That(solver.Check(), Is.EqualTo(Microsoft.Z3.Status.SATISFIABLE));
                    var after = IrSmtBackend.ReadResourceCount(solver);
                    Assert.That(after, Is.Not.Null);
                    nativeCost = IrSmtBackend.ComputeResourceDelta(before.GetValueOrDefault(), after!.Value);
                    Assert.That(nativeCost, Is.GreaterThan(0));
                    cancellation.Cancel();
                    meter.ConsumeNative(nativeCost);
                    return BackendCheckResult.Unsatisfiable([]);
                }
                finally
                {
                    published = meter.Consumed;
                }
            }, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }
        Assert.That(canceled, Is.True);
        Assert.That(published, Is.GreaterThanOrEqualTo(nativeCost));
        return "run: Canceled\nnative-cost-retained: " + (published >= nativeCost);
    }

    private static async Task<string> NativeResource()
    {
        using var runner = new SmtNativeRunner(static () => new Microsoft.Z3.Context(), retireAfterFailure: true);
        using var solver = runner.Context.MkSolver();
        using var predicate = runner.Context.MkBoolConst("native-budget");
        solver.Assert(predicate);
        long published = 0;
        var result = await runner.CheckAsync(() =>
        {
            var meter = new SmtQueryResourceMeter(1, CancellationToken.None);
            try
            {
                SmtNativeCheck.Run(solver, [], meter);
                return BackendCheckResult.Satisfiable(new BackendModel([]));
            }
            finally
            {
                published = meter.Consumed;
            }
        }, CancellationToken.None);
        Assert.That(result.FailureReason, Is.EqualTo(BackendFailureReason.ResourceLimit));
        Assert.That(published, Is.GreaterThan(1));
        return "outcome: Unknown\nreason: ResourceLimit\nnative-cost-retained: " + (published > 1);
    }

    private static async Task<string> NativeInfrastructure()
    {
        using var runner = new SmtNativeRunner(static () => new Microsoft.Z3.Context(), retireAfterFailure: true);
        using var solver = runner.Context.MkSolver();
        var failure = await runner.CheckAsync(() =>
        {
            using var predicate = runner.Context.MkTrue();
            solver.Assert(predicate);
            throw new InvalidOperationException("Native selector bookkeeping did not complete.");
        }, CancellationToken.None);
        Assert.That(failure.FailureReason, Is.EqualTo(BackendFailureReason.InfrastructureFailure));
        var later = await runner.CheckAsync(() => BackendCheckResult.Unsatisfiable([]), CancellationToken.None);
        Assert.That(later.FailureReason, Is.EqualTo(BackendFailureReason.Unavailable));
        return "outcome: Unknown\nreason: InfrastructureFailure\nretired: " + (later.FailureReason == BackendFailureReason.Unavailable);
    }

    private sealed class EntryModelBackend(IrVarId variable, IrValue value) : ISmtBackend
    {
        public Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(BackendCheckResult.Satisfiable(new BackendModel([KeyValuePair.Create(variable, value)])));
        }
    }
}
