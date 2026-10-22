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
    private static readonly ResourceStorageKind[] PrimitiveCarrierFields =
    [
        ResourceStorageKind.Byte, ResourceStorageKind.Byte, ResourceStorageKind.Byte,
        ResourceStorageKind.Word16, ResourceStorageKind.Word16, ResourceStorageKind.Word16,
        ResourceStorageKind.Word32, ResourceStorageKind.Word32, ResourceStorageKind.Word32,
        ResourceStorageKind.Word64, ResourceStorageKind.Word64, ResourceStorageKind.Word64,
        ResourceStorageKind.Reference, ResourceStorageKind.Reference
    ];

    [TestCase(false)]
    [TestCase(true)]
    public void GeneratedCarrierShapesPreservePrimitiveWidthsAndIgnoreStaticStorage(bool valueType)
    {
        var (compilation, type) = CompileCarrierShape("namespace Frames { [CompilerGenerated] public " +
            (valueType ? "struct" : "class") + " Frame {public bool A;public byte B;public sbyte C;" +
            "public char D;public short E;public ushort F;public int G;public uint H;public float I;" +
            "public long J;public ulong K;public double L;public object M;public string N;public static decimal Ignored;} }",
            "Frames.Frame");
        var shape = InspectCarrierShape(type, compilation, TestContext.CurrentContext.CancellationToken);
        Assert.That(shape, Is.Not.Null);
        Assert.That(shape!.MetadataName, Is.EqualTo("Frames.Frame"));
        Assert.That(shape.IsValueType, Is.EqualTo(valueType));
        Assert.That(shape.Fields, Is.EqualTo(PrimitiveCarrierFields));
    }

    [TestCase("public class Frame {}", "Frame")]
    [TestCase("[CompilerGenerated] public class Frame<T> {}", "Frame`1")]
    [TestCase("public class Parent {} [CompilerGenerated] public class Frame:Parent {}", "Frame")]
    [TestCase("[CompilerGenerated,System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] public class Frame {}", "Frame")]
    [TestCase("[CompilerGenerated] public class Frame {public decimal Unsupported;}", "Frame")]
    [TestCase("public sealed class CompilerGeneratedAttribute:Attribute {} [CompilerGenerated] public class Frame {}", "Frame")]
    [TestCase("namespace System.Runtime.CompilerServices {public sealed class CompilerGeneratedAttribute:Attribute {}} [CompilerGenerated] public class Frame {}", "Frame")]
    public void CarrierShapeAdmissionRejectsUnsupportedTypesAndAttributeShadows(string declaration, string metadataName)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(metadataName);
        var (compilation, type) = CompileCarrierShape(declaration, metadataName);
        Assert.That(InspectCarrierShape(type, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CarrierFieldBudgetRejectsTheFirstExcessSlotWithoutPartialShape(bool excess)
    {
        var count = ResourceAllocationShape.MaximumFields + (excess ? 1 : 0);
        var fields = string.Concat(Enumerable.Range(0, count).Select(static index => "public byte Field" + index + ";"));
        var (compilation, type) = CompileCarrierShape("[CompilerGenerated] public class Frame {" + fields + "}", "Frame");
        var shape = InspectCarrierShape(type, compilation, TestContext.CurrentContext.CancellationToken);
        if (excess)
        { Assert.That(shape, Is.Null); }
        else
        {
            Assert.That(shape, Is.Not.Null);
            Assert.That(shape!.Fields, Has.Length.EqualTo(count));
            Assert.That(shape.Fields.All(static field => field == ResourceStorageKind.Byte), Is.True);
        }
    }

    [Test]
    public void GeneratedCarrierMetadataNamesHaveAnIndependentLengthBudget()
    {
        var names = Enumerable.Range(0, 17).Select(index => new string('N', 250) + index).ToArray();
        var declarations = string.Concat(names.Select(static name => name is null ? throw new ArgumentNullException(nameof(name)) : "public class " + name + "{"));
        var metadataName = string.Join("+", names) + "+Frame";
        Assert.That(metadataName.Length, Is.GreaterThan(4096));
        var (compilation, type) = CompileCarrierShape(declarations + "[CompilerGenerated] public class Frame {}" +
            new string('}', names.Length), metadataName);
        Assert.That(InspectCarrierShape(type, compilation, TestContext.CurrentContext.CancellationToken), Is.Null);
    }

    [Test]
    public void CarrierOwnershipUsesContainingSymbolsRatherThanMatchingOwnerNames()
    {
        var (compilation, type) = CompileCarrierShape("namespace A {public class Owner {[CompilerGenerated] public class Frame {}}}" +
            "namespace B {public class Owner {}}", "A.Owner+Frame");
        var method = typeof(CompilerTaskCarrierCollector).GetMethod("Inside", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        Assert.That(method!.Invoke(null, [type, compilation.GetTypeByMetadataName("A.Owner")!]), Is.True);
        Assert.That(method.Invoke(null, [type, compilation.GetTypeByMetadataName("B.Owner")!]), Is.False);
    }

    [Test]
    public void CarrierShapeCancellationDoesNotReturnAPartialFieldInventory()
    {
        var (compilation, type) = CompileCarrierShape("[CompilerGenerated] public class Frame {public bool Value;}", "Frame");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action inspect = () => { _ = InspectCarrierShape(type, compilation, canceled.Token); };
        var exception = Assert.Throws<TargetInvocationException>(inspect)!;
        Assert.That(exception.InnerException, Is.TypeOf<OperationCanceledException>());
    }

    private static (CSharpCompilation Compilation, INamedTypeSymbol Type) CompileCarrierShape(string declarations, string metadataName)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentNullException.ThrowIfNull(metadataName);
        var compilation = TestCompilation.Create("CarrierShapeControl", [("Frame.cs",
            "#pragma warning disable CS0436\nusing System;using System.Runtime.CompilerServices;" + declarations)]);
        TestCompilation.AssertNoErrors(compilation);
        var type = compilation.GetTypeByMetadataName(metadataName);
        Assert.That(type, Is.Not.Null);
        return (compilation, type!);
    }

    private static CompilerTaskCarrierType? InspectCarrierShape(INamedTypeSymbol type, CSharpCompilation compilation, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(compilation);
        var framework = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly.Identity.ToString();
        var method = typeof(CompilerTaskCarrierCollector).GetMethod("Shape", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        return (CompilerTaskCarrierType?)method!.Invoke(null, [type, framework, cancellation]);
    }

    [TestCase("array")]
    [TestCase("byte")]
    [TestCase("span")]
    [TestCase("async-array")]
    [TestCase("async-memory")]
    public void EveryImageWriteApiEnforcesTheSerializedSizeBudget(string api)
    {
        ArgumentNullException.ThrowIfNull(api);
        using var stream = CreateCarrierImageStream();
        stream.Position = CompilerTaskCarrierCollector.MaximumImageBytes;
        var payload = new byte[1];
        payload[0] = 42;
        Action write = api switch
        {
            "array" => () => stream.Write(payload, 0, payload.Length),
            "byte" => () => stream.WriteByte(payload[0]),
            "span" => () => stream.Write(payload.AsSpan()),
            "async-array" => () => stream.WriteAsync(payload, 0, payload.Length).GetAwaiter().GetResult(),
            "async-memory" => () => stream.WriteAsync(payload.AsMemory()).AsTask().GetAwaiter().GetResult(),
            _ => throw new ArgumentOutOfRangeException(nameof(api))
        };
        AssertImageBudgetRejection(write);
        Assert.That(stream.Length, Is.Zero, "Rejected writes must not extend the serialized image.");
    }

    [Test]
    public void ImageBudgetAcceptsTheLastByteAndRejectsGrowthWithoutChangingItsPrefix()
    {
        using var stream = CreateCarrierImageStream();
        stream.SetLength(CompilerTaskCarrierCollector.MaximumImageBytes);
        stream.Position = stream.Length - 1;
        stream.WriteByte(42);
        Assert.That(stream.Length, Is.EqualTo(CompilerTaskCarrierCollector.MaximumImageBytes));
        Assert.That(stream.Position, Is.EqualTo(stream.Length));
        AssertImageBudgetRejection(() => stream.WriteByte(43));
        AssertImageBudgetRejection(() => stream.SetLength(CompilerTaskCarrierCollector.MaximumImageBytes + 1L));
        Assert.That(stream.Length, Is.EqualTo(CompilerTaskCarrierCollector.MaximumImageBytes));
        stream.Position = stream.Length - 1;
        var crossing = new byte[2];
        crossing[0] = 99;
        crossing[1] = 100;
        AssertImageBudgetRejection(() => stream.Write(crossing, 0, crossing.Length));
        Assert.That(stream.ReadByte(), Is.EqualTo(42), "A rejected write must preserve the admitted prefix.");
        stream.SetLength(0);
        Assert.That(stream.Length, Is.Zero);
    }
    private static void AssertImageBudgetRejection(Action operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        Assert.That(operation, Throws.TypeOf<InvalidDataException>());
    }
    private static Stream CreateCarrierImageStream()
    {
        var type = typeof(CompilerTaskCarrierCollector).GetNestedType("CarrierImageStream", BindingFlags.NonPublic);
        Assert.That(type, Is.Not.Null);
        return (Stream)Activator.CreateInstance(type!, nonPublic: true)!;
    }

    [TestCase(false, "")]
    [TestCase(true, "")]
    [TestCase(false, "for(int i=0;i<n;i++) {var a=new int[4];}")]
    [TestCase(true, "for(int i=0;i<n;i++) {var a=new int[4];}")]
    public void DirectSourceFrameComponentRequiresOwnedBodiesAfterTransport(bool configured, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (compilation, method, projection, root) = Compile(configured, OptimizationLevel.Release, body);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var carriers = collector.Capture(method, "root", cancellation);
        projection = projection with
        {
            Carriers = carriers with
            {
                Shapes = carriers.Shapes! with
                {
                    SourceBodies = collector.CaptureSourceBodies(method, "root", cancellation),
                    CallbackActivations = collector.CaptureCallbackActivations(method, "root", cancellation)
                }
            }
        };
        var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
        var check = NativeTaskCarrierStorageVerifier.CheckDirectSourceFrames(decoded.Carriers!.Shapes!, decoded, root, Layout(), cancellation);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.FramesPerPassUpper, Is.EqualTo(1));
        Assert.That(check.RequiredCompilerModel, Is.EqualTo(NativeTaskCarrierStorageVerifier.SourceBodyModel));
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(NativeTaskCarrierStorageVerifier.DirectSourceFrameModel));
        var owned = NativeTaskCarrierStorageVerifier.CheckOwnedSourceFrames(decoded.Carriers.Shapes!, decoded, root, Layout(), cancellation);
        Assert.That(owned.UnknownReason, Is.Null);
        Assert.That(owned.FramesPerPassUpper, Is.EqualTo(2));
        Assert.That(owned.RequiredSourceCompilerModel, Is.EqualTo(NativeTaskCarrierStorageVerifier.SourceBodyModel));
        Assert.That(owned.RequiredCallbackCompilerModel, Is.EqualTo(NativeTaskCarrierStorageVerifier.CallbackActivationModel));
        Assert.That(owned.RequiredRuntimeModel, Is.EqualTo(NativeTaskCarrierStorageVerifier.OwnedSourceFrameModel));
    }

    [TestCase("callbacks", "TaskCallbackActivationEvidenceUnavailable")]
    [TestCase("missing", "TaskSourceBodyEvidenceUnavailable")]
    [TestCase("foreign", "TaskSourceBodyEvidenceInvalid")]
    [TestCase("layout", "IncompatibleResourceRuntimeLayout")]
    public void DirectSourceFrameFailuresPublishNeitherCountNorRequiredModel(string corruption, string reason)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        ArgumentNullException.ThrowIfNull(reason);
        var (compilation, method, projection, root) = Compile(false, OptimizationLevel.Release, "");
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var capture = collector.CaptureSourceBodies(method, "root", cancellation);
        var carriers = collector.Capture(method, "root", cancellation);
        var shapes = carriers.Shapes! with
        {
            CallbackActivations = corruption == "callbacks" ? null : collector.CaptureCallbackActivations(method, "root", cancellation),
            SourceBodies = corruption == "missing" ? null : corruption == "foreign" ?
                capture with { Inventory = capture.Inventory! with { ModuleSha256 = new string('0', 64) } } : capture
        };
        projection = projection with { Carriers = new(shapes, null) };
        var layout = corruption == "layout" ? new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "arm64", 8) : Layout();
        var check = NativeTaskCarrierStorageVerifier.CheckDirectSourceFrames(shapes, projection, root, layout, cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo(reason));
        Assert.That(check.FramesPerPassUpper, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action operation = () => { _ = NativeTaskCarrierStorageVerifier.CheckDirectSourceFrames(shapes, projection, root, layout, canceled.Token); };
        Assert.Throws<OperationCanceledException>(operation);
        var owned = NativeTaskCarrierStorageVerifier.CheckOwnedSourceFrames(shapes, projection, root, layout, cancellation);
        Assert.That(owned.UnknownReason, Is.EqualTo(reason));
        Assert.That(owned.FramesPerPassUpper, Is.Null);
        Assert.That(owned.RequiredSourceCompilerModel, Is.Null);
        Assert.That(owned.RequiredCallbackCompilerModel, Is.Null);
        Assert.That(owned.RequiredRuntimeModel, Is.Null);
        Action ownedOperation = () => { _ = NativeTaskCarrierStorageVerifier.CheckOwnedSourceFrames(shapes, projection, root, layout, canceled.Token); };
        Assert.Throws<OperationCanceledException>(ownedOperation);
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public void SourceBodyInventoryBindsEveryDistinctTargetToTheCallbackEmission(OptimizationLevel optimization, bool configured)
    {
        var (compilation, method) = CompileSourceProbe(
            "while(n>0) {var values=new int[n];values[0]=n;int length=values.Length;n--;} n=checked(n*2+1);n=(byte)n;",
            configured, optimization);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var callbacks = collector.CaptureCallbackActivations(method, "root", cancellation).Activations!;
        var capture = collector.CaptureSourceBodies(method, "root", cancellation);
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Inventory!.RootCallableId, Is.EqualTo("root"));
        Assert.That(capture.Inventory.ModuleSha256, Is.EqualTo(callbacks.ModuleSha256));
        Assert.That(capture.Inventory.MethodTokens.ToArray(), Is.EqualTo(callbacks.Forwarders.Select(forwarder =>
            forwarder is null ? throw new ArgumentNullException(nameof(forwarder)) : forwarder.ChildMethodToken).Distinct().Order().ToArray()));
        Assert.That(capture.Inventory.MethodTokens, Has.Length.EqualTo(1));
        var repeated = collector.CaptureSourceBodies(method, "root", cancellation);
        Assert.That(repeated.Inventory!.MethodTokens.ToArray(), Is.EqualTo(capture.Inventory.MethodTokens.ToArray()));
        Assert.That(repeated.Inventory.ModuleSha256, Is.EqualTo(capture.Inventory.ModuleSha256));
    }

    [TestCase("")]
    [TestCase("for(int i=0;i<n;i++) {var a=new int[4];}")]
    public void SourceBodyInventoryCanAccompanyAnAdmittedResourceProjection(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (compilation, method, projection, root) = Compile(false, OptimizationLevel.Release, body);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var capture = collector.CaptureSourceBodies(method, "root", cancellation);
        Assert.That(projection.Children, Has.Length.EqualTo(2));
        Assert.That(capture.UnknownReason, Is.Null);
        Assert.That(capture.Inventory!.MethodTokens, Has.Length.EqualTo(1));
        var carriers = collector.Capture(method, "root", cancellation);
        projection = projection with
        {
            Carriers = carriers with
            {
                Shapes = carriers.Shapes! with
                { SourceBodies = capture, CallbackActivations = collector.CaptureCallbackActivations(method, "root", cancellation) }
            }
        };
        var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
        var check = NativeTaskCarrierStorageVerifier.CheckSourceBodies(decoded.Carriers!.Shapes!, decoded, root, Layout(), cancellation);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.DirectSourceActivationsUpper, Is.EqualTo(1));
        Assert.That(check.RequiredCompilerModel, Is.EqualTo(NativeTaskCarrierStorageVerifier.SourceBodyModel));
        Assert.That(decoded.Carriers.Shapes!.SourceBodies!.Inventory!.MethodTokens.ToArray(), Is.EqualTo(capture.Inventory.MethodTokens.ToArray()));
    }

    [TestCase("owner")]
    [TestCase("module")]
    [TestCase("empty")]
    [TestCase("default")]
    [TestCase("duplicate")]
    [TestCase("token")]
    [TestCase("foreign")]
    [TestCase("extra")]
    [TestCase("both")]
    [TestCase("many")]
    [TestCase("callbacks")]
    public void SourceBodyInventoryRejectsUnownedTransportWithoutAnActivationBound(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, root) = Fixture(0);
        var inventory = new CompilerTaskSourceBodyInventory("root", shapes.ModuleSha256, [0x06000003]);
        inventory = mutation switch
        {
            "owner" => inventory with { RootCallableId = "foreign" },
            "module" => inventory with { ModuleSha256 = new string('b', 64) },
            "empty" => inventory with { MethodTokens = [] },
            "default" => inventory with { MethodTokens = default },
            "duplicate" => inventory with { MethodTokens = [0x06000003, 0x06000003] },
            "token" => inventory with { MethodTokens = [0x0a000003] },
            "foreign" => inventory with { MethodTokens = [0x06000004] },
            "extra" => inventory with { MethodTokens = [0x06000003, 0x06000004] },
            "many" => inventory with { MethodTokens = Enumerable.Range(1, 17).Select(value => 0x06000000 | value).ToImmutableArray() },
            _ => inventory
        };
        shapes = shapes with
        {
            SourceBodies = new(inventory, mutation == "both" ? "Unsupported" : null),
            CallbackActivations = mutation == "callbacks" ? null : new(new("root", shapes.ModuleSha256,
                [new(0x06000001, 0x06000003), new(0x06000002, 0x06000003)]), null)
        };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeTaskCarrierStorageVerifier.CheckSourceBodies(shapes, projection, root, Layout(), cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo(mutation == "callbacks" ? "TaskCallbackActivationEvidenceUnavailable" : "TaskSourceBodyEvidenceInvalid"));
        Assert.That(check.DirectSourceActivationsUpper, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.Null);
        Action encode = () => { _ = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation); };
        Assert.Throws<InvalidDataException>(encode);
    }

    [TestCase(null, "TaskSourceBodyEvidenceUnavailable")]
    [TestCase("UnsupportedSourceBody", "UnsupportedSourceBody")]
    [TestCase("", "TaskSourceBodyEvidenceInvalid")]
    public void SourceBodyUnknownNeverPublishesAnActivationModel(string? reason, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var (shapes, projection, root) = Fixture(0);
        shapes = shapes with
        {
            SourceBodies = reason is null ? null : new(null, reason),
            CallbackActivations = new(new("root", shapes.ModuleSha256, [new(0x06000001, 0x06000003), new(0x06000002, 0x06000003)]), null)
        };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeTaskCarrierStorageVerifier.CheckSourceBodies(shapes, projection, root, Layout(), cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo(expected));
        Assert.That(check.DirectSourceActivationsUpper, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.Null);
        if (reason is null || reason.Length != 0)
        {
            var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
            Assert.That(decoded.Carriers!.Shapes!.SourceBodies?.UnknownReason, Is.EqualTo(reason));
        }
        else
        {
            Action encode = () => { _ = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation); };
            Assert.Throws<InvalidDataException>(encode);
        }
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action verify = () => { _ = NativeTaskCarrierStorageVerifier.CheckSourceBodies(shapes, projection, root, Layout(), canceled.Token); };
        Assert.Throws<OperationCanceledException>(verify);
        var incompatible = NativeTaskCarrierStorageVerifier.CheckSourceBodies(shapes, projection, root, new("CoreCLR", "8.0.32", "arm64", 8), cancellation);
        Assert.That(incompatible.DirectSourceActivationsUpper, Is.Null);
        Assert.That(incompatible.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
    }

    [TestCase("missing")]
    [TestCase("extra")]
    [TestCase("value-extra")]
    [TestCase("methods-missing")]
    [TestCase("kind")]
    [TestCase("fraction")]
    [TestCase("both")]
    [TestCase("neither")]
    [TestCase("reason")]
    [TestCase("old")]
    [TestCase("callbacks")]
    public void SourceBodyCodecRejectsMalformedAndOldPayloads(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, owner) = Fixture(0);
        shapes = shapes with
        {
            SourceBodies = new(new("root", shapes.ModuleSha256, [0x06000003]), null),
            CallbackActivations = new(new("root", shapes.ModuleSha256, [new(0x06000001, 0x06000003), new(0x06000002, 0x06000003)]), null)
        };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var root = JsonNode.Parse(CompilerForkJoinProjectionCodec.Encode(projection, owner, cancellation))!.AsObject();
        var encodedShapes = root["carriers"]!["shapes"]!.AsObject();
        var capture = encodedShapes["bodies"]!.AsObject();
        var value = capture["value"]!.AsObject();
        switch (mutation)
        {
            case "missing":
                encodedShapes.Remove("bodies");
                break;
            case "extra":
                capture["extra"] = 0;
                break;
            case "value-extra":
                value["extra"] = 0;
                break;
            case "methods-missing":
                value.Remove("methods");
                break;
            case "kind":
                encodedShapes["bodies"] = 0;
                break;
            case "fraction":
                value["methods"] = new JsonArray(6.5);
                break;
            case "both":
                capture["unknown"] = "UnsupportedSourceBody";
                break;
            case "neither":
                capture["value"] = null;
                break;
            case "reason":
                capture["value"] = null;
                capture["unknown"] = new string('x', 513);
                break;
            case "callbacks":
                encodedShapes["activations"] = null;
                break;
            default:
                root["version"] = 5;
                break;
        }
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()), owner, cancellation); };
        Assert.Throws<InvalidDataException>(decode);
    }

    [TestCase("n=System.Math.Abs(n);", "TaskSourceBodyCallUnsupported")]
    [TestCase("Child(n);", "TaskSourceBodyCallUnsupported")]
    [TestCase("new object();", "TaskSourceBodyInstructionUnsupported")]
    [TestCase("var a=new byte[n];", "TaskSourceBodyArrayTypeUnsupported")]
    [TestCase("var a=new object[n];", "TaskSourceBodyArrayTypeUnsupported")]
    [TestCase("try {n/=n;} catch(System.Exception) {}", "TaskSourceBodyHandlingUnsupported")]
    public void SourceBodyInventoryRejectsUnsupportedEmittedOperations(string body, string expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(expected);
        var capture = CaptureSourceProbe(body);
        Assert.That(capture.Inventory, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo(expected));
    }

    [Test]
    public void SourceBodyInventoryRejectsOversizedBodiesWithoutPartialMethods()
    {
        var capture = CaptureSourceProbe(string.Concat(Enumerable.Repeat("n+=1;", 1100)));
        Assert.That(capture.Inventory, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskSourceBodyInstructionUnsupported"));
    }

    private static CompilerTaskSourceBodyCapture CaptureSourceProbe(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var (compilation, method) = CompileSourceProbe(body, false, OptimizationLevel.Debug);
        return new CompilerTaskCarrierCollector(compilation).CaptureSourceBodies(method, "root", TestContext.CurrentContext.CancellationToken);
    }

    private static (CSharpCompilation Compilation, IMethodSymbol Method) CompileSourceProbe(string body, bool configured, OptimizationLevel optimization)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using System.Threading.Tasks; public static class C {public static " + (configured ? "async Task" : "Task") +
            " Root(int n) => " + (configured ? "await " : "") + "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n)))" +
            (configured ? ".ConfigureAwait(false)" : "") + "; static void Child(int n) {" + body + "} }";
        var initial = TestCompilation.Create("SourceBodyControl", [("Subject.cs", source)]);
        var compilation = initial.WithOptions(initial.Options.WithOptimizationLevel(optimization));
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        return (compilation, method);
    }

    [Test]
    public void SourceBodyInventoryClearsEarlierMethodsWhenALaterBodyIsUnsupported()
    {
        const string source = "using System.Threading.Tasks; public static class C {public static Task Root(int n) => " +
            "Task.WhenAll(Task.Run(()=>Good(n)),Task.Run(()=>Bad(n))); static void Good(int n) {} static void Bad(int n) {Bad(n);} }";
        var compilation = TestCompilation.Create("SourceBodyPartialControl", [("Subject.cs", source)]);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var capture = collector.CaptureSourceBodies(method, "root", cancellation);
        Assert.That(capture.Inventory, Is.Null);
        Assert.That(capture.UnknownReason, Is.EqualTo("TaskSourceBodyCallUnsupported"));
        Assert.That(collector.CaptureCallbackActivations(method, "root", cancellation).Activations, Is.Not.Null,
            "Source-body rejection does not erase the separate forwarding fact.");
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action check = () => { _ = collector.CaptureSourceBodies(method, "root", canceled.Token); };
        Assert.Throws<OperationCanceledException>(check);
        Assert.That(collector.CaptureSourceBodies(method, " ", cancellation).Inventory, Is.Null);
    }

    [TestCase(OptimizationLevel.Debug, false)]
    [TestCase(OptimizationLevel.Release, false)]
    [TestCase(OptimizationLevel.Debug, true)]
    [TestCase(OptimizationLevel.Release, true)]
    public void CallbackActivationTransportFeedsNativeSourceComposition(OptimizationLevel optimization, bool configured)
    {
        var (compilation, method, projection, root) = Compile(configured, optimization);
        var collector = new CompilerTaskCarrierCollector(compilation);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var carriers = collector.Capture(method, "root", cancellation);
        projection = projection with
        {
            Carriers = carriers with
            {
                Shapes = carriers.Shapes! with
                { CallbackActivations = collector.CaptureCallbackActivations(method, "root", cancellation) }
            }
        };
        var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
        var evidence = decoded.Carriers!.Shapes!;
        Assert.That(evidence.CallbackActivations!.Activations!.Forwarders.ToArray(),
            Is.EqualTo(projection.Carriers.Shapes!.CallbackActivations!.Activations!.Forwarders.ToArray()));
        var check = NativeTaskCarrierStorageVerifier.CheckCallbackActivations(evidence, decoded, root, Layout(), cancellation);
        Assert.That(check.UnknownReason, Is.Null);
        Assert.That(check.WrapperActivationsUpper, Is.EqualTo(1));
        Assert.That(check.RequiredCompilerModel, Is.EqualTo(NativeTaskCarrierStorageVerifier.CallbackActivationModel));
        var combined = NativeForkJoinSourceCostVerifier.CheckCallbackSourceActivations(decoded, root, Layout(), cancellation);
        Assert.That(combined.UnknownReason, Is.Null);
        Assert.That(combined.Upper!.Constant, Is.EqualTo(new System.Numerics.BigInteger(2)));
        Assert.That(combined.RequiredSourceModel, Is.EqualTo(NativeForkJoinSourceCostVerifier.CallbackSourceActivationModel));
    }

    [TestCase("owner")]
    [TestCase("module")]
    [TestCase("count")]
    [TestCase("default")]
    [TestCase("null")]
    [TestCase("method-kind")]
    [TestCase("child-kind")]
    [TestCase("empty-token")]
    [TestCase("duplicate")]
    [TestCase("overlap")]
    [TestCase("both")]
    [TestCase("targets")]
    public void CallbackActivationAdmissionAndCodecRejectUnownedEvidence(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, root) = Fixture(0);
        if (mutation == "targets")
        { shapes = shapes with { MatchedCallbacks = 3 }; }
        var activations = new CompilerTaskCallbackActivation("root", shapes.ModuleSha256,
            [new(0x06000001, 0x06000003), new(0x06000002, 0x06000003)]);
        activations = mutation switch
        {
            "owner" => activations with { RootCallableId = "foreign" },
            "module" => activations with { ModuleSha256 = new string('b', 64) },
            "count" => activations with { Forwarders = [activations.Forwarders[0]] },
            "default" => activations with { Forwarders = default },
            "null" => activations with { Forwarders = [activations.Forwarders[0], null!] },
            "method-kind" => activations with { Forwarders = [new(0x0a000001, 0x06000003), activations.Forwarders[1]] },
            "child-kind" => activations with { Forwarders = [new(0x06000001, 0x0a000001), activations.Forwarders[1]] },
            "empty-token" => activations with { Forwarders = [new(0x06000000, 0x06000003), activations.Forwarders[1]] },
            "duplicate" => activations with { Forwarders = [activations.Forwarders[0], activations.Forwarders[0]] },
            "overlap" => activations with { Forwarders = [new(0x06000001, 0x06000002), activations.Forwarders[1]] },
            "both" => activations,
            "targets" => activations with { Forwarders = [new(0x06000001, 0x06000004), new(0x06000002, 0x06000005), new(0x06000003, 0x06000006)] },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        shapes = shapes with { CallbackActivations = new(activations, mutation == "both" ? "Unsupported" : null) };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeTaskCarrierStorageVerifier.CheckCallbackActivations(shapes, projection, root, Layout(), cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo("TaskCallbackActivationEvidenceInvalid"));
        Assert.That(check.WrapperActivationsUpper, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.Null);
        var combined = NativeForkJoinSourceCostVerifier.CheckCallbackSourceActivations(projection, root, Layout(), cancellation);
        Assert.That(combined.Upper, Is.Null);
        Assert.That(combined.RequiredSourceModel, Is.Null);
        Action encode = () => { _ = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation); };
        Assert.Throws<InvalidDataException>(encode);
    }

    [TestCase(null, "TaskCallbackActivationEvidenceUnavailable")]
    [TestCase("UnsupportedCallback", "UnsupportedCallback")]
    [TestCase("", "TaskCallbackActivationEvidenceInvalid")]
    public void CallbackActivationUnknownNeverBecomesAPartialBound(string? reason, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var (shapes, projection, root) = Fixture(0);
        shapes = shapes with { CallbackActivations = reason is null ? null : new(null, reason) };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeTaskCarrierStorageVerifier.CheckCallbackActivations(shapes, projection, root, Layout(), cancellation);
        Assert.That(check.UnknownReason, Is.EqualTo(expected));
        Assert.That(check.WrapperActivationsUpper, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.Null);
        if (reason is null || reason.Length != 0)
        {
            var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
            Assert.That(decoded.Carriers!.Shapes!.CallbackActivations?.UnknownReason, Is.EqualTo(reason));
        }
    }

    [TestCase("extra")]
    [TestCase("missing")]
    [TestCase("unknown-kind")]
    [TestCase("long-reason")]
    [TestCase("old-version")]
    public void CallbackDecoderRejectsMalformedTransport(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var (shapes, projection, root) = Fixture(0);
        shapes = shapes with { CallbackActivations = new(null, "UnsupportedCallback") };
        projection = projection with { Carriers = new(shapes, null) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var json = JsonNode.Parse(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation))!.AsObject();
        var evidence = json["carriers"]!["shapes"]!.AsObject();
        var capture = evidence["activations"]!.AsObject();
        switch (mutation)
        {
            case "extra":
                capture["extra"] = true;
                break;
            case "missing":
                evidence.Remove("activations");
                break;
            case "unknown-kind":
                capture["unknown"] = true;
                break;
            case "long-reason":
                capture["unknown"] = new string('x', 513);
                break;
            case "old-version":
                json["version"] = CompilerForkJoinProjectionCodec.Version - 1;
                break;
        }
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()), root, cancellation); };
        Assert.Throws<InvalidDataException>(decode);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CallbackSourceCompositionRequiresOwnedCarriersAndCompatibleLayout(bool incompatibleLayout)
    {
        var (shapes, projection, root) = Fixture(0);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var absent = NativeForkJoinSourceCostVerifier.CheckCallbackSourceActivations(projection, root, Layout(), cancellation);
        Assert.That(absent.Upper, Is.Null);
        Assert.That(absent.RequiredSourceModel, Is.Null);
        if (!incompatibleLayout)
        { shapes = shapes with { RootCallableId = "foreign" }; }
        var check = NativeTaskCarrierStorageVerifier.CheckCallbackActivations(shapes, projection, root,
            incompatibleLayout ? new("CoreCLR", "8.0.32", "arm64", 8) : Layout(), cancellation);
        Assert.That(check.WrapperActivationsUpper, Is.Null);
        Assert.That(check.RequiredCompilerModel, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo(incompatibleLayout ? "IncompatibleResourceRuntimeLayout" : "TaskCompilerCarrierOwnershipUnproved"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Action verify = () => { _ = NativeTaskCarrierStorageVerifier.CheckCallbackActivations(shapes, projection, root, Layout(), canceled.Token); };
        Assert.Throws<OperationCanceledException>(verify);
    }

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

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void CallbackBodyValidationRejectsMalformedInstructionsAndMissingReturns(int bodyMutation)
    {
        var result = ValidateForwarderProbe("public static void Probe() {Child(3);}",
            bodyMutation: bodyMutation);
        Assert.That(result.Target, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskCallbackForwarderBodyUnsupported"));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void SourceBodyValidationClearsMalformedInstructionEvidence(int bodyMutation)
    {
        var result = ValidateForwarderProbe("public static void Probe(int n) {var values=new int[n];values[0]=n;n=values.Length;}",
            bodyMutation: bodyMutation, sourceBody: true);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskSourceBodyInstructionUnsupported"));
    }

    [Test]
    public void SourceBodyValidationBindsTheArrayElementFrameworkIdentity()
    {
        var result = ValidateForwarderProbe("public static void Probe(int n) {var values=new int[n];}", sourceBody: true, foreignFramework: true);
        Assert.That(result.UnknownReason, Is.EqualTo("TaskSourceBodyArrayTypeUnsupported"));
    }

    [TestCase(0x7f000001, "TaskSourceBodyInstructionUnsupported")]
    [TestCase(0x0100ffff, "TaskSourceBodyInstructionUnsupported")]
    [TestCase(0x01000000, "TaskSourceBodyInstructionUnsupported")]
    [TestCase(0x06000001, "TaskSourceBodyArrayTypeUnsupported")]
    [TestCase(0x23000001, "TaskSourceBodyArrayTypeUnsupported")]
    public void SourceBodyValidationRejectsInvalidArrayTypeTokens(int token, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var result = ValidateForwarderProbe("public static void Probe(int n) {var values=new int[n];}",
            sourceBody: true, arrayTypeToken: token);
        Assert.That(result.Target, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(expected));
    }

    private static (int? Target, string? UnknownReason) ValidateForwarderProbe(string declaration, int targetMutation = 0, int bodyMutation = 0,
        bool sourceBody = false, bool foreignFramework = false, int? arrayTypeToken = null)
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
        if (bodyMutation != 0 || arrayTypeToken is not null)
        {
            // Keep a real emitted PE/header. A malformed final opcode or missing
            // return must discard a preceding owned call. A branch operand can
            // also overflow before the parser checks its target range.
            var bytes = stream.ToArray();
            var section = image.PEHeaders.SectionHeaders.Single(value => probe.RelativeVirtualAddress >= value.VirtualAddress &&
                probe.RelativeVirtualAddress < value.VirtualAddress + value.SizeOfRawData);
            var offset = section.PointerToRawData + probe.RelativeVirtualAddress - section.VirtualAddress;
            var headerBytes = (bytes[offset] & 3) == 2 ? 1 : (bytes[offset + 1] >> 4) * 4;
            var code = image.GetMethodBody(probe.RelativeVirtualAddress).GetILBytes()!;
            Assert.That(code[^1], Is.EqualTo(0x2a), "The fixture must end in ret.");
            if (arrayTypeToken is { } token)
            {
                var newArray = Array.IndexOf(code, (byte)0x8d);
                Assert.That(newArray, Is.GreaterThanOrEqualTo(0), "The fixture must allocate an array.");
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + headerBytes + newArray + 1, 4), token);
            }
            else if (bodyMutation == 3)
            {
                Assert.That(code.Length, Is.GreaterThanOrEqualTo(5));
                bytes[offset + headerBytes] = 0x38;
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset + headerBytes + 1, 4), int.MaxValue);
            }
            else
            { bytes[offset + headerBytes + code.Length - 1] = bodyMutation == 1 ? (byte)0xfe : bodyMutation == 4 ? (byte)0x24 : (byte)0x00; }
            using var mutatedStream = new MemoryStream(bytes, false);
            using var mutatedImage = new PEReader(mutatedStream);
            var mutatedBody = mutatedImage.GetMethodBody(probe.RelativeVirtualAddress);
            return sourceBody ? (null, CompilerTaskCarrierCollector.ValidateSourceBody(mutatedImage.GetMetadataReader(), mutatedBody,
                compilation.GetSpecialType(SpecialType.System_Int32).ContainingAssembly.Identity, TestContext.CurrentContext.CancellationToken)) :
                CompilerTaskCarrierCollector.ValidateCallbackForwarder(mutatedBody, targets, TestContext.CurrentContext.CancellationToken);
        }
        if (sourceBody)
        {
            return (null, CompilerTaskCarrierCollector.ValidateSourceBody(metadata, image.GetMethodBody(probe.RelativeVirtualAddress),
            foreignFramework ? new AssemblyIdentity("Foreign") : compilation.GetSpecialType(SpecialType.System_Int32).ContainingAssembly.Identity,
            TestContext.CurrentContext.CancellationToken));
        }
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
        Assert.That(result.TaskNonEmptyTraceCaptureCountUpper, Is.EqualTo(count + (configured ? 1 : 0)));
        Assert.That(result.TaskTraceRestoreCountUpper, Is.EqualTo(configured ? 1 : 0));
        Assert.That(result.TaskDispatchArrayCopiesUpper, Is.EqualTo(2 * count + (configured ? 4 : 0)));
        Assert.That(result.RequiredTraceCopyModel, Is.EqualTo(NativeTaskFaultCarrierStorageVerifier.TraceCopyModel));
        Assert.That(result.ContextTraceCaptureCountUpper, Is.EqualTo(count + (configured ? 1 : 0)));
        Assert.That(result.ContextTraceRestoreCountUpper, Is.EqualTo(count + (configured ? 1 : 0)));
        Assert.That(result.CombinedDispatchArrayCopiesUpper, Is.EqualTo(6 * count + (configured ? 8 : 0)));
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
        Assert.That(result.TaskNonEmptyTraceCaptureCountUpper, Is.Null);
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
        Assert.That(incompatible.TaskNonEmptyTraceCaptureCountUpper, Is.Null);
        Assert.That(incompatible.TaskDispatchArrayCopiesUpper, Is.Null);
        Assert.That(incompatible.RequiredTraceCopyModel, Is.Null);
        Assert.That(incompatible.CombinedDispatchArrayCopiesUpper, Is.Null);
        Assert.That(incompatible.RequiredCombinedTraceCopyModel, Is.Null);
        Assert.That(incompatible.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        var child = projection.Children[1];
        projection = projection with { Children = [projection.Children[0], child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } }] };
        var unowned = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(unowned.BookkeepingUpper, Is.Null);
        Assert.That(unowned.TaskNonEmptyTraceCaptureCountUpper, Is.Null);
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
