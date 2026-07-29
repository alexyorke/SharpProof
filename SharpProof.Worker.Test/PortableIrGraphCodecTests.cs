using System.Text.Json;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PortableIrGraphCodecTests {
    [Test]
    public void RoundTripPreservesEveryTermInstructionAndLocationShape() {
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

        using (Assert.EnterMultipleScope()) {
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
            Assert.That(
                JsonSerializer.Serialize(encodedAgain.Graph),
                Is.EqualTo(JsonSerializer.Serialize(encoded.Graph)));
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
    public void EncoderReturnsStableDenseMappingsForPreparationMetadata() {
        var fixture = CreateFixture();

        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots);
        var instructions = fixture.Program.Blocks
            .OrderBy(static block => block.Id.Value)
            .SelectMany(static block => block.Instructions)
            .ToArray();

        using (Assert.EnterMultipleScope()) {
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
    public void WireEnumCatalogsAreExhaustive() =>
        Assert.That(PortableIrGraphCodec.HasCompleteWireEnumCatalogs, Is.True);

    [TestCase(WireEnumMutation.OpaquePurity)]
    [TestCase(WireEnumMutation.UnaryOperator)]
    [TestCase(WireEnumMutation.BinaryOperator)]
    [TestCase(WireEnumMutation.HavocKind)]
    public void DecoderRejectsUnknownWireEnumCodes(WireEnumMutation mutation) {
        var fixture = CreateFixture();
        var graph = PortableIrGraphCodec.Encode(fixture.Program, fixture.Roots).Graph;

        switch (mutation) {
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

        Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph)));
    }

    [Test]
    public void RootsOnlyGraphDoesNotFabricateAProgram() {
        var factory = new IrFactory();
        IrTerm[] roots = [factory.Integer(42)];

        var encoded = PortableIrGraphCodec.Encode(factory, null, roots);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph);

        using (Assert.EnterMultipleScope()) {
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
    public void DecoderRejectsMalformedGraphs(MalformedMutation mutation) {
        var fixture = CreateFixture();
        var graph = PortableIrGraphCodec.Encode(
            fixture.Program,
            fixture.Roots).Graph;

        switch (mutation) {
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
            default:
                throw new AssertionException("Unknown mutation.");
        }

        Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph)));
    }

    [TestCase(DeepGraphKind.Terms, false)]
    [TestCase(DeepGraphKind.Terms, true)]
    [TestCase(DeepGraphKind.Types, false)]
    [TestCase(DeepGraphKind.Types, true)]
    public void DecoderRejectsVeryDeepAcyclicAndCyclicGraphs(
        DeepGraphKind kind,
        bool cyclic) {
        var graph = DeepGraph(kind, cyclic, 4096);

        Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph)));
    }

    private static PortableIrGraph DeepGraph(
        DeepGraphKind kind,
        bool cyclic,
        int depth) {
        var graph = new PortableIrGraph {
            Types = [
                new() { Kind = IrTypeKind.Boolean, Name = "bool" },
                new() { Kind = IrTypeKind.Integer, Name = "int" },
                new() { Kind = IrTypeKind.String, Name = "string" },
                new() { Kind = IrTypeKind.Reference, Name = "object" }
            ]
        };
        if (kind == DeepGraphKind.Types) {
            var types = graph.Types;
            Array.Resize(ref types, types.Length + depth);
            graph.Types = types;
            for (var index = 4; index < graph.Types.Length; index++)
                graph.Types[index] = new PortableIrType {
                    Kind = IrTypeKind.Sequence,
                    Name = $"sequence-{index}",
                    Element = index == graph.Types.Length - 1
                        ? cyclic ? 4 : 0
                        : index + 1
                };
        }
        else {
            graph.Terms = new PortableIrTerm[depth];
            for (var index = 0; index < graph.Terms.Length; index++)
                graph.Terms[index] = index == graph.Terms.Length - 1 &&
                    !cyclic
                    ? new PortableIrTerm {
                        Kind = IrTermKind.Boolean,
                        Type = 0,
                        A = 1
                    }
                    : new PortableIrTerm {
                        Kind = IrTermKind.Unary,
                        Type = 0,
                        A = (int)IrUnaryOperator.Not,
                        B = index == graph.Terms.Length - 1 ? 0 : index + 1
                    };
            graph.Roots = [0];
        }
        return graph;
    }

    private static CodecFixture CreateFixture() {
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
            factory.Cast(factory.ObjectType, numberTerm),
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

    public enum MalformedMutation {
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
        ProgramShape
    }

    public enum DeepGraphKind {
        Terms,
        Types
    }

    public enum WireEnumMutation {
        OpaquePurity,
        UnaryOperator,
        BinaryOperator,
        HavocKind
    }

    private sealed record CodecFixture(
        IrFactory Factory,
        IrProgram Program,
        IrTerm[] Roots,
        int ConditionalRoot,
        int FlagRoot);
}
