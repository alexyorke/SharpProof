using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PortableTotalExecutionTests
{
    [Test]
    public void TotalExceptionEdgesHavocOriginsAndSourceSpansRoundTrip()
    {
        var graph = CreateGraph();
        var decoded = PortableIrGraphCodec.Decode(graph);
        var json = JsonSerializer.Serialize(graph, WorkerProtocolJson.SharedOptions);
        Assert.That(decoded.Factory.Semantics, Is.EqualTo(IrExecutionSemantics.Total));
        Assert.That(decoded.Factory.GetTypeInfo(decoded.Factory.IntegerType).Width, Is.EqualTo(32));
        var result = new IrProgramInterpreter(decoded.Factory).Execute(decoded.Program!, null, 100,
            new IrProgramReplayOptions(_ => decoded.Factory.CreateIntegerValue(-1)));
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.Overflow));
        var span = decoded.Factory.GetOperationInfo(result.Exception.Site!.Value).SourceSpan!;
        Assert.That((span.Document, span.Start, span.Length), Is.EqualTo(("Subject.cs", 23, 7)));
        Assert.That(result.ConsumedApproximation, Is.False);
        Assert.That(JsonSerializer.Serialize(PortableIrGraphCodec.Encode(decoded.Factory,
            decoded.Program, decoded.Roots).Graph, WorkerProtocolJson.SharedOptions), Is.EqualTo(json));
    }

    [TestCase("semantics")]
    [TestCase("origin")]
    [TestCase("sourceSpan")]
    [TestCase("document")]
    [TestCase("start")]
    [TestCase("length")]
    public void MissingSemanticWireFieldsFailClosed(string field)
    {
        var json = JsonSerializer.SerializeToNode(CreateGraph(), WorkerProtocolJson.SharedOptions)!;
        var row = field switch
        {
            "semantics" => json,
            "origin" => json["blocks"]![0]!["instructions"]![0]!,
            "sourceSpan" => json["operations"]!.AsArray().First(operation => operation!["sourceSpan"] != null)!,
            _ => json["operations"]!.AsArray().First(operation => operation!["sourceSpan"] != null)!["sourceSpan"]!
        };
        Assert.That(row.AsObject().Remove(field), Is.True);
        Assert.Throws<JsonException>((Action)(() =>
        {
            _ = JsonSerializer.Deserialize<PortableIrGraph>(json.ToJsonString(), WorkerProtocolJson.SharedOptions);
        }));
    }

    [TestCase("mode")]
    [TestCase("origin")]
    [TestCase("unused-origin")]
    [TestCase("span-start")]
    [TestCase("span-length")]
    [TestCase("span-overflow")]
    [TestCase("span-document")]
    [TestCase("throw-kind")]
    [TestCase("throw-target")]
    public void MalformedSemanticMetadataIsRejected(string mutation)
    {
        var graph = CreateGraph();
        var instructions = graph.Blocks.SelectMany(block => block.Instructions).ToArray();
        var span = graph.Operations.Single(operation => operation.SourceSpan != null).SourceSpan!;
        switch (mutation)
        {
            case "mode":
                graph.Semantics = (IrExecutionSemantics)99;
                break;
            case "origin":
                instructions[0].Origin = (IrHavocOrigin)99;
                break;
            case "unused-origin":
                instructions.Single(row => row.Kind == IrInstructionKind.Throw).Origin = IrHavocOrigin.Input;
                break;
            case "span-start":
                span.Start = -1;
                break;
            case "span-length":
                span.Length = -1;
                break;
            case "span-overflow":
                span.Start = int.MaxValue;
                break;
            case "span-document":
                span.Document = " ";
                break;
            case "throw-kind":
                instructions.Single(row => row.Kind == IrInstructionKind.Throw).A = 99;
                break;
            case "throw-target":
                instructions.Single(row => row.Kind == IrInstructionKind.Throw).B = 99;
                break;
        }
        Assert.Throws<InvalidDataException>((Action)(() =>
        {
            _ = PortableIrGraphCodec.Decode(graph);
        }));
    }

    [Test]
    public void UnsignedModelPresentationUsesExactDecimalValue()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(64, false);
        Assert.That(WorkerProjections.FormatValue(factory.CreateIntegerValue(type, ulong.MaxValue)),
            Is.EqualTo(("Integer", "18446744073709551615")));
    }

    private static PortableIrGraph CreateGraph()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var variable = factory.CreateVariable("value", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exit = builder.CreateBlock();
        builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Approximation, variable);
        builder.Throw(entry, factory.CreateOperation("arithmetic", new IrSourceSpan("Subject.cs", 23, 7)),
            IrExceptionKind.Overflow, exit);
        builder.ExceptionalExit(exit, factory.CreateOperation());
        return PortableIrGraphCodec.Encode(builder.Build(), []).Graph;
    }
}
