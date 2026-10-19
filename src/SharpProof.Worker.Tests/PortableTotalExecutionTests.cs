using System.Collections.Immutable;
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
        var decoded = PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
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
            decoded.Program, decoded.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Graph, WorkerProtocolJson.SharedOptions), Is.EqualTo(json));
    }

    [TestCase("semantics")]
    [TestCase("origin")]
    [TestCase("sourceSpan")]
    [TestCase("document")]
    [TestCase("start")]
    [TestCase("length")]
    public void MissingSemanticWireFieldsFailClosed(string field)
    {
        System.ArgumentNullException.ThrowIfNull(field);
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
        System.ArgumentNullException.ThrowIfNull(mutation);
        var graph = CreateGraph();
        var instructions = graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).ToImmutableArray();
        var span = graph.Operations.Single(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.SourceSpan != null)).SourceSpan!;
        switch (mutation)
        {
            case "mode":
                graph = graph with { Semantics = (IrExecutionSemantics)99 };
                break;
            case "origin":
                graph = graph.ReplaceRow(instructions[0], instructions[0] with { Origin = (IrHavocOrigin)99 });
                break;
            case "unused-origin":
                graph = graph.ReplaceRow(instructions.Single(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Throw)), instructions.Single(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Throw)) with { Origin = IrHavocOrigin.Input });
                break;
            case "span-start":
                {
                    (span, graph) = graph.ChangeRow(span, span with { Start = -1 });
                }
                break;
            case "span-length":
                {
                    (span, graph) = graph.ChangeRow(span, span with { Length = -1 });
                }
                break;
            case "span-overflow":
                {
                    (span, graph) = graph.ChangeRow(span, span with { Start = int.MaxValue });
                }
                break;
            case "span-document":
                {
                    (span, graph) = graph.ChangeRow(span, span with { Document = " " });
                }
                break;
            case "throw-kind":
                graph = graph.ReplaceRow(instructions.Single(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Throw)), instructions.Single(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Throw)) with { A = 99 });
                break;
            case "throw-target":
                graph = graph.ReplaceRow(instructions.Single(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Throw)), instructions.Single(row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Throw)) with { B = 99 });
                break;
        }
        Assert.Throws<InvalidDataException>((Action)(() =>
        {
            _ = PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        return PortableIrGraphCodec.Encode(builder.Build(), [], cancellationToken: TestContext.CurrentContext.CancellationToken).Graph;
    }
}
