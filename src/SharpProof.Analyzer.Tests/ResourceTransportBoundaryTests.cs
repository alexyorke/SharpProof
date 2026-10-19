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
}
