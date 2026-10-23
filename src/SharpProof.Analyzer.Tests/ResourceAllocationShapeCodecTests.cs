using System.Collections.Immutable;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceAllocationShapeCodecTests
{
    [Test]
    public void EveryAllocationCategoryRoundTripsStorageFactsWithoutByteVerdicts()
    {
        var extent = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "n", int.MaxValue));
        var shapes = new[]
        {
            ResourceAllocationShape.Unsupported,
            ResourceAllocationShape.Array(ResourceStorageKind.Byte, extent),
            ResourceAllocationShape.Array(ResourceStorageKind.Reference, extent),
            ResourceAllocationShape.Object([ResourceStorageKind.Word32, ResourceStorageKind.Byte]),
            ResourceAllocationShape.String(extent),
            ResourceAllocationShape.Box(ResourceStorageKind.Word64),
            ResourceAllocationShape.Delegate(),
            ResourceAllocationShape.Closure([ResourceStorageKind.Reference, ResourceStorageKind.Word32])
        };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        foreach (var shape in shapes)
        {
            var payload = ResourceAllocationShapeCodec.Encode(shape, cancellation);
            var decoded = ResourceAllocationShapeCodec.Decode(payload, cancellation);
            Assert.That(decoded.Kind, Is.EqualTo(shape.Kind));
            Assert.That(decoded.Storage, Is.EqualTo(shape.Storage));
            Assert.That(decoded.Extent, Is.EqualTo(shape.Extent));
            Assert.That(decoded.Fields, Is.EqualTo(shape.Fields));
            Assert.That(ResourceAllocationShapeCodec.Encode(decoded, cancellation), Is.EqualTo(payload));
            var original = ResourceRuntimeLayout.Bound(shape, new("CoreCLR", "8.0.32", "x64", 8), cancellation);
            var transported = ResourceRuntimeLayout.Bound(decoded, new("CoreCLR", "8.0.32", "x64", 8), cancellation);
            Assert.That(transported.UnknownReason, Is.EqualTo(original.UnknownReason));
            if (original.Bounds is { } expected)
            {
                Assert.That(transported.Bounds!.Lower, Is.EqualTo(expected.Lower));
                Assert.That(transported.Bounds.Upper, Is.EqualTo(expected.Upper));
            }
            Assert.That(Encoding.UTF8.GetString(payload), Does.Not.Contain("upper"));
            Assert.That(Encoding.UTF8.GetString(payload), Does.Not.Contain("CoreCLR"));
        }
    }

    [TestCase("version")]
    [TestCase("expressionModel")]
    [TestCase("kind")]
    [TestCase("storage")]
    [TestCase("extent")]
    [TestCase("base64")]
    [TestCase("fields")]
    [TestCase("field-kind")]
    [TestCase("unspecified-field")]
    [TestCase("missing")]
    [TestCase("duplicate")]
    [TestCase("byte-verdict")]
    [TestCase("runtime-assertion")]
    [TestCase("unrelated-storage")]
    [TestCase("unrelated-extent")]
    [TestCase("unrelated-fields")]
    [TestCase("wrong-string-storage")]
    [TestCase("boxed-reference")]
    public void MalformedOrUnrelatedPayloadsCannotBecomeAllocationShapes(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var bytes = ResourceAllocationShapeCodec.Encode(ResourceAllocationShape.Object([ResourceStorageKind.Word32]), cancellation);
        var root = JsonNode.Parse(bytes)!.AsObject();
        switch (corruption)
        {
            case "version":
                root["version"] = 99;
                break;
            case "expressionModel":
                root["expressionModel"] = 99;
                break;
            case "kind":
                root["kind"] = 999;
                break;
            case "storage":
                root["storage"] = 999;
                break;
            case "extent":
                root["extent"] = null;
                break;
            case "base64":
                root["extent"] = " " + root["extent"]!.GetValue<string>();
                break;
            case "fields":
                root["fields"] = null;
                break;
            case "field-kind":
                root["fields"] = new JsonArray(999);
                break;
            case "unspecified-field":
                root["fields"] = new JsonArray(0);
                break;
            case "missing":
                root.Remove("fields");
                break;
            case "byte-verdict":
                root["upper"] = 24;
                break;
            case "runtime-assertion":
                root["runtime"] = "CoreCLR";
                break;
            case "unrelated-storage":
                root["storage"] = (int)ResourceStorageKind.Byte;
                break;
            case "unrelated-extent":
                root["extent"] = Convert.ToBase64String(ResourceExpressionCodec.Encode(ResourceExpression.Literal(2), cancellation));
                break;
            case "unrelated-fields":
                root["kind"] = (int)ResourceAllocationKind.Delegate;
                break;
            case "wrong-string-storage":
                root["kind"] = (int)ResourceAllocationKind.String;
                root["storage"] = (int)ResourceStorageKind.Word32;
                root["fields"] = new JsonArray();
                break;
            case "boxed-reference":
                root["kind"] = (int)ResourceAllocationKind.Box;
                root["storage"] = (int)ResourceStorageKind.Reference;
                root["fields"] = new JsonArray();
                break;
        }
        var json = root.ToJsonString();
        if (corruption == "duplicate")
        { json = json.Replace("\"version\":1,", "\"version\":1,\"version\":1,", StringComparison.Ordinal); }
        Action decode = () => ResourceAllocationShapeCodec.Decode(Encoding.UTF8.GetBytes(json), cancellation);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void AllocationTransportEnforcesPayloadAndFieldBudgetsAndCancellation()
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var payload = ResourceAllocationShapeCodec.Encode(ResourceAllocationShape.Object([]), cancellation);
        Action empty = () => ResourceAllocationShapeCodec.Decode([], cancellation);
        Action oversized = () => ResourceAllocationShapeCodec.Decode(new byte[ResourceAllocationShapeCodec.MaximumPayloadBytes + 1], cancellation);
        Assert.Throws<InvalidDataException>(empty);
        Assert.Throws<InvalidDataException>(oversized);
        var root = JsonNode.Parse(payload)!.AsObject();
        var fields = new JsonArray();
        for (int index = 0; index <= ResourceAllocationShape.MaximumFields; index++)
        { fields.Add((int)ResourceStorageKind.Byte); }
        root["fields"] = fields;
        Action tooMany = () => ResourceAllocationShapeCodec.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), cancellation);
        Assert.Throws<InvalidDataException>(tooMany);
        Action encodeCanceled = () => ResourceAllocationShapeCodec.Encode(ResourceAllocationShape.Delegate(), new CancellationToken(true));
        Action decodeCanceled = () => ResourceAllocationShapeCodec.Decode(payload, new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(encodeCanceled);
        Assert.Throws<OperationCanceledException>(decodeCanceled);
        var maximum = ResourceAllocationShape.Object(Enumerable.Repeat(ResourceStorageKind.Word64, ResourceAllocationShape.MaximumFields).ToImmutableArray());
        var roundTrip = ResourceAllocationShapeCodec.Decode(ResourceAllocationShapeCodec.Encode(maximum, cancellation), cancellation);
        Assert.That(roundTrip.Fields, Has.Length.EqualTo(ResourceAllocationShape.MaximumFields));
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void AllocationShapesRejectMalformedRootsIntegersAndUnknownKinds(int mutation)
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var root = JsonNode.Parse(ResourceAllocationShapeCodec.Encode(ResourceAllocationShape.Delegate(), token))!;
        switch (mutation)
        {
            case 0:
                root = new JsonArray();
                break;
            case 1:
                root["version"] = "1";
                break;
            case 2:
                root["storage"] = 0.5;
                break;
            case 3:
                root["kind"] = long.MaxValue;
                break;
            case 4:
                root["kind"] = 999;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var payload = Encoding.UTF8.GetBytes(root.ToJsonString());
        Assert.Throws<InvalidDataException>((Action)(() => ResourceAllocationShapeCodec.Decode(payload, token)));
    }

    [Test]
    public void AllocationEnvelopeEnforcesItsOwnBudgetAfterBase64Expansion()
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var extent = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, new string('n', 13000), 1000));
        for (var level = 0; level < 6; level++)
        { extent = ResourceExpression.Add(extent, extent); }
        var arithmetic = ResourceExpressionCodec.Encode(extent, token);
        Assert.That(arithmetic.Length, Is.LessThanOrEqualTo(ResourceExpressionCodec.MaximumPayloadBytes));
        Assert.That(Convert.ToBase64String(arithmetic).Length, Is.GreaterThan(ResourceAllocationShapeCodec.MaximumPayloadBytes));
        var shape = ResourceAllocationShape.Array(ResourceStorageKind.Byte, extent);
        Assert.Throws<ResourceExpressionLimitException>((Action)(() => ResourceAllocationShapeCodec.Encode(shape, token)));
    }
}
