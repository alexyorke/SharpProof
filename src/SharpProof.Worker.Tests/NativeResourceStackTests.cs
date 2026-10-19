using System.Text.Json.Nodes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Compiler.Semantics;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeResourceStackTests
{
    private static readonly ResourceActivationShape Root = new([ResourceStorageKind.Word32], false, true);
    private static readonly ResourceActivationShape Leaf = new([], false, false);

    [TestCase(0, 1, 32)]
    [TestCase(1, 2, 48)]
    [TestCase(2, 2, 48)]
    public void SequentialCallsAndLoopsReuseLogicalStackRecords(int kind, int depth, int bytes)
    {
        if (kind < 0 || kind > 2)
        { throw new ArgumentOutOfRangeException(nameof(kind)); }
        var call = ResourceProgram.Activate(Leaf, ResourceProgram.Zero);
        var body = kind == 2 ? ResourceProgram.Sequence(call, call) : ResourceProgram.Repeat(
            new(ResourceLoopKind.IncreasingAdd, ResourceExpression.Zero, ResourceExpression.Literal(kind == 0 ? 0 : 100), 1, 32, false),
            ResourceRepeatKind.Loop, call);
        AssertBound(ResourceProgram.Activate(Root, body), depth, bytes);
    }

    [TestCase(false, 5, 6, 144)]
    [TestCase(true, 8, 5, 120)]
    public void CheckedRecursiveRanksAccumulateRetainedActivations(bool division, int initial, int depth, int bytes)
    {
        var frame = new ResourceActivationShape([ResourceStorageKind.Word32], false, false);
        var rank = new ResourceLoopRank(division ? ResourceLoopKind.DecreasingDivide : ResourceLoopKind.DecreasingSubtract,
            ResourceExpression.Literal(initial), ResourceExpression.Zero, division ? 2 : 1, 32, false);
        AssertBound(ResourceProgram.Activate(frame, ResourceProgram.Repeat(rank, ResourceRepeatKind.TailRecurrence, ResourceProgram.Zero)), depth, bytes);
    }

    [TestCase(0, "ResourceStackRootActivationMissing")]
    [TestCase(1, "ResourceStackFrameStorageUnavailable")]
    [TestCase(2, "ResourceStackCallSemanticsUnsupported")]
    public void MissingFrameOrCallSemanticsProduceNoPartialBounds(int kind, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (kind < 0 || kind > 2)
        { throw new ArgumentOutOfRangeException(nameof(kind)); }
        var program = kind == 0 ? ResourceProgram.Zero : ResourceProgram.Activate(kind == 1 ? null : Root,
            kind == 2 ? ResourceProgram.MetadataBoundary : ResourceProgram.Zero);
        var check = NativeResourceStackVerifier.Derive(program, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo(reason));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void ActivationTransportRejectsSuppliedVerdictsAndMalformedShapeData(int mutation)
    {
        if (mutation < 0 || mutation > 2)
        { throw new ArgumentOutOfRangeException(nameof(mutation)); }
        var token = TestContext.CurrentContext.CancellationToken;
        var node = JsonNode.Parse(ResourceProgramCodec.Encode(ResourceProgram.Activate(Root, ResourceProgram.Zero), token))!;
        var shape = node["program"]!["activation"]!;
        if (mutation == 0)
        { shape["bytes"] = 0; }
        else if (mutation == 1)
        { shape["receiver"] = "true"; }
        else
        { shape["variables"]![0] = (int)ResourceStorageKind.Unspecified; }
        Assert.Throws<System.IO.InvalidDataException>((Action)(() => ResourceProgramCodec.Decode(System.Text.Encoding.UTF8.GetBytes(node.ToJsonString()), token)));
    }

    [TestCase("public static void Target() { Leaf(); Leaf(); } private static void Leaf() {}", 2, 32)]
    [TestCase("public static void Target() { Walk(5); } private static void Walk(int n) { if(n<=0) return; Walk(n-1); }", 7, 160)]
    [TestCase("public static void Target() { for(int i=0;i<100;i++) Leaf(); } private static void Leaf() {}", 2, 40)]
    [TestCase("public static Box Target() { return new Box(); } public class Box { public int Value=Initialize(); public Box() { int local=1; } private static int Initialize() => 1; }", 3, 80)]
    [TestCase("public static Box Target() { return new Box(); } public class Box { public int Value=Initialize(); private static int Initialize() => 1; }", 3, 72)]
    public void CompilerPreservesSourceActivationBoundaries(string members, int depth, int bytes)
    {
        ArgumentNullException.ThrowIfNull(members);
        var compilation = TestCompilation.Create("StackSourceControl", "public static class C { " + members + " }");
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var declaration = (MethodDeclarationSyntax)method.DeclaringSyntaxReferences.Single().GetSyntax(TestContext.CurrentContext.CancellationToken);
        var body = compilation.GetSemanticModel(declaration.SyntaxTree).GetOperation(declaration.Body!, TestContext.CurrentContext.CancellationToken);
        var analysis = ResourceBoundsAnalysis.Analyze(method, body, TestContext.CurrentContext.CancellationToken);
        Assert.That(analysis.Bounds, Is.Not.Null, analysis.UnknownReason);
        AssertBound(analysis.Bounds!.Program, depth, bytes);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(5)]
    [TestCase(32)]
    public void RecursiveDepthBoundsMatchIndependentExecutedCalls(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var active = 0;
        var maximum = 0;
        Walk(count);
        Assert.That(active, Is.Zero);
        var rank = new ResourceLoopRank(ResourceLoopKind.DecreasingSubtract, ResourceExpression.Literal(count), ResourceExpression.Zero, 1, 32, false);
        var check = NativeResourceStackVerifier.Derive(ResourceProgram.Activate(Leaf,
            ResourceProgram.Repeat(rank, ResourceRepeatKind.TailRecurrence, ResourceProgram.Zero)), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds!.Depth.Constant, Is.EqualTo(new System.Numerics.BigInteger(maximum)));

        void Walk(int remaining)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(remaining);
            active++;
            maximum = Math.Max(maximum, active);
            if (remaining > 0)
            { Walk(remaining - 1); }
            active--;
        }
    }
    [TestCase(0)]
    [TestCase(1)]
    public void UnsupportedRecursiveRanksDoNotPublishBounds(int mutation)
    {
        var rank = new ResourceLoopRank(mutation == 0 ? ResourceLoopKind.IncreasingAdd : ResourceLoopKind.DecreasingSubtract,
            ResourceExpression.Literal(4), ResourceExpression.One, 1, 32, false);
        var check = NativeResourceStackVerifier.Derive(ResourceProgram.Activate(Leaf,
            ResourceProgram.Repeat(rank, ResourceRepeatKind.TailRecurrence, ResourceProgram.Zero)), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Bounds, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("UnsupportedResourceRecurrence"));
    }

    [Test]
    public void RecursivePrefixCallsRemainWithinTheConservativeRetainedChainBound()
    {
        var frame = new ResourceActivationShape([ResourceStorageKind.Word32], false, false);
        var prefix = ResourceProgram.Activate(Root, ResourceProgram.Activate(Leaf, ResourceProgram.Zero));
        var rank = new ResourceLoopRank(ResourceLoopKind.DecreasingSubtract, ResourceExpression.Literal(4), ResourceExpression.Zero, 1, 32, false);
        AssertBound(ResourceProgram.Activate(frame, ResourceProgram.Repeat(rank, ResourceRepeatKind.TailRecurrence, prefix)), 7, 168);
    }

    [Test]
    public void ActivationShapesRejectUninitializedOversizedAndUnsupportedStorage()
    {
        Assert.Throws<ArgumentException>((Action)(() => new ResourceActivationShape(default, false, false)));
        Assert.Throws<ArgumentException>((Action)(() => new ResourceActivationShape(
            System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(ResourceStorageKind.Word32, ResourceActivationShape.MaximumVariables + 1)), false, false)));
        Assert.Throws<ArgumentException>((Action)(() => new ResourceActivationShape([(ResourceStorageKind)999], false, false)));
    }

    [Test]
    public void CancellationPropagatesWithoutAnUnknownStackVerdict()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>((Action)(() => NativeResourceStackVerifier.Derive(
            ResourceProgram.Activate(Leaf, ResourceProgram.Zero), cancellation.Token)));
    }
    private static void AssertBound(ResourceProgram program, int depth, int bytes)
    {
        ArgumentNullException.ThrowIfNull(program);
        var token = TestContext.CurrentContext.CancellationToken;
        var roundTrip = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(program, token), token);
        var check = NativeResourceStackVerifier.Derive(roundTrip, token);
        Assert.That(check.Bounds, Is.Not.Null, check.UnknownReason);
        Assert.That(check.Bounds!.Depth.Constant, Is.EqualTo(new System.Numerics.BigInteger(depth)));
        Assert.That(check.Bounds.StorageBytes.Constant, Is.EqualTo(new System.Numerics.BigInteger(bytes)));
    }
}
