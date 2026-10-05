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
        var actual = scenario == "mixed-snapshot-owners" ? await MixedSnapshotOwners()
            : scenario == "write-operand-approximation" ? await WriteOperandApproximation()
            : scenario == "synchronization-projection" ? await SynchronizationProjection(fixture)
            : scenario == "native-infrastructure" ? await NativeInfrastructure()
            : scenario == "native-cancellation" ? await NativeCancellation()
            : scenario == "native-resource" ? await NativeResource()
            : scenario == "passive-vc" ? await PassiveVc(fixture.Source)
            : scenario == "passive-ownership" ? PassiveOwnership()
            : scenario == "total-artifact" ? await TotalArtifact(fixture.Source)
            : scenario == "reachable-source" ? await ReachableSource(fixture.Source)
            : scenario == "total-claim-results" ? await TotalClaimResults(fixture.Source)
            : scenario == "artifact-passive-enrollment" ? await ArtifactPassiveEnrollment(fixture.Source)
            : scenario == "vc-shadow" ? await NativeVc(fixture.Source)
            : scenario == "vc-shadow-reference" ? await NativeVc(fixture.Source)
            : scenario == "typed-il-call-requires" ? await TypedIlCallRequires(fixture.Source)
            : scenario == "typed-il-shadow" ? await TypedIlGolden(fixture.Source, artifact => NativeVc(artifact))
            : scenario == "typed-il-artifact" ? await TypedIlGolden(fixture.Source, artifact => TotalArtifact(artifact))
            : scenario == "vc-loop-prologue-reentry" ? VcLoopPrologueReentry(fixture.Source)
            : scenario == "vc-exception-multi-entry" ? await VcExceptionMultiEntry()
            : scenario == "vc-assume-placement" ? VcAssumePlacement(fixture.Source)
            : scenario == "vc-shadow-meter-boundary" ? await NativeMeterBoundary(fixture.Source)
            : scenario == "vc-shadow-completed-boundary" ? await NativeCompletedBoundary(fixture.Source)
            : scenario == "model-boolean" ? await BooleanModels()
            : scenario.StartsWith("model-", StringComparison.Ordinal) ? await TypedModel(scenario)
            : scenario.StartsWith("replay-", StringComparison.Ordinal) ? await Replay(scenario) : await Verify(fixture, scenario);
        GoldenTest.Compare(fixture, actual);
    }

    private static async Task<string> MixedSnapshotOwners()
    {
        var output = new StringBuilder();
        foreach (var array in new[] { false, true })
        {
            foreach (var chooseOld in new[] { false, true })
            {
                var factory = new IrFactory(IrExecutionSemantics.Total);
                var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) : factory.ObjectType;
                PassiveParameterBinding Parameter(string name, IrTypeId parameterType)
                {
                    return new(factory.CreateVariable(name + ":entry", parameterType), factory.CreateVariable(name + ":current", parameterType),
                        factory.CreateVariable(name + ":old", parameterType));
                }
                var a = Parameter("a", type);
                var b = Parameter("b", type);
                var flag = Parameter("flag", factory.BooleanType);
                var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
                IrTerm Read(IrTerm owner)
                { return array ? factory.SequenceAccess(owner, factory.Integer(0)) : factory.PureOpaque(field, owner); }
                IrTerm Equal(IrTerm left, IrTerm right)
                { return factory.Binary(IrBinaryOperator.Equal, left, right); }
                var site = factory.CreateOperation();
                var requires = new List<PassiveContractClause>();
                foreach (var parameter in new[] { a, b })
                {
                    requires.Add(new(factory.Binary(IrBinaryOperator.NotEqual, factory.Variable(parameter.Entry), factory.Null(type)), factory.Boolean(true), site));
                    if (array)
                    { requires.Add(new(factory.Binary(IrBinaryOperator.GreaterThan, factory.Length(factory.Variable(parameter.Entry)), factory.Integer(0)), factory.Boolean(true), site)); }
                }
                requires.Add(new(Equal(factory.Variable(flag.Entry), factory.Boolean(chooseOld)), factory.Boolean(true), site));
                requires.Add(new(Equal(Read(factory.Conditional(factory.Variable(flag.Entry), factory.Variable(a.Entry), factory.Variable(b.Entry))), factory.Integer(1)), factory.Boolean(true), site));
                var builder = new IrProgramBuilder(factory);
                var block = builder.CreateBlock();
                if (array)
                { builder.ElementStore(block, site, factory.Variable(b.Current), factory.Integer(0), factory.Integer(2)); }
                else
                { builder.FieldStore(block, site, IrWriteRegion.Field, factory.Variable(b.Current), field, factory.Integer(2)); }
                builder.Return(block, site, factory.Integer(0));
                var result = factory.CreateVariable("result", factory.IntegerType);
                var mixed = factory.Conditional(factory.Variable(flag.Current), factory.Variable(a.Old), factory.Variable(b.Current));
                var candidate = new PassiveCallableCandidate("mixed-snapshot", builder.Build(), [a, b, flag], result, [.. requires],
                    [new(Equal(Read(mixed), factory.Integer(1)), factory.Boolean(true), site)]);
                Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
                using var solver = new PassiveCallableSolver(plan!);
                Assert.That((await solver.VerifyFeasibilityAsync()).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
                var verified = await solver.VerifyEnsuresAsync(0);
                Assert.That(verified.Outcome, chooseOld ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>());
                output.Append(array ? "array-" : "field-").Append(chooseOld ? "old: " : "current: ")
                    .Append(verified.Outcome!.GetType().Name).Append('/').Append(verified.Reason).Append('\n');
            }
        }
        foreach (var array in new[] { false, true })
        {
            var (candidate, _) = PassiveCallableVcTests.BodyOldHeapCandidate(array);
            Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
            using var solver = new PassiveCallableSolver(plan!);
            foreach (var correct in new[] { false, true })
            {
                var verified = await solver.VerifyEnsuresAsync(correct ? 1 : 0);
                output.Append(array ? "array-" : "field-").Append(correct ? "body-old-true: " : "body-old-false: ")
                    .Append(verified.Outcome!.GetType().Name).Append('/').Append(verified.Reason).Append('\n');
            }
        }
        return output.ToString();
    }

    private static async Task<string> WriteOperandApproximation()
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var factory = subject.Factory;
        var block = subject.Builder.CreateBlock();
        var receiver = factory.CreateVariable("receiver", factory.ObjectType);
        var value = factory.CreateVariable("approximate", factory.IntegerType);
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType, "field:Value", factory.IntegerType, false);
        subject.Builder.Allocate(block, subject.Site, factory.ObjectType, receiver);
        subject.Builder.Havoc(block, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Approximation, value);
        subject.Builder.FieldStore(block, factory.CreateOperation("write-site"), IrWriteRegion.Field,
            factory.Variable(receiver), field, factory.Variable(value));
        subject.Builder.Return(block, subject.Site, factory.Integer(0));
        Assert.That(PassiveCallableVcBuilder.TryBuild(subject.Candidate(factory.Boolean(true)), out var plan, out var failure),
            Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyPurityAsync();
        var output = new StringBuilder("outcome: " + (result.Outcome == null ? "Unknown" : result.Outcome.GetType().Name) + "\nreason: " +
            result.Reason + "\nwrite-witness: " + (result.WriteWitness != null) + "\n");
        foreach (var approximate in new[] { false, true })
        {
            var call = PassiveCallableVcTests.SkippedCallEffectCandidate(approximate);
            Assert.That(PassiveCallableVcBuilder.TryBuild(call.Candidate, out var callPlan, out failure), Is.True, failure.ToString());
            using var callSolver = new PassiveCallableSolver(callPlan!);
            var callResult = await callSolver.VerifyPurityAsync();
            output.AppendLine("skipped-call-" + (approximate ? "approximation" : "constant") + ": " +
                (callResult.Outcome?.GetType().Name ?? "Unknown") + "/" + callResult.Reason + "/witness=" + (callResult.WriteWitness != null));
        }
        foreach (var array in new[] { false, true })
        {
            var unchanged = PassiveCallableVcTests.OpaqueWriterHeapCandidate(array, restore: false, nonwriter: true);
            Assert.That(PassiveCallableVcBuilder.TryBuild(unchanged, out var unchangedPlan, out failure), Is.True, failure.ToString());
            using var unchangedSolver = new PassiveCallableSolver(unchangedPlan!);
            var unchangedResult = await unchangedSolver.VerifyEnsuresAsync(0);
            output.AppendLine((array ? "array" : "field") + "-nonwriter: " + unchangedResult.Outcome!.GetType().Name);
            foreach (var restore in new[] { false, true })
            {
                var heap = PassiveCallableVcTests.OpaqueWriterHeapCandidate(array, restore);
                Assert.That(PassiveCallableVcBuilder.TryBuild(heap, out var heapPlan, out failure), Is.True, failure.ToString());
                using var heapSolver = new PassiveCallableSolver(heapPlan!);
                var heapResult = await heapSolver.VerifyEnsuresAsync(0);
                output.AppendLine((array ? "array" : "field") + (restore ? "-restored" : "-unknown") + ": " +
                    heapResult.Outcome!.GetType().Name + "/" + heapResult.Reason);
            }
        }
        return output.ToString();
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
        foreach (var array in new[] { false, true })
        {
            var (heapCandidate, inputs) = PassiveLoopCutterTests.ExceptionLoopHeapCandidate(array);
            var heapExecution = new IrProgramInterpreter(heapCandidate.Factory).Execute(heapCandidate.Program, inputs);
            Assert.That(PassiveCallableVcBuilder.TryBuild(heapCandidate, out var heapPlan, out reason), Is.True, reason.ToString());
            using var heapSolver = new PassiveCallableSolver(heapPlan!);
            var heapEnsures = await heapSolver.VerifyEnsuresAsync(0);
            output.AppendLine((array ? "element" : "field") + "-loop-original-return: " + heapExecution.ReturnValue!.IntegerNumericValue);
            output.AppendLine((array ? "element" : "field") + "-loop-false-ensures: " + heapEnsures.Outcome!.GetType().Name);
        }
        foreach (var explicitThrows in new[] { false, true })
        {
            var pendingCandidate = PassiveExceptionLoopTests.ChangingPendingExceptionCandidate(explicitThrows);
            var pendingExecution = new IrProgramInterpreter(pendingCandidate.Factory).Execute(pendingCandidate.Program);
            var sites = pendingCandidate.Program.Blocks.SelectMany(block => block.Instructions).OfType<IrThrowInstruction>()
                .Select(thrown => thrown.Operation).ToArray();
            var prefix = explicitThrows ? "explicit-pending" : "builtin-pending";
            output.AppendLine(prefix + "-original-kind: " + pendingExecution.Exception!.Kind);
            Assert.That(PassiveCallableVcBuilder.TryBuild(pendingCandidate, out var pendingPlan, out reason), Is.True, reason.ToString());
            using var pendingSolver = new PassiveCallableSolver(pendingPlan!);
            foreach (var allowReplacement in new[] { false, true })
            {
                var allowed = explicitThrows ? ImmutableHashSet<IrExceptionKind>.Empty : ImmutableHashSet.Create(IrExceptionKind.Overflow);
                if (!explicitThrows && allowReplacement)
                { allowed = allowed.Add(IrExceptionKind.DivideByZero); }
                var pendingPolicy = await pendingSolver.VerifyExceptionsAsync(allowed,
                    allowedSite: site => site == sites[0] || allowReplacement && site == sites[1], exactSite: _ => true);
                output.AppendLine(prefix + (allowReplacement ? "-complete-policy: " : "-restricted-policy: ") + pendingPolicy.Outcome!.GetType().Name);
            }
        }
        foreach (var conditional in new[] { false, true })
        {
            var (receiverCandidate, inputs, _) = PassiveLoopCutterTests.ChangingHeapReceiverCandidate(conditional);
            var receiverExecution = new IrProgramInterpreter(receiverCandidate.Factory).Execute(receiverCandidate.Program, inputs);
            Assert.That(PassiveCallableVcBuilder.TryBuild(receiverCandidate, out var receiverPlan, out reason), Is.True, reason.ToString());
            using var receiverSolver = new PassiveCallableSolver(receiverPlan!);
            var receiverEnsures = await receiverSolver.VerifyEnsuresAsync(0);
            var prefix = conditional ? "conditional-receiver" : "nested-receiver";
            output.AppendLine(prefix + "-original-return: " + receiverExecution.ReturnValue!.IntegerNumericValue);
            output.AppendLine(prefix + "-false-ensures: " + receiverEnsures.Outcome!.GetType().Name);
        }
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

    private static async Task<string> NativeMeterBoundary(string source)
    {
        using var project = new ShadowTestProject(source);
        var preparation = project.Snapshot.Callables.Single();
        Assert.That(PassiveCallableVcBuilder.TryBuild(PassiveCallableArtifactAdapter.Enroll(preparation)!, out var plan, out var reason), Is.True, reason.ToString());
        long entryCost;
        long normalCost;
        using (var probe = new PassiveCallableSolver(plan!))
        {
            Assert.That((await probe.VerifyEntryAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
            entryCost = probe.ConsumedResourceCount;
            Assert.That((await probe.VerifyNormalCompletionAsync()).Outcome, Is.TypeOf<RefutedOutcome>());
            normalCost = probe.ConsumedResourceCount - entryCost;
        }
        Assert.That(normalCost, Is.GreaterThan(0));
        project.Request.Budgets.MethodRlimit = checked((uint)(project.Request.Budgets.QueryRlimit + entryCost + normalCost / 2));
        TotalCallableClaimCheck? check = null;
        await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets, value => check = value, CancellationToken.None);
        Assert.That(check, Is.Not.Null);
        Assert.That(check!.Feasibility, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
        Assert.That(check.Enrolled, Is.True);
        Assert.That(check.Checked, Is.False);
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
        var result = response.ClaimResults.Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.ResourceLimit));
        var output = new StringBuilder();
        output.AppendLine("authority: native");
        output.AppendLine("budget: actual native meter");
        output.AppendLine("entry-and-normal: " + check.Feasibility);
        output.AppendLine("enrolled: " + check.Enrolled);
        output.AppendLine("checked: " + check.Checked);
        output.AppendLine("reason: " + result.Reason);
        output.AppendLine("outcome: " + result.Outcome);
        return output.ToString();
    }

    private static async Task<string> NativeCompletedBoundary(string source)
    {
        using var project = new ShadowTestProject(source);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        using var backend = new NativeCallableBackend(new IrSmtBackendOptions(project.Request.Budgets.QueryRlimit));
        using var worker = new SharpProofWorker(backend);
        using var cancellation = new CancellationTokenSource();
        var completed = await worker.VerifyAsync(project.Request, project.Snapshot, cancellation.Token);
        await cancellation.CancelAsync();
        var canceled = await worker.VerifyAsync(project.Request, project.Snapshot, cancellation.Token);
        var next = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        foreach (var response in new[] { completed, canceled, next })
        { Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True); }
        Assert.That(completed.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        Assert.That(canceled.RunStatus, Is.EqualTo(WorkerRunStatus.Canceled));
        Assert.That(next.ClaimResults.Single().Outcome, Is.EqualTo(WorkerClaimOutcome.Proven));
        var output = new StringBuilder();
        output.AppendLine("authority: native");
        output.AppendLine("boundary: caller canceled after completed verification");
        output.AppendLine("completed-run: " + completed.RunStatus);
        output.AppendLine("completed-outcome: " + completed.ClaimResults.Single().Outcome);
        output.AppendLine("canceled-run: " + canceled.RunStatus);
        output.AppendLine("cache: " + completed.Summary.CacheStatus);
        output.AppendLine("next-run: " + next.RunStatus);
        output.AppendLine("next-failure: " + next.FailureReason);
        output.AppendLine("same-backend-outcome: " + next.ClaimResults.Single().Outcome);
        return output.ToString();
    }

    private static async Task<string> NativeVc(string source)
    {
        using var project = new ShadowTestProject(source, cacheEnabled: true);
        return await NativeVc(project);
    }

    private static async Task<string> NativeVc(CompilerManifestArtifact artifact)
    {
        using var project = new ShadowTestProject(artifact, cacheEnabled: true);
        return await NativeVc(project);
    }

    private static async Task<string> TypedIlCallRequires(string source)
    {
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        const string boundaryMarker = "// golden-library:";
        var boundary = source.IndexOf(boundaryMarker, StringComparison.Ordinal);
        Assert.That(boundary, Is.GreaterThan(0));
        using var subject = new MetadataTestSubject(source[(boundary + boundaryMarker.Length)..], source[..boundary]);
        var references = CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
        var batch = CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(subject.Compilation, CancellationToken.None), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None, enableMetadataRequires: true);
        Assert.That(batch.Gaps, Is.Empty);
        var body = batch.Callers.Single().Body;
        var encoded = CompilerTotalCallableArtifactCodec.Encode(body)!;
        var detached = JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(JsonSerializer.Serialize(encoded))!;
        var decoded = CompilerDecodedShadowBody.Decode(body.CallableId, detached, CancellationToken.None, references);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(decoded);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var check = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(check.Outcome, Is.InstanceOf<RefutedOutcome>());
        Assert.That(plan!.ReplayCallPrecondition(0, check.EntryModel, CancellationToken.None), Is.EqualTo(check.CallPreconditionWitness));
        var output = new StringBuilder();
        output.AppendLine("shadow callers: " + batch.Callers.Length.ToString(CultureInfo.InvariantCulture));
        output.AppendLine("preconditions: " + decoded.Body.CallPreconditions.Length.ToString(CultureInfo.InvariantCulture));
        output.AppendLine("origin: metadata");
        output.AppendLine("clause source span: none");
        output.AppendLine("native: Refuted");
        output.AppendLine("original replay witness: matched");
        var original = decoded.Body;
        var factory = original.Program.Factory;
        var entry = original.Parameters.Single().Entry;
        var nominalInputs = source.Contains("// golden-nominal-inputs", StringComparison.Ordinal);
        var referenceInputs = nominalInputs || source.Contains("// golden-reference-inputs", StringComparison.Ordinal);
        object?[] inputs = nominalInputs ? [null, Activator.CreateInstance(subject.RootMethod.GetParameters()[0].ParameterType)] :
            referenceInputs ? [null, "hello"] : [0, 1];
        foreach (var input in inputs)
        {
            var observations = new List<bool>();
            var marker = original.CallPreconditions.Single().Instruction;
            var replay = new IrProgramReplayOptions(static _ => null)
            {
                AssignmentObserver = (instruction, value, _) =>
                { if (instruction.Id == marker) { observations.Add(value.Boolean); } }
            };
            var concrete = referenceInputs ? input == null ? factory.CreateNullValue(factory.GetVariableInfo(entry).Type) :
                nominalInputs ? factory.CreateReferenceValue(factory.GetVariableInfo(entry).Type, input) : factory.CreateStringValue((string)input) : factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, (long)(int)input!);
            var execution = new IrProgramInterpreter(factory).Execute(original.Program,
                new Dictionary<IrVarId, IrValue> { [entry] = concrete },
                10000, replay);
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            if (referenceInputs)
            {
                if (nominalInputs)
                {
                    Assert.That(execution.ReturnValue!.Type, Is.EqualTo(factory.ObjectType));
                    if (input == null)
                    { Assert.That(execution.ReturnValue.Kind, Is.EqualTo(IrValueKind.Null)); }
                    else
                    { Assert.That(execution.ReturnValue.Reference, Is.SameAs(input)); }
                }
                else
                { Assert.That(execution.ReturnValue, Is.SameAs(concrete)); }
                Assert.That(subject.RootMethod.Invoke(null, [input]), Is.SameAs(input));
            }
            else
            { Assert.That(subject.RootMethod.Invoke(null, [input]), Is.EqualTo((int)execution.ReturnValue!.IntegerNumericValue)); }
            var renderedInput = nominalInputs ? input == null ? "null" : "token" : referenceInputs ? (string?)input ?? "null" : ((int)input!).ToString(CultureInfo.InvariantCulture);
            var renderedReturn = nominalInputs ? execution.ReturnValue!.Kind == IrValueKind.Null ? "null" : "token" : referenceInputs ? execution.ReturnValue!.Kind == IrValueKind.Null ? "null" : execution.ReturnValue.String :
                execution.ReturnValue!.IntegerNumericValue.ToString(CultureInfo.InvariantCulture);
            output.AppendLine("input " + renderedInput + ": marker " +
                (observations.Single() ? "true" : "false") + ", return " + renderedReturn);
        }
        return output.ToString();
    }

    private static async Task<string> TypedIlGolden(string source, Func<CompilerManifestArtifact, Task<string>> render)
    {
        const string marker = "// metadata-library";
        var boundary = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.That(boundary, Is.GreaterThan(0));
        using var subject = new MetadataTestSubject(source[(boundary + marker.Length)..], source[..boundary]);
        return await render(subject.CreateArtifact());
    }

    private static async Task<string> NativeVc(ShadowTestProject project)
    {
        var checks = new Dictionary<string, TotalCallableClaimCheck>(StringComparer.Ordinal);
        foreach (var preparation in project.Snapshot.Callables)
        {
            await TotalCallableVerifier.VerifyAsync(preparation, project.Request.Budgets,
                check => checks[check.ClaimId] = check, CancellationToken.None);
        }
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var output = new StringBuilder();
        WorkerClaimResult[]? original = null;
        foreach (var run in new[] { "normal", "cache-hit" })
        {
            var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
            Assert.That(response.Errors, Is.Empty);
            Assert.That(WorkerProtocolJson.Validate(response, project.Bind().InputHash, response.Manifest).IsValid, Is.True);
            if (original != null)
            {
                Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Hit));
                Assert.That(JsonSerializer.Serialize(response.ClaimResults, WorkerProtocolJson.SharedOptions),
                    Is.EqualTo(JsonSerializer.Serialize(original, WorkerProtocolJson.SharedOptions)));
            }
            original = response.ClaimResults;
            var claims = response.Manifest.Claims.Where(claim => claim.Kind == WorkerClaimKind.Postcondition).ToArray();
            var ids = claims.Select(claim => claim.ClaimId).ToHashSet(StringComparer.Ordinal);
            var results = response.ClaimResults.Where(result => ids.Contains(result.ClaimId)).ToDictionary(result => result.ClaimId, StringComparer.Ordinal);
            Assert.That(results.Keys, Is.EquivalentTo(ids));
            output.AppendLine("run: " + run);
            output.AppendLine("authority: native");
            output.AppendLine("request-bindings: True");
            output.AppendLine("cache: " + response.Summary.CacheStatus);
            output.AppendLine("postconditions: " + claims.Length);
            output.AppendLine("enrolled: " + checks.Values.Count(check => check.Enrolled));
            output.AppendLine("checked: " + checks.Values.Count(check => check.Checked));
            output.AppendLine("unknown: " + results.Values.Count(result => result.Outcome == WorkerClaimOutcome.Unknown));
            output.AppendLine("proven: " + results.Values.Count(result => result.Outcome == WorkerClaimOutcome.Proven));
            output.AppendLine("refuted: " + results.Values.Count(result => result.Outcome == WorkerClaimOutcome.Refuted));
            // Artifact claim hashes can include temporary metadata document paths.
            // Callable identity and source ordinal give every fixture stable ordering.
            var ordered = claims.OrderBy(claim => claim.CallableId, StringComparer.Ordinal).ThenBy(claim => claim.Ordinal);
            foreach (var claim in ordered)
            {
                var result = results[claim.ClaimId];
                var preparation = project.Snapshot.Callables.Single(callable => callable.Entry.CallableId == claim.CallableId);
                checks.TryGetValue(claim.ClaimId, out var check);
                if (check != null)
                {
                    var expected = CallableClaimResultAssembler.FromTotal(preparation, check);
                    Assert.That((result.Outcome, result.Reason, result.Vacuity), Is.EqualTo((expected.Outcome, expected.Reason, expected.Vacuity)));
                }
                output.AppendLine("row: " + claim.CallableId + "#" + claim.Ordinal);
                output.AppendLine("  totalPresent: " + (preparation.Total != null));
                output.AppendLine("  enrolled: " + (check?.Enrolled ?? false));
                output.AppendLine("  checked: " + (check?.Checked ?? false));
                output.AppendLine("  outcome: " + result.Outcome);
                output.AppendLine("  reason: " + result.Reason);
                output.AppendLine("  vacuity: " + result.Vacuity);
                output.AppendLine("  feasibility: " + (check?.Feasibility ?? PassiveCallableFeasibilityKind.Unknown));
                output.AppendLine("  conditional: " + result.Assumptions.Any(assumption => assumption.Used &&
                    assumption.Kind is WorkerAssumptionKind.UserAssume or WorkerAssumptionKind.TrustedBoundary or WorkerAssumptionKind.ApiSpecification));
            }
        }
        return output.ToString();
    }

    private static async Task<string> ReachableSource(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var reachable = artifact.ReachableSource!;
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _);
        var output = new StringBuilder();
        output.AppendLine(CultureInfo.InvariantCulture, $"collection-complete: {reachable.CollectionComplete}");
        output.AppendLine(CultureInfo.InvariantCulture, $"bodies: {reachable.Bodies.Length}");
        output.AppendLine(CultureInfo.InvariantCulture, $"incomplete-calls: {reachable.Bodies.Count(body => !body.CallsComplete)}");
        output.AppendLine(CultureInfo.InvariantCulture, $"preserved-calls: {reachable.Bodies.Sum(body => body.SourceCalls.Length)}");
        if (source.Contains("// golden-total-call-preconditions: true", StringComparison.Ordinal))
        {
            CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
            var encodedCount = artifact.Callables.Sum(callable => callable.Total?.CallPreconditions.Length ?? 0);
            var decodedCount = prepared.Sum(callable => callable.Total?.CallPreconditions.Length ?? 0);
            Assert.That(encodedCount, Is.EqualTo(2));
            Assert.That(decodedCount, Is.EqualTo(encodedCount));
            output.AppendLine(CultureInfo.InvariantCulture, $"total-call-preconditions: encoded={encodedCount} decoded={decodedCount}");
        }
        if (source.Contains("// golden-mutation: void-call-return", StringComparison.Ordinal))
        {
            var graph = reachable.Bodies.Single(body => body.IsCallSkeleton).Graph!;
            // Keep the first void member unchanged so canonical graph closure
            // cannot mask the second member's invalid return signature.
            var member = graph.Members.Single(value => value.Name.Contains("Helper", StringComparison.Ordinal));
            member.ReturnType = Array.FindIndex(graph.Types,
                type => type.Kind == IrTypeKind.Integer && type.Width == 32 && type.Signed);
            Assert.That(member.ReturnType, Is.GreaterThanOrEqualTo(0));
            PortableIrGraphCodec.Decode(graph);
            Assert.Throws<JsonException>(new Action(() => CompilerManifestArtifactJson.DeserializePrepared(
                CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out _)));
            output.AppendLine("void-call-return-mutation: rejected");
        }
        if (source.Contains("// golden-native-call-preconditions: true", StringComparison.Ordinal))
        {
            CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var prepared);
            var count = 0;
            foreach (var owner in prepared.Where(owner => owner.Total?.CallPreconditions.Length > 0))
            {
                var candidate = PassiveCallableArtifactAdapter.Enroll(owner)!;
                Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
                using var solver = new PassiveCallableSolver(plan!);
                for (var ordinal = 0; ordinal < plan!.CallPreconditionCount; ordinal++)
                {
                    var result = await solver.VerifyCallPreconditionAsync(ordinal);
                    var outcome = result.Outcome switch
                    {
                        ProvenOutcome => "Proven",
                        RefutedOutcome => "Refuted",
                        _ => "Unknown"
                    };
                    output.AppendLine(CultureInfo.InvariantCulture,
                        $"call-precondition: {ordinal} outcome={outcome} reason={result.Reason} witness={result.CallPreconditionWitness != null}");
                    count++;
                }
            }
            Assert.That(count, Is.GreaterThan(0));
        }
        if (source.Contains("// golden-native-input-havoc: true", StringComparison.Ordinal))
        { output.Append(await InputHavocWitnesses()); }
        return output.ToString();
    }

    private static async Task<string> InputHavocWitnesses()
    {
        var output = new StringBuilder();
        foreach (var kind in new[] { "call", "allocation", "write", "exception" })
        {
            var subject = new PassiveCallableVcTests.ScalarSubject();
            var factory = subject.Factory;
            var builder = subject.Builder;
            var entry = builder.CreateBlock();
            var effect = builder.CreateBlock();
            var quiet = builder.CreateBlock();
            var effectSite = factory.CreateOperation("effect-site");
            builder.Havoc(entry, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Input, subject.Parameter.Current);
            builder.Assign(entry, subject.Site, subject.Parameter.Current, factory.Integer(42));
            builder.Havoc(entry, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Input, subject.Parameter.Current);
            var predicate = factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Current), factory.Integer(0));
            var safe = factory.Boolean(true);
            var marker = builder.Assign(entry, subject.Site, factory.CreateVariable("call-marker", factory.BooleanType),
                factory.Binary(IrBinaryOperator.AndAlso, safe, predicate));
            builder.Branch(entry, subject.Site, factory.Binary(IrBinaryOperator.Equal,
                factory.Variable(subject.Parameter.Current), factory.Integer(1)), effect, quiet);
            builder.Return(quiet, subject.Site, factory.Integer(0));
            if (kind == "exception")
            {
                var exit = builder.CreateBlock();
                builder.Throw(effect, effectSite, IrExceptionKind.Overflow, exit);
                builder.ExceptionalExit(exit, subject.Site);
            }
            else
            {
                if (kind == "allocation")
                { builder.Allocate(effect, effectSite, factory.ObjectType); }
                if (kind == "write")
                { builder.Write(effect, effectSite, IrWriteRegion.Static); }
                builder.Return(effect, subject.Site, factory.Integer(0));
            }
            var requires = factory.Binary(IrBinaryOperator.Equal, factory.Variable(subject.Parameter.Entry), factory.Integer(1));
            var candidate = new PassiveCallableCandidate("input-havoc", builder.Build(), [subject.Parameter], subject.Result,
                [new(requires, safe, subject.Site)], [], callPreconditions: [new(marker.Id, predicate, safe)]);
            Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
            using var solver = new PassiveCallableSolver(plan!);
            var result = kind switch
            {
                "call" => await solver.VerifyCallPreconditionAsync(0),
                "allocation" => await solver.VerifyAllocationsAsync(),
                "write" => await solver.VerifyPurityAsync(),
                _ => await solver.VerifyExceptionsAsync(ImmutableHashSet<IrExceptionKind>.Empty)
            };
            Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>());
            Assert.That(result.EntryModel[subject.Parameter.Entry].IntegerNumericValue, Is.EqualTo(BigInteger.One));
            var witness = kind switch
            {
                "call" => result.CallPreconditionWitness == subject.Site,
                "allocation" => result.AllocationWitness == effectSite,
                "write" => result.WriteWitness == effectSite,
                _ => result.ExceptionWitness?.Kind == IrExceptionKind.Overflow
            };
            Assert.That(witness, Is.True);
            output.AppendLine(CultureInfo.InvariantCulture, $"input-havoc: {kind} outcome=Refuted original-input=1 witness={witness}");
        }
        return output.ToString();
    }

    private static async Task<string> TotalArtifact(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var output = new StringBuilder(await TotalArtifact(artifact));
        using var project = new ShadowTestProject(NativeAliasingBoundaryTests.DistinctSealedSource);
        var total = project.Snapshot.Callables.Single().Total!;
        output.AppendLine("disjoint-entry-pairs: " + string.Join(",", total.DisjointInputs.Select(pair => pair.Left + ":" + pair.Right)));
        using var worker = SharpProofWorker.Create(project.Request.Budgets);
        var response = await worker.VerifyAsync(project.Request, project.Snapshot, CancellationToken.None);
        Assert.That(response.Errors, Is.Empty);
        output.AppendLine("distinct-sealed-identity: " + response.ClaimResults.Single().Outcome);
        return output.ToString();
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

    private static async Task<string> SynchronizationProjection(GoldenCase fixture)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(fixture.Source);
        CompilerManifestArtifactJson.DeserializePrepared(CompilerManifestArtifactJson.SerializeProducerValidated(artifact), out var preparations);
        var preparation = preparations.Single();
        var claim = preparation.EffectClaims.Single();
        var result = await NativeEffectSiteVerifier.VerifySynchronizationShadowAsync(preparation, claim.ClaimId,
            claim.Constraint.AllowedCapabilities, new WorkerBudgets());
        return result.Outcome is ProvenOutcome ? "Proven" : result.Outcome is RefutedOutcome ? "Refuted" : "Unknown";
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
        foreach (var semantics in new[] { IrExecutionSemantics.Total })
        {
            var factory = new IrFactory(semantics);
            var parameter = factory.CreateVariable("entry", factory.BooleanType);
            using var backend = new CallableSolverSession(factory, new IrSmtBackendOptions());
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
                    var before = SmtNativeUtilities.ReadResourceCount(solver);
                    Assert.That(solver.Check(), Is.EqualTo(Microsoft.Z3.Status.SATISFIABLE));
                    var after = SmtNativeUtilities.ReadResourceCount(solver);
                    Assert.That(after, Is.Not.Null);
                    nativeCost = SmtNativeUtilities.ComputeResourceDelta(before.GetValueOrDefault(), after!.Value);
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
