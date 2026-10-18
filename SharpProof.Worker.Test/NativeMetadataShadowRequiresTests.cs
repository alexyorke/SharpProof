using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Frontend;
using SharpProof.Host;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeMetadataShadowRequiresTests
{
    private static readonly ImmutableArray<bool> SuccessfulObservation = [true];
    [TestCase("Positive", "int", "value + 1", "0", 1, false)]
    [TestCase("Positive", "int", "value + 1", "1", 2, true)]
    [TestCase("InRange(1, 10)", "int", "value + 1", "0", 1, false)]
    [TestCase("InRange(1, 10)", "int", "value + 1", "5", 6, true)]
    [TestCase("NotNull", "string", "value == null ? 0 : 1", null, 0, false)]
    [TestCase("NotNull", "string", "value == null ? 0 : 1", "hello", 1, true)]
    public async Task GenuineMetadataCallsRecordTypedObligationAndExecuteOriginalBody(string attribute,
        string type, string body, string? input, int result, bool predicate)
    {
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(type);
        var callerType = type == "string" ? "int" : type;
        var argument = type == "string" ? "value == 0 ? null : \"hello\"" : "value";
        using var subject = new MetadataTestSubject(
            $"using SharpProof.Attributes; public static class Library {{ public static int Target([{attribute}] {type} value) => {body}; }}",
            $"public static class Subject {{ public static int Target({callerType} value) => Library.Target({argument}); }}");
        var baseline = JsonSerializer.Serialize(new ClaimManifestBuilder(subject.Compilation).Build().Manifest);
        var references = Capture(subject.Compilation);
        var batch = Prepare(subject.Compilation, references, true);
        Assert.That(batch.Gaps, Is.Empty);
        Assert.That(batch.Callers, Has.Length.EqualTo(1));
        var prepared = batch.Callers.Single().Body;
        Assert.That(prepared.CallPreconditions, Has.Length.EqualTo(1));
        Assert.That(prepared.Clauses, Is.Empty);
        var row = prepared.CallPreconditions.Single();
        Assert.That(row.MetadataClause, Is.Not.Null);
        Assert.That(prepared.Program.Factory.GetOperationInfo(row.ClauseSite).SourceSpan, Is.Null);
        var detached = Decode(prepared, references);
        Assert.Throws<InvalidDataException>(new Action(() =>
            CompilerTotalCallableArtifactCodec.DecodeShadowBody(prepared.CallableId,
                CompilerTotalCallableArtifactCodec.Encode(prepared)!, TestContext.CurrentContext.CancellationToken)));
        var native = detached.Body;
        var callerInput = type == "string" ? input == null ? "0" : "1" : input;
        var (execution, observations) = Execute(native, callerType, callerInput);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(result)));
        Assert.That(observations, Is.EqualTo(new[] { predicate }));
        var runtimeInput = int.Parse(callerInput!, CultureInfo.InvariantCulture);
        Assert.That(subject.RootMethod.Invoke(null, [runtimeInput]), Is.EqualTo(result));
        Assert.That(JsonSerializer.Serialize(new ClaimManifestBuilder(subject.Compilation).Build().Manifest), Is.EqualTo(baseline));
        Assert.That(Prepare(subject.Compilation, references, false).Callers, Is.Empty);
        await AssertOutcome(detached, false);
    }

    [TestCase("Positive", "int", "1")]
    [TestCase("InRange(1, 10)", "int", "5")]
    [TestCase("NotNull", "string", "\"hello\"")]
    public async Task ConstantArgumentsProveWithoutImportedPremises(string attribute, string type, string argument)
    {
        System.ArgumentNullException.ThrowIfNull(argument);
        System.ArgumentNullException.ThrowIfNull(attribute);
        System.ArgumentNullException.ThrowIfNull(type);
        using var subject = new MetadataTestSubject(
            $"using SharpProof.Attributes; public static class Library {{ public static int Target([{attribute}] {type} value) => 7; }}",
            $"public static class Subject {{ public static int Target(int value) => Library.Target({argument}); }}");
        var references = Capture(subject.Compilation);
        var batch = Prepare(subject.Compilation, references, true);
        Assert.That(batch.Gaps, Is.Empty);
        var detached = Decode(batch.Callers.Single().Body, references);
        Assert.That(detached.Body.Clauses, Is.Empty);
        await AssertOutcome(detached, true);
    }

    [TestCase("return Library.Target(10 / value);", "0", false, 0)]
    [TestCase("if (value == 0) return 7; return Library.Target(value);", "0", true, 0)]
    [TestCase("return Library.Target(value);", "0", false, 1)]
    public void FaultAndUnreachableCallsPreserveExecutionBarrier(string caller, string input, bool returns, int observations)
    {
        System.ArgumentNullException.ThrowIfNull(caller);
        System.ArgumentNullException.ThrowIfNull(input);
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static int Target([Positive] int value) => 10 / value; }",
            $"public static class Subject {{ public static int Target(int value) {{ {caller} }} }}");
        var references = Capture(subject.Compilation);
        var batch = Prepare(subject.Compilation, references, true);
        Assert.That(batch.Gaps, Is.Empty);
        var detached = Decode(batch.Callers.Single().Body, references);
        var (execution, observed) = Execute(detached.Body, "int", input);
        Assert.That(observed, Has.Count.EqualTo(observations));
        Assert.That(execution.Status, Is.EqualTo(returns ? IrProgramExecutionStatus.Returned : IrProgramExecutionStatus.Exception));
        if (returns)
        { Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(7))); }
        else
        {
            Assert.That(execution.Exception!.Kind, Is.EqualTo(IrExceptionKind.DivideByZero));
            var runtime = Assert.Throws<TargetInvocationException>(new Action(() => subject.RootMethod.Invoke(null, [0])));
            Assert.That(runtime!.InnerException, Is.InstanceOf<DivideByZeroException>());
        }
        if (observations == 1)
        { Assert.That(observed.Single(), Is.False, "The marker remains before a fault in the callee body."); }
    }

    [Test]
    public void ArgumentMutationUsesCapturedEarlierValue()
    {
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static int Target([InRange(1, 1)] int first, int second) => first * 10 + second; }",
            "public static class Subject { public static int Target(int value) => Library.Target(value, value = 2); }");
        var references = Capture(subject.Compilation);
        var batch = Prepare(subject.Compilation, references, true);
        Assert.That(batch.Gaps, Is.Empty);
        var detached = Decode(batch.Callers.Single().Body, references);
        var (execution, observed) = Execute(detached.Body, "int", "1");
        Assert.That(observed, Is.EqualTo(SuccessfulObservation));
        Assert.That(execution.ReturnValue!.IntegerNumericValue, Is.EqualTo(new System.Numerics.BigInteger(12)));
        Assert.That(subject.RootMethod.Invoke(null, [1]), Is.EqualTo(12));
    }

    [TestCase("truncated")]
    [TestCase("wrong-constructor")]
    [TestCase("duplicate")]
    [TestCase("reversed-range")]
    [TestCase("lookalike")]
    public void MalformedImportedClaimsDoNotBecomeNativeRows(string mode)
    {
        System.ArgumentNullException.ThrowIfNull(mode);
        using var directory = new TempDirectory("sharpproof-native-metadata-invalid-");
        var path = Path.Combine(directory.FullName, "MetadataTarget.dll");
        File.WriteAllBytes(path, MetadataClosedAttributeEvidenceTests.CreateImage(mode));
        var compilation = CSharpCompilation.Create("MalformedShadowCaller",
            [CSharpSyntaxTree.ParseText("public static class Subject { public static int Target(int value) => MetadataTarget.Read(value, value); }",
                path: "Subject.cs")],
            TestMetadataReferences.WithSharpProof.Add(MetadataReference.CreateFromFile(path)),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        var batch = Prepare(compilation, Capture(compilation), true);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Any(gap => gap is null ? throw new System.ArgumentNullException(nameof(gap)) : (gap.Reason == "UnsupportedBody")), Is.True);
    }

    [Test]
    public void MetadataPreparationRequiresCapturedOwnership()
    {
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static int Target([Positive] int value) => value; }",
            "public static class Subject { public static int Target(int value) => Library.Target(value); }");
        var batch = Prepare(subject.Compilation, null, true);
        Assert.That(batch.Callers, Is.Empty);
        Assert.That(batch.Gaps.Single().Reason, Is.EqualTo("MissingMetadataOwnership"));
    }


    [Test]
    public void OrdinarySourceRowsRemainByteStableWithMetadataOptIn()
    {
        var compilation = TestCompilation.Create("SourceRowsStable", """
            #undef SHARPPROOF_CONTRACTS
            using SharpProof.Attributes;
            public static class Subject {
                public static int Leaf(int value) { Contract.Requires(value > 0); return value + 1; }
                public static int Target(int value) => Leaf(value);
            }
            """);
        var references = Capture(compilation);
        var ordinary = Prepare(compilation, references, false).Callers.Single().Body;
        var optedIn = Prepare(compilation, references, true).Callers.Single().Body;
        Assert.That(ordinary.CallPreconditions.Single().MetadataClause, Is.Null);
        Assert.That(optedIn.CallPreconditions.Single().MetadataClause, Is.Null);
        Assert.That(JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(optedIn)),
            Is.EqualTo(JsonSerializer.Serialize(CompilerTotalCallableArtifactCodec.Encode(ordinary))));
    }

    private static CompilerReferenceSnapshot[] Capture(CSharpCompilation compilation)
    { System.ArgumentNullException.ThrowIfNull(compilation, nameof(compilation)); return CompilerCompilationCapture.CaptureReferences(compilation.References, CompilerCompilationCapture.ReferenceCaptureLimits.Default, TestContext.CurrentContext.CancellationToken); }

    private static CompilerShadowPreparationBatch Prepare(CSharpCompilation compilation,
        CompilerReferenceSnapshot[]? references, bool enabled)
    {
        System.ArgumentNullException.ThrowIfNull(compilation);
        return CompilerTotalCallableLowerer.PrepareShadowCallers(compilation, WorkerFeatureSet.All,
            CompilerCompilationCapture.CaptureTrees(compilation, TestContext.CurrentContext.CancellationToken), references,
            CompilerSpecificationPackProvider.ResolveConfiguration([]), TestContext.CurrentContext.CancellationToken, enabled);
    }

    private static CompilerDecodedShadowBody Decode(CompilerTotalCallablePreparation body, CompilerReferenceSnapshot[] references)
    {
        System.ArgumentNullException.ThrowIfNull(references, nameof(references));
        System.ArgumentNullException.ThrowIfNull(body);
        var artifact = CompilerTotalCallableArtifactCodec.Encode(body)!;
        var detached = JsonSerializer.Deserialize<CompilerTotalCallableArtifact>(JsonSerializer.Serialize(artifact))!;
        return CompilerDecodedShadowBody.Decode(body.CallableId, detached, TestContext.CurrentContext.CancellationToken, references);
    }

    private static (IrProgramExecutionResult Execution, List<bool> Observations) Execute(
        CompilerTotalCallablePreparation body, string type, string? input)
    {
        System.ArgumentNullException.ThrowIfNull(body);
        System.ArgumentNullException.ThrowIfNull(type);
        var factory = body.Program.Factory;
        var entry = body.Parameters.Single().Entry;
        var value = type == "string" ? input == null ? factory.CreateNullValue(factory.StringType) : factory.CreateStringValue(input) :
            factory.CreateIntegerValue(factory.GetVariableInfo(entry).Type, long.Parse(input!, CultureInfo.InvariantCulture));
        var observations = new List<bool>();
        var markers = body.CallPreconditions.Select(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Instruction)).ToHashSet();
        var options = new IrProgramReplayOptions(static _ => null)
        {
            AssignmentObserver = (instruction, assigned, _) =>
            { System.ArgumentNullException.ThrowIfNull(assigned, nameof(assigned)); System.ArgumentNullException.ThrowIfNull(instruction, nameof(instruction)); if (markers.Contains(instruction.Id)) { observations.Add(assigned.Boolean); } }
        };
        var execution = new IrProgramInterpreter(factory).Execute(body.Program, new Dictionary<IrVarId, IrValue> { [entry] = value }, 10000, options);
        Assert.That(execution.ConsumedApproximation, Is.False);
        return (execution, observations);
    }

    private static async Task AssertOutcome(CompilerDecodedShadowBody detached, bool proven)
    {
        System.ArgumentNullException.ThrowIfNull(detached);
        ContainerNativeLibrary.InstallZ3ResolverRequired(typeof(Microsoft.Z3.Context).Assembly);
        var candidate = PassiveCallableArtifactAdapter.EnrollShadow(detached);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var reason, cancellationToken: TestContext.CurrentContext.CancellationToken), Is.True, reason.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var checkedCall = await solver.VerifyCallPreconditionAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(checkedCall.Outcome, proven ? Is.InstanceOf<ProvenOutcome>() : Is.InstanceOf<RefutedOutcome>(), checkedCall.Reason.ToString());
        if (!proven)
        {
            Assert.That(checkedCall.CallPreconditionWitness, Is.Not.Null);
            Assert.That(plan!.ReplayCallPrecondition(0, checkedCall.EntryModel, TestContext.CurrentContext.CancellationToken), Is.EqualTo(checkedCall.CallPreconditionWitness));
        }
    }
}
