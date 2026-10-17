using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Host;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// A distinct, prospectively pinned method-reservation boundary. No calibration or retry.
// Counts below cover the actual supplied session, not independent Horn proposal sessions.
[TestFixture]
public sealed class CountUpHoudiniResourceBoundaryTests
{
    private const string Source =
        """
        using SharpProof.Attributes;
        public static class Subject {
            public static int CountUp(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() == n);
                var i = 0;
                while (i < n) { i++; }
                return i;
            }
            public static int CountDown(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() == 0);
                var i = n;
                while (i > 0) { i--; }
                return i;
            }
            public static int Nested(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() == n);
                var i = 0;
                while (i < n) {
                    var j = 0;
                    while (j < 3) { j++; }
                    i++;
                }
                return i;
            }
            public static int StepTwo(int n) {
                Contract.Requires(n >= 0);
                Contract.Ensures(Contract.Result<int>() <= n);
                var i = 0;
                while (i < n) { i += 2; }
                return i;
            }
            [DoesNotThrow]
            public static int Sum([NotNull] int[] values) {
                var total = 0;
                for (var i = 0; i < values.Length; i++) { total += values[i]; }
                return total;
            }
            [DoesNotThrow]
            public static int SumFromOne([NotNull] int[] values) {
                var total = 0;
                for (var i = 1; i <= values.Length; i++) { total += values[i]; }
                return total;
            }
            private static int s_counter;
            [ZeroAllocations]
            public static int GuardedAllocation(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) {
                    if (i < 0) { total += new int[1].Length; }
                    total++;
                }
                return total;
            }
            [EnforcePure]
            public static int GuardedWrite(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) {
                    if (i < 0) { s_counter = i; }
                    total++;
                }
                return total;
            }
            [ZeroAllocations]
            public static int ReachedAllocation(int n) {
                var total = 0;
                for (var i = 0; i < n; i++) {
                    if (i > 2) { total += new int[1].Length; }
                    total++;
                }
                return total;
            }
            public static int DoubleCount(int n) {
                Contract.Requires(n >= 0 && n <= 1000);
                Contract.Ensures(Contract.Result<int>() == 2 * n);
                var i = 0;
                var s = 0;
                while (i < n) { i++; s += 2; }
                return s;
            }
            public static int WrongDoubleCount(int n) {
                Contract.Requires(n >= 1 && n <= 1000);
                Contract.Ensures(Contract.Result<int>() == 2 * n + 1);
                var i = 0;
                var s = 0;
                while (i < n) { i++; s += 2; }
                return s;
            }
            public static int NeedsNoNegative(int n) {
                Contract.Ensures(Contract.Result<int>() == n);
                var i = 0;
                while (i < n) { i++; }
                return i;
            }
        }
        """;

    private const uint BoundaryMethodRlimit = 3_096_189;

    private static readonly int[] RuntimeInputs = [0, 1, 2, 4, 16];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [Test]
    public async Task ActualCountUpFinishesTheFirstPassThenDeniesTheCombinedRecheckReservation()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT")
            ?? throw new InvalidOperationException("The canonical tooling repository root is required.");
        var output = Path.Combine(repositoryRoot, "artifacts", "coverage", "semantic", "countup-houdini-resource-boundary");
        Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output, "observation.json"),
            "{\"captureComplete\":false,\"stage\":\"setup\",\"coverageClaim\":false}", new UTF8Encoding(false));
        var compilation = TestCompilation.Create("CountUpNativeResourceObservation", [("Subject.cs", Source)]);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var imageBytes = image.ToArray();
        var runtimeTrees = compilation.SyntaxTrees.Select(tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (CSharpSyntaxTree.ParseText(tree.GetText(),
            ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols(
                ((CSharpParseOptions)tree.Options).PreprocessorSymbolNames.Where(symbol => symbol is null ? throw new System.ArgumentNullException(nameof(symbol)) : (symbol != Contract.ConditionalSymbol))), tree.FilePath)));
        var runtimeCompilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(runtimeTrees);
        TestCompilation.AssertNoErrors(runtimeCompilation);
        using var runtimeImage = new MemoryStream();
        Assert.That(runtimeCompilation.Emit(runtimeImage).Success, Is.True);
        var runtimeBytes = runtimeImage.ToArray();
        var runtimeMethod = Assembly.Load(runtimeBytes).GetType("Subject")!.GetMethod("CountUp")!;
        foreach (var input in RuntimeInputs)
        {
            Assert.That(runtimeMethod.Invoke(null, [input]), Is.EqualTo(input));
        }
        var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
            WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
            WorkerBudgets.DefaultMaximumExpressionDepth, CancellationToken.None);
        var artifactJson = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(artifactJson, out var preparations);
        var preparation = preparations.Single(value => value is null ? throw new System.ArgumentNullException(nameof(value)) : (value.Entry.CallableId.Contains(":Subject.CountUp(", StringComparison.Ordinal)));
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        Assert.That(candidate!.CallableId, Is.EqualTo(preparation.Entry.CallableId));
        var ownedClaim = preparation.Total!.Clauses.Single(clause => clause is null ? throw new System.ArgumentNullException(nameof(clause)) : (clause.Kind == CompilerContractKind.Ensures)).ClaimId!;
        Assert.That(preparation.Entry.ClaimIds, Does.Contain(ownedClaim));
        var budgets = new WorkerBudgets { MethodRlimit = BoundaryMethodRlimit };
        Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
        Assert.That(budgets.MethodRlimit, Is.EqualTo(BoundaryMethodRlimit));
        Assert.That(budgets.MethodWallTimeMilliseconds, Is.EqualTo(10_000));
        Assert.That(budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300_000));
        Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
        await File.WriteAllTextAsync(Path.Combine(output, "source.cs.txt"), Source, new UTF8Encoding(false));
        await File.WriteAllTextAsync(Path.Combine(output, "artifact.json"), artifactJson, new UTF8Encoding(false));
        await File.WriteAllBytesAsync(Path.Combine(output, "native-image.dll"), imageBytes);
        var checks = new List<TotalCallableClaimCheck>();
        var reads = new List<long>();
        var budgetReads = new List<BudgetRead>();
        var recorder = new QueryRecords();
        long? baseline = null;
        long? finalConsumed = null;
        var droppedReads = 0;
        var completed = false;
        var methodCanceled = false;
        var projectCanceled = false;
        string? failureType = null;
        var sourceRoot = TestRepository.FindRoot();
        var solverSource = await File.ReadAllLinesAsync(Path.Combine(sourceRoot,
            "SharpProof.Worker", "Vc", "PassiveCallableSolver.cs"), TestContext.CurrentContext.CancellationToken);
        var budgetSource = await File.ReadAllLinesAsync(Path.Combine(sourceRoot,
            "SharpProof.Worker", "MethodResourceBudget.cs"), TestContext.CurrentContext.CancellationToken);
        var firstPassLine = Array.IndexOf(solverSource,
            "                var evidence = await VerifyAsync(query, null, cancellationToken, plan).ConfigureAwait(false);") + 1;
        var combinedRecheckLine = Array.IndexOf(solverSource,
            "                var together = await VerifyAsync(plan.AllInvariantsQuery(), null, cancellationToken, plan).ConfigureAwait(false);") + 1;
        var verifyQueryLine = Array.IndexOf(solverSource,
            "        if (!_budget.TryStartQuery())") + 1;
        var budgetReadLine = Array.IndexOf(budgetSource,
            "        var current = _readConsumedResourceCount();") + 1;
        Assert.That(firstPassLine, Is.GreaterThan(0));
        Assert.That(combinedRecheckLine, Is.GreaterThan(0));
        Assert.That(verifyQueryLine, Is.GreaterThan(0));
        Assert.That(budgetReadLine, Is.GreaterThan(0));
        try
        {
            ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
            using var session = new CallableSolverSession(candidate.Factory, new IrSmtBackendOptions(budgets.QueryRlimit));
            baseline = session.ConsumedResourceCount;
            Assert.That(baseline, Is.Zero, "The sole measured session has not been warmed.");
            long ReadResources()
            {
                var actual = session.ConsumedResourceCount;
                if (reads.Count < 1_024)
                {
                    reads.Add(actual);
                    var frames = new StackTrace(true).GetFrames().Where(frame =>
                        frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.GetMethod()?.DeclaringType == typeof(MethodResourceBudget) ||
                        frame.GetMethod()?.DeclaringType?.FullName?.StartsWith(typeof(PassiveCallableSolver).FullName!, StringComparison.Ordinal) == true))
                        .Select(frame => frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (new CallFrame(frame.GetMethod()?.DeclaringType?.FullName,
                            frame.GetMethod()?.Name, frame.GetFileName(), frame.GetFileLineNumber()))).ToArray();
                    budgetReads.Add(new(actual, recorder.Started,
                        frames.Any(frame => frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.Type == typeof(MethodResourceBudget).FullName && frame.Method == "TryStartQuery")) ||
                        frames.Any(frame => frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.Type == typeof(MethodResourceBudget).FullName && frame.Method == "GetConsumedResourceCount" &&
                            frame.Line == budgetReadLine && frame.File?.EndsWith("/SharpProof.Worker/MethodResourceBudget.cs", StringComparison.Ordinal) == true)) &&
                        frames.Any(frame => frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.Type?.StartsWith(typeof(PassiveCallableSolver).FullName + "+<VerifyAsync>", StringComparison.Ordinal) == true &&
                            frame.Method == "MoveNext" && frame.Line == verifyQueryLine &&
                            frame.File?.EndsWith("/SharpProof.Worker/Vc/PassiveCallableSolver.cs", StringComparison.Ordinal) == true)),
                        frames.Any(frame => frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.Type?.Contains("HoudiniAsync", StringComparison.Ordinal) == true && frame.Line == combinedRecheckLine)), frames));
                }
                else
                {
                    droppedReads++;
                }
                return actual;
            }
            var meter = new MethodResourceBudget(ReadResources, budgets.QueryRlimit, budgets.MethodRlimit);
            Assert.That(reads, Has.Count.EqualTo(1));
            Assert.That(reads[0], Is.EqualTo(baseline));
            var backend = new RecordingNativeBackend(session, baseline.Value, recorder);
            using var projectBoundary = new CancellationTokenSource(budgets.ProjectWallTimeMilliseconds);
            using var methodBoundary = CancellationTokenSource.CreateLinkedTokenSource(projectBoundary.Token);
            methodBoundary.CancelAfter(budgets.MethodWallTimeMilliseconds);
            try
            {
                await TotalCallableVerifier.VerifyAsync(preparation, budgets, checks.Add, null,
                    methodBoundary.Token, backend, meter, ReadResources);
                completed = true;
            }
            finally
            {
                finalConsumed = session.ConsumedResourceCount;
                methodCanceled = methodBoundary.IsCancellationRequested;
                projectCanceled = projectBoundary.IsCancellationRequested;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
        {
            failureType = exception.GetType().FullName;
            throw;
        }
        finally
        {
            // One post-session-disposal file. No per-query console or file writes.
            var payload = new
            {
                captureComplete = true,
                sourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source))),
                artifactSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(artifactJson))),
                imageSha256 = Convert.ToHexString(SHA256.HashData(imageBytes)),
                runtimeImageSha256 = Convert.ToHexString(SHA256.HashData(runtimeBytes)),
                callableId = preparation.Entry.CallableId,
                claimId = ownedClaim,
                runtimeInputs = RuntimeInputs,
                budgets,
                baseline,
                finalConsumed,
                completed,
                failureType,
                meterReads = reads,
                budgetReads,
                methodCanceled,
                projectCanceled,
                droppedReads,
                startedQueries = recorder.Started,
                droppedQueryRecords = recorder.Dropped,
                nativeDelta = recorder.NativeDelta,
                queries = recorder.Values,
                published = checks.Select(check =>
                {
                    System.ArgumentNullException.ThrowIfNull(check);
                    return new
                    {
                        check.ClaimId,
                        check.Enrolled,
                        check.Checked,
                        outcome = check.Evidence.Outcome?.GetType().Name,
                        reason = check.Evidence.Reason.ToString(),
                        feasibility = check.Feasibility.ToString(),
                        vacuity = check.Vacuity.ToString(),
                        check.Evidence.Core,
                        entryModelCount = check.Evidence.EntryModel.Count
                    };
                }),
                scope = "Actual supplied session/method meter only; independently created Horn sessions are not observed.",
                phaseAttribution = "Raw portable-PDB call frames only; absent frames mean phase capture is incomplete.",
                reducedBudgetSelected = true,
                coverageClaim = false,
                performanceClaim = false
            };
            await File.WriteAllTextAsync(Path.Combine(output, "observation.json"),
                JsonSerializer.Serialize(payload, JsonOptions), new UTF8Encoding(false));
        }
        var final = checks.Last(check => check is null ? throw new System.ArgumentNullException(nameof(check)) : (check.ClaimId == ownedClaim));
        Assert.That(completed, Is.True);
        Assert.That(methodCanceled, Is.False);
        Assert.That(projectCanceled, Is.False);
        Assert.That(final.Enrolled, Is.True);
        Assert.That(final.Checked, Is.True);
        Assert.That(final.Evidence.Outcome, Is.Null);
        Assert.That(final.Evidence.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete));
        Assert.That(final.Evidence.QueryCompleted, Is.True);
        Assert.That(final.Feasibility, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That(final.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
        Assert.That(final.Evidence.EntryModel, Is.Empty);
        Assert.That(final.Evidence.Core, Is.Empty);
        Assert.That(final.Evidence.BodyAssumptions, Is.Empty);
        Assert.That(recorder.Dropped, Is.Zero);
        Assert.That(droppedReads, Is.Zero);
        Assert.That(recorder.NativeDelta, Is.EqualTo(finalConsumed - baseline));
        Assert.That(recorder.Started, Is.EqualTo(15), "Five real prelude checks and ten first-pass checks; no later supplied-session reservation fits.");
        Assert.That(recorder.Values, Has.Count.EqualTo(15));
        Assert.That(recorder.Values.Take(5).Select(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.Status)), Is.EqualTo(
            "Satisfiable,Satisfiable,Satisfiable,Satisfiable,Unsatisfiable".Split(',')));
        Assert.That(recorder.Values.Skip(5).Select(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.Status)), Is.EqualTo(
            "Unsatisfiable,Satisfiable,Satisfiable,Unsatisfiable,Unsatisfiable,Unsatisfiable,Unsatisfiable,Unsatisfiable,Unsatisfiable,Unsatisfiable".Split(',')));
        Assert.That(recorder.Values.All(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.Reason == "None" && query.FailureType == null)), Is.True);

        Assert.That(recorder.Values.Skip(5).All(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (query.Frames.Any(frame =>
            frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.Type?.Contains("HoudiniAsync", StringComparison.Ordinal) == true && frame.Line == firstPassLine)))), Is.True);
        Assert.That(recorder.Values.All(query => query is null ? throw new System.ArgumentNullException(nameof(query)) : (!query.Frames.Any(frame =>
            frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.Type?.Contains("HoudiniAsync", StringComparison.Ordinal) == true && frame.Line == combinedRecheckLine)))), Is.True);
        var denied = budgetReads.Where(read => read is null ? throw new System.ArgumentNullException(nameof(read)) : (read.InTryStartQuery &&
            budgets.MethodRlimit - checked(read.Actual - baseline!.Value) < budgets.QueryRlimit)).ToArray();
        Assert.That(denied, Is.Not.Empty, "Actual reader evidence of reservation denial is required.");
        var firstDenied = denied[0];
        Assert.That(firstDenied.InCombinedRecheck, Is.True);
        Assert.That(firstDenied.CompletedNativeQueries, Is.EqualTo(15));
        Assert.That(firstDenied.Actual, Is.EqualTo(recorder.Values[^1].After));
        Assert.That(firstDenied.Actual - baseline!.Value, Is.LessThanOrEqualTo(budgets.MethodRlimit));
        Assert.That(budgets.MethodRlimit - (firstDenied.Actual - baseline!.Value), Is.LessThan(budgets.QueryRlimit));
        Assert.That(budgetReads.Where(read => read is null ? throw new System.ArgumentNullException(nameof(read)) : (read.InTryStartQuery)).TakeWhile(read => read is null ? throw new System.ArgumentNullException(nameof(read)) : (!ReferenceEquals(read, firstDenied)))
            .All(read => read is null ? throw new System.ArgumentNullException(nameof(read)) : (budgets.MethodRlimit - (read.Actual - baseline!.Value) >= budgets.QueryRlimit)), Is.True);
        Assert.That(denied.All(read => read is null ? throw new System.ArgumentNullException(nameof(read)) : (read.Actual == firstDenied.Actual)), Is.True);
        Assert.That(finalConsumed, Is.EqualTo(firstDenied.Actual));
    }

    private sealed class QueryRecords
    {
        internal int Started { get; set; }
        internal int Dropped { get; set; }
        internal long NativeDelta { get; set; }
        internal List<QueryRecord> Values { get; } = [];
    }

    private sealed record BudgetRead(long Actual, int CompletedNativeQueries,
        bool InTryStartQuery, bool InCombinedRecheck, CallFrame[] Frames)
    {
        public CallFrame[] Frames { get; init; } = Frames ?? throw new System.ArgumentNullException(nameof(Frames));
    }

    private sealed record CallFrame(string? Type, string? Method, string? File, int Line);

    private sealed record QueryRecord(int Ordinal, string Diagnostic, int Location, int AssumptionCount,
        int ModelVariableCount, long Before, long After, long RelativeBefore, long RelativeAfter,
        long Delta, string? Status, string? Reason, int[]? UnsatCore, bool? HasModel,
        string? FailureType, CallFrame[] Frames)
    {
        public string Diagnostic { get; init; } = Diagnostic ?? throw new System.ArgumentNullException(nameof(Diagnostic));
        public CallFrame[] Frames { get; init; } = Frames ?? throw new System.ArgumentNullException(nameof(Frames));
    }

    // No manufactured BackendCheckResult: exactly one real call, exact returned object.
    private sealed class RecordingNativeBackend(CallableSolverSession session, long baseline, QueryRecords records) : ISmtBackend
    {
        private QueryRecords _guarded_records = records ?? throw new System.ArgumentNullException(nameof(records));
        private CallableSolverSession _guarded_session = baseline < 0 ? throw new ArgumentOutOfRangeException(nameof(baseline)) : session ?? throw new System.ArgumentNullException(nameof(session));
        public async Task<BackendCheckResult> CheckAsync(VerificationQuery query, CancellationToken cancellationToken)
        {
            var ordinal = _guarded_records.Started++;
            var before = _guarded_session.ConsumedResourceCount;
            var frames = _guarded_records.Values.Count < 256
                ? new StackTrace(true).GetFrames().Where(frame =>
                    frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (frame.GetMethod()?.DeclaringType?.FullName?.StartsWith(typeof(PassiveCallableSolver).FullName!, StringComparison.Ordinal) == true))
                    .Select(frame => frame is null ? throw new System.ArgumentNullException(nameof(frame)) : (new CallFrame(frame.GetMethod()?.DeclaringType?.FullName,
                        frame.GetMethod()?.Name, frame.GetFileName(), frame.GetFileLineNumber()))).ToArray()
                : [];
            BackendCheckResult? result = null;
            string? failureType = null;
            try
            {
                result = await _guarded_session.CheckAsync(query, cancellationToken).ConfigureAwait(false);
                return result;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                failureType = exception.GetType().FullName;
                throw;
            }
            finally
            {
                var after = _guarded_session.ConsumedResourceCount;
                var delta = checked(after - before);
                _guarded_records.NativeDelta = checked(_guarded_records.NativeDelta + delta);
                if (_guarded_records.Values.Count < 256)
                {
                    _guarded_records.Values.Add(new QueryRecord(ordinal, query.Goal.Diagnostic.ToString(), query.Goal.Location.Value,
                        query.Assumptions.Length, query.ModelVariables.Length, before, after, checked(before - baseline),
                        checked(after - baseline), delta, result?.Status.ToString(), result?.FailureReason.ToString(),
                        result?.UnsatCore.ToArray(), result == null ? null : result.Model != null, failureType, frames));
                }
                else
                {
                    _guarded_records.Dropped++;
                }
            }
        }
    }
}
