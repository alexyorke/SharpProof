using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using SharpProof.Specs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using System.Text.Json.Nodes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeTaskCarrierStorageTests
{
    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public void CallbackWrapperActivationComesFromTheSameEmittedModule(OptimizationLevel optimization, bool configured)
    {
        var (compilation, method, _, _) = Compile(configured, optimization);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var capture = collector.CaptureCallbackActivations(method, "root", cancellation);
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Activations, Is.Not.Null);
        var activations = capture.Activations!;
        Assert.That(activations.RootCallableId, Is.EqualTo("root"));
        Assert.That(activations.ModuleSha256, Is.EqualTo(collector.Capture(method, "root", cancellation).Shapes!.ModuleSha256));
        Assert.That(activations.Forwarders, Has.Length.EqualTo(2));
        Assert.That(activations.WrapperActivationsUpper, Is.EqualTo(1));
        foreach (var forwarder in activations.Forwarders)
        {
            Assert.That(forwarder.MethodToken >> 24, Is.EqualTo(6));
            Assert.That(forwarder.ChildMethodToken >> 24, Is.EqualTo(6));
            Assert.That(forwarder.MethodToken, Is.Not.EqualTo(forwarder.ChildMethodToken));
        }
        var repeated = collector.CaptureCallbackActivations(method, "root", cancellation).Activations!;
        Assert.That(repeated.ModuleSha256, Is.EqualTo(activations.ModuleSha256));
        Assert.That(repeated.Forwarders.ToArray(), Is.EqualTo(activations.Forwarders.ToArray()));
    }

    [Test]
    public void CallbackActivationCaptureRejectsUnsupportedRootsAndHonorsCancellation()
    {
        var (compilation, method, _, _) = Compile(false, OptimizationLevel.Release);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var capture = collector.CaptureCallbackActivations(method, " ", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.Activations, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierRootUnsupported"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = collector.CaptureCallbackActivations(method, "root", cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(7)]
    [TestCase(8)]
    [TestCase(9)]
    [TestCase(128)]
    [TestCase(int.MinValue)]
    [TestCase(int.MaxValue)]
    public void ConstantOnlyCallbacksHaveOneWrapperActivation(int value)
    {
        var literal = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var source = "using System.Threading.Tasks; public static class C {public static Task Root() => " +
            "Task.WhenAll(Task.Run(()=>Child(" + literal + ")),Task.Run(()=>Child(" + literal + "))); static void Child(int n) {} }";
        var compilation = TestCompilation.Create("ConstantCallback", [("Subject.cs", source)]);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var capture = new CompilerTaskCarrierCollector(compilation).CaptureCallbackActivations(method, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Activations!.WrapperActivationsUpper, Is.EqualTo(1));
        Assert.That(capture.Activations.Forwarders, Has.Length.EqualTo(2));
    }

    [TestCase(OptimizationLevel.Debug)]
    [TestCase(OptimizationLevel.Release)]
    public void NamedArgumentCallbacksKeepTheirOwnedForwardTarget(OptimizationLevel optimization)
    {
        const string source = "using System.Threading.Tasks; public static class C {public static Task Root(int n) => " +
            "Task.WhenAll(Task.Run(()=>Child(right:3,left:n)),Task.Run(()=>Child(right:n,left:-128))); static void Child(int left,int right) {} }";
        var initial = TestCompilation.Create("NamedCallback", [("Subject.cs", source)]);
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(optimization));
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var capture = new CompilerTaskCarrierCollector(compilation).CaptureCallbackActivations(method, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Activations!.Forwarders, Has.Length.EqualTo(2));
        Assert.That(capture.Activations.WrapperActivationsUpper, Is.EqualTo(1));
    }

    [TestCase("public static void Probe() {Child(3);}", null)]
    [TestCase("public static void Probe() {}", "TaskCallbackForwarderBodyUnsupported")]
    [TestCase("public static void Probe() {Child(3);Child(4);}", "TaskCallbackForwarderCallUnsupported")]
    [TestCase("public static void Probe() {Console.WriteLine();}", "TaskCallbackForwarderCallUnsupported")]
    [TestCase("public static void Probe() {try {Child(3);} catch(Exception) {}}", "TaskCallbackForwarderBodyUnsupported")]
    [TestCase("public static void Probe() {new object();Child(3);}", "TaskCallbackForwarderBodyUnsupported")]
    [TestCase("public static void Probe() {again: Child(3);goto again;}", "TaskCallbackForwarderBodyUnsupported")]
    public void CallbackBodyValidationRequiresOneOwnedForwardCall(string declaration, string? expected)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var result = ValidateForwarderProbe(declaration);
        Assert.That(result.UnknownReason, Is.EqualTo(expected));
        Assert.That(result.Target.HasValue, Is.EqualTo(expected is null));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CallbackBodyValidationRejectsUnavailableTargetSets(bool invalidToken)
    {
        var result = ValidateForwarderProbe("public static void Probe() {Child(3);}", invalidToken ? 2 : 1);
        Assert.That(result.Target, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskCallbackForwarderTargetUnavailable"));
    }

    private static (int? Target, string? UnknownReason) ValidateForwarderProbe(string declaration, int targetMutation = 0)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var compilation = TestCompilation.Create("ForwarderBodyControl", [("Subject.cs",
            "using System; public static class C {" + declaration + " static void Child(int n) {} }")]);
        using var stream = new MemoryStream();
        Assert.That(compilation.Emit(stream, cancellationToken: TestContext.CurrentContext.CancellationToken).Success, Is.True);
        stream.Position = 0;
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var probe = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition).Single(value => metadata.GetString(value.Name) == "Probe");
        var child = metadata.MethodDefinitions.Single(handle => metadata.GetString(metadata.GetMethodDefinition(handle).Name) == "Child");
        var targets = targetMutation == 1 ? ImmutableHashSet<int>.Empty :
            ImmutableHashSet.Create(targetMutation == 2 ? 0x0a000001 : System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(child));
        return CompilerTaskCarrierCollector.ValidateCallbackForwarder(image.GetMethodBody(probe.RelativeVirtualAddress), targets,
            TestContext.CurrentContext.CancellationToken);
    }

    [TestCase("public static void Probe() {}", false, "TaskRootExceptionBoundaryHandlerUnsupported")]
    [TestCase("public static void Probe() {try {int.Parse(\"0\");} finally {int.Parse(\"1\");}}", false, "TaskRootExceptionBoundaryHandlerUnsupported")]
    [TestCase("public static void Probe() {try {int.Parse(\"0\");} catch(InvalidOperationException) {}}", false, "TaskRootExceptionBoundaryCatchUnsupported")]
    [TestCase("public static void Probe() {try {int.Parse(\"0\");} catch(CustomException) {}}", false, "TaskRootExceptionBoundaryCatchUnsupported")]
    [TestCase("public static void Probe() {try {Helper();} catch(Exception) {}}", false, "TaskRootExceptionBoundaryCallUnsupported")]
    [TestCase("public static unsafe void Probe(delegate* unmanaged<void> p) {try {p();} catch(Exception) {}}", false, "TaskRootExceptionBoundaryCallUnsupported")]
    [TestCase("public static void Probe() {try {int.Parse(\"0\");} catch(Exception) {} new object();}", false, "TaskRootExceptionBoundarySourceEscape")]
    [TestCase("public static void Probe(Exception e) {try {int.Parse(\"0\");} catch(Exception) {} throw e;}", false, "TaskRootExceptionBoundarySourceEscape")]
    [TestCase("public static void Probe() {try {int.Parse(\"0\");} catch(Exception) {} Task.WhenAll(Array.Empty<Task>());}", false, "TaskRootExceptionBoundarySourceEscape")]
    [TestCase("public static void Probe() {try {int.Parse(\"0\");} catch(Exception) {}}", false, "TaskRootExceptionBoundaryCallsIncomplete")]
    [TestCase("public static async Task<int> Probe() => await Task.FromResult(1).ConfigureAwait(false);", true, "TaskRootExceptionBoundaryCallsIncomplete")]
    [TestCase("public static void Probe(int n) {try {switch(n) {case 0: n=7;break;case 1:n=8;break;case 2:n=9;break;case 3:n=10;break;case 4:n=11;break;default:n=12;break;}} catch(Exception) {}}", false, "TaskRootExceptionBoundaryBodyUnsupported")]
    public void EmittedBodyValidatorRejectsUnsupportedBoundaries(string declaration, bool asyncBody, string expected)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(expected);
        Assert.That(ValidateProbeBody(declaration, asyncBody), Is.EqualTo(expected));
    }

    [Test]
    public void EmittedBodyValidatorRejectsOversizedBodiesAndMalformedCallTokens()
    {
        var statements = string.Concat(Enumerable.Range(0, 1500).Select(index =>
            "n += " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + ";"));
        Assert.That(ValidateProbeBody("public static void Probe(int n) {try {" + statements + "} catch(Exception) {}}", false),
            Is.EqualTo("TaskRootExceptionBoundaryBodyUnsupported"));
        Assert.That(ValidateProbeBody("public static void Probe() {try {int.Parse(\"0\");} catch(Exception) {}}", false, corruptCall: true),
            Is.EqualTo("TaskRootExceptionBoundaryEmissionInvalid"));
    }

    [TestCase(1)]
    [TestCase(17)]
    public void EmittedBodyValidatorChecksJoinBudgets(int children)
    {
        Assert.That(ValidateProbeBody("public static void Probe() {}", false, children), Is.EqualTo("TaskRootExceptionBoundaryCallsIncomplete"));
    }

    [Test]
    public void EmittedBodyValidatorHonorsCancellation()
    {
        Action check = () => { _ = ValidateProbeBody("public static void Probe() {}", false, cancel: true); };
        Assert.Throws<OperationCanceledException>(check);
    }

    private static string? ValidateProbeBody(string declaration, bool asyncBody, int children = 2, bool corruptCall = false, bool cancel = false)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var source = "using System; using System.Threading.Tasks; public static class C {" + declaration +
            " static void Helper() {} } public sealed class CustomException : Exception {}";
        var initial = TestCompilation.Create("BoundaryBodyControl", [("Subject.cs", source)], allowUnsafe: true);
        var compilation = initial.WithOptions(initial.Options.WithAllowUnsafe(true).WithOptimizationLevel(OptimizationLevel.Debug));
        TestCompilation.AssertNoErrors(compilation);
        using var emitted = new MemoryStream();
        var emission = compilation.Emit(emitted, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(emission.Success, Is.True);
        var bytes = emitted.ToArray();
        if (corruptCall)
        { CorruptProbeCall(bytes); }
        using var stream = new MemoryStream(bytes, false);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
            .Single(value => metadata.GetString(value.Name) == (asyncBody ? "MoveNext" : "Probe"));
        var body = image.GetMethodBody(method.RelativeVirtualAddress);
        using var cancellation = new CancellationTokenSource();
        if (cancel)
        { cancellation.Cancel(); }
        return CompilerTaskCarrierCollector.ValidateExceptionBoundary(metadata, body,
            compilation.GetTypeByMetadataName("System.Exception")!.ContainingAssembly.Identity, children,
            cancel ? cancellation.Token : TestContext.CurrentContext.CancellationToken);
    }

    private static void CorruptProbeCall(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, false);
        using var image = new PEReader(stream);
        var metadata = image.GetMetadataReader();
        var method = metadata.MethodDefinitions.Select(metadata.GetMethodDefinition)
            .Single(value => metadata.GetString(value.Name) == "Probe");
        var section = image.PEHeaders.SectionHeaders.Single(value => method.RelativeVirtualAddress >= value.VirtualAddress &&
            method.RelativeVirtualAddress < value.VirtualAddress + value.SizeOfRawData);
        var offset = section.PointerToRawData + method.RelativeVirtualAddress - section.VirtualAddress;
        Assert.That(bytes[offset] & 3, Is.EqualTo(3), "This fixture requires a fat method header.");
        var headerBytes = (bytes[offset + 1] >> 4) * 4;
        var code = image.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
        var cursor = 0;
        while (CompilerScalarIlDecoder.TryReadMetadataInstruction(code, ref cursor, out var instruction))
        {
            if (instruction.RawCode is "Call" or "Callvirt")
            {
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + headerBytes + instruction.Offset + 1, 4), -1);
                return;
            }
        }
        Assert.Fail("The emitted fixture must contain a call operand to corrupt.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BoundaryOwnershipReusesCarrierAndLayoutAdmission(bool incompatibleLayout)
    {
        var (shapes, projection, root) = Fixture(2);
        if (!incompatibleLayout)
        { shapes = shapes with { RootCallableId = "foreign" }; }
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var reason = NativeTaskCarrierStorageVerifier.CheckExceptionBoundary(shapes, projection, root,
            incompatibleLayout ? new("CoreCLR", "8.0.32", "arm64", 8) : Layout(), cancellation);
        Assert.That(reason, Is.EqualTo(incompatibleLayout ? "IncompatibleResourceRuntimeLayout" : "TaskCompilerCarrierOwnershipUnproved"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action check = () => { _ = NativeTaskCarrierStorageVerifier.CheckExceptionBoundary(shapes, projection, root, Layout(), canceled.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [TestCase(OptimizationLevel.Debug)]
    [TestCase(OptimizationLevel.Release)]
    public void EmittedBoundarySurvivesTransportAndNativeOwnershipChecks(OptimizationLevel optimization)
    {
        var (compilation, method, projection, root) = Compile(true, optimization);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var collector = new CompilerTaskCarrierCollector(compilation);
        var capture = collector.Capture(method, "root", cancellation);
        projection = projection with
        {
            Carriers = capture with
            {
                Shapes = capture.Shapes! with
                { RootExceptionBoundary = collector.CaptureExceptionBoundary(method, "root", cancellation) }
            }
        };
        var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
        Assert.That(decoded.Carriers!.Shapes!.RootExceptionBoundary, Is.EqualTo(projection.Carriers.Shapes!.RootExceptionBoundary));
        Assert.That(NativeTaskCarrierStorageVerifier.CheckExceptionBoundary(decoded.Carriers.Shapes, decoded, root, Layout(), cancellation), Is.Null);
    }

    [TestCase("root")]
    [TestCase("module")]
    [TestCase("state")]
    [TestCase("token")]
    [TestCase("empty-token")]
    [TestCase("offset")]
    [TestCase("length")]
    [TestCase("overflow")]
    [TestCase("both")]
    public void BoundaryTransportAndNativeChecksRejectUnownedEvidence(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, root) = Fixture(2);
        var boundary = new CompilerTaskRootExceptionBoundary("root", shapes.ModuleSha256, shapes.StateMachine!.MetadataName, 0x06000001, 0, 100);
        boundary = mutation switch
        {
            "root" => boundary with { RootCallableId = "foreign" },
            "module" => boundary with { ModuleSha256 = new string('b', 64) },
            "state" => boundary with { StateMachineMetadataName = "foreign" },
            "token" => boundary with { MethodToken = 0x02000001 },
            "empty-token" => boundary with { MethodToken = 0x06000000 },
            "offset" => boundary with { TryOffset = -1 },
            "length" => boundary with { TryLength = 0 },
            "overflow" => boundary with { TryOffset = int.MaxValue, TryLength = int.MaxValue },
            "both" => boundary,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        shapes = shapes with { RootExceptionBoundary = new(boundary, mutation == "both" ? "Unsupported" : null) };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(NativeTaskCarrierStorageVerifier.CheckExceptionBoundary(shapes, projection, root, Layout(), cancellation), Is.EqualTo("TaskRootExceptionBoundaryInvalid"));
        Action encode = () => { _ = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation); };
        Assert.Throws<InvalidDataException>(encode);
    }

    [TestCase(null, "TaskRootExceptionBoundaryUnavailable")]
    [TestCase("UnsupportedBoundary", "UnsupportedBoundary")]
    [TestCase("", "TaskRootExceptionBoundaryInvalid")]
    public void MissingOrUnknownBoundariesNeverBecomeSuccess(string? reason, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var (shapes, projection, root) = Fixture(2);
        shapes = shapes with { RootExceptionBoundary = reason is null ? null : new(null, reason) };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(NativeTaskCarrierStorageVerifier.CheckExceptionBoundary(shapes, projection, root, Layout(), cancellation), Is.EqualTo(expected));
        if (reason is null || reason.Length != 0)
        {
            var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
            Assert.That(decoded.Carriers!.Shapes!.RootExceptionBoundary, Is.EqualTo(shapes.RootExceptionBoundary));
        }
    }

    [TestCase("missing")]
    [TestCase("extra")]
    [TestCase("kind")]
    [TestCase("neither")]
    [TestCase("long-reason")]
    [TestCase("old-version")]
    public void BoundaryDecoderRejectsMalformedAndOldTransport(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, root) = Fixture(2);
        shapes = shapes with { RootExceptionBoundary = new(null, "UnsupportedBoundary") };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var json = JsonNode.Parse(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation))!.AsObject();
        var evidence = json["carriers"]!["shapes"]!.AsObject();
        var boundary = evidence["boundary"]!.AsObject();
        switch (mutation)
        {
            case "missing":
                evidence.Remove("boundary");
                break;
            case "extra":
                boundary["extra"] = true;
                break;
            case "kind":
                evidence["boundary"] = true;
                break;
            case "neither":
                boundary["unknown"] = null;
                break;
            case "long-reason":
                boundary["unknown"] = new string('x', 513);
                break;
            case "old-version":
                json["version"] = CompilerForkJoinProjectionCodec.Version - 1;
                break;
        }
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), root, cancellation); };
        Assert.Throws<InvalidDataException>(decode);
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Release, true)]
    public void AsyncRootBoundaryComesFromTheSameEmittedModule(OptimizationLevel optimization, bool captureCarriersFirst)
    {
        var (compilation, method, _, _) = Compile(true, optimization);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var before = captureCarriersFirst ? collector.Capture(method, "root", cancellation).Shapes : null;
        var capture = collector.CaptureExceptionBoundary(method, "root", cancellation);
        Assert.That(capture.Boundary, Is.Not.Null, capture.UnknownReason);
        var boundary = capture.Boundary!;
        var shapes = before ?? collector.Capture(method, "root", cancellation).Shapes!;
        Assert.That(boundary.RootCallableId, Is.EqualTo("root"));
        Assert.That(boundary.ModuleSha256, Is.EqualTo(shapes.ModuleSha256));
        Assert.That(boundary.StateMachineMetadataName, Is.EqualTo(shapes.StateMachine!.MetadataName));
        Assert.That(boundary.MethodToken >> 24, Is.EqualTo(6));
        Assert.That(boundary.TryOffset, Is.GreaterThanOrEqualTo(0));
        Assert.That(boundary.TryLength, Is.GreaterThan(0));
        Assert.That(collector.CaptureExceptionBoundary(method, "root", cancellation).Boundary, Is.EqualTo(boundary));
    }

    [Test]
    public void SynchronousTaskRootsCannotClaimALocalAsyncCatchBoundary()
    {
        var (compilation, method, _, _) = Compile(false, OptimizationLevel.Release);
        var capture = new CompilerTaskCarrierCollector(compilation).CaptureExceptionBoundary(method, "root",
            TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.Boundary, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskRootExceptionBoundaryAsyncRequired"));
    }

    [Test]
    public void ExceptionBoundaryCaptureRejectsUnsupportedRootsAndHonorsCancellation()
    {
        var (compilation, method, _, _) = Compile(true, OptimizationLevel.Release);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var capture = collector.CaptureExceptionBoundary(method, " ", TestContext.CurrentContext.CancellationToken);
        Assert.That(capture.Boundary, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskCarrierRootUnsupported"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action canceled = () => { _ = collector.CaptureExceptionBoundary(method, "root", cancellation.Token); };
        Assert.Throws<OperationCanceledException>(canceled);
    }

    [TestCase(0, 224L, 0)]
    [TestCase(1, 272L, 1)]
    [TestCase(2, 224L, 4)]
    public void DerivesDirectCarriersAndSeparatesEmbeddedState(int stateKind, long expected, int stateFields)
    {
        var (shapes, projection, root) = Fixture(stateKind);
        var result = NativeTaskCarrierStorageVerifier.Check(shapes, projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.DirectUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 0,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(result.AsyncBoxStateFields, Has.Length.EqualTo(stateFields));
        if (stateKind == 1)
        { Assert.That(result.AsyncBoxStateFields[0], Is.EqualTo(ResourceStorageKind.Reference)); }
    }

    [TestCase("owner")]
    [TestCase("hash")]
    [TestCase("compiler")]
    [TestCase("compiler-length")]
    [TestCase("few-callbacks")]
    [TestCase("many-callbacks")]
    [TestCase("default-frames")]
    [TestCase("excess-frames")]
    [TestCase("null-frame")]
    [TestCase("value-frame")]
    [TestCase("default-fields")]
    [TestCase("unknown-field")]
    [TestCase("invalid-field")]
    [TestCase("missing-name")]
    [TestCase("long-name")]
    [TestCase("fields-limit")]
    [TestCase("aggregate-limit")]
    [TestCase("state-kind")]
    [TestCase("bad-state")]
    public void InvalidLaterCarriersNeverPublishPartialStorage(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, root) = Fixture(0);
        var frame = shapes.CallbackFrames[1];
        shapes = mutation switch
        {
            "owner" => shapes with { RootCallableId = "foreign" },
            "hash" => shapes with { ModuleSha256 = "bad" },
            "compiler" => shapes with { CompilerIdentity = " " },
            "compiler-length" => shapes with { CompilerIdentity = new string('x', 513) },
            "few-callbacks" => shapes with { MatchedCallbacks = 1 },
            "many-callbacks" => shapes with { MatchedCallbacks = 65 },
            "default-frames" => shapes with { CallbackFrames = default },
            "excess-frames" => shapes with { CallbackFrames = [frame, frame, frame] },
            "null-frame" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], null!] },
            "value-frame" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], frame with { IsValueType = true }] },
            "default-fields" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], frame with { Fields = default }] },
            "unknown-field" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], frame with { Fields = [ResourceStorageKind.Unspecified] }] },
            "invalid-field" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], frame with { Fields = [(ResourceStorageKind)1000] }] },
            "missing-name" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], frame with { MetadataName = " " }] },
            "long-name" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], frame with { MetadataName = new string('x', 4097) }] },
            "fields-limit" => shapes with { CallbackFrames = [shapes.CallbackFrames[0], frame with { Fields = Enumerable.Repeat(ResourceStorageKind.Byte, 1025).ToImmutableArray() }] },
            "aggregate-limit" => shapes with { MatchedCallbacks = 5, CallbackFrames = Enumerable.Repeat(frame with { Fields = Enumerable.Repeat(ResourceStorageKind.Byte, 1024).ToImmutableArray() }, 5).ToImmutableArray() },
            "state-kind" => shapes with { StateMachine = frame },
            "bad-state" => shapes with { StateMachine = frame with { Fields = [ResourceStorageKind.Unspecified] } },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        if (mutation == "bad-state")
        { projection = projection with { ConfiguredAwait = true }; }
        var result = NativeTaskCarrierStorageVerifier.Check(shapes, projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.DirectUpper, Is.Null);
        Assert.That(result.AsyncBoxStateFields, Is.Empty);
        Assert.That(result.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RequiresCompatibleLayoutForEveryExecutionKind(bool configured)
    {
        var (shapes, projection, root) = Fixture(configured ? 2 : 0);
        var result = NativeTaskCarrierStorageVerifier.Check(shapes, projection, root,
            new("CoreCLR", "8.0.32", "arm64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.DirectUpper, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
    }

    [Test]
    public void CancellationPropagates()
    {
        var (shapes, projection, root) = Fixture(0);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = NativeTaskCarrierStorageVerifier.Check(shapes, projection, root, Layout(), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public void ChecksActualCompilerEmission(OptimizationLevel optimization, bool configured)
    {
        var (compilation, method, projection, root) = Compile(configured, optimization);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var capture = new CompilerTaskCarrierCollector(compilation).Capture(method, "root", cancellation);
        Assert.That(capture.UnknownReason, Is.Null);
        projection = projection with { Carriers = capture };
        var payload = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation);
        var decoded = CompilerForkJoinProjectionCodec.Decode(payload, root, cancellation);
        Assert.That(decoded.Carriers!.Shapes!.ModuleSha256, Is.EqualTo(capture.Shapes!.ModuleSha256));
        Assert.That(decoded.Carriers.UnknownReason, Is.Null);
        var result = NativeTaskCarrierStorageVerifier.Check(decoded.Carriers.Shapes, decoded, root, Layout(), cancellation);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.DirectUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 0, cancellation),
            Is.GreaterThanOrEqualTo(new System.Numerics.BigInteger(216)));
        Assert.That(result.AsyncBoxStateFields.IsEmpty, Is.EqualTo(!configured));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CaptureUnknownSurvivesTransportWithoutDiscardingSourceCosts(bool configured)
    {
        var (_, projection, root) = Fixture(configured ? 2 : 0);
        projection = projection with { Carriers = new(null, "TaskCarrierEmissionFailed") };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
        Assert.That(decoded.Carriers!.Shapes, Is.Null);
        Assert.That(decoded.Carriers.UnknownReason, Is.EqualTo("TaskCarrierEmissionFailed"));
        Assert.That(NativeForkJoinSourceCostVerifier.Check(decoded, root, cancellation).HasSourceCosts, Is.True);
    }

    [TestCase("owner")]
    [TestCase("module")]
    [TestCase("compiler")]
    [TestCase("compiler-length")]
    [TestCase("callback-kind")]
    [TestCase("few-callbacks")]
    [TestCase("many-callbacks")]
    [TestCase("frames-count")]
    [TestCase("null-frame")]
    [TestCase("value-frame")]
    [TestCase("type-name")]
    [TestCase("type-kind")]
    [TestCase("fields-kind")]
    [TestCase("field-kind")]
    [TestCase("invalid-field")]
    [TestCase("fields-limit")]
    [TestCase("aggregate-limit")]
    [TestCase("state-kind")]
    [TestCase("missing-state")]
    [TestCase("extra-shape")]
    [TestCase("extra-capture")]
    [TestCase("both")]
    [TestCase("neither")]
    [TestCase("unknown-empty")]
    [TestCase("unknown-kind")]
    [TestCase("missing-carriers")]
    [TestCase("carrier-kind")]
    [TestCase("duplicate")]
    public void DetachedCarrierTransportRejectsMalformedEvidence(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, root) = Fixture(0);
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var json = JsonNode.Parse(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation))!.AsObject();
        var capture = json["carriers"]!.AsObject();
        var evidence = capture["shapes"]!.AsObject();
        var frames = evidence["frames"]!.AsArray();
        var frame = frames[1]!.AsObject();
        switch (mutation)
        {
            case "owner":
                evidence["root"] = "foreign";
                break;
            case "module":
                evidence["module"] = "bad";
                break;
            case "compiler":
                evidence["compiler"] = " ";
                break;
            case "compiler-length":
                evidence["compiler"] = new string('x', 513);
                break;
            case "callback-kind":
                evidence["callbacks"] = "2";
                break;
            case "few-callbacks":
                evidence["callbacks"] = 1;
                break;
            case "many-callbacks":
                evidence["callbacks"] = 65;
                break;
            case "frames-count":
                frames.Add(frame.DeepClone());
                break;
            case "null-frame":
                frames[1] = null;
                break;
            case "value-frame":
                frame["value"] = true;
                break;
            case "type-name":
                frame["name"] = " ";
                break;
            case "type-kind":
                frame["value"] = "false";
                break;
            case "fields-kind":
                frame["fields"] = null;
                break;
            case "field-kind":
                frame["fields"]![0] = "1";
                break;
            case "invalid-field":
                frame["fields"]![0] = 0;
                break;
            case "fields-limit":
                frame["fields"] = FieldArray(1025);
                break;
            case "aggregate-limit":
                evidence["callbacks"] = 5;
                var repeated = new JsonArray();
                for (var index = 0; index < 5; index++)
                { var clone = frame.DeepClone(); clone["fields"] = FieldArray(1024); repeated.Add(clone); }
                evidence["frames"] = repeated;
                break;
            case "state-kind":
                evidence["state"] = frame.DeepClone();
                break;
            case "missing-state":
                evidence.Remove("state");
                break;
            case "extra-shape":
                evidence["extra"] = true;
                break;
            case "extra-capture":
                capture["extra"] = true;
                break;
            case "both":
                capture["unknown"] = "TaskCarrierEmissionFailed";
                break;
            case "neither":
                capture["shapes"] = null;
                break;
            case "unknown-empty":
                capture["shapes"] = null;
                capture["unknown"] = "";
                break;
            case "unknown-kind":
                capture["shapes"] = null;
                capture["unknown"] = 1;
                break;
            case "missing-carriers":
                json.Remove("carriers");
                break;
            case "carrier-kind":
                json["carriers"] = true;
                break;
        }
        var text = json.ToJsonString();
        if (mutation == "duplicate")
        { text = text.Replace("\"callbacks\":2", "\"callbacks\":2,\"callbacks\":2", StringComparison.Ordinal); }
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode(System.Text.Encoding.UTF8.GetBytes(text), root, cancellation); };
        Assert.Throws<InvalidDataException>(decode);
    }

    private static JsonArray FieldArray(int length)
    {
        return new(Enumerable.Range(0, length).Select(static index => (JsonNode?)JsonValue.Create(3)).ToArray());
    }

    [TestCase(0, 456L)]
    [TestCase(1, 720L)]
    [TestCase(2, 696L)]
    public void ComposesReviewedRuntimeAndCompilerStorage(int stateKind, long expected)
    {
        var (shapes, projection, root) = Fixture(stateKind);
        projection = projection with { Carriers = new(shapes, null) };
        var result = NativeTaskStorageVerifier.Check(projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.PeakUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 0,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(result.RetainedUpper, Is.EqualTo(result.PeakUpper));
    }

    [TestCase(7, 896L)]
    [TestCase(0, 448L)]
    [TestCase(-5, 448L)]
    public void ComposedPeakIncludesEveryChildButRetainedOutputExcludesPrivateArrays(int inputSize, long expectedPeak)
    {
        var (compilation, method, projection, root) = Compile(false, OptimizationLevel.Release,
            "for(int i=0;i<n;i++){var data=new int[2];}");
        var cancellation = TestContext.CurrentContext.CancellationToken;
        projection = projection with { Carriers = new CompilerTaskCarrierCollector(compilation).Capture(method, "root", cancellation) };
        var result = NativeTaskStorageVerifier.Check(projection, root, Layout(), cancellation);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.PeakUpper!.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) : inputSize, cancellation),
            Is.EqualTo(new System.Numerics.BigInteger(expectedPeak)));
        Assert.That(result.RetainedUpper!.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) : inputSize, cancellation),
            Is.EqualTo(new System.Numerics.BigInteger(448)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingCompilerEvidenceCannotBecomeAZeroRuntimeBound(bool captureFailed)
    {
        var (_, projection, root) = Fixture(0);
        projection = projection with { Carriers = captureFailed ? new(null, new string('x', 512)) : null };
        var result = NativeTaskStorageVerifier.Check(projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.PeakUpper, Is.Null);
        Assert.That(result.RetainedUpper, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskCompilerCarrierEvidenceUnavailable"));
    }

    [Test]
    public void CarrierCompositionDoesNotPublishAChildBoundThatExhaustsTheExpressionBudget()
    {
        var (shapes, projection, root) = Fixture(0);
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var terms = Enumerable.Repeat(input, 2045).ToList();
        while (terms.Count > 1)
        {
            var next = new List<ResourceExpression>();
            for (var index = 0; index < terms.Count; index += 2)
            { next.Add(index + 1 < terms.Count ? ResourceExpression.Add(terms[index], terms[index + 1]) : terms[index]); }
            terms = next;
        }
        var length = ResourceExpression.Difference(terms[0], 1);
        Assert.That(length.NodeCount, Is.EqualTo(4090));
        var child = projection.Children[0];
        var allocation = ResourceProgram.TypedArray(ResourceAllocationShape.Array(ResourceStorageKind.Word64, length));
        projection = projection with
        {
            Carriers = new(shapes, null),
            Children = [child with { Cost = allocation }, projection.Children[1]]
        };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var children = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, root, Layout(), cancellation);
        Assert.That(children.UnknownReason, Is.Null);
        Assert.That(children.CumulativeUpper!.NodeCount, Is.EqualTo(ResourceExpression.MaximumNodes - 1));
        var result = NativeTaskStorageVerifier.Check(projection, root, Layout(), cancellation);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskRuntimeStorageExpressionBudgetExceeded"));
        Assert.That(result.PeakUpper, Is.Null);
        Assert.That(result.RetainedUpper, Is.Null);
    }

    [Test]
    public void AChildWithArrayCountsButNoStorageShapeCannotSupplyRootLifetimeBytes()
    {
        var (shapes, projection, root) = Fixture(0);
        var child = projection.Children[1];
        projection = projection with
        {
            Carriers = new(shapes, null),
            Children = [projection.Children[0], child with { Cost = ResourceProgram.Array(ResourceExpression.Literal(2)) }]
        };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(NativeForkJoinSourceCostVerifier.Check(projection, root, cancellation).HasSourceCosts, Is.True);
        var result = NativeTaskStorageVerifier.Check(projection, root, Layout(), cancellation);
        Assert.That(result.UnknownReason, Is.EqualTo("MissingResourceAllocationStorage"));
        Assert.That(result.PeakUpper, Is.Null);
        Assert.That(result.RetainedUpper, Is.Null);
    }

    [Test]
    public void OversizedEmbeddedStateReturnsUnknownWithoutAPartialCarrierBound()
    {
        var (shapes, projection, root) = Fixture(2);
        shapes = shapes with { StateMachine = shapes.StateMachine! with { Fields = Enumerable.Repeat(ResourceStorageKind.Word32, 1017).ToImmutableArray() } };
        projection = projection with { Carriers = new(shapes, null) };
        var result = NativeTaskStorageVerifier.Check(projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.PeakUpper, Is.Null);
        Assert.That(result.RetainedUpper, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskRuntimeStorageFieldBudgetExceeded"));
    }

    [Test]
    public void ComposedStorageHonorsCancellation()
    {
        var (shapes, projection, root) = Fixture(0);
        projection = projection with { Carriers = new(shapes, null) };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = NativeTaskStorageVerifier.Check(projection, root, Layout(), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [Test]
    public void RuntimeStorageCatalogHasDistinctOwnedNormalCompletionPremises()
    {
        Assert.That(ResourceTaskStorageCatalog.ContentSha256, Has.Length.EqualTo(64));
        Assert.That(ResourceTaskStorageCatalog.ConfiguredContentSha256, Is.Not.EqualTo(ResourceTaskStorageCatalog.ContentSha256));
        var id = ResourceTaskStorageCatalog.AssumptionId("root", "assembly", false);
        Assert.That(ResourceTaskStorageCatalog.AssumptionId("foreign", "assembly", false), Is.Not.EqualTo(id));
        Assert.That(ResourceTaskStorageCatalog.AssumptionId("root", "foreign", false), Is.Not.EqualTo(id));
        Assert.That(ResourceTaskStorageCatalog.AssumptionId("root", "assembly", true), Is.Not.EqualTo(id));
        Assert.That(id, Does.Contain(ResourceTaskStorageCatalog.Witness));
    }

    [Test]
    public void ReviewedCarrierFieldsMatchPinnedRuntimeImplementationMetadata()
    {
        using var stream = File.OpenRead("/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.32/System.Private.CoreLib.dll");
        using var image = new PEReader(stream);
        var reader = image.GetMetadataReader();
        var task = RuntimeType(reader, "System.Threading.Tasks", "Task");
        var taskFields = InstanceFields(reader, task);
        string[] expectedTaskFields = [
            "m_taskId", "m_action", "m_stateObject", "m_taskScheduler", "m_stateFlags", "m_continuationObject", "m_contingentProperties"];
        Assert.That(taskFields.Select(static field => field.Name).Order(StringComparer.Ordinal), Is.EqualTo(expectedTaskFields.Order(StringComparer.Ordinal)));
        Assert.That(taskFields.Select(static field => field.Kind == 0x08 ? ResourceStorageKind.Word32 :
            field.Kind is 0x12 or 0x1c ? ResourceStorageKind.Reference : ResourceStorageKind.Unspecified).Order(),
            Is.EqualTo(ResourceTaskStorageCatalog.TaskFields.Order()));
        var join = task.GetNestedTypes().Select(reader.GetTypeDefinition).Single(type => reader.GetString(type.Name) == "WhenAllPromise");
        Assert.That(InstanceFields(reader, join).Select(static field => field.Kind).Order(), Is.EqualTo(new byte[] { 0x08, 0x1c }));
        Assert.That(ResourceTaskStorageCatalog.JoinFields.Skip(ResourceTaskStorageCatalog.TaskFields.Length).Order(),
            Is.EqualTo(new[] { ResourceStorageKind.Word32, ResourceStorageKind.Reference }.Order()));
        Assert.That(InstanceFields(reader, RuntimeType(reader, "System.Threading.Tasks", "Task`1")),
            Is.EqualTo(new[] { ("m_result", (byte)0x13) }));
        var voidResult = RuntimeType(reader, "System.Threading.Tasks", "VoidTaskResult");
        Assert.That(InstanceFields(reader, voidResult), Is.Empty);
        Assert.That(voidResult.GetLayout().Size, Is.EqualTo(1));
        var builder = RuntimeType(reader, "System.Runtime.CompilerServices", "AsyncTaskMethodBuilder`1");
        var box = builder.GetNestedTypes().Select(reader.GetTypeDefinition).Single(type => reader.GetString(type.Name) == "AsyncStateMachineBox`1");
        Assert.That(InstanceFields(reader, box), Is.EqualTo(new[] { ("StateMachine", (byte)0x13) }));
    }

    [TestCase(2, false, 2176L)]
    [TestCase(3, false, 3432L)]
    [TestCase(4, false, 4424L)]
    [TestCase(5, false, 6328L)]
    [TestCase(8, false, 9760L)]
    [TestCase(9, false, 13704L)]
    [TestCase(16, false, 23672L)]
    [TestCase(2, true, 2544L)]
    [TestCase(16, true, 24040L)]
    public void FaultCarrierPrefixesIncludeGrowthAndLosingPublicationCandidates(int count, bool configured, long expected)
    {
        var (projection, root) = FaultProjection(count, configured);
        var result = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.BookkeepingUpper!.Constant, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(result.TaskTraceCaptureCountUpper, Is.EqualTo(2 * count + (configured ? 1 : 0)));
        Assert.That(result.TaskTraceRestoreCountUpper, Is.EqualTo(configured ? 1 : 0));
        Assert.That(result.TaskDispatchArrayCopiesUpper, Is.EqualTo(4 * count + (configured ? 4 : 0)));
        Assert.That(result.RequiredTraceCopyModel, Is.EqualTo(NativeTaskFaultCarrierStorageVerifier.TraceCopyModel));
        Assert.That(result.ContextTraceCaptureCountUpper, Is.EqualTo(count + (configured ? 1 : 0)));
        Assert.That(result.ContextTraceRestoreCountUpper, Is.EqualTo(count + (configured ? 1 : 0)));
        Assert.That(result.CombinedDispatchArrayCopiesUpper, Is.EqualTo(8 * count + (configured ? 8 : 0)));
        Assert.That(result.RequiredCombinedTraceCopyModel, Is.EqualTo(NativeTaskFaultCarrierStorageVerifier.CombinedTraceCopyModel));
    }

    [TestCase(2, false, 1136L)]
    [TestCase(2, true, 1352L)]
    [TestCase(8, false, 3920L)]
    [TestCase(8, true, 4136L)]
    [TestCase(16, false, 7632L)]
    [TestCase(16, true, 7848L)]
    public void ClosedNotificationPrefixesIncludeBothRecordingSitesAndEveryHolder(int count, bool configured, long expected)
    {
        var (projection, root) = FaultProjection(count, configured);
        var check = NativeTaskFaultNotificationStorageVerifier.CheckJoinedPrefixes(projection, root, Layout(),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.PrefixUpper!.Constant, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultNotificationCatalog.LifecycleModel));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ClosedNotificationPrefixesRejectLayoutOrLaterChildOwnershipFailures(bool badLayout)
    {
        var (projection, root) = FaultProjection(2, false);
        if (!badLayout)
        {
            var child = projection.Children[1];
            projection = projection with
            {
                Children = projection.Children.SetItem(1,
                child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } })
            };
        }
        var layout = badLayout ? new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x86", 4) : Layout();
        var check = NativeTaskFaultNotificationStorageVerifier.CheckJoinedPrefixes(projection, root, layout,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.PrefixUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo(badLayout ? "IncompatibleResourceRuntimeLayout" : "ForkJoinChildOwnershipInvalid"));
    }

    [Test]
    public void ClosedNotificationPrefixesHonorCancellation()
    {
        var (projection, root) = FaultProjection(2, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = NativeTaskFaultNotificationStorageVerifier.CheckJoinedPrefixes(projection, root, Layout(), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [TestCase(1)]
    [TestCase(17)]
    public void FaultCarrierPrefixesRejectCountsOutsideTheOwnedJoinDomain(int count)
    {
        var (projection, root) = FaultProjection(count, false);
        var result = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.BookkeepingUpper, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ForkJoinRootOwnershipInvalid"));
        Assert.That(result.TaskTraceCaptureCountUpper, Is.Null);
        Assert.That(result.TaskTraceRestoreCountUpper, Is.Null);
        Assert.That(result.TaskDispatchArrayCopiesUpper, Is.Null);
        Assert.That(result.RequiredTraceCopyModel, Is.Null);
        Assert.That(result.ContextTraceCaptureCountUpper, Is.Null);
        Assert.That(result.ContextTraceRestoreCountUpper, Is.Null);
        Assert.That(result.CombinedDispatchArrayCopiesUpper, Is.Null);
        Assert.That(result.RequiredCombinedTraceCopyModel, Is.Null);
    }

    [Test]
    public void FaultCarrierPrefixesRejectIncompatibleLayoutsAndUnownedLaterChildren()
    {
        var (projection, root) = FaultProjection(2, false);
        var incompatible = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, new("CoreCLR", "8.0.32", "x86", 4),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(incompatible.BookkeepingUpper, Is.Null);
        Assert.That(incompatible.TaskDispatchArrayCopiesUpper, Is.Null);
        Assert.That(incompatible.RequiredTraceCopyModel, Is.Null);
        Assert.That(incompatible.CombinedDispatchArrayCopiesUpper, Is.Null);
        Assert.That(incompatible.RequiredCombinedTraceCopyModel, Is.Null);
        Assert.That(incompatible.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        var child = projection.Children[1];
        projection = projection with { Children = [projection.Children[0], child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } }] };
        var unowned = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(unowned.BookkeepingUpper, Is.Null);
        Assert.That(unowned.TaskDispatchArrayCopiesUpper, Is.Null);
        Assert.That(unowned.RequiredTraceCopyModel, Is.Null);
        Assert.That(unowned.CombinedDispatchArrayCopiesUpper, Is.Null);
        Assert.That(unowned.RequiredCombinedTraceCopyModel, Is.Null);
        Assert.That(unowned.UnknownReason, Is.EqualTo("ForkJoinChildOwnershipInvalid"));
    }

    [Test]
    public void FaultCarrierPrefixesPreserveCancellation()
    {
        var (projection, root) = FaultProjection(2, false);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, Layout(), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [Test]
    public void FaultCarrierFieldsMatchPinnedRuntimeImplementationMetadata()
    {
        using var stream = File.OpenRead("/usr/share/dotnet/shared/Microsoft.NETCore.App/8.0.32/System.Private.CoreLib.dll");
        using var image = new PEReader(stream);
        var reader = image.GetMetadataReader();
        var task = RuntimeType(reader, "System.Threading.Tasks", "Task");
        var contingent = task.GetNestedTypes().Select(reader.GetTypeDefinition).Single(type => reader.GetString(type.Name) == "ContingentProperties");
        (string, byte)[] expectedContingent = [
            ("m_capturedContext", 0x12), ("m_completionEvent", 0x12), ("m_exceptionsHolder", 0x12), ("m_cancellationToken", 0x11),
            ("m_cancellationRegistration", 0x15), ("m_internalCancellationRequested", 0x08), ("m_completionCountdown", 0x08),
            ("m_exceptionalChildren", 0x15), ("m_parent", 0x12)];
        Assert.That(InstanceFields(reader, contingent), Is.EqualTo(expectedContingent));
        (string, byte)[] expectedToken = [("_source", 0x12)];
        Assert.That(InstanceFields(reader, RuntimeType(reader, "System.Threading", "CancellationToken")), Is.EqualTo(expectedToken));
        (string, byte)[] expectedHolder = [("m_task", 0x12), ("m_faultExceptions", 0x15), ("m_cancellationException", 0x12), ("m_isHandled", 0x02)];
        Assert.That(InstanceFields(reader, RuntimeType(reader, "System.Threading.Tasks", "TaskExceptionHolder")), Is.EqualTo(expectedHolder));
        (string, byte)[] expectedDispatch = [("_exception", 0x12), ("_dispatchState", 0x11)];
        Assert.That(InstanceFields(reader, RuntimeType(reader, "System.Runtime.ExceptionServices", "ExceptionDispatchInfo")), Is.EqualTo(expectedDispatch));
        var exception = RuntimeType(reader, "System", "Exception");
        var state = exception.GetNestedTypes().Select(reader.GetTypeDefinition).Single(type => reader.GetString(type.Name) == "DispatchState");
        (string, byte)[] expectedState = [("StackTrace", 0x1d), ("DynamicMethods", 0x1d), ("RemoteStackTrace", 0x0e),
            ("IpForWatsonBuckets", 0x19), ("WatsonBuckets", 0x1d)];
        Assert.That(InstanceFields(reader, state), Is.EqualTo(expectedState));
        (string, byte)[] expectedList = [("_items", 0x1d), ("_size", 0x08), ("_version", 0x08)];
        Assert.That(InstanceFields(reader, RuntimeType(reader, "System.Collections.Generic", "List" + (char)96 + "1")), Is.EqualTo(expectedList));
        Assert.That(ResourceTaskFaultCarrierCatalog.ContingentFields, Has.Length.EqualTo(9));
        Assert.That(ResourceTaskFaultCarrierCatalog.HolderFields, Has.Length.EqualTo(4));
        Assert.That(ResourceTaskFaultCarrierCatalog.DispatchFields, Has.Length.EqualTo(6));
        Assert.That(ResourceTaskFaultCarrierCatalog.ListFields, Has.Length.EqualTo(3));
    }

    private static (CompilerForkJoinProjection Projection, CompilerTotalEntryPreparation Root) FaultProjection(int count, bool configured)
    {
        var (_, projection, root) = Fixture(configured ? 2 : 0);
        var child = projection.Children[0];
        var children = Enumerable.Range(0, count).Select(index => child with
        { Owner = child.Owner with { CallableId = "root:fork-child:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) } }).ToImmutableArray();
        return (projection with { Children = children }, root);
    }
    private static TypeDefinition RuntimeType(MetadataReader reader, string namesp, string name)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(namesp);
        ArgumentNullException.ThrowIfNull(name);
        return reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(type =>
            reader.GetString(type.Namespace) == namesp && reader.GetString(type.Name) == name);
    }

    private static (string Name, byte Kind)[] InstanceFields(MetadataReader reader, TypeDefinition type)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (type.Equals(default(TypeDefinition)))
        { throw new ArgumentException("A metadata type is required.", nameof(type)); }
        return type.GetFields().Select(reader.GetFieldDefinition).Where(static field => (field.Attributes & FieldAttributes.Static) == 0)
            .Select(field =>
            {
                var signature = reader.GetBlobReader(field.Signature);
                Assert.That(signature.ReadSignatureHeader().Kind, Is.EqualTo(SignatureKind.Field));
                var kind = signature.ReadByte();
                while (kind is 0x1f or 0x20)
                { _ = signature.ReadCompressedInteger(); kind = signature.ReadByte(); }
                return (reader.GetString(field.Name), kind);
            }).ToArray();
    }

    private static (CompilerTaskCarrierShapes Shapes, CompilerForkJoinProjection Projection, CompilerTotalEntryPreparation Root) Fixture(int stateKind)
    {
        var (_, _, projection, root) = Compile(false, OptimizationLevel.Release);
        var state = stateKind == 0 ? null : new CompilerTaskCarrierType("C+State", stateKind == 2,
            [ResourceStorageKind.Word32, ResourceStorageKind.Reference, ResourceStorageKind.Reference, ResourceStorageKind.Word32]);
        var shapes = new CompilerTaskCarrierShapes("root", new string('a', 64), "compiler", 2,
            [new("C+Frame1", false, [ResourceStorageKind.Reference, ResourceStorageKind.Word32]),
             new("C+Frame2", false, [ResourceStorageKind.Byte])], state);
        return (shapes, projection with { ConfiguredAwait = stateKind != 0 }, root);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method, CompilerForkJoinProjection Projection, CompilerTotalEntryPreparation Root)
        Compile(bool configured, OptimizationLevel optimization, string body = "")
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using System.Threading.Tasks; public static class C {public static " + (configured ? "async Task" : "Task") +
            " Root(int n) => " + (configured ? "await " : "") + "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n)))" +
            (configured ? ".ConfigureAwait(false)" : "") + "; static void Child(int n) {" + body + "} }";
        var initial = TestCompilation.Create("CarrierStorage", [("Subject.cs", source)]);
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var projection = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken).Projection!;
        Assert.That(projection, Is.Not.Null);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = new CompilerTotalParameter(factory.CreateVariable("entry:n", factory.IntegerType),
            factory.CreateVariable("current:n", factory.IntegerType), factory.CreateVariable("old:n", factory.IntegerType));
        return (compilation, method, projection, new("root", factory, [parameter], []));
    }

    private static ResourceRuntimeLayoutContext Layout()
    {
        return new("CoreCLR", "8.0.32", "x64", 8);
    }
}
