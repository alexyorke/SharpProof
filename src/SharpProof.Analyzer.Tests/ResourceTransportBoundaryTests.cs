using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class ResourceTransportBoundaryTests
{
    private static ResourceExpression Input(string name = "n")
    {
        ArgumentNullException.ThrowIfNull(name);
        return ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, name, 1000));
    }

    [TestCase(5, 1)]
    [TestCase(6, 2)]
    [TestCase(7, 2)]
    [TestCase(8, 2)]
    [TestCase(9, 4)]
    public void UnaryOperationsPreserveIntegerSemanticsAcrossTransport(int kind, int expected)
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var expression = kind switch
        {
            5 => ResourceExpression.Difference(Input(), 2),
            6 => ResourceExpression.DivideCeiling(Input(), 2),
            7 => ResourceExpression.Geometric(Input(), 2, true, cancellation),
            8 => ResourceExpression.Geometric(Input(), 2, false, cancellation),
            _ => ResourceExpression.Aligned(Input(), 2)
        };
        var decoded = ResourceExpressionCodec.Decode(ResourceExpressionCodec.Encode(expression, cancellation), cancellation);
        Assert.That(decoded, Is.EqualTo(expression));
        Assert.That(decoded.Evaluate(static _ => 3, cancellation), Is.EqualTo(new BigInteger(expected)));
    }

    [TestCase("arity")]
    [TestCase("input-number")]
    [TestCase("binary-number")]
    [TestCase("constant-input")]
    [TestCase("integer-type")]
    [TestCase("missing-field")]
    public void CorruptedArithmeticRecordsCannotReachTheProofKernel(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var root = JsonNode.Parse(ResourceExpressionCodec.Encode(ResourceExpression.Add(Input(), ResourceExpression.One), cancellation))!.AsObject();
        var node = root["expression"]!.AsObject();
        switch (corruption)
        {
            case "arity":
                node["operands"] = new JsonArray();
                break;
            case "input-number":
                node = node["operands"]![0]!.AsObject();
                node["number"] = "1";
                break;
            case "binary-number":
                node["number"] = "1";
                break;
            case "constant-input":
                node = node["operands"]![1]!.AsObject();
                node["input"] = new JsonObject();
                break;
            case "integer-type":
                node["kind"] = "2";
                break;
            default:
                node.Remove("number");
                break;
        }
        Action decode = () => ResourceExpressionCodec.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), cancellation);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void EncodingBudgetBoundsRepeatedLabelsRatherThanOnlyUniqueObjects()
    {
        var expression = Input(new string('n', 2000));
        for (int level = 0; level < 10; level++)
        { expression = ResourceExpression.Add(expression, expression); }
        Assert.That(expression.NodeCount, Is.LessThan(ResourceExpression.MaximumNodes));
        Action encode = () => ResourceExpressionCodec.Encode(expression, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<ResourceExpressionLimitException>(encode);
    }
    [TestCase("width")]
    [TestCase("step")]
    [TestCase("overflow-mode")]
    [TestCase("integer-type")]
    [TestCase("missing-field")]
    [TestCase("unknown-field")]
    [TestCase("record-type")]
    [TestCase("text-type")]
    [TestCase("noncanonical-expression")]
    public void CorruptedProgramRecordsCannotReachTheNativeChecker(string corruption)
    {
        ArgumentNullException.ThrowIfNull(corruption);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var rank = new ResourceLoopRank(ResourceLoopKind.IncreasingAdd, ResourceExpression.Zero, Input(), 1, 32, false);
        var program = ResourceProgram.Repeat(rank, ResourceRepeatKind.Loop, ResourceProgram.Array(Input()));
        var root = JsonNode.Parse(ResourceProgramCodec.Encode(program, cancellation))!.AsObject();
        var node = root["program"]!.AsObject();
        switch (corruption)
        {
            case "width":
                node["rank"]!["width"] = 16;
                break;
            case "step":
                node["rank"]!["step"] = "01";
                break;
            case "overflow-mode":
                node["rank"]!["checked"] = 1;
                break;
            case "integer-type":
                node["kind"] = "4";
                break;
            case "missing-field":
                node.Remove("length");
                break;
            case "unknown-field":
                node["extra"] = 0;
                break;
            case "record-type":
                root["program"] = new JsonArray();
                break;
            case "text-type":
                node["rank"]!["initial"] = 0;
                break;
            default:
                node["rank"]!["initial"] = " " + node["rank"]!["initial"]!.GetValue<string>();
                break;
        }
        Action decode = () => ResourceProgramCodec.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), cancellation);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void ProgramEncodingHasItsOwnAggregatePayloadBudget()
    {
        var program = ResourceProgram.Array(Input(new string('n', 2000)));
        for (int level = 0; level < 9; level++)
        { program = ResourceProgram.Sequence(program, program); }
        Assert.That(program.NodeCount, Is.LessThan(ResourceExpression.MaximumNodes));
        Action encode = () => ResourceProgramCodec.Encode(program, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<ResourceExpressionLimitException>(encode);
    }
    private static ResourceExpression PayloadTree(int repeatedLabelLength, int finalLabelLength)
    {
        var repeated = Input(new string('n', repeatedLabelLength));
        for (int level = 0; level < 9; level++)
        { repeated = ResourceExpression.Add(repeated, repeated); }
        return ResourceExpression.Add(repeated, Input(new string('t', finalLabelLength)));
    }

    [Test]
    public void EncodingReservesTheEnvelopeByteAtTheExactPayloadBoundary()
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var baseline = ResourceExpressionCodec.Encode(PayloadTree(1, 1), cancellation);
        var available = ResourceExpressionCodec.MaximumPayloadBytes - baseline.Length;
        var repeated = 1 + available / 512;
        var final = 1 + available % 512;
        var exact = PayloadTree(repeated, final);
        var payload = ResourceExpressionCodec.Encode(exact, cancellation);
        Assert.That(payload, Has.Length.EqualTo(ResourceExpressionCodec.MaximumPayloadBytes));
        Assert.That(ResourceExpressionCodec.Decode(payload, cancellation), Is.EqualTo(exact));
        Action exceed = () => ResourceExpressionCodec.Encode(PayloadTree(repeated, final + 1), cancellation);
        Assert.Throws<ResourceExpressionLimitException>(exceed);
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    public void ProgramTransportRejectsUnknownKindsArityAndUnrelatedActivationPayloads(int mutation)
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var program = ResourceProgram.Activate(new ResourceActivationShape([], false, false), ResourceProgram.Zero);
        var root = JsonNode.Parse(ResourceProgramCodec.Encode(program, token))!;
        var node = root["program"]!;
        switch (mutation)
        {
            case 0:
                node["kind"] = 999;
                break;
            case 1:
                node["repeatKind"] = 999;
                break;
            case 2:
                node["repeatKind"] = (int)ResourceRepeatKind.TailRecurrence;
                break;
            case 3:
                node["children"] = null;
                break;
            case 4:
                node["children"] = new JsonArray();
                break;
            case 5:
                node["rank"] = new JsonObject();
                break;
            case 6:
                node["activation"]!["variables"] = null;
                break;
            case 7:
                var variables = new JsonArray();
                for (var index = 0; index <= ResourceActivationShape.MaximumVariables; index++)
                { variables.Add((int)ResourceStorageKind.Word32); }
                node["activation"]!["variables"] = variables;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        var payload = Encoding.UTF8.GetBytes(root.ToJsonString());
        Assert.Throws<InvalidDataException>((Action)(() => ResourceProgramCodec.Decode(payload, token)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProgramTransportRejectsEmptyAndOversizedEnvelopes(bool oversized)
    {
        var payload = oversized ? new byte[ResourceProgramCodec.MaximumPayloadBytes + 1] : [];
        Assert.Throws<InvalidDataException>((Action)(() => ResourceProgramCodec.Decode(payload, TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void ProgramTransportCountsWireDepthInsteadOfAcceptingProducerDepthClaims()
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var root = JsonNode.Parse(ResourceProgramCodec.Encode(ResourceProgram.Zero, token))!;
        var template = JsonNode.Parse(ResourceProgramCodec.Encode(
            ResourceProgram.Activate(new ResourceActivationShape([], false, false), ResourceProgram.Zero), token))!["program"]!;
        var node = root["program"]!.DeepClone();
        for (var depth = 0; depth < ResourceExpression.MaximumDepth + 1; depth++)
        {
            var parent = template.DeepClone();
            parent["children"] = new JsonArray(node);
            node = parent;
        }
        root["program"] = node;
        var options = new System.Text.Json.JsonSerializerOptions { MaxDepth = ResourceExpression.MaximumDepth * 3 + 8 };
        var payload = Encoding.UTF8.GetBytes(root.ToJsonString(options));
        Assert.That(payload.Length, Is.LessThan(ResourceProgramCodec.MaximumPayloadBytes));
        Assert.Throws<InvalidDataException>((Action)(() => ResourceProgramCodec.Decode(payload, token)));
    }

    [Test]
    public void ManagedAllocationTransportRequiresCanonicalBase64()
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var root = JsonNode.Parse(ResourceProgramCodec.Encode(ResourceProgram.Managed(ResourceAllocationShape.Object([])), token))!;
        root["program"]!["allocation"] = " " + root["program"]!["allocation"]!.GetValue<string>();
        Assert.Throws<InvalidDataException>((Action)(() => ResourceProgramCodec.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), token)));
    }

    [TestCase(2, 5)]
    [TestCase(10, 10)]
    public void MaximumArithmeticKeepsItsMeaningAcrossTransport(int size, int expected)
    {
        var token = TestContext.CurrentContext.CancellationToken;
        var expression = ResourceExpression.Max(Input(), ResourceExpression.Literal(5));
        var decoded = ResourceExpressionCodec.Decode(ResourceExpressionCodec.Encode(expression, token), token);
        Assert.That(decoded, Is.EqualTo(expression));
        Assert.That(decoded.Evaluate(_ => size, token), Is.EqualTo(new BigInteger(expected)));
    }
}
