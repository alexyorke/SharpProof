using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class TypedPortableIntegerTests
{
    [Test]
    public void RoundTripPreservesWidthSignednessBitsAndOperators()
    {
        var factory = new IrFactory();
        var roots = new List<IrTerm>();
        var values = new Dictionary<IrVarId, IrValue>();
        foreach (var width in new[] { 8, 16, 32, 64 })
        {
            foreach (var isSigned in new[] { false, true })
            {
                var type = factory.GetOrCreateIntegerType(width, isSigned);
                var mask = width == 64 ? ulong.MaxValue : (1UL << width) - 1;
                var variable = factory.CreateVariable($"input{width}{isSigned}", type);
                values.Add(variable, factory.CreateIntegerValueFromBits(type, mask));
                roots.Add(factory.IntegerBits(type, mask));
                roots.Add(factory.Binary(IrBinaryOperator.Add, factory.Variable(variable), factory.IntegerBits(type, 1)));
                roots.Add(factory.Binary(IrBinaryOperator.LessThan, factory.Variable(variable), factory.IntegerBits(type, 1)));
                roots.Add(factory.Cast(factory.GetOrCreateIntegerType(32, true), factory.Variable(variable)));
            }
        }
        var encoded = PortableIrGraphCodec.Encode(factory, null, roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var json = JsonSerializer.Serialize(encoded.Graph, WorkerProtocolJson.SharedOptions);
        var decoded = PortableIrGraphCodec.Decode(JsonSerializer.Deserialize<PortableIrGraph>(
            json, WorkerProtocolJson.SharedOptions)!, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decodedValues = encoded.VariableIndices.ToDictionary(
            pair => decoded.Variables[pair.Value],
            pair => decoded.Factory.CreateIntegerValueFromBits(
                decoded.Factory.GetVariableInfo(decoded.Variables[pair.Value]).Type,
                values[pair.Key].IntegerBits));
        for (var index = 0; index < roots.Count; index++)
        {
            var expected = new IrInterpreter(factory).Evaluate(roots[index], values);
            var actual = new IrInterpreter(decoded.Factory).Evaluate(decoded.Roots[index], decodedValues);
            Assert.That(actual.Status, Is.EqualTo(expected.Status));
            if (expected.Value!.Kind == IrValueKind.Integer)
            {
                Assert.That(actual.Value!.IntegerBits, Is.EqualTo(expected.Value.IntegerBits));
                Assert.That((actual.Value.IntegerWidth, actual.Value.IntegerSigned),
                    Is.EqualTo((expected.Value.IntegerWidth, expected.Value.IntegerSigned)));
            }
            else
            {
                Assert.That(actual.Value!.Boolean, Is.EqualTo(expected.Value.Boolean));
            }
        }
        Assert.That(JsonSerializer.Serialize(PortableIrGraphCodec.Encode(
                decoded.Factory, null, decoded.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Graph, WorkerProtocolJson.SharedOptions),
            Is.EqualTo(json));
    }

    [TestCase("width")]
    [TestCase("signed")]
    [TestCase("bits")]
    public void MissingIntegerWireMetadataIsRejected(string field)
    {
        System.ArgumentNullException.ThrowIfNull(field);
        var graph = CreateLiteralGraph(64);
        var json = JsonSerializer.SerializeToNode(graph, WorkerProtocolJson.SharedOptions)!;
        var row = field == "bits" ? json["terms"]![0]!
            : json["types"]!.AsArray().Single(type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type!["width"]!.GetValue<int>() == 64))!;
        Assert.That(row.AsObject().Remove(field), Is.True);
        Assert.Throws<JsonException>((Action)(() => JsonSerializer.Deserialize<PortableIrGraph>(json.ToJsonString(),
            WorkerProtocolJson.SharedOptions)));
    }

    [TestCase(0)]
    [TestCase(7)]
    [TestCase(128)]
    public void InvalidTypedWidthCannotBecomeLegacy(int width)
    {
        var graph = CreateLiteralGraph(64);
        graph.Types.Single(type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.Width == 64)).Width = width;
        Assert.Throws<InvalidDataException>((Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void SignednessAndOutOfWidthBitsAreRejected()
    {
        var graph = CreateLiteralGraph(64);
        graph.Types.Single(type => type is null ? throw new System.ArgumentNullException(nameof(type)) : (type.Width == 64)).Signed = true;
        Assert.Throws<InvalidDataException>((Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        graph = CreateLiteralGraph(8);
        graph.Terms[0].Bits = 256;
        Assert.Throws<InvalidDataException>((Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    private static PortableIrGraph CreateLiteralGraph(int width)
    {
        var factory = new IrFactory();
        var type = factory.GetOrCreateIntegerType(width, false);
        return PortableIrGraphCodec.Encode(factory, null,
            [factory.IntegerBits(type, width == 64 ? ulong.MaxValue : (1UL << width) - 1)], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Graph;
    }
}
