using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeForkJoinProjectionTests
{
    [TestCase("", 7, 0L)]
    [TestCase("for(int i=0;i<count;i++){var a=new int[2];}", 7, 320L)]
    [TestCase("for(int i=0;i<count;i++){var a=new int[2];}", -5, 96L)]
    [TestCase("for(int i=0;i<count;i++){var a=new int[2];var b=new byte[3];}", 7, 640L)]
    [TestCase("for(int i=0;i<count;i++){var a=new byte[0];}", 7, 240L)]
    [TestCase("for(long i=1;i<count;i*=2){var a=new byte[9];}", 7, 200L)]
    [TestCase("var a=new int[count];", 7, 96L)]
    public void ChildStorageCompositionIncludesEveryChildAndPrimitiveWidth(string body, int size, long expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        var projection = Lower(body);
        var memory = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, Root(projection), Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(memory.UnknownReason, Is.Null);
        var cumulative = memory.CumulativeUpper!.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) : size,
            TestContext.CurrentContext.CancellationToken);
        var peak = memory.PeakAuxiliaryUpper!.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) : size,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(cumulative, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(peak, Is.EqualTo(cumulative));
        Assert.That(memory.RetainedUpper, Is.EqualTo(ResourceExpression.Zero));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChildStorageSurvivesTransportWithoutInventingCarrierCosts(bool configuredAwait)
    {
        var projection = Lower("for(int i=0;i<count;i++){var a=new int[2];}") with { ConfiguredAwait = configuredAwait };
        var root = Root(projection);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var decoded = CompilerForkJoinProjectionCodec.Decode(CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation), root, cancellation);
        var memory = NativeForkJoinSourceCostVerifier.CheckChildStorage(decoded, root, Layout(), cancellation);
        Assert.That(memory.UnknownReason, Is.Null);
        Assert.That(memory.CumulativeUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 7, cancellation),
            Is.EqualTo(new System.Numerics.BigInteger(320)));
        Assert.That(memory.RetainedUpper, Is.EqualTo(ResourceExpression.Zero));
    }

    [Test]
    public void ChildStorageUsesFormalOrdinalsForNamedArguments()
    {
        var projection = LowerSource("using System.Threading.Tasks; public static class C {public static Task Root(int n) => Task.WhenAll(Task.Run(()=>Child(right:3,left:n)),Task.Run(()=>Child(right:n,left:1))); static void Child(int left,int right) {var a=new int[left];var b=new byte[right];}}");
        var memory = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, Root(projection), Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(memory.UnknownReason, Is.Null);
        Assert.That(memory.CumulativeUpper!.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : 7,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(new System.Numerics.BigInteger(152)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChildStorageRejectsIncompatibleLayoutsEvenWithoutAllocations(bool arrays)
    {
        var projection = Lower(arrays ? "var a=new int[2];" : "");
        var memory = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, Root(projection),
            new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x86", 4), TestContext.CurrentContext.CancellationToken);
        Assert.That(memory.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(memory.CumulativeUpper, Is.Null);
        Assert.That(memory.PeakAuxiliaryUpper, Is.Null);
        Assert.That(memory.RetainedUpper, Is.Null);
    }

    [Test]
    public void ChildStorageRejectsAnInvalidLaterChildWithoutPartialNumbers()
    {
        var projection = Lower("var a=new int[2];");
        projection = projection with
        {
            Children = [projection.Children[0], projection.Children[1] with
        { Owner = projection.Children[1].Owner with { EffectsCompleteAtEntry = false } }]
        };
        var memory = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, Root(projection), Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(memory.UnknownReason, Is.EqualTo("ForkJoinChildOwnershipInvalid"));
        Assert.That(memory.CumulativeUpper, Is.Null);
        Assert.That(memory.PeakAuxiliaryUpper, Is.Null);
        Assert.That(memory.RetainedUpper, Is.Null);
    }

    [Test]
    public void ChildStorageHonorsCancellation()
    {
        var projection = Lower("");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, Root(projection), Layout(), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    private static ResourceRuntimeLayoutContext Layout()
    {
        return new("CoreCLR", "8.0.32", "x64", 8);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IndependentlyChecksEveryProjectedChildAfterCodecRoundTrip(bool arrays)
    {
        var projection = Lower(arrays ? "for(int i=0;i<count;i++){var value=new int[2];}" : "for(int i=0;i<count;i++){}");
        foreach (var child in projection.Children)
        {
            var cancellation = TestContext.CurrentContext.CancellationToken;
            var owner = CompilerTotalCallableArtifactCodec.Decode(
                CompilerTotalCallableArtifactCodec.Encode(child.Owner, cancellation),
                new WorkerCallableManifestEntry { CallableId = child.Owner.CallableId }, [], cancellation)!;
            var program = ResourceProgramCodec.Decode(ResourceProgramCodec.Encode(child.Cost, cancellation), cancellation);
            var check = NativeResourceProgramVerifier.Verify(program, owner.CallableId, owner, cancellation);
            Assert.That(check.Verified, Is.True, check.UnknownReason);
            Assert.That(check.Cost!.LoopEntries.ToDisplayString(), Is.EqualTo("max(0, count)"));
            Assert.That(check.Cost.Arrays.ToDisplayString(), Is.EqualTo(arrays ? "max(0, count)" : "0"));
            Assert.That(check.Cost.Elements.ToDisplayString(), Is.EqualTo(arrays ? "(max(0, count) * 2)" : "0"));
        }
    }

    [Test]
    public void ChildCostsWithoutTheirInputBindingsHaveNoNativeCost()
    {
        var cost = Lower("for(int i=0;i<count;i++){}").Children[0].Cost;
        var unrelated = Lower("").Children[0].Owner with { Parameters = [] };
        var check = NativeResourceProgramVerifier.Verify(cost, unrelated.CallableId, unrelated, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Verified, Is.False);
        Assert.That(check.Cost, Is.Null);
        Assert.That(check.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ComposesCheckedSourceCostsWithRootAndConstantArguments(bool arrays)
    {
        var projection = Lower(arrays ? "for(int i=0;i<count;i++){var value=new int[2];}" : "for(int i=0;i<count;i++){}");
        var result = NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.HasSourceCosts, Is.True, result.UnknownReason);
        Assert.That(result.Upper!.LoopEntries.ToDisplayString(), Is.EqualTo("(max(0, n) + 3)"));
        Assert.That(result.Upper.Arrays.ToDisplayString(), Is.EqualTo(arrays ? "(max(0, n) + 3)" : "0"));
        Assert.That(result.Upper.Elements.ToDisplayString(), Is.EqualTo(arrays ? "((max(0, n) * 2) + 6)" : "0"));
    }

    [TestCase(-3, 0)]
    [TestCase(0, 0)]
    [TestCase(7, 7)]
    public void ConstantArgumentsUseTheChildsNonnegativeSizeDomain(int value, int expected)
    {
        var projection = Lower("for(int i=0;i<count;i++){}");
        projection = projection with
        {
            Children = [
            projection.Children[0] with { Arguments = [new(0, null, value)] },
            projection.Children[1] with { Arguments = [new(0, null, 0)] }]
        };
        var result = NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.HasSourceCosts, Is.True, result.UnknownReason);
        Assert.That(result.Upper!.LoopEntries.Constant, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [TestCase("root-id")]
    [TestCase("root-input")]
    [TestCase("root-alias")]
    [TestCase("child-alias")]
    [TestCase("child-id")]
    [TestCase("child-input")]
    [TestCase("child-receiver")]
    [TestCase("missing-map")]
    [TestCase("bad-formal")]
    [TestCase("bad-root")]
    [TestCase("ambiguous")]
    [TestCase("unbound")]
    [TestCase("unproved-cost")]
    public void InvalidOwnershipOrLaterChildrenNeverReturnPartialSourceCosts(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var projection = Lower("for(int i=0;i<count;i++){}");
        var root = Root(projection);
        var second = projection.Children[1];
        switch (mutation)
        {
            case "root-id":
                root = root with { CallableId = "other" };
                break;
            case "root-input":
                root = root with { Parameters = [] };
                break;
            case "root-alias":
                root = root with { Parameters = [new(root.Parameters[0].Entry, root.Parameters[0].Entry, root.Parameters[0].Old)] };
                break;
            case "child-alias":
                second = second with { Owner = second.Owner with { Parameters = [new(second.Owner.Parameters[0].Entry, second.Owner.Parameters[0].Entry, second.Owner.Parameters[0].Old)] } };
                break;
            case "child-id":
                second = second with { Owner = second.Owner with { CallableId = "other" } };
                break;
            case "child-input":
                second = second with { Owner = second.Owner with { Parameters = [] } };
                break;
            case "child-receiver":
                second = second with { Owner = second.Owner with { HasReceiver = true } };
                break;
            case "missing-map":
                second = second with { Arguments = [] };
                break;
            case "bad-formal":
                second = second with { Arguments = [new(-1, 0, null)] };
                break;
            case "bad-root":
                second = second with { Arguments = [new(0, 5, null)] };
                break;
            case "ambiguous":
                second = second with { Arguments = [new(0, 0, 3)] };
                break;
            case "unbound":
                second = second with { Arguments = [new(0, null, null)] };
                break;
            default:
                second = second with { Cost = SharpProof.Ir.ResourceProgram.MetadataBoundary };
                break;
        }
        projection = projection with { Children = [projection.Children[0], second] };
        var result = NativeForkJoinSourceCostVerifier.Check(projection, root, TestContext.CurrentContext.CancellationToken);
        Assert.That(result.HasSourceCosts, Is.False);
        Assert.That(result.Upper, Is.Null);
        Assert.That(result.UnknownReason, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void SourceCostCompositionHonorsCancellation()
    {
        var projection = Lower("for(int i=0;i<count;i++){}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () => { _ = NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(check);
    }

    [Test]
    public void NamedArgumentsComposeByFormalOrdinalRatherThanEvaluationOrder()
    {
        var projection = LowerSource("using System.Threading.Tasks; public static class C { public static Task Root(int n,int m) => Task.WhenAll(Task.Run(()=>Child(right:m,left:n)),Task.Run(()=>Child(right:n,left:3))); static void Child(int left,int right) {for(int i=0;i<left;i++){} for(int j=0;j<right;j++){}} }");
        var result = NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.HasSourceCosts, Is.True, result.UnknownReason);
        var value = result.Upper!.LoopEntries.Evaluate(static input => input is null ? throw new ArgumentNullException(nameof(input)) : input.Ordinal == 0 ? 4 : 7,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(value, Is.EqualTo(new System.Numerics.BigInteger(18)));
    }

    [TestCase("for(int i=2;i<count;i++){}", 5, 4)]
    [TestCase("int remaining=count; while(remaining>0){remaining/=2;}", 8, 6)]
    [TestCase("if(count>0){for(int i=0;i<count;i++){}}else{for(int i=0;i<2;i++){}}", 4, 7)]
    public void SubstitutionPreservesDifferenceGeometricAndMaximumExpressions(string body, int inputValue, int expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        var projection = Lower(body);
        var result = NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(result.HasSourceCosts, Is.True, result.UnknownReason);
        var value = result.Upper!.LoopEntries.Evaluate(input => input is null ? throw new ArgumentNullException(nameof(input)) : inputValue,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(value, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProjectionTransportPreservesComposedSourceCosts(bool configuredAwait)
    {
        var projection = Lower("for(int i=0;i<count;i++){var value=new int[2];}") with { ConfiguredAwait = configuredAwait };
        var root = Root(projection);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var payload = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation);
        var decoded = CompilerForkJoinProjectionCodec.Decode(payload, root, cancellation);
        var result = NativeForkJoinSourceCostVerifier.Check(decoded, root, cancellation);
        Assert.That(decoded.ConfiguredAwait, Is.EqualTo(configuredAwait));
        Assert.That(result.HasSourceCosts, Is.True, result.UnknownReason);
        Assert.That(result.Upper!.LoopEntries.ToDisplayString(), Is.EqualTo("(max(0, n) + 3)"));
        Assert.That(result.Upper.Elements.ToDisplayString(), Is.EqualTo("((max(0, n) * 2) + 6)"));
        Assert.That(decoded.Children[0].Owner.Program.Factory, Is.Not.SameAs(projection.Children[0].Owner.Program.Factory));
    }

    [TestCase("version")]
    [TestCase("root")]
    [TestCase("missing")]
    [TestCase("extra")]
    [TestCase("child-id")]
    [TestCase("child-effects")]
    [TestCase("child-role")]
    [TestCase("unowned-cost")]
    [TestCase("bad-input")]
    [TestCase("ambiguous")]
    [TestCase("integer-kind")]
    [TestCase("extra-argument")]
    [TestCase("bad-cost")]
    [TestCase("duplicate")]
    [TestCase("duplicate-body")]
    [TestCase("configured-kind")]
    [TestCase("missing-configured-kind")]
    public void ProjectionTransportRejectsMalformedOrUnownedChildren(string mutation)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var projection = Lower("for(int i=0;i<count;i++){}");
        var root = Root(projection);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var payload = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation);
        var node = System.Text.Json.Nodes.JsonNode.Parse(payload)!.AsObject();
        var child = node["children"]![0]!.AsObject();
        var argument = child["arguments"]![0]!.AsObject();
        switch (mutation)
        {
            case "configured-kind":
                node["configuredAwait"] = 1;
                break;
            case "missing-configured-kind":
                node.Remove("configuredAwait");
                break;
            case "version":
                node["version"] = 99;
                break;
            case "root":
                node["root"] = "other";
                break;
            case "missing":
                node.Remove("framework");
                break;
            case "extra":
                node["extra"] = true;
                break;
            case "child-id":
                child["owner"] = "other";
                break;
            case "child-effects":
                child["body"]!["effectsCompleteAtEntry"] = false;
                break;
            case "child-role":
                child["body"]!["result"] = 0;
                break;
            case "unowned-cost":
                child["body"]!["parameters"] = new System.Text.Json.Nodes.JsonArray();
                break;
            case "bad-input":
                argument["input"] = 50;
                break;
            case "ambiguous":
                argument["constant"] = 3;
                break;
            case "integer-kind":
                argument["parameter"] = "0";
                break;
            case "extra-argument":
                argument["extra"] = true;
                break;
            case "bad-cost":
                child["cost"] = "invalid-base64!";
                break;
        }
        var json = node.ToJsonString();
        if (mutation == "duplicate")
        { json = json.Replace("\"version\":3", "\"version\":3,\"version\":3", StringComparison.Ordinal); }
        else if (mutation == "duplicate-body")
        { json = json.Replace("\"effectsCompleteAtEntry\":true", "\"effectsCompleteAtEntry\":true,\"effectsCompleteAtEntry\":true", StringComparison.Ordinal); }
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode(System.Text.Encoding.UTF8.GetBytes(json), root, cancellation); };
        Assert.Throws<InvalidDataException>(decode);
    }

    [Test]
    public void ProjectionTransportRejectsOversizedPayloadsAndHonorsCancellation()
    {
        var projection = Lower("");
        var root = Root(projection);
        Action oversized = () => { _ = CompilerForkJoinProjectionCodec.Decode(new byte[CompilerForkJoinProjectionCodec.MaximumPayloadBytes + 1], root, TestContext.CurrentContext.CancellationToken); };
        Assert.Throws<InvalidDataException>(oversized);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action encode = () => { _ = CompilerForkJoinProjectionCodec.Encode(projection, root, cancellation.Token); };
        Action decode = () => { _ = CompilerForkJoinProjectionCodec.Decode([], root, cancellation.Token); };
        Assert.Throws<OperationCanceledException>(encode);
        Assert.Throws<OperationCanceledException>(decode);
    }

    private static CompilerTotalEntryPreparation Root(CompilerForkJoinProjection projection)
    {
        ArgumentNullException.ThrowIfNull(projection);
        var factory = new SharpProof.Ir.IrFactory(SharpProof.Ir.IrExecutionSemantics.Total);
        var parameters = projection.RootParameterNames.Select(name => name is null ? throw new ArgumentNullException(nameof(name)) :
            new CompilerTotalParameter(factory.CreateVariable("entry:" + name, factory.IntegerType),
                factory.CreateVariable("current:" + name, factory.IntegerType), factory.CreateVariable("old:" + name, factory.IntegerType))).ToImmutableArray();
        return new(projection.RootCallableId, factory, parameters, []);
    }

    private static CompilerForkJoinProjection Lower(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var source = "using System.Threading.Tasks; public static class C {public static Task Root(int n) => Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) {" + body + "}}";
        return LowerSource(source);
    }

    private static CompilerForkJoinProjection LowerSource(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("ForkJoinProjection", ("Subject.cs", source));
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Root");
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        var result = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken);
        Assert.That(result.UnknownReason, Is.Null);
        return result.Projection!;
    }
}
