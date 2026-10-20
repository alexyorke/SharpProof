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
        var initial = TestCompilation.Create("CarrierStorage", ("Subject.cs", source));
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
