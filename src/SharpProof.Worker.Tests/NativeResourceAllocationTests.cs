using System.Numerics;
using System.Text.Json.Nodes;
using NUnit.Framework;
using Microsoft.CodeAnalysis;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeResourceAllocationTests
{
    private static ResourceRuntimeLayoutContext Layout()
    {
        return new(ResourceRuntimeLayout.RuntimeFamily, ResourceRuntimeLayout.RuntimeVersion,
            ResourceRuntimeLayout.TargetArchitecture, ResourceRuntimeLayout.PointerBytes);
    }

    private static CompilerTotalCallablePreparation Owner()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Return(block, factory.CreateOperation("return"));
        return new("owned", builder.Build(), [], null, []);
    }

    private static ResourceProgram Array(ResourceStorageKind storage, int length)
    {
        return ResourceProgram.TypedArray(ResourceAllocationShape.Array(storage, ResourceExpression.Literal(length)));
    }

    private static NativeResourceAllocationCheck Check(ResourceProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var decoded = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(program, cancellation), cancellation);
        return NativeResourceAllocationVerifier.Verify(decoded, "owned", Owner(), Layout(), cancellation);
    }

    [TestCase((int)ResourceStorageKind.Byte, 0, 24)]
    [TestCase((int)ResourceStorageKind.Byte, 9, 40)]
    [TestCase((int)ResourceStorageKind.Word16, 5, 40)]
    [TestCase((int)ResourceStorageKind.Word32, 3, 40)]
    [TestCase((int)ResourceStorageKind.Word64, 3, 48)]
    [TestCase((int)ResourceStorageKind.Reference, 3, 48)]
    [TestCase((int)ResourceStorageKind.NativeInteger, 3, 48)]
    public void TypedArrayTransportDerivesConditionalBytes(int storage, int length, int expected)
    {
        var result = Check(Array((ResourceStorageKind)storage, length));
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.ConditionalUpperBound!.Constant, Is.EqualTo(new BigInteger(expected)));
    }

    [Test]
    public void CompositionUsesUpperEnvelopesWithoutClaimingLowerBounds()
    {
        var rank = new ResourceLoopRank(ResourceLoopKind.IncreasingAdd, ResourceExpression.Zero,
            ResourceExpression.Literal(3), 1, 32, false);
        var body = ResourceProgram.Choice(Array(ResourceStorageKind.Byte, 1), Array(ResourceStorageKind.Word64, 3));
        var program = ResourceProgram.Sequence(Array(ResourceStorageKind.Word32, 2),
            ResourceProgram.Repeat(rank, ResourceRepeatKind.Loop, body));
        var result = Check(program);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.ConditionalUpperBound!.Constant, Is.EqualTo(new BigInteger(32 + 3 * 48)));
    }

    [Test]
    public void UnspecifiedStorageDoesNotEraseEstablishedElementCosts()
    {
        var program = ResourceProgram.Array(ResourceExpression.Literal(7));
        var result = Check(program);
        Assert.That(result.ConditionalUpperBound, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("MissingResourceAllocationStorage"));
        var elements = NativeResourceProgramVerifier.Verify(program, "owned", Owner(), TestContext.CurrentContext.CancellationToken);
        Assert.That(elements.Cost!.Elements.Constant, Is.EqualTo(new BigInteger(7)));
    }

    [Test]
    public void IncompatibleLayoutHasNoPartialBoundEvenWithoutAllocations()
    {
        var result = NativeResourceAllocationVerifier.Verify(ResourceProgram.Zero, "owned", Owner(),
            new("CoreCLR", "8.0.32", "arm64", 8), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.ConditionalUpperBound, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
    }

    [Test]
    public void UnownedTypedExtentCannotSupplyConditionalBytes()
    {
        var foreign = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "foreign", int.MaxValue));
        var result = Check(ResourceProgram.TypedArray(ResourceAllocationShape.Array(ResourceStorageKind.Byte, foreign)));
        Assert.That(result.ConditionalUpperBound, Is.Null);
        Assert.That(result.UnknownReason, Does.Contain("ordinal"));
    }

    [Test]
    public void InvalidRankCannotSupplyConditionalBytes()
    {
        var rank = new ResourceLoopRank(ResourceLoopKind.IncreasingMultiply, ResourceExpression.One,
            ResourceExpression.Literal(10), 1, 32, false);
        var result = Check(ResourceProgram.Repeat(rank, ResourceRepeatKind.Loop, Array(ResourceStorageKind.Byte, 1)));
        Assert.That(result.ConditionalUpperBound, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("UnprovedResourceProgress"));
    }

    [Test]
    public void MetadataHasNoImplicitByteCost()
    {
        var result = Check(ResourceProgram.Sequence(Array(ResourceStorageKind.Byte, 1), ResourceProgram.MetadataBoundary));
        Assert.That(result.ConditionalUpperBound, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void TransportRejectsUnrelatedOrConflictingAllocationFacts(int mutation)
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var root = JsonNode.Parse(ResourceProgramCodec.Encode(Array(ResourceStorageKind.Word32, 3), cancellation))!.AsObject();
        var program = root["program"]!.AsObject();
        if (mutation == 0)
        { program["length"] = Convert.ToBase64String(ResourceExpressionCodec.Encode(ResourceExpression.One, cancellation)); }
        else if (mutation == 1)
        { program["allocation"] = Convert.ToBase64String(ResourceAllocationShapeCodec.Encode(ResourceAllocationShape.String(ResourceExpression.Literal(3)), cancellation)); }
        else if (mutation == 2)
        { program["allocation"] = " " + program["allocation"]!.GetValue<string>(); }
        else
        { program["kind"] = (int)ResourceProgramKind.Zero; program["length"] = null; }
        Action decode = () => ResourceProgramCodec.Decode(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()), cancellation);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void ArrayFactoryRejectsOtherManagedShapes()
    {
        Action create = () => ResourceProgram.TypedArray(ResourceAllocationShape.Delegate());
        Assert.Throws<ArgumentException>(create);
    }

    [Test]
    public void CancellationPropagatesWithoutPartialBound()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action verify = () => NativeResourceAllocationVerifier.Verify(ResourceProgram.Zero, "owned", Owner(), Layout(), cancellation.Token);
        Assert.Throws<OperationCanceledException>(verify);
    }
    [TestCase("var a = new byte[n];", 32)]
    [TestCase("var a = new long[n];", 80)]
    [TestCase("for(int i=0;i<n;i++) { var a = new int[3]; }", 280)]
    [TestCase("Leaf(n);", 224)]
    [TestCase("if(n>0) { var a=new short[3]; } else { var a=new double[3]; }", 48)]
    public void CompilerOwnedProgramsRetainStorageThroughCallsAndTransport(string body, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { [ZeroAllocations] public static void Target(int n) { " + body +
            " } private static void Leaf(int size) { for(int i=0;i<size;i++) { var a = new int[2]; } } }";
        var compilation = TestCompilation.Create("ResourceBytes", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax>()
            .Single(static candidate => candidate is null ? throw new ArgumentNullException(nameof(candidate)) : candidate.Identifier.ValueText == "Target");
        var model = compilation.GetSemanticModel(tree);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var analysis = ResourceBoundsAnalysis.Analyze((Microsoft.CodeAnalysis.IMethodSymbol)model.GetDeclaredSymbol(method)!,
            model.GetOperation(method.Body!), cancellation);
        Assert.That(analysis.Proven, Is.True, analysis.UnknownReason);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CompilerTotalCallableArtifactTests.CreateArtifact(source), cancellationToken: cancellation);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellationToken: cancellation);
        var owner = preparations.Single();
        Assert.That(owner.Total, Is.Not.Null);
        var decoded = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(analysis.Bounds!.Program, cancellation), cancellation);
        var result = NativeResourceAllocationVerifier.Verify(decoded, owner.Entry.CallableId, owner.Total!, Layout(), cancellation);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.ConditionalUpperBound!.Evaluate(static _ => new BigInteger(7), cancellation), Is.EqualTo(new BigInteger(expected)));
    }
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void DetachedNativeProgramsRejectConflictingAllocationFacts(int mutation)
    {
        var shape = mutation == 0 ? ResourceAllocationShape.String(ResourceExpression.One) :
            ResourceAllocationShape.Array(ResourceStorageKind.Byte, ResourceExpression.One);
        var kind = mutation == 2 ? ResourceProgramKind.Zero : ResourceProgramKind.AllocateArray;
        var length = mutation == 1 ? ResourceExpression.Literal(2) : ResourceExpression.One;
        var constructor = typeof(ResourceProgram).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var program = (ResourceProgram)constructor.Invoke([kind, length, null, ResourceRepeatKind.Loop,
            System.Collections.Immutable.ImmutableArray<ResourceProgram>.Empty, null, shape, null]);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var counts = NativeResourceProgramVerifier.Verify(program, "owned", Owner(), cancellation);
        Assert.That(counts.Cost, Is.Null);
        Assert.That(counts.UnknownReason, Is.EqualTo("MalformedResourceAllocation"));
        var bytes = NativeResourceAllocationVerifier.Verify(program, "owned", Owner(), Layout(), cancellation);
        Assert.That(bytes.ConditionalUpperBound, Is.Null);
        Assert.That(bytes.UnknownReason, Is.EqualTo("MalformedResourceAllocation"));
    }
    [TestCase(0, 32)]
    [TestCase(1, 32)]
    [TestCase(2, 24)]
    [TestCase(3, 64)]
    [TestCase(4, 96)]
    public void ManagedStorageFactsRoundTripWithoutInventingArrayCounts(int kind, int expected)
    {
        var shape = kind switch
        {
            0 => ResourceAllocationShape.Object([ResourceStorageKind.Byte, ResourceStorageKind.Word32]),
            1 => ResourceAllocationShape.String(ResourceExpression.Literal(3)),
            2 => ResourceAllocationShape.Box(ResourceStorageKind.Word64),
            3 => ResourceAllocationShape.Delegate(),
            _ => ResourceAllocationShape.Closure([ResourceStorageKind.Word32, ResourceStorageKind.Word32])
        };
        var program = ResourceProgram.Managed(shape);
        var result = Check(program);
        Assert.That(result.UnknownReason, Is.Null);
        Assert.That(result.ConditionalUpperBound!.Constant, Is.EqualTo(new BigInteger(expected)));
        var counts = NativeResourceProgramVerifier.Verify(program, "owned", Owner(), TestContext.CurrentContext.CancellationToken);
        Assert.That(counts.Cost!.LoopEntries.Constant, Is.EqualTo(BigInteger.Zero));
        Assert.That(counts.Cost.Arrays.Constant, Is.EqualTo(BigInteger.Zero));
        Assert.That(counts.Cost.Elements.Constant, Is.EqualTo(BigInteger.Zero));
    }

    [Test]
    public void ManagedTransportRejectsArrayOrMissingStoragePayloads()
    {
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var root = JsonNode.Parse(ResourceProgramCodec.Encode(ResourceProgram.Managed(ResourceAllocationShape.Object([])), cancellation))!.AsObject();
        var program = root["program"]!.AsObject();
        program["allocation"] = Convert.ToBase64String(ResourceAllocationShapeCodec.Encode(ResourceAllocationShape.Array(ResourceStorageKind.Byte, ResourceExpression.One), cancellation));
        Action wrongShape = () => ResourceProgramCodec.Decode(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()), cancellation);
        Assert.Throws<InvalidDataException>(wrongShape);
        program["allocation"] = null;
        Action missing = () => ResourceProgramCodec.Decode(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()), cancellation);
        Assert.Throws<InvalidDataException>(missing);
    }

    [Test]
    public void ManagedFieldsContributeToTheExpandedProgramBudget()
    {
        var fields = System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(ResourceStorageKind.Byte, 1024));
        var program = ResourceProgram.Managed(ResourceAllocationShape.Object(fields));
        Assert.That(program.NodeCount, Is.EqualTo(1026));
        var pair = ResourceProgram.Sequence(program, program);
        Action tooMany = () => ResourceProgram.Sequence(pair, pair);
        Assert.Throws<ResourceExpressionLimitException>(tooMany);
    }
    [TestCase(0, "MalformedResourceActivation")]
    [TestCase(1, "MalformedResourceActivation")]
    [TestCase(2, "MalformedResourceAllocation")]
    [TestCase(3, "MalformedResourceAllocation")]
    public void DetachedProgramsCannotSmuggleActivationOrManagedAllocationPayloads(int mutation, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var constructor = typeof(ResourceProgram).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var kind = mutation == 0 ? ResourceProgramKind.Activation : mutation == 1 ? ResourceProgramKind.Zero : ResourceProgramKind.AllocateManaged;
        var activation = mutation < 2 ? new ResourceActivationShape([], false, false) : null;
        var length = mutation == 3 ? ResourceExpression.One : null;
        var shape = mutation == 3 ? ResourceAllocationShape.Object([]) : null;
        var program = (ResourceProgram)constructor.Invoke([kind, length, null, ResourceRepeatKind.Loop,
            System.Collections.Immutable.ImmutableArray<ResourceProgram>.Empty, null, shape, activation]);
        var token = TestContext.CurrentContext.CancellationToken;
        var counts = NativeResourceProgramVerifier.Verify(program, "owned", Owner(), token);
        Assert.That(counts.Verified, Is.False);
        Assert.That(counts.Cost, Is.Null);
        Assert.That(counts.UnknownReason, Is.EqualTo(expected));
        var bytes = NativeResourceAllocationVerifier.Verify(program, "owned", Owner(), Layout(), token);
        Assert.That(bytes.ConditionalUpperBound, Is.Null);
        Assert.That(bytes.UnknownReason, Is.EqualTo(expected));
    }
}
