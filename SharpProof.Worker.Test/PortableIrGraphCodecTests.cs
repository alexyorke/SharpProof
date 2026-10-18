using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class PortableIrGraphCodecTests
{
    [Test]
    public void WireArraysPreserveOmittedEmptyAndExplicitNull()
    {
        var omitted = JsonSerializer.Deserialize<PortableIrMember>("{}", WorkerProtocolJson.Options)!;
        var empty = JsonSerializer.Deserialize<PortableIrMember>(
            "{\"parameterTypes\":[]}", WorkerProtocolJson.Options)!;
        var explicitNull = JsonSerializer.Deserialize<PortableIrMember>(
            "{\"parameterTypes\":null}", WorkerProtocolJson.Options)!;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(omitted.ParameterTypes.IsDefault, Is.False);
            Assert.That(omitted.ParameterTypes, Is.Empty);
            Assert.That(empty.ParameterTypes.IsDefault, Is.False);
            Assert.That(empty.ParameterTypes, Is.Empty);
            Assert.That(explicitNull.ParameterTypes.IsDefault, Is.True);
            Assert.That(JsonSerializer.SerializeToNode(explicitNull, WorkerProtocolJson.Options)!["parameterTypes"], Is.Null);
        }
    }

    [Test]
    public void ArgumentOutOfRangeFaultSurvivesPortableRoundTripWithStableOlderCodes()
    {
        Assert.That((int)IrExceptionKind.Unknown, Is.EqualTo(6));
        Assert.That((int)IrExceptionKind.Explicit, Is.EqualTo(7));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exceptional = builder.CreateBlock();
        builder.Throw(entry, factory.CreateOperation("interface-bounds"), IrExceptionKind.ArgumentOutOfRange, exceptional);
        builder.ExceptionalExit(exceptional, factory.CreateOperation("exceptional-exit"));
        var encoded = PortableIrGraphCodec.Encode(factory, builder.Build(), [], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var result = new IrProgramInterpreter(decoded.Factory).Execute(decoded.Program!);
        Assert.That(result.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(result.Exception!.Kind, Is.EqualTo(IrExceptionKind.ArgumentOutOfRange));
        Assert.That(PortableIrGraphCodec.HasCompleteWireEnumCatalogs, Is.True);
    }
    [Test]
    public void EncodingRejectsProgramFromAnotherFactory()
    {
        var fixture = CreateFixture();
        Assert.Throws<ArgumentException>((Action)(() =>
            PortableIrGraphCodec.Encode(
                new IrFactory(), fixture.Program, fixture.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
        Assert.DoesNotThrow((Action)(() =>
            PortableIrGraphCodec.Encode(
                fixture.Factory, fixture.Program, fixture.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void RoundTripPreservesSubstitutedStringAllocationIdentity()
    {
        var factory = new IrFactory();
        var input = factory.CreateVariable("input", factory.StringType);
        var term = factory.Cast(factory.ObjectType, factory.Binary(IrBinaryOperator.StringConcat,
            factory.Variable(input), factory.String("suffix")));
        var substituted = IrSubstitution.Substitute(factory, term, input, factory.String("prefix"));
        var encoded = PortableIrGraphCodec.Encode(factory, null, [substituted], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(new IrInterpreter(decoded.Factory).Evaluate(decoded.Roots[0]).Status,
            Is.EqualTo(IrEvaluationStatus.Unsupported));
        AssertGraphJsonEqual(encoded.Graph, PortableIrGraphCodec.Encode(decoded.Factory, null, decoded.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Graph);
    }

    [Test]
    public void RoundTripPreservesEveryTermInstructionAndLocationShape()
    {
        var fixture = CreateFixture(includeAllocation: true);

        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var encodedAgain = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var totalFactory = new IrFactory(IrExecutionSemantics.Total);
        var empty = totalFactory.EmptyArray(totalFactory.GetOrCreateSequenceType(totalFactory.IntegerType));
        var encodedEmpty = PortableIrGraphCodec.Encode(totalFactory, null, [empty], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decodedEmpty = PortableIrGraphCodec.Decode(encodedEmpty.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(decodedEmpty.Roots.Single(), Is.TypeOf<IrEmptyArrayTerm>());
        AssertGraphJsonEqual(encodedEmpty.Graph, PortableIrGraphCodec.Encode(decodedEmpty.Factory, null, decodedEmpty.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Graph);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                encoded.Graph.Terms.Concat(encodedEmpty.Graph.Terms).Select(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind)).Distinct(),
                Is.EquivalentTo(Enum.GetValues<IrTermKind>()));
            Assert.That(
                encoded.Graph.Blocks
                    .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                    .Select(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind))
                    .Distinct(),
                Is.EquivalentTo(Enum.GetValues<IrInstructionKind>()));
            Assert.That(
                encoded.Graph.Blocks
                    .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                    .Where(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Location != null))
                    .Select(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Location!.Kind))
                    .Distinct(),
                Is.EquivalentTo(Enum.GetValues<IrLocationKind>()));
            Assert.That(decoded.Program, Is.Not.Null);
            Assert.That(decoded.Roots, Has.Count.EqualTo(fixture.Roots.Length));
            Assert.That(decoded.Variables, Has.Count.EqualTo(encoded.Graph.Variables.Length));
            Assert.That(decoded.Program!.Blocks, Has.Length.EqualTo(encoded.Graph.Blocks.Length));
            Assert.That(
                decoded.Instructions,
                Has.Count.EqualTo(
                    encoded.Graph.Blocks.Sum(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions.Length))));
            AssertGraphJsonEqual(encoded.Graph, encodedAgain.Graph);
        }

        var decodedConditional = (IrConditionalTerm)decoded.Roots[fixture.ConditionalRoot];
        var decodedFlag = (IrVariableTerm)decoded.Roots[fixture.FlagRoot];
        Assert.That(decodedConditional.Condition, Is.SameAs(decodedFlag));

        var call = decoded.Program!.Blocks
            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrCallInstruction>()
            .Single();
        var memberLoad = decoded.Program.Blocks
            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .OfType<IrLoadInstruction>()
            .Select(static load => load is null ? throw new System.ArgumentNullException(nameof(load)) : (load.Location))
            .OfType<IrMemberLocation>()
            .Single();
        Assert.That(
            decoded.Factory.GetMemberInfo(call.Member).Identity,
            Is.EqualTo(decoded.Factory.GetMemberInfo(memberLoad.Member).Identity));
        Assert.That(
            decoded.Instructions
                .Select(static instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Operation))
                .Distinct()
                .Count(),
            Is.EqualTo(decoded.Instructions.Count));
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
            [unused], cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = PortableIrGraphCodec.Decode(
            encoded.Graph,
            [encoded.VariableIndices[unused]], cancellationToken: TestContext.CurrentContext.CancellationToken);
        var reencoded = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots,
            decoded.Variables, cancellationToken: TestContext.CurrentContext.CancellationToken);

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
            [items], cancellationToken: TestContext.CurrentContext.CancellationToken).Graph;
        var sequenceIndex = PortableGraphChanges.FindIndex(
            graph.Types,
            static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Name == "Numbers"));
        var integerIndex = PortableGraphChanges.FindIndex(
            graph.Types,
            static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTypeKind.Integer));
        var variable = graph.Variables.Single(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Name == "items"));
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
                graph.Operations.Select(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Description)),
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
                    : @operator is IrBinaryOperator.StringConcat or IrBinaryOperator.StringEquals
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
            roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                encoded.Graph.Terms
                    .Where(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Unary))
                    .Select(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.A)),
                Is.EqualTo(Enumerable.Range(0, Enum.GetValues<IrUnaryOperator>().Length)));
            Assert.That(
                encoded.Graph.Terms
                    .Where(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Binary))
                    .Select(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.A)),
                Is.EqualTo(Enumerable.Range(0, Enum.GetValues<IrBinaryOperator>().Length)));
            Assert.That(
                encoded.Graph.Blocks
                    .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                    .Where(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Havoc))
                    .Select(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.A)),
                Is.EqualTo(Enumerable.Range(0, Enum.GetValues<IrHavocKind>().Length)));
        }

        var decoded = PortableIrGraphCodec.Decode(encoded.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var reencoded = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        AssertGraphJsonEqual(encoded.Graph, reencoded.Graph);
    }

    [Test]
    public void EncoderReturnsStableDenseMappingsForPreparationMetadata()
    {
        var fixture = CreateFixture();

        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var instructions = fixture.Program.Blocks
            .OrderBy(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Id.Value))
            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
            .ToArray();

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                encoded.VariableIndices.Values,
                Is.EquivalentTo(Enumerable.Range(0, encoded.Graph.Variables.Length)));
            Assert.That(
                instructions.Select(
                    instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (encoded.InstructionIndices[instruction.Id])),
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
        var fixture = CreateFixture(includeExceptionEdges: false);
        var encoded = PortableIrGraphCodec.Encode(
            fixture.Factory,
            fixture.Program,
            fixture.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            encoded.Graph,
            WorkerProtocolJson.Options);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        Assert.That(encoded.Graph.Semantics, Is.EqualTo(IrExecutionSemantics.Legacy));
        Assert.That(encoded.Graph.Operations.Select(operation => operation is null ? throw new System.ArgumentNullException(nameof(operation)) : (operation.SourceSpan)), Has.All.Null);
        Assert.That(encoded.Graph.Blocks.SelectMany(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)).Select(instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Origin)),
            Has.All.EqualTo(IrHavocOrigin.Approximation));

        Assert.That(
            hash,
            Is.EqualTo(
                "3DAAF8FCCFCD144CF735A4ECEEA0B078B414CC404E203A04BB8D24A6FE49AE29"));

        var previous = JsonNode.Parse(bytes)!;
        previous.AsObject().Remove("semantics");
        foreach (var operation in previous["operations"]!.AsArray())
        {
            operation!.AsObject().Remove("sourceSpan");
        }
        foreach (var block in previous["blocks"]!.AsArray())
        {
            foreach (var instruction in block!["instructions"]!.AsArray())
            {
                instruction!.AsObject().Remove("origin");
            }
        }
        var previousBytes = JsonSerializer.SerializeToUtf8Bytes(previous, WorkerProtocolJson.Options);
        Assert.That(Convert.ToHexString(SHA256.HashData(previousBytes)),
            Is.EqualTo("369E597B3404366C0029C4B06E0C21DFC18C951557107EC703AE574AC483F51F"));
        TestContext.Progress.WriteLine($"Canonical wire: {bytes.Length} bytes; previous {previousBytes.Length}; " +
            $"{encoded.Graph.Types.Length} types; {encoded.Graph.Terms.Length} terms.");

        var decodedGraph = JsonSerializer.Deserialize<PortableIrGraph>(
            bytes,
            WorkerProtocolJson.Options)!;
        var decoded = PortableIrGraphCodec.Decode(decodedGraph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var reencoded = PortableIrGraphCodec.Encode(
            decoded.Factory,
            decoded.Program,
            decoded.Roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
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
        graph = graph.ReplaceRow(callMember, callMember with { DocumentationCommentId = "M:System.Linq.Enumerable.Empty``1" });

        Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void DecoderPreservesSuffixBoundCallIdentityRoundTrip()
    {
        var graph = CreateCallIdentityGraph();
        graph = graph.ReplaceRow(graph.Members.Single(), graph.Members.Single() with { DocumentationCommentId = "M:Subject.Transform(System.Int32)" });

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            graph,
            WorkerProtocolJson.Options);
        var serialized = JsonSerializer.Deserialize<PortableIrGraph>(
            bytes,
            WorkerProtocolJson.Options)!;

        var decoded = PortableIrGraphCodec.Decode(serialized, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(decoded.Program, Is.Not.Null);
        Assert.That(
            serialized.Members.Single().DocumentationCommentId,
            Is.EqualTo("M:Subject.Transform(System.Int32)"));
    }

    [Test]
    public void DecoderCanonicalizesCallIdentityDisplaySuffix()
    {
        var graph = CreateCallIdentityGraph(
            "call:SubjectAssembly::M:Subject.Transform(System.Int32)~display");
        graph = graph.ReplaceRow(graph.Members.Single(), graph.Members.Single() with { DocumentationCommentId = "M:Subject.Transform(System.Int32)" });

        Assert.DoesNotThrow((Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    [Test]
    public void DecoderAcceptsCallNameWithoutDocumentationDelimiter()
    {
        var graph = CreateCallIdentityGraph("call:SubjectAssembly");

        Assert.That(graph.Members.Single().DocumentationCommentId, Is.Null);
        Assert.DoesNotThrow((Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    private static void AssertGraphJsonEqual(
        PortableIrGraph expected,
        PortableIrGraph actual)
    {
        System.ArgumentNullException.ThrowIfNull(actual);
        System.ArgumentNullException.ThrowIfNull(expected);
        Assert.That(
            JsonSerializer.Serialize(actual),
            Is.EqualTo(JsonSerializer.Serialize(expected)));
    }

    private static PortableIrGraph CreateCallIdentityGraph(
        string name = "call:SubjectAssembly::M:Subject.Transform(System.Int32)")
    {
        System.ArgumentNullException.ThrowIfNull(name);
        var factory = new IrFactory();
        var member = factory.GetOrCreateMember(
            factory.CreateIdentity(),
            factory.ObjectType,
            name,
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
            [factory.Variable(argument)], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken).Graph;
    }

    [Test]
    public void DecoderRejectsNonCanonicalOptionalSentinels()
    {
        AssertDecoderRejects(graph =>
        {
            System.ArgumentNullException.ThrowIfNull(graph);
            var call = graph.Blocks
                .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                .First(static instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Kind == IrInstructionKind.Call));
            graph = graph.ReplaceRow(call, call with { A = -2 });
            return graph;
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
            System.ArgumentNullException.ThrowIfNull(graph);
            switch (mutation)
            {
                case UnreachableMetadataMutation.Type:
                    graph = graph with
                    {
                        Types = [.. graph.Types, new PortableIrType(
                        IrTypeKind.Reference, "Unused", -1)]
                    };
                    break;
                case UnreachableMetadataMutation.Identity:
                    graph = graph with { Identities = [.. graph.Identities, graph.Identities.Length] };
                    break;
                case UnreachableMetadataMutation.Variable:
                    graph = graph with
                    {
                        Variables = [.. graph.Variables,
                        new PortableIrVariable("unused", 1)]
                    };
                    break;
                case UnreachableMetadataMutation.Member:
                    graph = graph with { Identities = [.. graph.Identities, graph.Identities.Length] };
                    graph = graph with
                    {
                        Members = [.. graph.Members, new PortableIrMember(
                        graph.Identities.Length - 1,
                        3,
                        "Unused",
                        1,
                        true,
                        [])]
                    };
                    break;
                case UnreachableMetadataMutation.Operation:
                    graph = graph with
                    {
                        Operations = [.. graph.Operations,
                        new PortableIrOperation("unused")]
                    };
                    break;
                case UnreachableMetadataMutation.Term:
                    graph = graph with
                    {
                        Terms = [.. graph.Terms, new PortableIrTerm(
                        IrTermKind.Integer,
                        1,
                        number: 42,
                        items: [])]
                    };
                    break;
                case UnreachableMetadataMutation.ReorderedOperations:
                    graph = graph with { Operations = graph.Operations.SetItem(0, graph.Operations[1]).SetItem(1, graph.Operations[0]) };
                    foreach (var instruction in graph.Blocks.SelectMany(
                                 static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions)))
                    {
                        graph = graph.ReplaceRow(instruction, instruction with
                        {
                            Operation = instruction.Operation switch
                            {
                                0 => 1,
                                1 => 0,
                                _ => instruction.Operation
                            }
                        });
                    }
                    foreach (var term in graph.Terms.Where(static term =>
                                 term is null ? throw new System.ArgumentNullException(nameof(term)) : (term.Kind == IrTermKind.Opaque && term.C != 0)))
                    {
                        graph = graph.ReplaceRow(term, term with
                        {
                            D = term.D switch
                            {
                                0 => 1,
                                1 => 0,
                                _ => term.D
                            }
                        });
                    }
                    break;
                default:
                    throw new AssertionException("Unknown metadata mutation.");
            }
            return graph;
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
        var exception = AssertDecoderRejects(
            graph =>
            {
                System.ArgumentNullException.ThrowIfNull(graph);
                switch (mutation)
                {
                    case CanonicalSlotMutation.TermUnusedIndex:
                        graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)) with { B = 0 });
                        break;
                    case CanonicalSlotMutation.TermUnusedNumber:
                        graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)) with { Number = 1 });
                        break;
                    case CanonicalSlotMutation.TermUnusedText:
                        graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)) with { Text = "tampered" });
                        break;
                    case CanonicalSlotMutation.TermEmptyItems:
                        graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Null)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Null)) with { Items = [0] });
                        break;
                    case CanonicalSlotMutation.InstructionUnusedIndex:
                        graph = graph.ReplaceRow(graph.Blocks
                            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                            .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Assign)), graph.Blocks
                            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                            .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Assign)) with
                            { C = 0 });
                        break;
                    case CanonicalSlotMutation.InstructionEmptyItems:
                        graph = graph.ReplaceRow(graph.Blocks
                            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                            .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Assign)), graph.Blocks
                            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                            .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Assign)) with
                            { Items = [0] });
                        break;
                    case CanonicalSlotMutation.InstructionUnusedLocation:
                        graph = graph.ReplaceRow(graph.Blocks
                            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                            .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Call)), graph.Blocks
                            .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                            .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Call)) with
                            { Location = new() });
                        break;
                    default:
                        throw new AssertionException("Unknown mutation.");
                }
                return graph;
            },
            serialize: true);
        if (mutation == CanonicalSlotMutation.TermUnusedIndex)
        {
            Assert.That(exception.Message,
                Is.EqualTo("Portable IR Boolean slots are not canonical."));
        }
    }

    [TestCase(WireEnumMutation.OpaquePurity)]
    [TestCase(WireEnumMutation.UnaryOperator)]
    [TestCase(WireEnumMutation.BinaryOperator)]
    [TestCase(WireEnumMutation.HavocKind)]
    public void DecoderRejectsUnknownWireEnumCodes(WireEnumMutation mutation)
    {
        var exception = AssertDecoderRejects(graph =>
        {
            System.ArgumentNullException.ThrowIfNull(graph);
            switch (mutation)
            {
                case WireEnumMutation.OpaquePurity:
                    graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Opaque)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Opaque)) with { C = 999 });
                    break;
                case WireEnumMutation.UnaryOperator:
                    graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Unary)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Unary)) with { A = 999 });
                    break;
                case WireEnumMutation.BinaryOperator:
                    graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Binary)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Binary)) with { A = 999 });
                    break;
                case WireEnumMutation.HavocKind:
                    graph = graph.ReplaceRow(graph.Blocks.SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                        .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Havoc)), graph.Blocks.SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                        .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Havoc)) with
                        { A = 999 });
                    break;
                default:
                    throw new AssertionException("Unknown mutation.");
            }
            return graph;
        });
        Assert.That(exception.Message,
            Is.EqualTo("Portable IR contains an unknown enum value."));
    }

    [Test]
    public void DecoderRejectsPureOpaqueTermWithImpureOperation()
    {
        var failure = AssertDecoderRejects(graph =>
        {
            System.ArgumentNullException.ThrowIfNull(graph);
            var opaque = graph.Terms.Single(static row =>
                row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Opaque && row.C != 0));
            Assert.That(opaque.D, Is.GreaterThanOrEqualTo(0));
            graph = graph.ReplaceRow(opaque, opaque with { C = 0 });
            return graph;
        });

        Assert.That(failure.Message,
            Is.EqualTo("Portable IR term metadata is invalid."));
    }

    [Test]
    public void DecoderRejectsNonBooleanScalarPayload()
    {
        var failure = AssertDecoderRejects(graph =>
            {
                System.ArgumentNullException.ThrowIfNull(graph);
                graph = graph.ReplaceRow(graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)), graph.Terms.First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean)) with { A = 2 });
                return graph;
            });

        Assert.That(failure.Message,
            Is.EqualTo("Portable IR term metadata is invalid."));
    }

    [Test]
    public void RootsOnlyGraphDoesNotFabricateAProgram()
    {
        var factory = new IrFactory();
        IrTerm[] roots = [factory.Integer(42)];

        var encoded = PortableIrGraphCodec.Encode(factory, null, roots, variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var decoded = PortableIrGraphCodec.Decode(encoded.Graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken);

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
            System.ArgumentNullException.ThrowIfNull(graph);
            switch (mutation)
            {
                case MalformedMutation.TermIndex:
                    graph = graph with { Roots = graph.Roots.SetItem(0, graph.Terms.Length) };
                    break;
                case MalformedMutation.TermCycle:
                    var unaryIndex = PortableGraphChanges.FindIndex(
                        graph.Terms,
                        static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Unary));
                    graph = graph with { Terms = graph.Terms.SetItem(unaryIndex, graph.Terms[unaryIndex] with { B = unaryIndex }) };
                    break;
                case MalformedMutation.TypeCycle:
                    var sequenceIndex = PortableGraphChanges.FindIndex(
                        graph.Types,
                        static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTypeKind.Sequence));
                    graph = graph with { Types = graph.Types.SetItem(sequenceIndex, graph.Types[sequenceIndex] with { Element = sequenceIndex }) };
                    break;
                case MalformedMutation.TermKind:
                    graph = graph with { Terms = graph.Terms.SetItem(0, graph.Terms[0] with { Kind = (IrTermKind)999 }) };
                    break;
                case MalformedMutation.TermType:
                    var booleanIndex = PortableGraphChanges.FindIndex(
                        graph.Terms,
                        static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrTermKind.Boolean));
                    graph = graph with { Terms = graph.Terms.SetItem(booleanIndex, graph.Terms[booleanIndex] with { Type = 1 }) };
                    break;
                case MalformedMutation.InstructionKind:
                    graph = graph with { Blocks = graph.Blocks.SetItem(0, graph.Blocks[0] with { Instructions = graph.Blocks[0].Instructions.SetItem(0, graph.Blocks[0].Instructions[0] with { Kind = (IrInstructionKind)999 }) }) };
                    break;
                case MalformedMutation.NullTopLevelArray:
                    graph = graph with { Roots = default };
                    break;
                case MalformedMutation.NullMemberParameters:
                    graph = graph with { Members = graph.Members.SetItem(0, graph.Members[0] with { ParameterTypes = default }) };
                    break;
                case MalformedMutation.NonCanonicalIdentity:
                    graph = graph with { Identities = graph.Identities.SetItem(0, 1) };
                    break;
                case MalformedMutation.CollapsedMemberPartition:
                    graph = graph with { Members = graph.Members.SetItem(1, graph.Members[0]) };
                    break;
                case MalformedMutation.CollapsedTermPartition:
                    graph = graph with { Terms = graph.Terms.SetItem(1, graph.Terms[0]) };
                    break;
                case MalformedMutation.NullInstructionItems:
                    graph = graph with { Blocks = graph.Blocks.SetItem(0, graph.Blocks[0] with { Instructions = graph.Blocks[0].Instructions.SetItem(0, graph.Blocks[0].Instructions[0] with { Items = default }) }) };
                    break;
                case MalformedMutation.LocationKind:
                    graph = graph.ReplaceRow(graph.Blocks[0].Instructions
                        .First(static instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Location != null))
                        .Location!, graph.Blocks[0].Instructions
                        .First(static instruction => instruction is null ? throw new System.ArgumentNullException(nameof(instruction)) : (instruction.Location != null))
                        .Location! with
                        { Kind = (IrLocationKind)999 });
                    break;
                case MalformedMutation.ProgramShape:
                    graph = graph with { HasProgram = false };
                    break;
                case MalformedMutation.DuplicateHavocVariable:
                    var havoc = graph.Blocks
                        .SelectMany(static block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions))
                        .First(static row => row is null ? throw new System.ArgumentNullException(nameof(row)) : (row.Kind == IrInstructionKind.Havoc));
                    graph = graph.ReplaceRow(havoc, havoc with { Items = [havoc.Items[0], havoc.Items[0]] });
                    break;
                case MalformedMutation.WhitespaceOperationDescription:
                    graph = graph with { Operations = graph.Operations.SetItem(0, graph.Operations[0] with { Description = " " }) };
                    break;
                case MalformedMutation.WhitespaceBlockName:
                    graph = graph with { Blocks = graph.Blocks.SetItem(0, graph.Blocks[0] with { Name = "\t" }) };
                    break;
                default:
                    throw new AssertionException("Unknown mutation.");
            }
            return graph;
        });

        if (mutation == MalformedMutation.WhitespaceOperationDescription)
        {
            Assert.That(
                exception!.Message,
                Is.EqualTo("Portable IR operation description cannot be whitespace."));
        }
        if (mutation == MalformedMutation.DuplicateHavocVariable)
        {
            Assert.That(exception.Message,
                Is.EqualTo("Portable IR havoc variables are not canonical."));
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
            (Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
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
            PortableIrGraphCodec.Encode(factory, null, [term], variables: null, cancellationToken: TestContext.CurrentContext.CancellationToken)));
    }

    private static IrTerm DeepTerm(IrFactory factory)
    {
        System.ArgumentNullException.ThrowIfNull(factory);
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
        System.ArgumentNullException.ThrowIfNull(factory);
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
        Func<PortableIrGraph, PortableIrGraph> mutate,
        bool serialize = false)
    {
        System.ArgumentNullException.ThrowIfNull(mutate);
        var fixture = CreateFixture();
        var graph = PortableIrGraphCodec.Encode(
            fixture.Program,
            fixture.Roots, cancellationToken: TestContext.CurrentContext.CancellationToken).Graph;
        if (serialize)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                graph,
                WorkerProtocolJson.Options);
            graph = JsonSerializer.Deserialize<PortableIrGraph>(
                bytes,
                WorkerProtocolJson.Options)!;
        }

        graph = mutate(graph);
        return Assert.Throws<InvalidDataException>(
            (Action)(() => PortableIrGraphCodec.Decode(graph, externalVariableIndices: null, cancellationToken: TestContext.CurrentContext.CancellationToken)))!;
    }

    private static PortableIrGraph DeepGraph(
        DeepGraphKind kind,
        bool cyclic,
        int depth)
    {
        if (depth < 0)
        { throw new System.ArgumentException("Value must be initialized.", nameof(depth)); }

        var graph = new PortableIrGraph
        {
            Types = [
                new() { Kind = IrTypeKind.Boolean, Name = "bool" },
                new() { Kind = IrTypeKind.Integer, Name = "int", Signed = true },
                new() { Kind = IrTypeKind.String, Name = "string" },
                new() { Kind = IrTypeKind.Reference, Name = "object" }
            ]
        };
        if (kind == DeepGraphKind.Types)
        {
            var types = graph.Types;
            types = types.AddRange(Enumerable.Repeat<PortableIrType>(null!, depth));
            graph = graph with { Types = types };
            for (var index = 4; index < graph.Types.Length; index++)
            {
                graph = graph with
                {
                    Types = graph.Types.SetItem(index, new PortableIrType
                    {
                        Kind = IrTypeKind.Sequence,
                        Name = $"sequence-{index}",
                        Element = index == graph.Types.Length - 1
                        ? cyclic ? 4 : 0
                        : index + 1
                    })
                };
            }
            graph = graph with { Variables = [new PortableIrVariable("deep", 4)] };
            graph = graph with
            {
                Terms = [new PortableIrTerm(
                IrTermKind.Variable,
                4,
                a: 0,
                items: [])]
            };
            graph = graph with { Roots = [0] };
        }
        else
        {
            graph = graph with { Terms = Enumerable.Repeat<PortableIrTerm>(null!, depth).ToImmutableArray() };
            for (var index = 0; index < graph.Terms.Length; index++)
            {
                graph = graph with
                {
                    Terms = graph.Terms.SetItem(index, index == graph.Terms.Length - 1 &&
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
                    })
                };
            }

            graph = graph with { Roots = [0] };
        }
        return graph;
    }

    private static CodecFixture CreateFixture(bool includeExceptionEdges = true, bool includeAllocation = false)
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
        var exceptionalExit = includeExceptionEdges ? builder.CreateBlock("exceptional-exit") : (IrBlockId?)null;
        var memberLocation = builder.MemberLocation(valueMember, boxTerm);
        var sequenceLocation = builder.SequenceLocation(sequenceTerm, numberTerm);
        builder.Assign(entry, factory.CreateOperation("same"), result, numberTerm);
        builder.Load(entry, factory.CreateOperation("same"), result, memberLocation);
        builder.Store(entry, factory.CreateOperation("store-member"), memberLocation, numberTerm);
        builder.Load(entry, factory.CreateOperation("load-sequence"), result, sequenceLocation);
        builder.Store(entry, factory.CreateOperation("store-sequence"), sequenceLocation, numberTerm);
        if (includeAllocation)
        {
            builder.Allocate(entry, factory.CreateOperation("allocate"), factory.ObjectType);
            builder.Allocate(entry, factory.CreateOperation("allocate-string"), factory.StringType);
            builder.Allocate(entry, factory.CreateOperation("allocate-value"), boxType, box);
            builder.Lock(entry, factory.CreateOperation("synchronize"), boxTerm);
            foreach (var region in Enum.GetValues<IrWriteRegion>())
            { builder.Write(entry, factory.CreateOperation("write:" + region), region); }
        }
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
        if (exceptionalExit is { } exceptional)
        {
            builder.Throw(exit, factory.CreateOperation("throw"), IrExceptionKind.DivideByZero, exceptional);
            builder.ExceptionalExit(exceptional, factory.CreateOperation("exceptional-exit"));
        }
        else
        {
            builder.Return(exit, factory.CreateOperation("return"), numberTerm);
        }
        var program = builder.Build();
        return new CodecFixture(
            factory,
            program,
            roots.ToImmutableArray(),
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
        ImmutableArray<IrTerm> Roots,
        int ConditionalRoot,
        int FlagRoot)
    {
        public IrFactory Factory { get; init; } = Factory ?? throw new System.ArgumentNullException(nameof(Factory));
        public IrProgram Program { get; init; } = Program ?? throw new System.ArgumentNullException(nameof(Program));
        public ImmutableArray<IrTerm> Roots { get; init; } = Roots.IsDefault ? throw new ArgumentException("Array must be initialized.", nameof(Roots)) : Roots;
    }
}
