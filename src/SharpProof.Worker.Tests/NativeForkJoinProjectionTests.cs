using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeForkJoinProjectionTests
{
    [TestCase(false, 1)]
    [TestCase(true, 3)]
    public void SourceActivationBoundsComeFromOwnedCallPrograms(bool nested, int expected)
    {
        var projection = nested ? LowerSource("using System.Threading.Tasks; public static class C {public static Task Root(int n) => " +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) { Middle(count); } " +
            "static void Middle(int value) { Leaf(value); } static void Leaf(int value) { }}") : Lower("");
        var check = NativeForkJoinSourceCostVerifier.CheckSourceActivations(projection, Root(projection),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Upper, Is.Not.Null, check.UnknownReason);
        Assert.That(check.Upper!.Constant, Is.EqualTo(new System.Numerics.BigInteger(expected)));
        Assert.That(check.RequiredSourceModel, Is.EqualTo(NativeResourceStackVerifier.ModelId));
    }

    [Test]
    public void MissingActivationStructureDoesNotInventFramesOrInvalidateIndependentSourceCosts()
    {
        var projection = Lower("");
        projection = projection with { Children = projection.Children.SetItem(1, projection.Children[1] with { Cost = ResourceProgram.Zero }) };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), cancellation).HasSourceCosts, Is.True);
        var check = NativeForkJoinSourceCostVerifier.CheckSourceActivations(projection, Root(projection), cancellation);
        Assert.That(check.Upper, Is.Null);
        Assert.That(check.RequiredSourceModel, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("ResourceStackRootActivationMissing"));
        var combined = NativeForkJoinSourceCostVerifier.CheckCallbackSourceActivations(projection, Root(projection), Layout(), cancellation);
        Assert.That(combined.Upper, Is.Null);
        Assert.That(combined.RequiredSourceModel, Is.Null);
        Assert.That(combined.UnknownReason, Is.EqualTo("ResourceStackRootActivationMissing"));
    }

    [Test]
    public void ActivationBoundsRejectUnownedLaterCallbacksAndHonorCancellation()
    {
        var projection = Lower("");
        projection = projection with
        {
            Children = projection.Children.SetItem(1, projection.Children[1] with
            { Owner = projection.Children[1].Owner with { EffectsCompleteAtEntry = false } })
        };
        var root = Root(projection);
        var check = NativeForkJoinSourceCostVerifier.CheckSourceActivations(projection, root, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.Upper, Is.Null);
        Assert.That(check.RequiredSourceModel, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("ForkJoinChildOwnershipInvalid"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action canceled = () => { _ = NativeForkJoinSourceCostVerifier.CheckSourceActivations(projection, root, cancellation.Token); };
        Assert.Throws<OperationCanceledException>(canceled);
    }

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
    [TestCase("for(long i=0;i<count;i+=3){}", 8, 4)]
    [TestCase("for(long i=1;i<count;i*=2){}", 8, 5)]
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
        {
            var property = "\"version\":" + CompilerForkJoinProjectionCodec.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.That(json, Does.Contain(property));
            json = json.Replace(property, property + "," + property, StringComparison.Ordinal);
        }
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

    [TestCase("empty", "ForkJoinInputOwnershipInvalid")]
    [TestCase("control", "ForkJoinInputOwnershipInvalid")]
    [TestCase("oversized", "ForkJoinSourceCostBudgetExceeded")]
    public void InvalidRootDisplayNamesCannotPublishPartialChildBounds(string shape, string reason)
    {
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(reason);
        var projection = Lower("var a=new int[count];");
        var root = Root(projection);
        var name = shape switch
        {
            "empty" => string.Empty,
            "control" => "n\n",
            "oversized" => new string('n', ResourceExpression.MaximumTextLength + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(shape))
        };
        projection = projection with { RootParameterNames = [name] };
        AssertNoPartialCosts(projection, root, reason);
    }

    [Test]
    public void CombinedInstructionBudgetIncludesTheLaterChildBeforeStorageComposition()
    {
        var projection = Lower("var a=new int[count];");
        var root = Root(projection);
        var child = projection.Children[1];
        var factory = child.Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        for (var index = 0; index < ResourceExpression.MaximumNodes; index++)
        { builder.Assign(entry, factory.CreateOperation(), child.Owner.Parameters[0].Current, factory.Integer(index)); }
        builder.Return(entry, factory.CreateOperation());
        projection = projection with
        {
            Children = projection.Children.SetItem(1, child with { Owner = child.Owner with { Program = builder.Build() } })
        };
        AssertNoPartialCosts(projection, root, "ForkJoinSourceCostBudgetExceeded");
    }

    [TestCase(IrWriteRegion.Static)]
    [TestCase(IrWriteRegion.Parameter)]
    [TestCase(IrWriteRegion.Unknown)]
    public void LaterChildExternalWritesDiscardAllSourceAndStorageBounds(IrWriteRegion region)
    {
        var projection = Lower("var a=new int[count];");
        var root = Root(projection);
        var child = projection.Children[1];
        var factory = child.Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        builder.Write(entry, factory.CreateOperation("external-write"), region);
        builder.Return(entry, factory.CreateOperation());
        projection = projection with
        {
            Children = projection.Children.SetItem(1, child with { Owner = child.Owner with { Program = builder.Build() } })
        };
        AssertNoPartialCosts(projection, root, "ForkJoinChildIsolationUnproved");
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var activations = NativeForkJoinSourceCostVerifier.CheckSourceActivations(projection, root, cancellation);
        Assert.That(activations.Upper, Is.Null);
        Assert.That(activations.RequiredSourceModel, Is.Null);
        Assert.That(activations.UnknownReason, Is.EqualTo("ForkJoinChildIsolationUnproved"));
        var callbacks = NativeForkJoinSourceCostVerifier.CheckCallbackSourceActivations(projection, root, Layout(), cancellation);
        Assert.That(callbacks.Upper, Is.Null);
        Assert.That(callbacks.RequiredSourceModel, Is.Null);
        Assert.That(callbacks.UnknownReason, Is.EqualTo("ForkJoinChildIsolationUnproved"));
    }

    private static void AssertNoPartialCosts(CompilerForkJoinProjection projection, CompilerTotalEntryPreparation root, string reason)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(reason);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var source = NativeForkJoinSourceCostVerifier.Check(projection, root, cancellation);
        Assert.That(source.HasSourceCosts, Is.False);
        Assert.That(source.Upper, Is.Null);
        Assert.That(source.UnknownReason, Is.EqualTo(reason));
        var storage = NativeForkJoinSourceCostVerifier.CheckChildStorage(projection, root, Layout(), cancellation);
        Assert.That(storage.CumulativeUpper, Is.Null);
        Assert.That(storage.PeakAuxiliaryUpper, Is.Null);
        Assert.That(storage.RetainedUpper, Is.Null);
        Assert.That(storage.UnknownReason, Is.EqualTo(reason));
    }
    [Test]
    public void EmptyIrFaultProfileDoesNotClaimCompleteRuntimeFaultCoverage()
    {
        var projection = Lower("");
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.HasOrigins, Is.True, check.UnknownReason);
        Assert.That(check.ExceptionKinds, Is.Empty);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(NativeTaskIrFaultOriginVerifier.Model, Is.EqualTo("task-join-owned-ir-fault-origins-v1"));
    }

    [TestCase("int value=7/count;", 0, IrExceptionKind.DivideByZero)]
    [TestCase("int value=7%count;", 0, IrExceptionKind.DivideByZero)]
    [TestCase("checked {int value=count+1;}", int.MaxValue, IrExceptionKind.Overflow)]
    [TestCase("int[] array=null;int value=array.Length;", 0, IrExceptionKind.NullReference)]
    [TestCase("int[] array=null;int value=array[count];", 0, IrExceptionKind.NullReference)]
    [TestCase("int[] array=new int[1];int value=array[count];", 2, IrExceptionKind.IndexOutOfRange)]
    public void PrimitiveIrFaultProfilesContainIndependentlyExecutedFaults(string body, int input, IrExceptionKind expected)
    {
        ArgumentNullException.ThrowIfNull(body);
        var projection = Lower(body);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(check.HasOrigins, Is.True, check.UnknownReason);
        Assert.That(check.ExceptionKinds, Does.Contain(expected));
        var child = projection.Children[0].Owner;
        var factory = child.Program.Factory;
        var values = ImmutableDictionary<IrVarId, IrValue>.Empty.Add(child.Parameters[0].Entry, factory.CreateIntegerValue(input));
        var execution = new IrProgramInterpreter(factory).Execute(child.Program, values, 1000,
            new IrProgramReplayOptions(static _ => null), cancellation);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
        Assert.That(execution.Exception!.Kind, Is.EqualTo(expected));
    }

    [Test]
    public void ConstantArraySitesKeepAllocationFailureSeparateFromIrExceptionKinds()
    {
        var projection = Lower("var array=new int[1];");
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.HasOrigins, Is.True, check.UnknownReason);
        Assert.That(check.HasIrAllocations, Is.True);
        Assert.That(check.ExceptionKinds, Does.Contain(IrExceptionKind.Overflow));
    }

    [TestCase(IrExceptionKind.DivideByZero, true)]
    [TestCase(IrExceptionKind.Overflow, true)]
    [TestCase(IrExceptionKind.NullReference, true)]
    [TestCase(IrExceptionKind.IndexOutOfRange, true)]
    [TestCase(IrExceptionKind.InvalidCast, true)]
    [TestCase(IrExceptionKind.Argument, true)]
    [TestCase(IrExceptionKind.ArgumentOutOfRange, true)]
    [TestCase(IrExceptionKind.Unknown, false)]
    [TestCase(IrExceptionKind.Explicit, false)]
    public void TypedIrThrowsRequireAClassifiedFamily(IrExceptionKind kind, bool classified)
    {
        if (!Enum.IsDefined(kind))
        { throw new ArgumentOutOfRangeException(nameof(kind)); }
        var projection = Lower("");
        var child = projection.Children[1];
        var factory = child.Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exit = builder.CreateBlock();
        builder.Throw(entry, factory.CreateOperation(), kind, exit);
        builder.ExceptionalExit(exit, factory.CreateOperation());
        projection = projection with
        {
            Children = projection.Children.SetItem(1,
            child with { Owner = child.Owner with { Program = builder.Build() }, Cost = ResourceProgram.Zero })
        };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), cancellation).HasSourceCosts, Is.True);
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(check.HasOrigins, Is.EqualTo(classified), check.UnknownReason);
        var instances = NativeTaskSourceExceptionInstanceVerifier.Check(projection, Root(projection), Layout(), cancellation);
        if (classified)
        {
            var upper = kind == IrExceptionKind.ArgumentOutOfRange ? 304 : kind == IrExceptionKind.Argument ? 288 : 272;
            Assert.That(instances.SourceInstanceUpper!.Constant, Is.EqualTo(new System.Numerics.BigInteger(upper)));
            Assert.That(instances.RequiredRuntimeModel, Is.EqualTo(NativeTaskSourceExceptionInstanceVerifier.Model));
            Assert.That(instances.InitialResolverCapacityUpper, Is.EqualTo(30));
            Assert.That(instances.RequiredTrackerModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.FreshSourceTrackerModel));
        }
        else
        {
            Assert.That(instances.SourceInstanceUpper, Is.Null);
            Assert.That(instances.RequiredRuntimeModel, Is.Null);
            Assert.That(instances.InitialResolverCapacityUpper, Is.Null);
            Assert.That(instances.RequiredTrackerModel, Is.Null);
        }
        if (classified)
        { Assert.That(check.ExceptionKinds, Is.EquivalentTo(new[] { kind })); }
        else
        {
            Assert.That(check.ExceptionKinds, Is.Null);
            Assert.That(check.HasIrAllocations, Is.False);
            Assert.That(check.UnknownReason, Is.EqualTo("TaskIrFaultOriginsUnclassifiedThrow"));
        }
    }

    [Test]
    public void LaterUnclassifiedFaultsDiscardEarlierAllocationAndFamilyFacts()
    {
        var projection = Lower("var array=new int[1];");
        var child = projection.Children[1];
        var factory = child.Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exit = builder.CreateBlock();
        builder.Throw(entry, factory.CreateOperation(), IrExceptionKind.Unknown, exit);
        builder.ExceptionalExit(exit, factory.CreateOperation());
        projection = projection with
        {
            Children = projection.Children.SetItem(1,
            child with { Owner = child.Owner with { Program = builder.Build() }, Cost = ResourceProgram.Zero })
        };
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.HasOrigins, Is.False);
        Assert.That(check.ExceptionKinds, Is.Null);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.UnknownReason, Is.EqualTo("TaskIrFaultOriginsUnclassifiedThrow"));
    }

    [Test]
    public void RuntimeArrayLimitApproximationsAreNotReclassifiedAsKnownFaults()
    {
        var projection = Lower("var array=new int[count];");
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.HasOrigins, Is.False);
        Assert.That(check.ExceptionKinds, Is.Null);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.UnknownReason, Is.EqualTo("TaskIrFaultOriginsApproximationUnsupported"));
    }

    [Test]
    public void IrFaultClassificationHasASharedTermBudgetAcrossChildren()
    {
        var projection = Lower("");
        var child = projection.Children[0];
        var factory = child.Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        IrTerm term = factory.Binary(IrBinaryOperator.Divide, factory.Variable(child.Owner.Parameters[0].Entry), factory.Integer(1));
        for (var depth = 0; depth < 7; depth++)
        { term = factory.Binary(IrBinaryOperator.Add, term, term); }
        builder.Assign(entry, factory.CreateOperation(), factory.CreateVariable("value", factory.IntegerType), term);
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        var children = Enumerable.Range(0, 16).Select(index => child with
        { Owner = child.Owner with { CallableId = "root:fork-child:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), Program = program } }).ToImmutableArray();
        projection = projection with { Children = children };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), cancellation).HasSourceCosts, Is.True);
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(check.ExceptionKinds, Is.Null);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.UnknownReason, Is.EqualTo("TaskIrFaultOriginsBudgetExceeded"));
    }

    [TestCase("entry-exit")]
    [TestCase("bypass-exit")]
    [TestCase("assume")]
    [TestCase("assert")]
    public void IrFaultOriginsRejectUnclassifiedControlTransfers(string shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var projection = Lower("");
        var factory = projection.Children[1].Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        switch (shape)
        {
            case "entry-exit":
                builder.ExceptionalExit(entry, factory.CreateOperation());
                break;
            case "bypass-exit":
                var exit = builder.CreateBlock();
                builder.Goto(entry, factory.CreateOperation(), exit);
                builder.ExceptionalExit(exit, factory.CreateOperation());
                break;
            case "assume":
                builder.Assume(entry, factory.CreateOperation(), factory.Boolean(false));
                builder.Return(entry, factory.CreateOperation());
                break;
            case "assert":
                builder.Assert(entry, factory.CreateOperation(), factory.Boolean(false));
                builder.Return(entry, factory.CreateOperation());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
        projection = WithFaultOriginProgram(projection, builder.Build());
        var cancellation = TestContext.CurrentContext.CancellationToken;
        Assert.That(NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), cancellation).HasSourceCosts, Is.True);
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(check.ExceptionKinds, Is.Null);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.UnknownReason, Is.EqualTo(shape.EndsWith("exit", StringComparison.Ordinal)
            ? "TaskIrFaultOriginsUnclassifiedExit" : "TaskIrFaultOriginsControlUnsupported"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OwnedIrUnboxingKeepsFaultCandidatesDespiteTypedDefaults(bool boolean)
    {
        var projection = Lower("");
        var factory = projection.Children[1].Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var owner = factory.CreateVariable("boxed", factory.ObjectType);
        var targetType = boolean ? factory.BooleanType : factory.IntegerType;
        var target = factory.CreateVariable("value", targetType);
        builder.Assign(entry, factory.CreateOperation(), owner, factory.Null(factory.ObjectType));
        builder.Assign(entry, factory.CreateOperation(), target, factory.Cast(targetType, factory.Variable(owner)));
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        projection = WithFaultOriginProgram(projection, program);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(check.HasOrigins, Is.True, check.UnknownReason);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.ExceptionKinds, Is.EquivalentTo(new[] { IrExceptionKind.NullReference, IrExceptionKind.InvalidCast }));
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000,
            new IrProgramReplayOptions(static _ => null), cancellation);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        if (boolean)
        { Assert.That(execution.GetCurrentValue(target)!.Boolean, Is.False); }
        else
        { Assert.That(execution.GetCurrentValue(target)!.Integer, Is.Zero); }
    }

    [Test]
    public void UnsupportedSourceDowncastsRemainOutsideTheOwnedProjection()
    {
        const string source = "using System.Threading.Tasks; public static class C {public static Task Root(int n) => " +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(3))); static void Child(int count) " +
            "{object value=(object)new int[1];byte[] other=(byte[])value;}}";
        var result = LowerSourceResult(source);
        Assert.That(result.Projection, Is.Null);
        Assert.That(result.UnknownReason, Is.EqualTo("ForkJoinChildLoweringIncomplete"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OwnedReferenceCastsRetainTypeFaultCandidates(bool upcast)
    {
        var projection = Lower("");
        var factory = projection.Children[1].Owner.Program.Factory;
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var targetType = upcast ? factory.ObjectType : array;
        var operandType = upcast ? array : factory.ObjectType;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var result = factory.CreateVariable("cast", targetType);
        var operand = factory.CreateVariable("operand", operandType);
        builder.Assign(entry, factory.CreateOperation(), operand, factory.Null(operandType));
        builder.Assign(entry, factory.CreateOperation(), result,
            factory.Cast(targetType, factory.Variable(operand)));
        builder.Return(entry, factory.CreateOperation());
        var program = builder.Build();
        projection = WithFaultOriginProgram(projection, program);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(check.HasOrigins, Is.True, check.UnknownReason);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.ExceptionKinds, upcast ? Is.Empty : Is.EquivalentTo(new[] { IrExceptionKind.InvalidCast }));
        var execution = new IrProgramInterpreter(factory).Execute(program, null, 1000,
            new IrProgramReplayOptions(static _ => null), cancellation);
        Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
        Assert.That(execution.GetCurrentValue(result)!.Kind, Is.EqualTo(IrValueKind.Null));
    }

    [Test]
    public void SharedFaultBudgetAlsoRejectsAnInstructionAfterTermTraversal()
    {
        var projection = Lower("");
        projection = projection with
        {
            Children = projection.Children.Select(child =>
            {
                ArgumentNullException.ThrowIfNull(child);
                var factory = child.Owner.Program.Factory;
                var builder = new IrProgramBuilder(factory);
                var entry = builder.CreateBlock();
                var body = builder.CreateBlock();
                var exit = builder.CreateBlock();
                builder.Goto(entry, factory.CreateOperation(), body);
                var value = factory.CreateVariable("value", factory.IntegerType);
                for (var index = 0; index < 1023; index++)
                { builder.Assign(body, factory.CreateOperation(), value, factory.Integer(index)); }
                builder.Goto(body, factory.CreateOperation(), exit);
                builder.Return(exit, factory.CreateOperation());
                return child with { Owner = child.Owner with { Program = builder.Build() }, Cost = ResourceProgram.Zero };
            }).ToImmutableArray()
        };
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var source = NativeForkJoinSourceCostVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(source.HasSourceCosts, Is.True, source.UnknownReason);
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, Root(projection), cancellation);
        Assert.That(check.ExceptionKinds, Is.Null);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.UnknownReason, Is.EqualTo("TaskIrFaultOriginsBudgetExceeded"));
    }

    [Test]
    public void EmptySourceExceptionInstanceBoundDoesNotIncludeRuntimeFaults()
    {
        var projection = Lower("");
        var check = NativeTaskSourceExceptionInstanceVerifier.Check(projection, Root(projection), Layout(),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.SourceInstanceUpper!.Constant, Is.EqualTo(System.Numerics.BigInteger.Zero));
        Assert.That(check.RequiredRuntimeModel, Is.EqualTo(NativeTaskSourceExceptionInstanceVerifier.Model));
        Assert.That(check.InitialResolverCapacityUpper, Is.Zero);
        Assert.That(check.RequiredTrackerModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.FreshSourceTrackerModel));
    }

    [Test]
    public void CaughtSourceExceptionsCannotPublishASingleInstancePerCallbackBound()
    {
        var projection = Lower("");
        var factory = projection.Children[1].Owner.Program.Factory;
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var handler = builder.CreateBlock();
        builder.Throw(entry, factory.CreateOperation(), IrExceptionKind.Argument, handler);
        builder.Return(handler, factory.CreateOperation());
        projection = WithFaultOriginProgram(projection, builder.Build());
        var check = NativeTaskSourceExceptionInstanceVerifier.Check(projection, Root(projection), Layout(),
            TestContext.CurrentContext.CancellationToken);
        Assert.That(check.SourceInstanceUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("TaskSourceExceptionHandlingUnsupported"));
        Assert.That(check.InitialResolverCapacityUpper, Is.Null);
        Assert.That(check.RequiredTrackerModel, Is.Null);
    }

    [Test]
    public void SourceExceptionInstancesRequireCompatibleLayoutAndHonorCancellation()
    {
        var projection = Lower("");
        var root = Root(projection);
        var check = NativeTaskSourceExceptionInstanceVerifier.Check(projection, root,
            new("CoreCLR", "8.0.32", "x86", 4), TestContext.CurrentContext.CancellationToken);
        Assert.That(check.SourceInstanceUpper, Is.Null);
        Assert.That(check.RequiredRuntimeModel, Is.Null);
        Assert.That(check.UnknownReason, Is.EqualTo("IncompatibleResourceRuntimeLayout"));
        Assert.That(check.InitialResolverCapacityUpper, Is.Null);
        Assert.That(check.RequiredTrackerModel, Is.Null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action canceled = () =>
        { _ = NativeTaskSourceExceptionInstanceVerifier.Check(projection, root, Layout(), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(canceled);
    }

    private static CompilerForkJoinProjection WithFaultOriginProgram(CompilerForkJoinProjection projection, IrProgram program)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(program);
        var child = projection.Children[1];
        var replaced = child with { Owner = child.Owner with { Program = program }, Cost = ResourceProgram.Zero };
        return projection with { Children = projection.Children.SetItem(1, replaced) };
    }

    [Test]
    public void IrFaultClassificationRejectsUnownedChildrenAndHonorsCancellation()
    {
        var projection = Lower("");
        var root = Root(projection);
        var child = projection.Children[1];
        projection = projection with
        {
            Children = projection.Children.SetItem(1,
            child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } })
        };
        var check = NativeTaskIrFaultOriginVerifier.Check(projection, root, TestContext.CurrentContext.CancellationToken);
        Assert.That(check.ExceptionKinds, Is.Null);
        Assert.That(check.HasIrAllocations, Is.False);
        Assert.That(check.UnknownReason, Is.EqualTo("ForkJoinChildOwnershipInvalid"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action cancel = () => { _ = NativeTaskIrFaultOriginVerifier.Check(projection, root, cancellation.Token); };
        Assert.Throws<OperationCanceledException>(cancel);
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
        var result = LowerSourceResult(source);
        Assert.That(result.UnknownReason, Is.Null);
        return result.Projection!;
    }

    private static CompilerForkJoinProjectionResult LowerSourceResult(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var compilation = TestCompilation.Create("ForkJoinProjection", [("Subject.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        var tree = compilation.SyntaxTrees.Single();
        var declaration = tree.GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(static method => method is null ? throw new ArgumentNullException(nameof(method)) : method.Identifier.ValueText == "Root");
        var method = (IMethodSymbol)compilation.GetSemanticModel(tree).GetDeclaredSymbol(declaration)!;
        return CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken);
    }
}
