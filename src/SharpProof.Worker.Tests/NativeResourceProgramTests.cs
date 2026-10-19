using System.Numerics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeResourceProgramTests
{
    private static CompilerTotalCallablePreparation Owner()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Return(block, factory.CreateOperation("return"));
        return new("owned", builder.Build(), [], null, []);
    }

    private static ResourceLoopRank Rank(int kind, int start, int limit, long step, bool checkedStep = false, int width = 32)
    {
        return new((ResourceLoopKind)kind, ResourceExpression.Literal(start), ResourceExpression.Literal(limit), step, width, checkedStep);
    }

    private static NativeResourceProgramCheck Check(ResourceProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return NativeResourceProgramVerifier.Verify(program, "owned", Owner(), TestContext.CurrentContext.CancellationToken);
    }

    [TestCase(0, 0, 10, 1, 10)]
    [TestCase(0, 2, 10, 3, 3)]
    [TestCase(0, 10, 2, 3, 0)]
    [TestCase(1, 1, 17, 2, 5)]
    [TestCase(1, 1, 1, 2, 0)]
    [TestCase(2, 17, 2, 3, 5)]
    [TestCase(2, 2, 17, 3, 0)]
    [TestCase(3, 17, 0, 2, 5)]
    public void RankBoundsMatchIndependentSmallExecutions(int kind, int start, int limit, long step, int expected)
    {
        var result = Check(ResourceProgram.Repeat(Rank(kind, start, limit, step), ResourceRepeatKind.Loop,
            ResourceProgram.Array(ResourceExpression.Literal(3))));
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        int observed = 0;
        var counter = new BigInteger(start);
        while (kind < 2 ? counter < limit : counter > limit)
        {
            Assert.That(++observed, Is.LessThan(100));
            counter = kind switch { 0 => counter + step, 1 => counter * step, 2 => counter - step, _ => counter / step };
        }
        Assert.That(observed, Is.EqualTo(expected));
        Assert.That(result.Cost!.LoopEntries.Constant, Is.EqualTo(new BigInteger(observed)));
        Assert.That(result.Cost.Arrays.Constant, Is.EqualTo(new BigInteger(observed)));
        Assert.That(result.Cost.Elements.Constant, Is.EqualTo(new BigInteger(observed * 3)));
    }

    [Test]
    public void CompositionCountsNestedLoopsAndTakesSeparateBranchMaximums()
    {
        var allocation = ResourceProgram.Array(ResourceExpression.Literal(4));
        var inner = ResourceProgram.Repeat(Rank(0, 0, 3, 1), ResourceRepeatKind.Loop, allocation);
        var nested = ResourceProgram.Repeat(Rank(0, 0, 2, 1), ResourceRepeatKind.Loop, inner);
        var result = Check(ResourceProgram.Sequence(ResourceProgram.Zero, nested));
        Assert.That(result.Cost!.LoopEntries.Constant, Is.EqualTo(new BigInteger(8)));
        Assert.That(result.Cost.Arrays.Constant, Is.EqualTo(new BigInteger(6)));
        Assert.That(result.Cost.Elements.Constant, Is.EqualTo(new BigInteger(24)));
        var choice = Check(ResourceProgram.Choice(nested, ResourceProgram.Array(ResourceExpression.Literal(50))));
        Assert.That(choice.Cost!.Arrays.Constant, Is.EqualTo(new BigInteger(6)));
        Assert.That(choice.Cost.Elements.Constant, Is.EqualTo(new BigInteger(50)));
    }

    [Test]
    public void TailRecurrenceCountsCostsWithoutInventingLoopEntries()
    {
        var repeat = ResourceProgram.Repeat(Rank(2, 10, 0, 3), ResourceRepeatKind.TailRecurrence,
            ResourceProgram.Array(ResourceExpression.Literal(2)));
        var result = Check(repeat);
        Assert.That(result.Verified, Is.True, result.UnknownReason);
        Assert.That(result.Cost!.LoopEntries.Constant, Is.EqualTo(BigInteger.Zero));
        Assert.That(result.Cost.Arrays.Constant, Is.EqualTo(new BigInteger(4)));
        Assert.That(result.Cost.Elements.Constant, Is.EqualTo(new BigInteger(8)));
    }

    [TestCase(0, 0, 10, 0, 32, "MalformedResourceRank")]
    [TestCase(99, 0, 10, 1, 32, "MalformedResourceRank")]
    [TestCase(0, 0, 10, 1, 16, "MalformedResourceRank")]
    [TestCase(1, 1, 10, 1, 32, "UnprovedResourceProgress")]
    [TestCase(1, 2, 10, 2, 32, "UnprovedResourceProgress")]
    [TestCase(3, 10, 1, 2, 32, "UnprovedResourceProgress")]
    public void UnprovedRanksHaveExplicitUnknownWithoutPartialBounds(int kind, int start, int limit, long step, int width, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var result = Check(ResourceProgram.Sequence(ResourceProgram.Array(ResourceExpression.One),
            ResourceProgram.Repeat(Rank(kind, start, limit, step, width: width), ResourceRepeatKind.Loop, ResourceProgram.Zero)));
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Cost, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo(reason));
    }

    [Test]
    public void CheckedOverflowBoundsThePrefixWhileUncheckedWrapIsUnknown()
    {
        var unproved = Check(ResourceProgram.Repeat(Rank(0, 0, int.MaxValue, 2), ResourceRepeatKind.Loop, ResourceProgram.Zero));
        Assert.That(unproved.UnknownReason, Is.EqualTo("ResourceCounterMayOverflow"));
        var prefix = Check(ResourceProgram.Repeat(Rank(0, 0, int.MaxValue, 2, true), ResourceRepeatKind.Loop, ResourceProgram.Zero));
        Assert.That(prefix.Cost!.LoopEntries.Constant, Is.EqualTo(new BigInteger(1073741824)));
    }

    [TestCase("Leaf(n);")]
    [TestCase("var a = new int[n];")]
    [TestCase("for(int i=0;i<n;i++) { var a = new int[2]; }")]
    [TestCase("for(int i=n;i>0;i-=2) { var a = new int[3]; }")]
    [TestCase("for(int i=1;i<16;i*=2) { var a = new int[i]; }")]
    [TestCase("int remaining=n; while(remaining>0) { var a = new int[2]; remaining/=2; }")]
    [TestCase("if(n>0) { var a=new int[2]; } else { var b=new int[5]; }")]
    public void ActualSourceProgramsHaveIndependentlyRecomputedWorkerBounds(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using SharpProof.Attributes; public static class C { [ZeroAllocations] public static void Target(int n) { " + body +
            " } private static void Leaf(int size) { for(int i=0;i<size;i++) { var a = new int[2]; } } }";
        var compilation = TestCompilation.Create("ResourceProgram", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var method = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Target");
        var model = compilation.GetSemanticModel(tree);
        var analysis = ResourceBoundsAnalysis.Analyze((IMethodSymbol)model.GetDeclaredSymbol(method)!, model.GetOperation(method.Body!), TestContext.CurrentContext.CancellationToken);
        Assert.That(analysis.Proven, Is.True, analysis.UnknownReason);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CompilerTotalCallableArtifactTests.CreateArtifact(source), cancellationToken: TestContext.CurrentContext.CancellationToken);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var owner = preparations.Single();
        Assert.That(owner.Total, Is.Not.Null);
        var payload = ResourceProgramCodec.Encode(analysis.Bounds!.Program, TestContext.CurrentContext.CancellationToken);
        var decoded = ResourceProgramCodec.Decode(payload, TestContext.CurrentContext.CancellationToken);
        Assert.That(ResourceProgramCodec.Encode(decoded, TestContext.CurrentContext.CancellationToken), Is.EqualTo(payload));
        var check = NativeResourceProgramVerifier.Verify(decoded, owner.Entry.CallableId, owner.Total!, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Verified, Is.True, check.UnknownReason);
        Assert.That(check.Cost!.LoopEntries, Is.EqualTo(analysis.Bounds.LoopIterations.Expression));
        Assert.That(check.Cost.Arrays, Is.EqualTo(analysis.Bounds.Arrays.Expression));
        Assert.That(check.Cost.Elements, Is.EqualTo(analysis.Bounds.Elements.Expression));
    }

    [Test]
    public void CancellationDoesNotBecomeAnUnknownProof()
    {
        Action check = () => NativeResourceProgramVerifier.Verify(ResourceProgram.Zero, "owned", Owner(), new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(check);
    }
    [TestCase("version")]
    [TestCase("expressionModel")]
    [TestCase("costModel")]
    public void TransportRejectsIncompatibleModels(string field)
    {
        ArgumentNullException.ThrowIfNull(field);
        var payload = ResourceProgramCodec.Encode(ResourceProgram.Zero, TestContext.CurrentContext.CancellationToken);
        var root = JsonNode.Parse(payload)!.AsObject();
        root[field] = field == "costModel" ? JsonValue.Create("other") : JsonValue.Create(99);
        Action decode = () => ResourceProgramCodec.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(decode);
    }

    [TestCase("{\"kind\":99,\"length\":null,\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[]}")]
    [TestCase("{\"kind\":0,\"kind\":0,\"length\":null,\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[]}")]
    [TestCase("{\"kind\":0,\"length\":null,\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[],\"count\":0}")]
    [TestCase("{\"kind\":0,\"length\":null,\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":1,\"children\":[]}")]
    [TestCase("{\"kind\":0,\"length\":\"x\",\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[]}")]
    [TestCase("{\"kind\":1,\"length\":\"!\",\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[]}")]
    [TestCase("{\"kind\":2,\"length\":null,\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[]}")]
    public void MalformedTransportCannotSupplyAProducerVerdict(string node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var payload = "{\"version\":4,\"expressionModel\":1,\"costModel\":\"" + ResourceProgramCodec.CostModel + "\",\"program\":" + node + "}";
        Action decode = () => ResourceProgramCodec.Decode(Encoding.UTF8.GetBytes(payload), TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void ValidTransportDoesNotProveAnInvalidProgressRule()
    {
        var program = ResourceProgram.Repeat(Rank(1, 1, 10, 1), ResourceRepeatKind.Loop, ResourceProgram.Zero);
        var decoded = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(program, TestContext.CurrentContext.CancellationToken), TestContext.CurrentContext.CancellationToken);
        Assert.That(Check(decoded).UnknownReason, Is.EqualTo("UnprovedResourceProgress"));
    }

    [Test]
    public void RawTransportBudgetIsCheckedBeforeZeroSequencesCollapse()
    {
        const string zero = "{\"kind\":0,\"length\":null,\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[]}";
        var node = zero;
        for (int level = 0; level < 12; level++)
        { node = "{\"kind\":2,\"length\":null,\"rank\":null,\"allocation\":null,\"metadata\":null,\"repeatKind\":0,\"children\":[" + node + "," + node + "]}"; }
        var payload = Encoding.UTF8.GetBytes("{\"version\":4,\"expressionModel\":1,\"costModel\":\"" + ResourceProgramCodec.CostModel + "\",\"program\":" + node + "}");
        Assert.That(payload.Length, Is.LessThan(ResourceProgramCodec.MaximumPayloadBytes));
        Action decode = () => ResourceProgramCodec.Decode(payload, TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void UnownedInputsDoNotYieldPartialNativeBounds()
    {
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.ArrayLength, "foreign", int.MaxValue));
        var result = Check(ResourceProgram.Sequence(ResourceProgram.Array(ResourceExpression.One), ResourceProgram.Array(input)));
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Cost, Is.Null);
        Assert.That(result.UnknownReason, Does.Contain("ordinal"));
    }

    [Test]
    public void ProgramTransportHonorsCancellation()
    {
        var payload = ResourceProgramCodec.Encode(ResourceProgram.Zero, TestContext.CurrentContext.CancellationToken);
        Action encode = () => ResourceProgramCodec.Encode(ResourceProgram.Zero, new CancellationToken(true));
        Action decode = () => ResourceProgramCodec.Decode(payload, new CancellationToken(true));
        Assert.Throws<OperationCanceledException>(encode);
        Assert.Throws<OperationCanceledException>(decode);
    }
    [TestCase("witness", "")]
    [TestCase("catalog", " ")]
    [TestCase("assembly", "")]
    [TestCase("witness", "bad\nidentifier")]
    [TestCase("extra", "unknown")]
    public void MetadataTransportRejectsMalformedReferenceRecords(string field, string value)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(value);
        var program = ResourceProgram.MetadataCall(new ResourceMetadataReference("reviewed", "digest", "assembly"));
        var payload = ResourceProgramCodec.Encode(program, TestContext.CurrentContext.CancellationToken);
        var root = JsonNode.Parse(payload)!.AsObject();
        root["program"]!["metadata"]![field] = value;
        Action decode = () => ResourceProgramCodec.Decode(Encoding.UTF8.GetBytes(root.ToJsonString()), TestContext.CurrentContext.CancellationToken);
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void StructuredMetadataStillRequiresAnExplicitBindingAuthority()
    {
        var program = ResourceProgram.MetadataCall(new ResourceMetadataReference("reviewed", "digest", "assembly"));
        var decoded = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(program, TestContext.CurrentContext.CancellationToken), TestContext.CurrentContext.CancellationToken);
        Assert.That(decoded.Metadata!.AssemblyIdentity, Is.EqualTo("assembly"));
        var result = Check(decoded);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Cost, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
    }
    [Test]
    public void ConditionalMetadataCannotSilentlyBecomeAnUnconditionalNativeProof()
    {
        var program = ResourceProgram.Sequence(ResourceProgram.Array(ResourceExpression.One), ResourceProgram.MetadataBoundary);
        var decoded = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(program, TestContext.CurrentContext.CancellationToken), TestContext.CurrentContext.CancellationToken);
        var result = Check(decoded);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Cost, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ResourceMetadataAssumptionsNotBound"));
    }
    [Test]
    public void ProgramConstructionEnforcesNodeAndDepthBudgets()
    {
        Action tooMany = () =>
        {
            var program = ResourceProgram.Array(ResourceExpression.One);
            for (int level = 0; level < 14; level++)
            { program = ResourceProgram.Sequence(program, program); }
        };
        Action tooDeep = () =>
        {
            var program = ResourceProgram.Zero;
            for (int level = 0; level < ResourceExpression.MaximumDepth + 1; level++)
            { program = ResourceProgram.Repeat(Rank(0, 0, 1, 1), ResourceRepeatKind.Loop, program); }
        };
        Assert.Throws<ResourceExpressionLimitException>(tooMany);
        Assert.Throws<ResourceExpressionLimitException>(tooDeep);
    }
    [TestCase(true)]
    [TestCase(false)]
    public void RankDomainsCannotWidenThePhysicalCounter(bool increasing)
    {
        var large = ResourceExpression.Literal(long.MaxValue);
        var rank = new ResourceLoopRank(increasing ? ResourceLoopKind.IncreasingAdd : ResourceLoopKind.DecreasingSubtract,
            increasing ? ResourceExpression.Zero : large, increasing ? large : ResourceExpression.Zero, 1, 32, true);
        var result = Check(ResourceProgram.Repeat(rank, ResourceRepeatKind.Loop, ResourceProgram.Zero));
        Assert.That(result.UnknownReason, Is.EqualTo("UnsupportedResourceRankDomain"));
        Assert.That(result.Cost, Is.Null);
    }

    [Test]
    public void DescendingUncheckedNarrowUpdatesCannotWrap()
    {
        var rank = new ResourceLoopRank(ResourceLoopKind.DecreasingSubtract, ResourceExpression.Literal(2), ResourceExpression.Zero,
            long.MaxValue, 32, false);
        Assert.That(Check(ResourceProgram.Repeat(rank, ResourceRepeatKind.Loop, ResourceProgram.Zero)).UnknownReason,
            Is.EqualTo("ResourceCounterMayOverflow"));
    }

    [TestCase(0)]
    [TestCase(99)]
    public void RecurrenceKindsCannotBorrowAnUnrelatedProgressRule(int kind)
    {
        var repeat = kind == 0 ? ResourceProgram.Repeat(Rank(0, 0, 4, 1), ResourceRepeatKind.TailRecurrence, ResourceProgram.Zero) :
            ResourceProgram.Repeat(Rank(2, 4, 0, 1), (ResourceRepeatKind)kind, ResourceProgram.Zero);
        Assert.That(Check(repeat).UnknownReason, Is.EqualTo("UnsupportedResourceRecurrence"));
    }

    [Test]
    public void CompositionBudgetStopsDeepSymbolicCostsBeforePublishingBounds()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var type = factory.GetOrCreateIntegerType(32, true);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        builder.Return(block, factory.CreateOperation("return"));
        var owner = new CompilerTotalCallablePreparation("owned", builder.Build(),
            [new(factory.CreateVariable("entry", type), factory.CreateVariable("current", type), factory.CreateVariable("old", type))], null, []);
        var input = ResourceExpression.InputSize(new ResourceInput(0, ResourceInputKind.NonNegativeScalar, "n", int.MaxValue));
        var rank = new ResourceLoopRank(ResourceLoopKind.IncreasingAdd, ResourceExpression.Zero, input, 1, 32, false);
        var program = ResourceProgram.Zero;
        for (int index = 0; index < 70; index++)
        { program = ResourceProgram.Repeat(rank, ResourceRepeatKind.Loop, program); }
        var result = NativeResourceProgramVerifier.Verify(program, "owned", owner, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.EqualTo("ResourceProofBudgetExceeded"));
        Assert.That(result.Cost, Is.Null);
    }
    [Test]
    public void UnknownDetachedProgramKindsCannotProducePartialCosts()
    {
        var constructor = typeof(ResourceProgram).GetConstructors(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Single();
        var program = (ResourceProgram)constructor.Invoke([(ResourceProgramKind)999, null, null, ResourceRepeatKind.Loop, System.Collections.Immutable.ImmutableArray<ResourceProgram>.Empty, null, null, null]);
        var result = Check(program);
        Assert.That(result.Verified, Is.False);
        Assert.That(result.Cost, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("MalformedResourceProgram"));
    }
}
