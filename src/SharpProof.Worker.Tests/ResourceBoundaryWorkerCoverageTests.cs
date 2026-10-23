using System.Reflection;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ResourceBoundaryWorkerCoverageTests
{
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(3)]
    public void AccountingTraversalUsesOneBudgetForEveryVisitedNode(int budget)
    {
        var rank = new ResourceLoopRank(ResourceLoopKind.IncreasingAdd, ResourceExpression.Zero,
            ResourceExpression.One, 1, 32, false);
        var program = ResourceProgram.CreditLoop(rank, new ResourceCreditShape(0, 1, 32, false),
            ResourceProgram.Zero, ResourceProgram.Zero);
        if (budget < 3)
        {
            Assert.That(InvokeFailure(typeof(NativeResourceAccountingVerifier), "Collect",
                [program, budget, TestContext.CurrentContext.CancellationToken]), Is.TypeOf<ResourceExpressionLimitException>());
            return;
        }
        var method = typeof(NativeResourceAccountingVerifier).GetMethod("Collect", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        var evidence = (WorkerResourceAccountingEvidence[])method!.Invoke(null,
            [program, budget, TestContext.CurrentContext.CancellationToken])!;
        Assert.That(evidence, Has.Length.EqualTo(1));
        Assert.That(evidence.Single().MaximumSpend, Is.EqualTo("1"));
    }

    [TestCase((int)WorkerResourceMetric.LoopBodyEntries, 1)]
    [TestCase((int)WorkerResourceMetric.ArrayAllocations, 2)]
    [TestCase((int)WorkerResourceMetric.AllocatedElements, 3)]
    [TestCase((int)WorkerResourceMetric.AllocatedBytes, 4)]
    [TestCase((int)WorkerResourceMetric.SourceActivationDepth, 5)]
    [TestCase((int)WorkerResourceMetric.SourceStackBytes, 5)]
    public void MetricProjectionUsesOnlyTheCorrespondingVerifiedBound(int metric, int expected)
    {
        var method = typeof(NativeResourceContractVerifier).GetMethod("SelectUpperBound", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        var cost = new NativeResourceCost(ResourceExpression.One, ResourceExpression.Literal(2), ResourceExpression.Literal(3));
        var bound = (ResourceExpression)method!.Invoke(null,
            [(WorkerResourceMetric)metric, cost, ResourceExpression.Literal(4), ResourceExpression.Literal(5)])!;
        Assert.That(bound.Constant, Is.EqualTo(new System.Numerics.BigInteger(expected)));
    }

    [Test]
    public void UnknownMetricCannotSelectAnUnrelatedVerifiedBound()
    {
        var cost = new NativeResourceCost(ResourceExpression.One, ResourceExpression.One, ResourceExpression.One);
        var failure = InvokeFailure(typeof(NativeResourceContractVerifier), "SelectUpperBound",
            [(WorkerResourceMetric)999, cost, ResourceExpression.One, ResourceExpression.One]);
        Assert.That(failure, Is.TypeOf<InvalidDataException>());
        Assert.That(failure.Message, Is.EqualTo("Resource metric is unsupported."));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplayInputProjectionPreservesOwnedScalarValuesAndRejectsUnknownKinds(bool unknown)
    {
        var value = new IrFactory(IrExecutionSemantics.Total).CreateIntegerValue(7);
        if (unknown)
        {
            var failure = InvokeFailure(typeof(NativeResourceViolationReplay), "ReadReplayInput", [(ResourceInputKind)999, value]);
            Assert.That(failure, Is.TypeOf<InvalidDataException>());
            Assert.That(failure.Message, Is.EqualTo("Unsupported resource input during replay."));
            return;
        }
        var method = typeof(NativeResourceViolationReplay).GetMethod("ReadReplayInput", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        Assert.That(method!.Invoke(null, [ResourceInputKind.NonNegativeScalar, value]), Is.EqualTo(new System.Numerics.BigInteger(7)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplayFootprintAdmissionRequiresCompatibleStorage(bool requireExact)
    {
        var shape = ResourceAllocationShape.String(ResourceExpression.Literal(2));
        var compatible = ResourceRuntimeLayout.Bound(shape, Layout(), TestContext.CurrentContext.CancellationToken);
        var incompatible = ResourceRuntimeLayout.Bound(shape, new("CoreCLR", "8.0.32", "x86", 4),
            TestContext.CurrentContext.CancellationToken);
        var method = typeof(NativeResourceViolationReplay).GetMethod("ReadFootprintBytes", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        Assert.That(method!.Invoke(null, [compatible, requireExact]), Is.EqualTo(new System.Numerics.BigInteger(32)));
        Assert.That(method.Invoke(null, [incompatible, requireExact]), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplayFrameFootprintsKeepLowerBoundsSeparateFromExactStringCosts(bool requireExact)
    {
        var shape = ResourceAllocationShape.Object([.. Enumerable.Repeat(ResourceStorageKind.Byte, 9)]);
        var footprint = ResourceRuntimeLayout.Bound(shape, Layout(), TestContext.CurrentContext.CancellationToken);
        Assert.That(footprint.Bounds!.IsExact, Is.False);
        var method = typeof(NativeResourceViolationReplay).GetMethod("ReadFootprintBytes", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        var bytes = method!.Invoke(null, [footprint, requireExact]);

        Assert.That(bytes, requireExact ? Is.Null : Is.EqualTo(new System.Numerics.BigInteger(32)));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void IncompleteExpectedEvidenceCannotPublishAnAverage(int missing)
    {
        var distribution = new ResourceBooleanDistribution(0, "selected", 1, 1);
        var check = new NativeResourceExpectedCheck(missing == 0 ? null : distribution,
            missing == 1 ? null : ResourceExpression.One,
            missing == 2 ? null : ResourceExpression.One,
            missing == 3 ? null : ResourceExpression.One, null);

        var evidence = NativeResourceExpectedVerifier.Project(check);

        Assert.That(evidence.UnknownReason, Is.EqualTo("ResourceExpectedEvidenceIncomplete"));
        Assert.That(evidence.UpperBound, Is.Null);
        Assert.That(evidence.FalseUpperBound, Is.Null);
        Assert.That(evidence.TrueUpperBound, Is.Null);
    }

    [Test]
    public void MoreThanSixteenCreditSummariesCannotPublishPartialAccounting()
    {
        var rank = new ResourceLoopRank(ResourceLoopKind.IncreasingAdd, ResourceExpression.Zero,
            ResourceExpression.One, 1, 32, false);
        var credit = ResourceProgram.CreditLoop(rank, new ResourceCreditShape(0, 1, 32, false),
            ResourceProgram.Zero, ResourceProgram.Zero);
        var program = credit;
        for (var index = 1; index < 17; index++)
        { program = ResourceProgram.Sequence(program, credit); }

        Assert.Throws<ResourceExpressionLimitException>((Action)(() =>
            NativeResourceAccountingVerifier.Evidence(program, TestContext.CurrentContext.CancellationToken)));
    }

    [TestCase("allocation", "UnsupportedResourceAllocationSemantics")]
    [TestCase("lower", "MalformedResourceProgram")]
    public void UnsupportedPrivateCostNodesFailClosed(string pass, string reason)
    {
        ArgumentNullException.ThrowIfNull(pass);
        ArgumentNullException.ThrowIfNull(reason);
        var type = pass == "allocation" ? typeof(NativeResourceAllocationVerifier) : typeof(NativeResourceProgramVerifier);
        var arguments = pass == "allocation" ? new object[] { ResourceProgram.MetadataBoundary, Layout(), TestContext.CurrentContext.CancellationToken } :
            new object[] { ResourceProgram.MetadataBoundary, TestContext.CurrentContext.CancellationToken };

        var exception = InvokeFailure(type, pass == "allocation" ? "Visit" : "VisitLower", arguments);

        Assert.That(exception, Is.TypeOf<InvalidDataException>());
        Assert.That(exception.Message, Is.EqualTo(reason));
    }

    [Test]
    public void LowerCostTraversalRejectsAnUnknownLoopKind()
    {
        var rank = new ResourceLoopRank((ResourceLoopKind)999, ResourceExpression.Zero,
            ResourceExpression.One, 1, 32, false);
        var program = ResourceProgram.Repeat(rank, ResourceRepeatKind.Loop, ResourceProgram.Zero);

        var exception = InvokeFailure(typeof(NativeResourceProgramVerifier), "VisitLower",
            [program, TestContext.CurrentContext.CancellationToken]);

        Assert.That(exception, Is.TypeOf<InvalidDataException>());
        Assert.That(exception.Message, Is.EqualTo("MalformedResourceRank"));
    }

    [Test]
    public void RecursiveStackTraversalRequiresAnActiveOwnedFrame()
    {
        var rank = new ResourceLoopRank(ResourceLoopKind.DecreasingSubtract, ResourceExpression.One,
            ResourceExpression.Zero, 1, 32, false);
        var program = ResourceProgram.Repeat(rank, ResourceRepeatKind.TailRecurrence, ResourceProgram.Zero);

        var exception = InvokeFailure(typeof(NativeResourceStackVerifier), "Visit",
            [program, null!, TestContext.CurrentContext.CancellationToken]);

        Assert.That(exception, Is.TypeOf<InvalidDataException>());
        Assert.That(exception.Message, Is.EqualTo("ResourceStackRecursiveFrameMissing"));
    }

    [Test]
    public void LegacyUnboundedIntegerStorageCannotSupplyAnExactByteRefutation()
    {
        var factory = new IrFactory();
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var variable = factory.CreateVariable("values", type);
        var seed = builder.Write(block, factory.CreateOperation(), IrWriteRegion.Local);
        // The public builder rejects Legacy array storage before the replay
        // boundary. Construct the detached instruction to test that boundary.
        var allocation = new IrAllocationInstruction(seed.Id, seed.Operation, type, variable, factory.Integer(1));
        var method = typeof(NativeResourceViolationReplay).GetMethod("ExactArrayBytes", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);

        var bytes = method!.Invoke(null, [factory, allocation, 1, Layout(), TestContext.CurrentContext.CancellationToken]);

        Assert.That(bytes, Is.Null);
    }

    [Test]
    public void MemoryAdmissionRejectsAReferenceAssignedFromAScalar()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var reference = factory.CreateVariable("reference", factory.ObjectType);
        var seed = builder.Write(entry, factory.CreateOperation(), IrWriteRegion.Local);
        var returned = builder.Return(entry, factory.CreateOperation());
        var owned = builder.Build();
        var mismatched = new IrAssignInstruction(seed.Id, seed.Operation, reference, factory.Integer(1));
        var malformed = new IrProgram(factory, owned.Scope, owned.Entry,
            [new IrBasicBlock(entry, null, [mismatched, returned])]);

        Assert.That(NativeResourceMemoryEnvelope.Admits(Owner(owned),
            TestContext.CurrentContext.CancellationToken), Is.True);
        Assert.That(NativeResourceMemoryEnvelope.Admits(Owner(malformed),
            TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [TestCase("branch", false)]
    [TestCase("goto", false)]
    [TestCase("throw", false)]
    [TestCase("return", true)]
    [TestCase("exceptional-exit", true)]
    public void ReachableUsesRequireDominatingOwnedOriginsAcrossTerminalShapes(string terminal, bool admitted)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var allocation = builder.CreateBlock();
        var use = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var variable = factory.CreateVariable("values", type);
        switch (terminal)
        {
            case "branch":
                builder.Branch(entry, factory.CreateOperation(), factory.Boolean(false), allocation, use);
                break;
            case "goto":
                builder.Goto(entry, factory.CreateOperation(), use);
                break;
            case "throw":
                builder.Throw(entry, factory.CreateOperation(), IrExceptionKind.Unknown, use);
                break;
            case "return":
                builder.Return(entry, factory.CreateOperation());
                break;
            case "exceptional-exit":
                builder.ExceptionalExit(entry, factory.CreateOperation());
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(terminal));
        }
        builder.Allocate(allocation, factory.CreateOperation(), type, variable, factory.Integer(1));
        builder.Goto(allocation, factory.CreateOperation(), use);
        builder.Return(use, factory.CreateOperation(), factory.Variable(variable));

        // Return and exceptional exit have no edge to the disconnected use;
        // all other shapes have a path that bypasses the allocation.
        Assert.That(NativeResourceMemoryEnvelope.Admits(Owner(builder.Build()),
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(admitted));
    }

    [Test]
    public void ScalarUnaryTermsDoNotIntroduceBorrowedReferenceOwnership()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var value = factory.CreateVariable("value", factory.IntegerType);
        builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(1));
        builder.Return(entry, factory.CreateOperation(), factory.Unary(IrUnaryOperator.Negate, factory.Variable(value)));

        Assert.That(NativeResourceMemoryEnvelope.Admits(Owner(builder.Build()),
            TestContext.CurrentContext.CancellationToken), Is.True);
    }

    private static CompilerTotalCallablePreparation Owner(IrProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new("owner", program, [], null, []) { EffectsCompleteAtEntry = true };
    }

    private static ResourceRuntimeLayoutContext Layout()
    {
        return new("CoreCLR", "8.0.32", "x64", 8);
    }

    private static Exception InvokeFailure(Type type, string name, object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(arguments);
        var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        var exception = Assert.Throws<TargetInvocationException>((Action)(() => method!.Invoke(null, arguments)));
        Assert.That(exception!.InnerException, Is.Not.Null);
        return exception.InnerException!;
    }
}
