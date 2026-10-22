using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

internal sealed partial class NativeResourceContractTests
{
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
