using System.Collections.Immutable;
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
        Compile(bool configured, OptimizationLevel optimization)
    {
        var source = "using System.Threading.Tasks; public static class C {public static " + (configured ? "async Task" : "Task") +
            " Root(int n) => " + (configured ? "await " : "") + "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n)))" +
            (configured ? ".ConfigureAwait(false)" : "") + "; static void Child(int n) {} }";
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
