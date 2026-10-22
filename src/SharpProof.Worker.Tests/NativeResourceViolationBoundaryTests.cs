using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
    [TestCase("valid", true)]
    [TestCase("extra", true)]
    [TestCase("missing-first", false)]
    [TestCase("missing-later", false)]
    [TestCase("wrong-type", false)]
    [TestCase("foreign-value", false)]
    [TestCase("null-value", false)]
    public void ReplayEntryModelsMustBindEveryDeclaredParameter(string mutation, bool admitted)
    {
        ArgumentNullException.ThrowIfNull(mutation);
        var owner = Prepare(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)]", "var value=new int[1];")).Total!;
        var factory = owner.Program.Factory;
        var inputs = ImmutableDictionary<IrVarId, IrValue>.Empty;
        foreach (var parameter in owner.Parameters)
        {
            var type = factory.GetVariableInfo(parameter.Entry).Type;
            inputs = inputs.Add(parameter.Entry, factory.GetTypeInfo(type).CreateDefaultValue(factory));
        }
        var first = owner.Parameters[0].Entry;
        switch (mutation)
        {
            case "valid":
                break;
            case "extra":
                inputs = inputs.Add(factory.CreateVariable("extra", factory.BooleanType), factory.CreateBooleanValue(false));
                break;
            case "missing-first":
                inputs = inputs.Remove(first);
                break;
            case "missing-later":
                inputs = inputs.Remove(owner.Parameters[1].Entry);
                break;
            case "wrong-type":
                inputs = inputs.SetItem(first, factory.CreateBooleanValue(false));
                break;
            case "foreign-value":
                inputs = inputs.SetItem(first, new IrFactory(IrExecutionSemantics.Total).CreateIntegerValue(0));
                break;
            case "null-value":
                inputs = inputs.SetItem(first, null!);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }
        Assert.That(NativeResourceViolationReplay.EntryModelIsOwned(owner, inputs,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(admitted));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReplayEntryModelShapeRequiresANonNullReceiver(bool nonnull)
    {
        var owner = Read(CreateArtifact("using SharpProof.Attributes; public sealed class C { " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)] public void Target() {} }")).Total!;
        var factory = owner.Program.Factory;
        var entry = owner.Parameters[owner.Parameters.Length - 1].Entry;
        var type = factory.GetVariableInfo(entry).Type;
        var value = nonnull ? factory.CreateReferenceValue(type, new object()) : factory.CreateNullValue(type);
        var inputs = ImmutableDictionary<IrVarId, IrValue>.Empty.Add(entry, value);
        Assert.That(NativeResourceViolationReplay.EntryModelIsOwned(owner, inputs,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(nonnull));
    }

    [TestCase(false, true)]
    [TestCase(true, false)]
    public void EmptyReplayEntriesCannotPretendToHaveAReceiver(bool receiver, bool admitted)
    {
        var owner = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)] public static void Target() {} }")).Total! with
        { HasReceiver = receiver };
        Assert.That(NativeResourceViolationReplay.EntryModelIsOwned(owner, ImmutableDictionary<IrVarId, IrValue>.Empty,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(admitted));
    }

    [Test]
    public void DefaultReplayParameterListsCannotBecomeEmptyEntryProofs()
    {
        var owner = Prepare(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)]", "")).Total! with
        { Parameters = default };
        Assert.That(NativeResourceViolationReplay.EntryModelIsOwned(owner, ImmutableDictionary<IrVarId, IrValue>.Empty,
            TestContext.CurrentContext.CancellationToken), Is.False);
    }

    [Test]
    public void ReplayEntryShapeValidationPropagatesCancellationBeforeRejectingAModel()
    {
        var owner = Prepare(Source("[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)]", "")).Total!;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Action check = () =>
        {
            _ = NativeResourceViolationReplay.EntryModelIsOwned(owner,
            ImmutableDictionary<IrVarId, IrValue>.Empty, cancellation.Token);
        };
        Assert.That(check, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task InstanceViolationSearchRequiresANonNullReceiverModel()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public sealed class C { " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)] public void Target() {var value=new int[1];} }"));
        Assert.That(target.Total, Is.Not.Null);
        Assert.That(target.Total!.HasReceiver, Is.True);
        var result = await FindBoundaryViolation(target, ResourceExpression.Zero);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("1"));
        var receiver = result.Model.Single(static value => value is null ? throw new ArgumentNullException(nameof(value)) :
            value.Variable == "this");
        Assert.That(receiver.Kind, Is.Not.EqualTo("null"));
        Assert.That(receiver.Value, Is.Not.EqualTo("null"));
    }

    [TestCase(true, 1, false, 32)]
    [TestCase(true, -1, false, null)]
    [TestCase(false, 1, false, null)]
    [TestCase(true, 1, true, null)]
    public void ExactArrayByteCandidatesRequireANonnegativeExtentArrayShapeAndCompatibleLayout(
        bool array, int elements, bool incompatible, int? expected)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock();
        var type = array ? factory.GetOrCreateSequenceType(factory.IntegerType) :
            factory.GetOrCreateReferenceType(factory.CreateIdentity(), "Object");
        var target = factory.CreateVariable("value", type);
        var instruction = builder.Allocate(entry, factory.CreateOperation(), type, target, array ? factory.Integer(1) : null);
        var layout = incompatible ? new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x86", 4) :
            new ResourceRuntimeLayoutContext("CoreCLR", "8.0.32", "x64", 8);
        var method = typeof(NativeResourceViolationReplay).GetMethod("ExactArrayBytes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.That(method, Is.Not.Null);
        var bytes = method!.Invoke(null, [factory, instruction, elements, layout, TestContext.CurrentContext.CancellationToken]);
        Assert.That(bytes, expected is { } amount ? Is.EqualTo(new System.Numerics.BigInteger(amount)) : Is.Null);
    }

    [Test]
    public async Task UnsupportedViolationMetricsCannotCreateARefutation()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.LoopBodyEntries,0)] public static void Target() {} }"));
        var claim = target.EffectClaims.Single();
        target = target with
        {
            EffectClaims = [claim with
            {
                Constraint = claim.Constraint with
                {
                    ResourceUpperBound = claim.Constraint.ResourceUpperBound! with { Metric = (WorkerResourceMetric)999 }
                }
            }]
        };
        Assert.That(await FindBoundaryViolation(target, ResourceExpression.Zero), Is.Null);
    }

    [Test]
    public async Task ByteViolationSearchRequiresOwnedLayoutEvidence()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedBytes,0)] public static void Target() {var value=new int[1];} }"));
        Assert.That(await FindBoundaryViolation(target, ResourceExpression.Zero), Is.Null);
    }

    [Test]
    public async Task LifetimeSearchDoesNotUseAnUnprovedParametricMemoryEnvelope()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.RetainedBytes,0)] public static int[] Target(int n) => new int[n]; }",
            reviewedRuntime: true, x64: true));
        Assert.That(await FindBoundaryViolation(target, ResourceExpression.Zero), Is.Null);
    }

    [Test]
    public async Task SearchHonorsTheEntryExpressionBudgetBeforeSamplingOrSolving()
    {
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,0)] public static void Target(int n) " +
            "{Contract.Requires(n>=0);var value=new int[1];} }"));
        Assert.That(await FindBoundaryViolation(target, ResourceExpression.Zero, new WorkerBudgets { MaximumExpressionDepth = 1 }), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ThresholdAndSolverCandidatesMustMeetEveryEntryConstraint(bool distinct)
    {
        var parameters = distinct ? "int left,int right" : "int left";
        var requires = distinct ? "left==7&&right==9" : "left==7";
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.ArrayAllocations,6)] public static void Target(" + parameters + ") " +
            "{Contract.Requires(" + requires + ");for(int i=0;i<left;i++){var value=new int[1];}} }"));
        var result = await FindBoundaryViolation(target, ResourceExpression.Literal(6));
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("7"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("6"));
        Assert.That(result.EffectWitness!.Location!.Line, Is.Positive);
        Assert.That(result.Model.Single(static value => value is null ? throw new ArgumentNullException(nameof(value)) :
            value.Variable == "parameter:0").Value, Is.EqualTo("7"));
        if (distinct)
        {
            Assert.That(result.Model.Single(static value => value is null ? throw new ArgumentNullException(nameof(value)) :
                value.Variable == "parameter:1").Value, Is.EqualTo("9"));
        }
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task ReferenceSizeLimitsUseTheValidatedConcreteEntryValue(bool text, bool nonnull)
    {
        var type = text ? "string" : "int[]";
        var requires = nonnull ? "Contract.Requires(value!=null);" : "";
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,0,\"value.Length\",1)] public static void Target(" + type +
            " value) {" + requires + "var allocation=new int[1];} }"));
        var limit = ResourceExpression.InputSize(new ResourceInput(0,
            text ? ResourceInputKind.StringLength : ResourceInputKind.ArrayLength, "value", int.MaxValue));
        Assert.That(target.EffectClaims.Single().Constraint.ResourceUpperBound!.Metric, Is.EqualTo(WorkerResourceMetric.AllocatedElements));
        var candidate = PassiveCallableArtifactAdapter.Enroll(target)!;
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out _, out var failure, TestContext.CurrentContext.CancellationToken),
            Is.True, failure.ToString());
        var result = await FindBoundaryViolation(target, limit);
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Outcome, Is.EqualTo(WorkerClaimOutcome.Refuted));
        Assert.That(result.ResourceBound!.ObservedLowerBound, Is.EqualTo("1"));
        Assert.That(result.ResourceBound.EvaluatedUpperBound, Is.EqualTo("0"));
        Assert.That(result.EffectWitness!.Location!.Line, Is.Positive);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReferenceSizeSearchRejectsCandidatesThatViolateTheMinimumLength(bool text)
    {
        var type = text ? "string" : "int[]";
        var target = Read(CreateArtifact("using SharpProof.Attributes; public static class C { " +
            "[ResourceUpperBound(ResourceMetric.AllocatedElements,0,\"value.Length\",1)] public static void Target(" + type +
            " value) {Contract.Requires(value!=null&&value.Length>=1);var allocation=new int[1];} }"));
        var limit = ResourceExpression.InputSize(new ResourceInput(0,
            text ? ResourceInputKind.StringLength : ResourceInputKind.ArrayLength, "value", int.MaxValue));
        Assert.That(target.EffectClaims.Single().Constraint.ResourceUpperBound!.Metric, Is.EqualTo(WorkerResourceMetric.AllocatedElements));
        Assert.That(await FindBoundaryViolation(target, limit), Is.Null);
    }

    private static Task<WorkerClaimResult?> FindBoundaryViolation(CompilerCallablePreparation target, ResourceExpression limit,
        WorkerBudgets? budgets = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(limit);
        return NativeResourceViolationReplay.FindAsync(target, target.EffectClaims.Single(), limit, budgets ?? new WorkerBudgets(),
            TestContext.CurrentContext.CancellationToken);
    }
}
