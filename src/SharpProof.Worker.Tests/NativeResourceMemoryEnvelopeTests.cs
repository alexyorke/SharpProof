using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [Test]
    public void EnvelopeRejectsAReferenceReadBeforeItsAllocation()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var value = factory.CreateVariable("value", type);
        var result = factory.CreateVariable("result", type);
        builder.Assign(entry, factory.CreateOperation(), result, factory.Variable(value));
        builder.Allocate(entry, factory.CreateOperation(), type, value, factory.Integer(1));
        builder.Return(entry, factory.CreateOperation(), factory.Variable(result));
        var owner = new SharpProof.CompilerArtifact.CompilerTotalCallablePreparation("owner", builder.Build(), [], result, [])
        { EffectsCompleteAtEntry = true };
        Assert.That(NativeResourceMemoryEnvelope.Admits(owner, TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [Test]
    public void EnvelopeAcceptsADominatingAllocationFromAnotherBlock()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var value = factory.CreateVariable("value", type);
        builder.Allocate(entry, factory.CreateOperation(), type, value, factory.Integer(1));
        builder.Goto(entry, factory.CreateOperation(), exit);
        builder.Return(exit, factory.CreateOperation(), factory.Variable(value));
        var owner = new SharpProof.CompilerArtifact.CompilerTotalCallablePreparation("owner", builder.Build(), [], value, [])
        { EffectsCompleteAtEntry = true };
        Assert.That(NativeResourceMemoryEnvelope.Admits(owner, TestContext.CurrentContext.CancellationToken), Is.True);
    }

    [Test]
    public void EnvelopeRejectsAnOversizedControlFlowGraph()
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        for (var index = 0; index <= ResourceMemoryTrace.MaximumEvents / 4; index++)
        { builder.Return(builder.CreateBlock(), factory.CreateOperation()); }
        var owner = new SharpProof.CompilerArtifact.CompilerTotalCallablePreparation("owner", builder.Build(), [], null, [])
        { EffectsCompleteAtEntry = true };
        Assert.That(NativeResourceMemoryEnvelope.Admits(owner, TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [TestCase("instructions")]
    [TestCase("definitions")]
    [TestCase("terms")]
    [TestCase("dominance")]
    public void MemoryEnvelopeRejectsExhaustedAnalysisBudgets(string budget)
    {
        ArgumentNullException.ThrowIfNull(budget);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var value = factory.CreateVariable("value", factory.IntegerType);
        switch (budget)
        {
            case "instructions":
            case "definitions":
                var count = budget == "definitions" ? ResourceExpression.MaximumNodes + 1 : ResourceExpression.MaximumNodes / 2 + 1;
                for (var index = 0; index < count; index++)
                { builder.Assign(entry, factory.CreateOperation(), value, factory.Integer(index)); }
                builder.Return(entry, factory.CreateOperation());
                break;
            case "terms":
                IrTerm term = factory.Variable(value);
                for (var depth = 0; depth < 13; depth++)
                { term = factory.Binary(IrBinaryOperator.Add, term, term); }
                builder.Return(entry, factory.CreateOperation(), term);
                break;
            case "dominance":
                var array = factory.GetOrCreateSequenceType(factory.IntegerType);
                var current = entry;
                for (var block = 0; block < 8; block++)
                {
                    for (var index = 0; index < 32; index++)
                    {
                        var allocated = factory.CreateVariable("array", array);
                        builder.Allocate(current, factory.CreateOperation(), array, allocated, factory.Integer(1));
                    }
                    if (block == 7)
                    { builder.Return(current, factory.CreateOperation()); }
                    else
                    {
                        var next = builder.CreateBlock();
                        builder.Goto(current, factory.CreateOperation(), next);
                        current = next;
                    }
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(budget));
        }
        var owner = new SharpProof.CompilerArtifact.CompilerTotalCallablePreparation("owner", builder.Build(), [], null, [])
        { EffectsCompleteAtEntry = true };
        Assert.That(NativeResourceMemoryEnvelope.Admits(owner, TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [TestCase("scalar", true)]
    [TestCase("reference", false)]
    [TestCase("memory", false)]
    public void MemoryEnvelopeHavocPreservesOnlyScalarOwnership(string shape, bool admitted)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var array = factory.GetOrCreateSequenceType(factory.IntegerType);
        var owned = factory.CreateVariable("owned", array);
        builder.Allocate(entry, factory.CreateOperation(), array, owned, factory.Integer(1));
        switch (shape)
        {
            case "scalar":
                builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Input,
                    factory.CreateVariable("input", factory.IntegerType));
                break;
            case "reference":
                builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Variables, IrHavocOrigin.Input, owned);
                break;
            case "memory":
                builder.Havoc(entry, factory.CreateOperation(), IrHavocKind.Memory, IrHavocOrigin.Input);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(shape));
        }
        builder.Return(entry, factory.CreateOperation(), factory.Variable(owned));
        var owner = new SharpProof.CompilerArtifact.CompilerTotalCallablePreparation("owner", builder.Build(), [], owned, [])
        { EffectsCompleteAtEntry = true };
        Assert.That(NativeResourceMemoryEnvelope.Admits(owner, TestContext.CurrentContext.CancellationToken), Is.EqualTo(admitted));
    }
    [TestCase(5, "bool", 1)]
    [TestCase(6, "bool", 1)]
    [TestCase(5, "byte", 1)]
    [TestCase(6, "byte", 1)]
    [TestCase(5, "short", 2)]
    [TestCase(6, "short", 2)]
    [TestCase(5, "char", 2)]
    [TestCase(6, "char", 2)]
    [TestCase(5, "int", 4)]
    [TestCase(6, "int", 4)]
    [TestCase(5, "long", 8)]
    [TestCase(6, "long", 8)]
    public async Task ParametricArrayLifetimesUseCheckedCumulativeEnvelopes(int metric, string element, int width)
    {
        ArgumentNullException.ThrowIfNull(element);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},31,nameof(n),{width})] public static {element}[] Target(int n) => new {element}[n]; }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason ?? target.ResourceUnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Does.Contain("n"));
        Assert.That(result.ResourceBound.Asymptotic!.UpperBound, Is.EqualTo("O(n)"));
        Assert.That(result.ResourceBound.Asymptotic.Lower!.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ProofCore, Does.Contain("resource-memory-envelope:" + NativeResourceMemoryEnvelope.ModelId));
        Assert.That(result.ResourceBound.MetadataAssumptions.Single().RuntimeModel, Is.EqualTo(ResourceRuntimeLayout.ModelId));
    }

    [TestCase(5)]
    [TestCase(6)]
    public async Task ArrayAliasesWithinTheAllocationBlockKeepTheirOwnedOrigin(int metric)
    {
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},31,nameof(n),4)] public static int[] Target(int n) " +
            "{var value=new int[n]; var alias=value; return alias;} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ProofCore, Does.Contain("resource-memory-envelope:" + NativeResourceMemoryEnvelope.ModelId));
    }

    [TestCase(5)]
    [TestCase(6)]
    public async Task LoopTemporariesHaveCumulativeAuxiliaryBoundsAndNoRetainedVoidOutput(int metric)
    {
        var declaration = metric == 5
            ? "[ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes,0,nameof(n),40)]"
            : "[ResourceUpperBound(ResourceMetric.RetainedBytes,0)]";
        var source = "using SharpProof.Attributes; public static class C { " + declaration +
            "public static void Target(int n) {for(int i=0;i<n;i++){var temporary=new int[3];}} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.Asymptotic!.UpperBound, Is.EqualTo(metric == 5 ? "O(n)" : "O(0)"));
        if (metric == 6)
        { Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo("0")); }
    }

    [Test]
    public async Task ScalarResultsRetainNoOwnedArrayGraph()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,0)] public static int Target(int n) {var value=new int[n]; return n;} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("0"));
    }

    [Test]
    public async Task NonconstantNegatedScalarReturnsUseCumulativeMemoryEnvelopes()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,0)] public static int Target(int n) {var value=new int[n]; return -n;} }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        Assert.That(target.Total, Is.Not.Null);
        var owner = target.Total!;
        var nonconstantNegation = false;
        foreach (var block in owner.Program.Blocks)
        {
            foreach (var instruction in block.Instructions)
            {
                foreach (var read in IrInstructionFacts.ReadTerms(instruction))
                {
                    nonconstantNegation |= IrTraversal.Any(read, static term =>
                    {
                        ArgumentNullException.ThrowIfNull(term);
                        return term is IrUnaryTerm { Operator: IrUnaryOperator.Negate } unary &&
                            !IrTraversal.CollectVariables(unary.Operand).IsEmpty;
                    });
                }
            }
        }
        Assert.That(nonconstantNegation, Is.True, "The compiled parameter negation must remain a nonconstant unary IR term.");
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("0"));
        Assert.That(result.ProofCore, Does.Contain("resource-memory-envelope:" + NativeResourceMemoryEnvelope.ModelId));
    }

    [TestCase(5, "public static int[] Target(int n,int[] input) => input;")]
    [TestCase(6, "public static int[] Target(int n,int[] input) => input;")]
    [TestCase(5, "public int[] Target(int n) => new int[n];")]
    [TestCase(6, "public int[] Target(int n) => new int[n];")]
    public async Task MemoryEnvelopesRejectBorrowedAndReceiverOrigins(int metric, string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var source = "using SharpProof.Attributes; public sealed class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},31,nameof(n),4)] " + method + " }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.UnknownReason, Is.Not.Null.And.Not.Empty);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [TestCase(5, "public static int Target(int n) {var value=new int[n]; return value.Length;}")]
    [TestCase(6, "public static int Target(int n) {var value=new int[n]; return value.Length;}")]
    [TestCase(5, "public static int[] Target(int n,bool flag) {var value=new int[n]; if(flag) return value; return null;}")]
    [TestCase(6, "public static int[] Target(int n,bool flag) {var value=new int[n]; if(flag) return value; return null;}")]
    [TestCase(5, "public static int Target(int n) {var value=new int[n]; if(n>0) return value[0]; return 0;}")]
    [TestCase(6, "public static int Target(int n) {var value=new int[n]; if(n>0) return value[0]; return 0;}")]
    public async Task DominatingOwnedArraysCrossCompilerGuardAndReturnBlocks(int metric, string method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},31,nameof(n),4)] " + method + " }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ProofCore, Does.Contain("resource-memory-envelope:" + NativeResourceMemoryEnvelope.ModelId));
        Assert.That(result.ResourceBound!.Asymptotic!.Lower!.UnknownReason, Is.Not.Null.And.Not.Empty);
        if (metric == 6 && method.StartsWith("public static int Target", StringComparison.Ordinal))
        { Assert.That(result.ResourceBound.InferredUpperBound, Is.EqualTo("0")); }
    }

    [TestCase(5, false)]
    [TestCase(6, false)]
    [TestCase(5, true)]
    [TestCase(6, true)]
    public async Task UnaryReturnGuardsPreserveTheBoundaryBetweenOwnedAndBorrowedArrays(int metric, bool borrowed)
    {
        var extraParameter = borrowed ? ",int[] input" : "";
        var selected = borrowed ? "input" : "value";
        var source = "using SharpProof.Attributes; public static class C { " +
            $"[ResourceUpperBound((ResourceMetric){metric},31,nameof(n),4)] public static int[] Target(int n,bool skip{extraParameter}) " +
            $"{{var value=new int[n]; if(!skip) return {selected}; return null;}} }}";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible,
            new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(borrowed ? WorkerClaimOutcome.Unknown : WorkerClaimOutcome.Proven),
            result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.Null);
        Assert.That(result.EffectWitness, Is.Null);
        if (borrowed)
        {
            Assert.That(result.ResourceBound.UnknownReason, Is.Not.Null.And.Not.Empty);
            Assert.That(result.ResourceBound.InferredUpperBound, Is.Null);
            Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.Null);
            Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
            Assert.That(result.Assumptions.Any(static assumption => assumption is not null && assumption.Used), Is.False);
        }
        else
        {
            Assert.That(result.ResourceBound.UnknownReason, Is.Null);
            Assert.That(result.ResourceBound.InferredUpperBound, Does.Contain("n"));
            Assert.That(result.ResourceBound.MetadataAssumptions, Has.Length.EqualTo(1));
            Assert.That(result.ProofCore, Does.Contain("resource-memory-envelope:" + NativeResourceMemoryEnvelope.ModelId));
        }
    }

    [TestCase("bypass")]
    [TestCase("redefinition")]
    [TestCase("cycle")]
    public void CrossBlockOriginsRejectMissingOrRedefinedAllocations(string mode)
    {
        ArgumentNullException.ThrowIfNull(mode);
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var allocated = builder.CreateBlock();
        var alternate = builder.CreateBlock();
        var exit = builder.CreateBlock();
        var type = factory.GetOrCreateSequenceType(factory.IntegerType);
        var value = factory.CreateVariable("value", type);
        var flag = factory.CreateVariable("flag", factory.BooleanType);
        if (mode == "redefinition")
        {
            builder.Goto(entry, factory.CreateOperation(), allocated);
            var foreign = factory.CreateVariable("uninitialized", type);
            builder.Assign(alternate, factory.CreateOperation(), value, factory.Variable(foreign));
        }
        else
        { builder.Branch(entry, factory.CreateOperation(), factory.Variable(flag), allocated, alternate); }
        builder.Allocate(allocated, factory.CreateOperation(), type, value, factory.Integer(1));
        builder.Goto(allocated, factory.CreateOperation(), mode == "bypass" ? exit : alternate);
        if (mode == "cycle")
        { builder.Branch(alternate, factory.CreateOperation(), factory.Variable(flag), allocated, exit); }
        else
        { builder.Goto(alternate, factory.CreateOperation(), exit); }
        builder.Return(exit, factory.CreateOperation(), factory.Variable(value));
        var owner = new SharpProof.CompilerArtifact.CompilerTotalCallablePreparation("owner", builder.Build(), [], value, [])
        { EffectsCompleteAtEntry = true };
        Assert.That(NativeResourceMemoryEnvelope.Admits(owner, TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MemoryEnvelopeRequiresAnAvailableVerifiedCumulativeProgram(bool missing)
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,31,nameof(n),4)] public static int[] Target(int n) => new int[n]; }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        target = target with { ResourceProgram = missing ? null : ResourceProgram.MetadataBoundary };
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.Null);
        Assert.That(result.ResourceBound.MetadataAssumptions, Is.Empty);
    }

    [Test]
    public async Task PreciseLifetimeBoundsRemainPreferredOverCumulativeEnvelopes()
    {
        const string source = "using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.PeakAuxiliaryBytes,0)] public static int[] Target(int n) => new int[2]; }";
        var target = Read(CreateArtifact(source, reviewedRuntime: true, x64: true));
        var result = (await NativeEffectClaims.VerifyAsync(target, CallableEntryFeasibility.Feasible, new WorkerBudgets(), TestContext.CurrentContext.CancellationToken)).Single();
        Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), result.ResourceBound?.UnknownReason);
        Assert.That(result.ResourceBound!.InferredUpperBound, Is.EqualTo("0"));
        Assert.That(result.ProofCore.Any(static item => item is null ? throw new ArgumentNullException(nameof(item)) : item.StartsWith("resource-memory-envelope:", StringComparison.Ordinal)), Is.False);
    }
}
