using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Specs;

namespace SharpProof.Worker.Test;

[TestFixture]
internal sealed class NativeTaskContextDispatchStorageTests
{
    [TestCase(2, false, 2176L)]
    [TestCase(3, false, 3432L)]
    [TestCase(16, false, 23672L)]
    [TestCase(2, true, 2544L)]
    [TestCase(16, true, 24040L)]
    public void ContextDispatchCandidatesAreIncludedOnceInTheBookkeepingPrefix(int children, bool configured, long taskBookkeeping)
    {
        var (projection, root) = Fixture(children, configured);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var carriers = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, Layout(), cancellation);
        var context = NativeTaskFaultCarrierStorageVerifier.CheckContextDispatchInstances(projection, root, Layout(), cancellation);
        var dispatch = ResourceRuntimeLayout.Bound(ResourceAllocationShape.Object(ResourceTaskFaultCarrierCatalog.DispatchFields), Layout(), cancellation);
        var expected = dispatch.Bounds!.Upper.Constant!.Value * (children + (configured ? 1 : 0));
        Assert.That(context.UnknownReason, Is.Null);
        Assert.That(context.InstanceUpper!.Constant, Is.EqualTo(expected));
        Assert.That(context.RequiredRuntimeModel, Is.EqualTo(NativeTaskFaultCarrierStorageVerifier.ContextDispatchInstanceModel));
        Assert.That(carriers.ContextDispatchInstanceUpper!.Constant, Is.EqualTo(expected));
        Assert.That(carriers.BookkeepingUpper!.Constant, Is.EqualTo(taskBookkeeping + expected));
        Assert.That(carriers.RequiredRuntimeModel, Is.EqualTo(ResourceTaskFaultCarrierCatalog.Model));
        Assert.That(carriers.RequiredContextDispatchInstanceModel, Is.EqualTo(context.RequiredRuntimeModel));
        // Captures construct candidates; restores only copy the separately
        // budgeted arrays. Including both here would double the instance count.
        Assert.That(carriers.ContextTraceCaptureCountUpper, Is.EqualTo(children + (configured ? 1 : 0)));
        Assert.That(carriers.ContextTraceRestoreCountUpper, Is.EqualTo(children + (configured ? 1 : 0)));
        Assert.That(carriers.CombinedDispatchArrayCopiesUpper, Is.EqualTo(6 * children + (configured ? 8 : 0)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(17)]
    public void ContextDispatchInstancesRejectUnownedChildCounts(int children)
    {
        var (projection, root) = Fixture(children, false);
        AssertUnknown(projection, root, Layout(), "ForkJoinRootOwnershipInvalid");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ContextDispatchInstancesRejectIncompleteLaterChildOwnership(bool configured)
    {
        var (projection, root) = Fixture(2, configured);
        var child = projection.Children[1];
        projection = projection with
        {
            Children = projection.Children.SetItem(1, child with { Owner = child.Owner with { EffectsCompleteAtEntry = false } })
        };
        AssertUnknown(projection, root, Layout(), "ForkJoinChildOwnershipInvalid");
    }

    [Test]
    public void ContextDispatchInstancesRequireTheCompleteSourceInstructionBudget()
    {
        var (projection, root) = Fixture(2, true);
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
        AssertUnknown(projection, root, Layout(), "ForkJoinSourceCostBudgetExceeded");
    }

    [TestCase("CoreCLR", "8.0.32", "x86", 4)]
    [TestCase("CoreCLR", "8.0.32", "arm64", 8)]
    [TestCase("CoreCLR", "8.0.31", "x64", 8)]
    public void ContextDispatchInstancesRejectIncompatibleLayouts(string runtime, string version, string architecture, int pointerBytes)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(architecture);
        var (projection, root) = Fixture(2, false);
        AssertUnknown(projection, root, new(runtime, version, architecture, pointerBytes), "IncompatibleResourceRuntimeLayout");
    }

    [Test]
    public void ContextDispatchInstancesHonorCancellation()
    {
        var (projection, root) = Fixture(2, true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action operation = () =>
        { _ = NativeTaskFaultCarrierStorageVerifier.CheckContextDispatchInstances(projection, root, Layout(), cancellation.Token); };
        Assert.Throws<OperationCanceledException>(operation);
    }

    private static void AssertUnknown(CompilerForkJoinProjection projection, CompilerTotalEntryPreparation root,
        ResourceRuntimeLayoutContext layout, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var cancellation = TestContext.CurrentContext.CancellationToken;
        var context = NativeTaskFaultCarrierStorageVerifier.CheckContextDispatchInstances(projection, root, layout, cancellation);
        var carriers = NativeTaskFaultCarrierStorageVerifier.Check(projection, root, layout, cancellation);
        Assert.That(context.UnknownReason, Is.EqualTo(reason));
        Assert.That(context.InstanceUpper, Is.Null);
        Assert.That(context.RequiredRuntimeModel, Is.Null);
        Assert.That(carriers.UnknownReason, Is.EqualTo(reason));
        Assert.That(carriers.BookkeepingUpper, Is.Null);
        Assert.That(carriers.RequiredRuntimeModel, Is.Null);
        Assert.That(carriers.ContextDispatchInstanceUpper, Is.Null);
        Assert.That(carriers.RequiredContextDispatchInstanceModel, Is.Null);
    }

    private static (CompilerForkJoinProjection Projection, CompilerTotalEntryPreparation Root) Fixture(int children, bool configured)
    {
        var source = "using System.Threading.Tasks; public static class C {public static Task Root(int n) => " +
            "Task.WhenAll(Task.Run(()=>Child(n)),Task.Run(()=>Child(n))); static void Child(int n) {var a=new int[n];} }";
        var compilation = TestCompilation.Create("ContextDispatchStorage", [("Subject.cs", source)]);
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("C")!.GetMembers("Root").OfType<IMethodSymbol>().Single();
        var projection = CompilerForkJoinResourceLowering.Lower(method, compilation, "root", TestContext.CurrentContext.CancellationToken).Projection!;
        Assert.That(projection, Is.Not.Null);
        var child = projection.Children[0];
        var entries = Enumerable.Range(0, children).Select(index => child with
        {
            Owner = child.Owner with { CallableId = "root:fork-child:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture) }
        }).ToImmutableArray();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var parameter = new CompilerTotalParameter(factory.CreateVariable("entry:n", factory.IntegerType),
            factory.CreateVariable("current:n", factory.IntegerType), factory.CreateVariable("old:n", factory.IntegerType));
        return (projection with { Children = entries, ConfiguredAwait = configured }, new("root", factory, [parameter], []));
    }

    private static ResourceRuntimeLayoutContext Layout()
    {
        return new("CoreCLR", "8.0.32", "x64", 8);
    }
}
