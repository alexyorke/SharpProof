using System.Reflection;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Host;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeMetadataReferenceOwnerTests
{
    [TestCase("string", false, false)]
    [TestCase("string", true, false)]
    [TestCase("object", false, false)]
    [TestCase("object", true, false)]
    [TestCase("string", false, true)]
    [TestCase("string", true, true)]
    [TestCase("object", false, true)]
    [TestCase("object", true, true)]
    public async Task RealReferenceEntryAndReturnPreserveIdentityAndOwnedRequires(string type, bool isNull, bool guarded)
    {
        using var subject = new MetadataTestSubject(
            $"using SharpProof.Attributes; public static class Library {{ public static {type} Target([NotNull] {type} value) => value; }}",
            $"public static class Subject {{ public static {type} Target({type} value) {{ {(guarded ? "if (value == null) return null;" : "")} return Library.Target(value); }} }}");
        var references = Capture(subject);
        var legacy = Prepare(subject, references, false);
        Assert.That(legacy.Callers, Is.Empty);
        Assert.That(legacy.Gaps.Single().Reason, Is.EqualTo("UnsupportedSignature"));
        var batch = Prepare(subject, references, true);
        Assert.That(batch.Gaps, Is.Empty);
        var body = Decode(batch.Callers.Single().Body, references);
        var factory = body.Body.Program.Factory;
        var entry = body.Body.Parameters.Single().Entry;
        object? runtime = isNull ? null : type == "string" ? "hello" : new object();
        var entryType = factory.GetVariableInfo(entry).Type;
        var input = runtime == null ? factory.CreateNullValue(entryType) : type == "string" ?
            factory.CreateStringValue((string)runtime) : factory.CreateReferenceValue(entryType, runtime);
        var observed = new List<bool>();
        var marker = body.Body.CallPreconditions.Single().Instruction;
        var execution = new IrProgramInterpreter(factory).Execute(body.Body.Program,
            new Dictionary<IrVarId, IrValue> { [entry] = input }, 10000,
            new IrProgramReplayOptions(static _ => null)
            { AssignmentObserver = (instruction, value, _) => { if (instruction.Id == marker) { observed.Add(value.Boolean); } } });
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ConsumedApproximation, Is.False);
        if (guarded && isNull)
        { Assert.That(execution.ReturnValue!.Kind, Is.EqualTo(IrValueKind.Null)); }
        else
        { Assert.That(execution.ReturnValue, Is.SameAs(input)); }
        Assert.That(subject.RootMethod.Invoke(null, [runtime]), Is.SameAs(runtime));
        Assert.That(observed.Count, Is.EqualTo(guarded && isNull ? 0 : 1));
        if (observed.Count != 0)
        { Assert.That(observed.Single(), Is.EqualTo(!isNull)); }
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(body);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var checkedCall = await solver.VerifyCallPreconditionAsync(0);
        Assert.That(checkedCall.Outcome, guarded ? Is.InstanceOf<ProvenOutcome>() : Is.InstanceOf<RefutedOutcome>());
        if (!guarded)
        {
            Assert.That(checkedCall.EntryModel[entry].Kind, Is.EqualTo(IrValueKind.Null));
            Assert.That(plan!.ReplayCallPrecondition(0, checkedCall.EntryModel, CancellationToken.None), Is.EqualTo(checkedCall.CallPreconditionWitness));
        }
    }

    [Test]
    public void ReferenceArgumentFaultPreventsCallObservation()
    {
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static int Target([NotNull] string value) => 7; }",
            "public static class Subject { public static int Target(string value) => Library.Target(value.Length == 0 ? null : value); }");
        var references = Capture(subject);
        var batch = Prepare(subject, references, true);
        Assert.That(batch.Gaps, Is.Empty);
        var decoded = Decode(batch.Callers.Single().Body, references).Body;
        var entry = decoded.Parameters.Single().Entry;
        var observations = 0;
        var marker = decoded.CallPreconditions.Single().Instruction;
        var result = new IrProgramInterpreter(decoded.Program.Factory).Execute(decoded.Program,
            new Dictionary<IrVarId, IrValue> { [entry] = decoded.Program.Factory.CreateNullValue(decoded.Program.Factory.StringType) },
            10000, new IrProgramReplayOptions(static _ => null)
            { AssignmentObserver = (instruction, _, _) => { if (instruction.Id == marker) { observations++; } } });
        Assert.That(observations, Is.Zero);
        Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.NullReference));
        var fault = Assert.Throws<TargetInvocationException>(new Action(() => subject.RootMethod.Invoke(null, [null])));
        Assert.That(fault!.InnerException, Is.TypeOf<NullReferenceException>());
    }

    [TestCase("int[]")]
    [TestCase("dynamic")]
    [TestCase("ValueToken")]
    public void UnqualifiedReferenceOwnerDomainsRemainExplicitGaps(string type)
    {
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static object Target([NotNull] object value) => value; }",
            $"public struct ValueToken {{ }} public class Subject {{ public static object Target({type} value) => Library.Target(value); }}");
        var batch = Prepare(subject, Capture(subject), true);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("UnsupportedSignature"));
    }

    private static CompilerReferenceSnapshot[] Capture(MetadataTestSubject subject)
    {
        return CompilerCompilationCapture.CaptureReferences(subject.Compilation.References,
            CompilerCompilationCapture.ReferenceCaptureLimits.Default, CancellationToken.None);
    }

    private static CompilerShadowPreparationBatch Prepare(MetadataTestSubject subject, CompilerReferenceSnapshot[] references, bool enabled)
    {
        return CompilerTotalCallableLowerer.PrepareShadowCallers(subject.Compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(subject.Compilation, CancellationToken.None), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), CancellationToken.None, enableMetadataRequires: enabled);
    }

    private static CompilerDecodedShadowBody Decode(CompilerTotalCallablePreparation body, CompilerReferenceSnapshot[] references)
    {
        return CompilerDecodedShadowBody.Decode(body.CallableId, JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(
            JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(body)))!, CancellationToken.None, references);
    }
}
