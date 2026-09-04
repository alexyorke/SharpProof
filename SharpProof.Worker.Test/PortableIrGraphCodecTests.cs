using System.Security.Cryptography;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PortableIrGraphCodecTests
{
    [Test]
    public void RoundTripPreservesEveryTermInstructionAndLocationShape()
    {
        var fixture = CreateFixture();

        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph);
        var encodedAgain = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                encoded.Graph.Terms.Select(static row => row.Kind).Distinct(),
                Is.EquivalentTo(Enum.GetValues<IrTermKind>()));
            Assert.That(
                encoded.Graph.Blocks
                    .SelectMany(static block => block.Instructions)
                    .Select(static row => row.Kind)
                    .Distinct(),
                Is.EquivalentTo(Enum.GetValues<IrInstructionKind>()));
            Assert.That(
                encoded.Graph.Blocks
                    .SelectMany(static block => block.Instructions)
                    .Where(static row => row.Location != null)
                    .Select(static row => row.Location!.Kind)
                    .Distinct(),
                Is.EquivalentTo(Enum.GetValues<IrLocationKind>()));
            Assert.That(decoded.Program, Is.Not.Null);
            Assert.That(decoded.Roots, Has.Count.EqualTo(fixture.Roots.Length));
            Assert.That(decoded.Variables, Has.Count.EqualTo(encoded.Graph.Variables.Length));
            Assert.That(decoded.Blocks, Has.Count.EqualTo(encoded.Graph.Blocks.Length));
            Assert.That(
                decoded.Instructions,
                Has.Count.EqualTo(
                    encoded.Graph.Blocks.Sum(static block => block.Instructions.Length)));
            AssertGraphJsonEqual(encoded.Graph, encodedAgain.Graph);
        }

        var decodedConditional = (IrConditionalTerm)decoded.Roots[fixture.ConditionalRoot];
        var decodedFlag = (IrVariableTerm)decoded.Roots[fixture.FlagRoot];
        Assert.That(decodedConditional.Condition, Is.SameAs(decodedFlag));

        var call = decoded.Program!.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrCallInstruction>()
            .Single();
        var memberLoad = decoded.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrLoadInstruction>()
            .Select(static load => load.Location)
            .OfType<IrMemberLocation>()
            .Single();
        Assert.That(
            decoded.Factory.GetMemberInfo(call.Member).Identity,
            Is.EqualTo(decoded.Factory.GetMemberInfo(memberLoad.Member).Identity));
        Assert.That(
            decoded.Instructions
                .Select(static instruction => instruction.Operation)
                .Distinct()
                .Count(),
            Is.EqualTo(decoded.Instructions.Count));
    }

    [Test]
    public void ProgramVariableCollectionCoversEveryInstructionAndLocationShape()
    {
        var fixture = CreateFixture();
        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots);

        var variables = CompilerLoweredArtifact.CollectProgramVariables(
            fixture.Program);

        Assert.That(
            variables,
            Is.EquivalentTo(encoded.VariableIndices.Keys));
    }

    [Test]
    public void RoundTripPreservesExplicitExtraVariables()
    {
        var fixture = CreateFixture();
        var unused = fixture.Factory.CreateVariable(
            "unused", fixture.Factory.IntegerType);

        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots,
            [unused]);
        var decoded = PortableIrGraphCodec.Decode(
            encoded.Graph,
            [encoded.VariableIndices[unused]]);
        var reencoded = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots,
            decoded.Variables);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                decoded.Variables,
                Has.Count.EqualTo(encoded.Graph.Variables.Length));
            AssertGraphJsonEqual(encoded.Graph, reencoded.Graph);
        }
    }

    [Test]
    public void MetadataRowsProjectEveryDeclaredValue()
    {
        var factory = new IrFactory();
        var sequenceType = factory.GetOrCreateSequenceType(
            factory.CreateIdentity(),
            factory.IntegerType,
            "Numbers");
        var items = factory.CreateVariable("items", sequenceType);
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "Transform",
            factory.IntegerType,
            isStatic: true,
            sequenceType);
        IrTerm[] roots = [
            factory.ImpureOpaque(
                factory.CreateOperation(),
                member,
                null,
                factory.Variable(items)),
            factory.ImpureOpaque(
                factory.CreateOperation("described"),
                member,
                null,
                factory.Variable(items))
        ];

        var graph = PortableIrGraphCodec.Encode(
            factory,
            program: null,
            roots,
            [items]).Graph;
        var sequenceIndex = Array.FindIndex(
            graph.Types,
            static row => row.Name == "Numbers");
        var integerIndex = Array.FindIndex(
            graph.Types,
            static row => row.Kind == IrTypeKind.Integer);
        var variable = graph.Variables.Single(static row => row.Name == "items");
        var memberRow = graph.Members.Single();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(sequenceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(graph.Types[sequenceIndex].Kind, Is.EqualTo(IrTypeKind.Sequence));
            Assert.That(graph.Types[sequenceIndex].Element, Is.EqualTo(integerIndex));
            Assert.That(variable.Type, Is.EqualTo(sequenceIndex));
            Assert.That(memberRow.Identity, Is.EqualTo(0));
            Assert.That(memberRow.DeclaringType, Is.GreaterThanOrEqualTo(0));
            Assert.That(memberRow.Name, Is.EqualTo("Transform"));
            Assert.That(memberRow.ReturnType, Is.EqualTo(integerIndex));
            Assert.That(memberRow.IsStatic, Is.True);
            Assert.That(memberRow.ParameterTypes, Is.EqualTo([sequenceIndex]));
            Assert.That(
                graph.Operations.Select(static row => row.Description),
                Is.EqualTo(new string?[] { null, "described" }));
        }
    }

    [Test]
    public void RoundTripPreservesEveryWireEnumVariant()
    {
        var factory = new IrFactory();
        var boolVariable = factory.CreateVariable("flag", factory.BooleanType);
        var integerVariable = factory.CreateVariable("number", factory.IntegerType);
        var stringVariable = factory.CreateVariable("text", factory.StringType);
        var boolTerm = factory.Variable(boolVariable);
        var integerTerm = factory.Variable(integerVariable);
        var stringTerm = factory.Variable(stringVariable);
        var unaryTerms = Enum.GetValues<IrUnaryOperator>()
            .Select(@operator => factory.Unary(
                @operator,
                @operator == IrUnaryOperator.Not ? boolTerm : integerTerm))
            .ToArray();
        var binaryTerms = Enum.GetValues<IrBinaryOperator>()
            .Select(@operator =>
            {
                var operands = @operator is
                    IrBinaryOperator.AndAlso or IrBinaryOperator.OrElse
                    ? (boolTerm, boolTerm)
                    : @operator == IrBinaryOperator.StringConcat
                        ? (stringTerm, stringTerm)
                        : (integerTerm, integerTerm);
                return factory.Binary(@operator, operands.Item1, operands.Item2);
            })
            .ToArray();
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        foreach (var havocKind in Enum.GetValues<IrHavocKind>())
        {
            builder.Havoc(
                entry,
                factory.CreateOperation($"havoc-{havocKind}"),
                havocKind,
                havocKind == IrHavocKind.Memory
                    ? []
                    : [boolVariable, integerVariable]);
        }

        builder.Return(entry, factory.CreateOperation("return"), integerTerm);
        var roots = unaryTerms.Concat(binaryTerms).ToArray();
        var encoded = PortableIrGraphCodec.Encode(
            factory,
            builder.Build(),
            roots);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                encoded.Graph.Terms
                    .Where(static row => row.Kind == IrTermKind.Unary)
                    .Select(static row => row.A),
                Is.EqualTo(Enumerable.Range(0, Enum.GetValues<IrUnaryOperator>().Length)));
            Assert.That(
                encoded.Graph.Terms
                    .Where(static row => row.Kind == IrTermKind.Binary)
                    .Select(static row => row.A),
                Is.EqualTo(Enumerable.Range(0, Enum.GetValues<IrBinaryOperator>().Length)));
            Assert.That(
                encoded.Graph.Blocks
                    .SelectMany(static block => block.Instructions)
                    .Where(static row => row.Kind == IrInstructionKind.Havoc)
                    .Select(static row => row.A),
                Is.EqualTo(Enumerable.Range(0, Enum.GetValues<IrHavocKind>().Length)));
        }

        var decoded = PortableIrGraphCodec.Decode(encoded.Graph);
        var reencoded = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots);
        AssertGraphJsonEqual(encoded.Graph, reencoded.Graph);
    }

    [Test]
    public void EncoderReturnsStableDenseMappingsForPreparationMetadata()
    {
        var fixture = CreateFixture();

        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots);
        var instructions = fixture.Program.Blocks
            .OrderBy(static block => block.Id.Value)
            .SelectMany(static block => block.Instructions)
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                encoded.VariableIndices.Values,
                Is.EquivalentTo(Enumerable.Range(0, encoded.Graph.Variables.Length)));
            Assert.That(
                instructions.Select(
                    instruction => encoded.InstructionIndices[instruction.Id]),
                Is.EqualTo(Enumerable.Range(0, instructions.Length)));
        }
    }

    [Test]
    public void WireEnumCatalogsAreExhaustive()
    {
        Assert.That(PortableIrGraphCodec.HasCompleteWireEnumCatalogs, Is.True);
    }

    [Test]
    public void SlotCatalogsAreExhaustiveAndDeclarative()
    {
        using (Assert.EnterMultipleScope())
        {
            Assert.That(PortableIrGraphCodec.HasCompleteSlotCatalogs, Is.True);
            Assert.That(
                PortableIrSlotCatalog.Terms.Select(static mapping => mapping.Kind),
                Is.EqualTo(Enum.GetNames<IrTermKind>()));
            Assert.That(
                PortableIrSlotCatalog.Locations.Select(static mapping => mapping.Kind),
                Is.EqualTo(Enum.GetNames<IrLocationKind>()));
            Assert.That(
                PortableIrSlotCatalog.Instructions.Select(
                    static mapping => mapping.Kind),
                Is.EqualTo(Enum.GetNames<IrInstructionKind>()));
            Assert.That(
                PortableIrSlotCatalog.Terms[(int)IrTermKind.Opaque].Slots,
                Is.EqualTo([
                    "memberIndex",
                    "optionalTermIndex",
                    "wire:OpaquePurities",
                    "operationWhenImpure",
                    "unused",
                    "unused",
                    "termIndices"]));
            Assert.That(
                PortableIrSlotCatalog.Instructions[(int)IrInstructionKind.Call].Slots,
                Is.EqualTo([
                    "optionalVariableIndex",
                    "memberIndex",
                    "optionalTermIndex",
                    "unused",
                    "termIndices",
                    "unused"]));
        }
    }

    [Test]
    public void CanonicalGoldenWireRoundTripsWithoutChangingBytes()
    {
        var fixture = CreateFixture();
        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            encoded.Graph,
            WorkerProtocolJson.Options);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        Assert.That(
            hash,
            Is.EqualTo(
                "AAA27C6AF3E73A71C545B94A78F722AE239012EB150972129D2FF6BABBF54E5B"));

        var decodedGraph = JsonSerializer.Deserialize<PortableIrGraph>(
            bytes,
            WorkerProtocolJson.Options)!;
        var decoded = PortableIrGraphCodec.Decode(decodedGraph);
        var reencoded = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots);
        var roundTripBytes = JsonSerializer.SerializeToUtf8Bytes(
            reencoded.Graph,
            WorkerProtocolJson.Options);
        Assert.That(roundTripBytes, Is.EqualTo(bytes));
    }

    [Test]
    public void DecoderRejectsDocumentationOnlyCallIdentitySpoof()
    {
        var graph = CreateCallIdentityGraph();
        var callMember = graph.Members.Single();
        callMember.DocumentationCommentId =
            "M:System.Linq.Enumerable.Empty``1";

        Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph)));
    }

    [Test]
    public void DecoderPreservesSuffixBoundCallIdentityRoundTrip()
    {
        var graph = CreateCallIdentityGraph();
        graph.Members.Single().DocumentationCommentId =
            "M:Subject.Transform(System.Int32)";

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            graph,
            WorkerProtocolJson.Options);
        var serialized = JsonSerializer.Deserialize<PortableIrGraph>(
            bytes,
            WorkerProtocolJson.Options)!;

        var decoded = PortableIrGraphCodec.Decode(serialized);
        Assert.That(decoded.Program, Is.Not.Null);
        Assert.That(
            serialized.Members.Single().DocumentationCommentId,
            Is.EqualTo("M:Subject.Transform(System.Int32)"));
    }

    private static void AssertGraphJsonEqual(
        PortableIrGraph expected,
        PortableIrGraph actual)
    {
        Assert.That(
            JsonSerializer.Serialize(actual),
            Is.EqualTo(JsonSerializer.Serialize(expected)));
    }

    private static PortableIrGraph CreateCallIdentityGraph()
    {
        var factory = new IrFactory();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            "call:SubjectAssembly::M:Subject.Transform(System.Int32)",
            factory.IntegerType,
            isStatic: true,
            factory.IntegerType);
        var argument = factory.CreateVariable("value", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        builder.SetEntry(entry);
        builder.Call(
            entry,
            factory.CreateOperation("call"),
            result,
            member,
            receiver: null,
            factory.Variable(argument));
        builder.Return(
            entry,
            factory.CreateOperation("return"),
            factory.Variable(result));
        return PortableIrGraphCodec.Encode(
            factory,
            builder.Build(),
            [factory.Variable(argument)]).Graph;
    }

    [Test]
    public void DecoderRejectsNonCanonicalOptionalSentinels()
    {
        AssertDecoderRejects(graph =>
        {
            var call = graph.Blocks
                .SelectMany(static block => block.Instructions)
                .First(static instruction => instruction.Kind == IrInstructionKind.Call);
            call.A = -2;
        });
    }

    [TestCase(UnreachableMetadataMutation.Type)]
    [TestCase(UnreachableMetadataMutation.Identity)]
    [TestCase(UnreachableMetadataMutation.Variable)]
    [TestCase(UnreachableMetadataMutation.Member)]
    [TestCase(UnreachableMetadataMutation.Operation)]
    [TestCase(UnreachableMetadataMutation.Term)]
    [TestCase(UnreachableMetadataMutation.ReorderedOperations)]
    public void DecoderRejectsMetadataOutsideTheCanonicalEncoderImage(
        UnreachableMetadataMutation mutation)
    {
        AssertDecoderRejects(graph =>
        {
            switch (mutation)
            {
                case UnreachableMetadataMutation.Type:
                    graph.Types = [.. graph.Types, new PortableIrType(
                        IrTypeKind.Reference, "Unused", -1)];
                    break;
                case UnreachableMetadataMutation.Identity:
                    graph.Identities = [.. graph.Identities, graph.Identities.Length];
                    break;
                case UnreachableMetadataMutation.Variable:
                    graph.Variables = [.. graph.Variables,
                        new PortableIrVariable("unused", 1)];
                    break;
                case UnreachableMetadataMutation.Member:
                    graph.Identities = [.. graph.Identities, graph.Identities.Length];
                    graph.Members = [.. graph.Members, new PortableIrMember(
                        graph.Identities.Length - 1,
                        3,
                        "Unused",
                        1,
                        true,
                        [])];
                    break;
                case UnreachableMetadataMutation.Operation:
                    graph.Operations = [.. graph.Operations,
                        new PortableIrOperation("unused")];
                    break;
                case UnreachableMetadataMutation.Term:
                    graph.Terms = [.. graph.Terms, new PortableIrTerm(
                        IrTermKind.Integer,
                        1,
                        number: 42,
                        items: [])];
                    break;
                case UnreachableMetadataMutation.ReorderedOperations:
                    (graph.Operations[0], graph.Operations[1]) =
                        (graph.Operations[1], graph.Operations[0]);
                    foreach (var instruction in graph.Blocks.SelectMany(
                                 static block => block.Instructions))
                    {
                        instruction.Operation = instruction.Operation switch
                        {
                            0 => 1,
                            1 => 0,
                            _ => instruction.Operation
                        };
                    }
                    foreach (var term in graph.Terms.Where(static term =>
                                 term.Kind == IrTermKind.Opaque && term.C != 0))
                    {
                        term.D = term.D switch
                        {
                            0 => 1,
                            1 => 0,
                            _ => term.D
                        };
                    }
                    break;
                default:
                    throw new AssertionException("Unknown metadata mutation.");
            }
        });
    }

    [TestCase(CanonicalSlotMutation.TermUnusedIndex)]
    [TestCase(CanonicalSlotMutation.TermUnusedNumber)]
    [TestCase(CanonicalSlotMutation.TermUnusedText)]
    [TestCase(CanonicalSlotMutation.TermEmptyItems)]
    [TestCase(CanonicalSlotMutation.InstructionUnusedIndex)]
    [TestCase(CanonicalSlotMutation.InstructionEmptyItems)]
    [TestCase(CanonicalSlotMutation.InstructionUnusedLocation)]
    public void DecoderRejectsNonCanonicalSlotsAfterSerialization(
        CanonicalSlotMutation mutation)
    {
        AssertDecoderRejects(
            graph =>
            {
                switch (mutation)
                {
                    case CanonicalSlotMutation.TermUnusedIndex:
                        graph.Terms.First(static row => row.Kind == IrTermKind.Boolean).B = 0;
                        break;
                    case CanonicalSlotMutation.TermUnusedNumber:
                        graph.Terms.First(static row => row.Kind == IrTermKind.Boolean).Number = 1;
                        break;
                    case CanonicalSlotMutation.TermUnusedText:
                        graph.Terms.First(static row => row.Kind == IrTermKind.Boolean).Text =
                            "tampered";
                        break;
                    case CanonicalSlotMutation.TermEmptyItems:
                        graph.Terms.First(static row => row.Kind == IrTermKind.Null).Items = [0];
                        break;
                    case CanonicalSlotMutation.InstructionUnusedIndex:
                        graph.Blocks
                            .SelectMany(static block => block.Instructions)
                            .First(static row => row.Kind == IrInstructionKind.Assign)
                            .C = 0;
                        break;
                    case CanonicalSlotMutation.InstructionEmptyItems:
                        graph.Blocks
                            .SelectMany(static block => block.Instructions)
                            .First(static row => row.Kind == IrInstructionKind.Assign)
                            .Items = [0];
                        break;
                    case CanonicalSlotMutation.InstructionUnusedLocation:
                        graph.Blocks
                            .SelectMany(static block => block.Instructions)
                            .First(static row => row.Kind == IrInstructionKind.Call)
                            .Location = new();
                        break;
                    default:
                        throw new AssertionException("Unknown mutation.");
                }
            },
            serialize: true);
    }

    [TestCase(WireEnumMutation.OpaquePurity)]
    [TestCase(WireEnumMutation.UnaryOperator)]
    [TestCase(WireEnumMutation.BinaryOperator)]
    [TestCase(WireEnumMutation.HavocKind)]
    public void DecoderRejectsUnknownWireEnumCodes(WireEnumMutation mutation)
    {
        AssertDecoderRejects(graph =>
        {
            switch (mutation)
            {
                case WireEnumMutation.OpaquePurity:
                    graph.Terms.First(static row => row.Kind == IrTermKind.Opaque).C = 999;
                    break;
                case WireEnumMutation.UnaryOperator:
                    graph.Terms.First(static row => row.Kind == IrTermKind.Unary).A = 999;
                    break;
                case WireEnumMutation.BinaryOperator:
                    graph.Terms.First(static row => row.Kind == IrTermKind.Binary).A = 999;
                    break;
                case WireEnumMutation.HavocKind:
                    graph.Blocks.SelectMany(static block => block.Instructions)
                        .First(static row => row.Kind == IrInstructionKind.Havoc).A = 999;
                    break;
                default:
                    throw new AssertionException("Unknown mutation.");
            }
        });
    }

    [Test]
    public void RootsOnlyGraphDoesNotFabricateAProgram()
    {
        var factory = new IrFactory();
        IrTerm[] roots = [factory.Integer(42)];

        var encoded = PortableIrGraphCodec.Encode(factory, null, roots);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(encoded.Graph.HasProgram, Is.False);
            Assert.That(encoded.Graph.Entry, Is.EqualTo(-1));
            Assert.That(encoded.Graph.Blocks, Is.Empty);
            Assert.That(decoded.Program, Is.Null);
            Assert.That(((IrIntegerTerm)decoded.Roots.Single()).Value, Is.EqualTo(42));
        }
    }

    [TestCase(MalformedMutation.TermIndex)]
    [TestCase(MalformedMutation.TermCycle)]
    [TestCase(MalformedMutation.TypeCycle)]
    [TestCase(MalformedMutation.TermKind)]
    [TestCase(MalformedMutation.TermType)]
    [TestCase(MalformedMutation.InstructionKind)]
    [TestCase(MalformedMutation.NullTopLevelArray)]
    [TestCase(MalformedMutation.NullMemberParameters)]
    [TestCase(MalformedMutation.NonCanonicalIdentity)]
    [TestCase(MalformedMutation.CollapsedMemberPartition)]
    [TestCase(MalformedMutation.CollapsedTermPartition)]
    [TestCase(MalformedMutation.NullInstructionItems)]
    [TestCase(MalformedMutation.LocationKind)]
    [TestCase(MalformedMutation.ProgramShape)]
    [TestCase(MalformedMutation.DuplicateHavocVariable)]
    [TestCase(MalformedMutation.WhitespaceOperationDescription)]
    [TestCase(MalformedMutation.WhitespaceBlockName)]
    public void DecoderRejectsMalformedGraphs(MalformedMutation mutation)
    {
        var exception = AssertDecoderRejects(graph =>
        {
            switch (mutation)
            {
                case MalformedMutation.TermIndex:
                    graph.Roots[0] = graph.Terms.Length;
                    break;
                case MalformedMutation.TermCycle:
                    var unaryIndex = Array.FindIndex(
                        graph.Terms,
                        static row => row.Kind == IrTermKind.Unary);
                    graph.Terms[unaryIndex].B = unaryIndex;
                    break;
                case MalformedMutation.TypeCycle:
                    var sequenceIndex = Array.FindIndex(
                        graph.Types,
                        static row => row.Kind == IrTypeKind.Sequence);
                    graph.Types[sequenceIndex].Element = sequenceIndex;
                    break;
                case MalformedMutation.TermKind:
                    graph.Terms[0].Kind = (IrTermKind)999;
                    break;
                case MalformedMutation.TermType:
                    var booleanIndex = Array.FindIndex(
                        graph.Terms,
                        static row => row.Kind == IrTermKind.Boolean);
                    graph.Terms[booleanIndex].Type = 1;
                    break;
                case MalformedMutation.InstructionKind:
                    graph.Blocks[0].Instructions[0].Kind = (IrInstructionKind)999;
                    break;
                case MalformedMutation.NullTopLevelArray:
                    graph.Roots = null!;
                    break;
                case MalformedMutation.NullMemberParameters:
                    graph.Members[0].ParameterTypes = null!;
                    break;
                case MalformedMutation.NonCanonicalIdentity:
                    graph.Identities[0] = 1;
                    break;
                case MalformedMutation.CollapsedMemberPartition:
                    graph.Members[1] = graph.Members[0];
                    break;
                case MalformedMutation.CollapsedTermPartition:
                    graph.Terms[1] = graph.Terms[0];
                    break;
                case MalformedMutation.NullInstructionItems:
                    graph.Blocks[0].Instructions[0].Items = null!;
                    break;
                case MalformedMutation.LocationKind:
                    graph.Blocks[0].Instructions
                        .First(static instruction => instruction.Location != null)
                        .Location!.Kind = (IrLocationKind)999;
                    break;
                case MalformedMutation.ProgramShape:
                    graph.HasProgram = false;
                    break;
                case MalformedMutation.DuplicateHavocVariable:
                    var havoc = graph.Blocks
                        .SelectMany(static block => block.Instructions)
                        .First(static row => row.Kind == IrInstructionKind.Havoc);
                    havoc.Items = [havoc.Items[0], havoc.Items[0]];
                    break;
                case MalformedMutation.WhitespaceOperationDescription:
                    graph.Operations[0].Description = " ";
                    break;
                case MalformedMutation.WhitespaceBlockName:
                    graph.Blocks[0].Name = "\t";
                    break;
                default:
                    throw new AssertionException("Unknown mutation.");
            }
        });

        if (mutation == MalformedMutation.WhitespaceOperationDescription)
        {
            Assert.That(
                exception!.Message,
                Is.EqualTo("Portable IR operation description cannot be whitespace."));
        }
    }

    [TestCase(DeepGraphKind.Terms, false)]
    [TestCase(DeepGraphKind.Terms, true)]
    [TestCase(DeepGraphKind.Types, false)]
    [TestCase(DeepGraphKind.Types, true)]
    public void DecoderRejectsVeryDeepAcyclicAndCyclicGraphs(
        DeepGraphKind kind,
        bool cyclic)
    {
        var graph = DeepGraph(kind, cyclic, 4096);

        Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph)));
    }

    [TestCase(DeepGraphKind.Terms)]
    [TestCase(DeepGraphKind.Types)]
    public void EncoderRejectsValuesDeeperThanTheDecoderLimit(
        DeepGraphKind kind)
    {
        var factory = new IrFactory();
        var term = kind switch
        {
            DeepGraphKind.Terms => DeepTerm(factory),
            DeepGraphKind.Types => DeepTypeTerm(factory),
            _ => throw new AssertionException("Unknown graph kind.")
        };

        Assert.Throws<InvalidDataException>((Action)(() =>
            PortableIrGraphCodec.Encode(factory, null, [term])));
    }

    private static IrTerm DeepTerm(IrFactory factory)
    {
        IrTerm term = factory.Variable(
            factory.CreateVariable("value", factory.BooleanType));
        for (var index = 0;
             index < PortableIrGraphCodec.MaximumGraphDepth;
             index++)
        {
            term = factory.Unary(IrUnaryOperator.Not, term);
        }
        return term;
    }

    private static IrVariableTerm DeepTypeTerm(IrFactory factory)
    {
        var type = factory.IntegerType;
        for (var index = 0;
             index < PortableIrGraphCodec.MaximumGraphDepth;
             index++)
        {
            type = factory.GetOrCreateSequenceType(type);
        }
        return factory.Variable(factory.CreateVariable("value", type));
    }

    private static InvalidDataException AssertDecoderRejects(
        Action<PortableIrGraph> mutate,
        bool serialize = false)
    {
        var fixture = CreateFixture();
        var graph = PortableIrGraphCodec.Encode(
            fixture.Program,
            fixture.Roots).Graph;
        if (serialize)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                graph,
                WorkerProtocolJson.Options);
            graph = JsonSerializer.Deserialize<PortableIrGraph>(
                bytes,
                WorkerProtocolJson.Options)!;
        }

        mutate(graph);
        return Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph)))!;
    }

    private static PortableIrGraph DeepGraph(
        DeepGraphKind kind,
        bool cyclic,
        int depth)
    {
        var graph = new PortableIrGraph
        {
            Types = [
                new() { Kind = IrTypeKind.Boolean, Name = "bool" },
                new() { Kind = IrTypeKind.Integer, Name = "int" },
                new() { Kind = IrTypeKind.String, Name = "string" },
                new() { Kind = IrTypeKind.Reference, Name = "object" }
            ]
        };
        if (kind == DeepGraphKind.Types)
        {
            var types = graph.Types;
            Array.Resize(ref types, types.Length + depth);
            graph.Types = types;
            for (var index = 4; index < graph.Types.Length; index++)
            {
                graph.Types[index] = new PortableIrType
                {
                    Kind = IrTypeKind.Sequence,
                    Name = $"sequence-{index}",
                    Element = index == graph.Types.Length - 1
                        ? cyclic ? 4 : 0
                        : index + 1
                };
            }
            graph.Variables = [new PortableIrVariable("deep", 4)];
            graph.Terms = [new PortableIrTerm(
                IrTermKind.Variable,
                4,
                a: 0,
                items: [])];
            graph.Roots = [0];
        }
        else
        {
            graph.Terms = new PortableIrTerm[depth];
            for (var index = 0; index < graph.Terms.Length; index++)
            {
                graph.Terms[index] = index == graph.Terms.Length - 1 &&
                    !cyclic
                    ? new PortableIrTerm
                    {
                        Kind = IrTermKind.Boolean,
                        Type = 0,
                        A = 1
                    }
                    : new PortableIrTerm
                    {
                        Kind = IrTermKind.Unary,
                        Type = 0,
                        A = (int)IrUnaryOperator.Not,
                        B = index == graph.Terms.Length - 1 ? 0 : index + 1
                    };
            }

            graph.Roots = [0];
        }
        return graph;
    }

    private static CodecFixture CreateFixture()
    {
        var factory = new IrFactory();
        var boxType = factory.GetOrCreateReferenceType(
            factory.CreateIdentity(),
            "Box");
        var sequenceType = factory.GetOrCreateSequenceType(
            factory.CreateIdentity(),
            factory.IntegerType,
            "Numbers");
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        var number = factory.CreateVariable("number", factory.IntegerType);
        var result = factory.CreateVariable("result", factory.IntegerType);
        var box = factory.CreateVariable("box", boxType);
        var sequence = factory.CreateVariable("sequence", sequenceType);
        var memberIdentity = factory.CreateIdentity();
        var valueMember = factory.GetOrCreateMember(
            memberIdentity,
            boxType,
            "Value",
            factory.IntegerType,
            isStatic: false);
        var callMember = factory.GetOrCreateMember(
            memberIdentity,
            boxType,
            "Transform",
            factory.IntegerType,
            isStatic: false,
            factory.IntegerType);

        var flagTerm = factory.Variable(flag);
        var numberTerm = factory.Variable(number);
        var boxTerm = factory.Variable(box);
        var sequenceTerm = factory.Variable(sequence);
        var conditional = factory.Conditional(
            flagTerm,
            numberTerm,
            factory.Integer(7));
        IrTerm[] roots = [
            factory.Boolean(true),
            factory.Integer(5),
            factory.String("text"),
            factory.Null(boxType),
            flagTerm,
            factory.PureOpaque(callMember, boxTerm, numberTerm),
            factory.ImpureOpaque(
                factory.CreateOperation("opaque"),
                callMember,
                boxTerm,
                numberTerm),
            factory.Unary(IrUnaryOperator.Not, flagTerm),
            factory.Binary(IrBinaryOperator.Add, numberTerm, factory.Integer(1)),
            conditional,
            factory.Cast(factory.ObjectType, boxTerm),
            factory.Length(sequenceTerm),
            factory.SequenceAccess(sequenceTerm, numberTerm)
        ];

        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var whenTrue = builder.CreateBlock("true");
        var whenFalse = builder.CreateBlock("false");
        var exit = builder.CreateBlock("exit");
        var memberLocation = builder.MemberLocation(valueMember, boxTerm);
        var sequenceLocation = builder.SequenceLocation(sequenceTerm, numberTerm);
        builder.Assign(entry, factory.CreateOperation("same"), result, numberTerm);
        builder.Load(entry, factory.CreateOperation("same"), result, memberLocation);
        builder.Store(entry, factory.CreateOperation("store-member"), memberLocation, numberTerm);
        builder.Load(entry, factory.CreateOperation("load-sequence"), result, sequenceLocation);
        builder.Store(entry, factory.CreateOperation("store-sequence"), sequenceLocation, numberTerm);
        builder.Call(
            entry,
            factory.CreateOperation("call"),
            result,
            callMember,
            boxTerm,
            numberTerm);
        builder.Assume(entry, factory.CreateOperation("assume"), flagTerm);
        builder.Assert(entry, factory.CreateOperation("assert"), flagTerm);
        builder.Havoc(
            entry,
            factory.CreateOperation("havoc"),
            IrHavocKind.VariablesAndMemory,
            result);
        builder.Branch(
            entry,
            factory.CreateOperation("branch"),
            flagTerm,
            whenTrue,
            whenFalse);
        builder.Goto(whenTrue, factory.CreateOperation("goto"), exit);
        builder.Return(whenFalse, factory.CreateOperation("return-false"), numberTerm);
        builder.Return(exit, factory.CreateOperation("return"), numberTerm);
        var program = builder.Build();
        return new CodecFixture(
            factory,
            program,
            roots,
            Array.IndexOf(roots, conditional),
            Array.IndexOf(roots, flagTerm));
    }

    public enum MalformedMutation
    {
        TermIndex,
        TermCycle,
        TypeCycle,
        TermKind,
        TermType,
        InstructionKind,
        NullTopLevelArray,
        NullMemberParameters,
        NonCanonicalIdentity,
        CollapsedMemberPartition,
        CollapsedTermPartition,
        NullInstructionItems,
        LocationKind,
        ProgramShape,
        DuplicateHavocVariable,
        WhitespaceOperationDescription,
        WhitespaceBlockName
    }

    public enum CanonicalSlotMutation
    {
        TermUnusedIndex,
        TermUnusedNumber,
        TermUnusedText,
        TermEmptyItems,
        InstructionUnusedIndex,
        InstructionEmptyItems,
        InstructionUnusedLocation
    }

    public enum DeepGraphKind
    {
        Terms,
        Types
    }

    public enum WireEnumMutation
    {
        OpaquePurity,
        UnaryOperator,
        BinaryOperator,
        HavocKind
    }

    public enum UnreachableMetadataMutation
    {
        Type,
        Identity,
        Variable,
        Member,
        Operation,
        Term,
        ReorderedOperations
    }

    private sealed record CodecFixture(
        IrFactory Factory,
        IrProgram Program,
        IrTerm[] Roots,
        int ConditionalRoot,
        int FlagRoot);
}
