using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeFieldGoalAdmissionTests
{
    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public Task PostconditionFieldReadsDistinguishEntrySnapshotsAndStoreViews(bool upcastStoreView, bool oldSnapshot)
    { return VerifyGoalAsync("Int32", upcastStoreView, oldSnapshot); }

    [TestCaseSource(nameof(RemainingPrimitiveCases))]
    public Task PrimitivePostconditionFieldsPreserveSnapshotAndViewAdmission(string typeName, bool upcastStoreView, bool oldSnapshot)
    { return VerifyGoalAsync(typeName, upcastStoreView, oldSnapshot); }

    public static IEnumerable<TestCaseData> RemainingPrimitiveCases()
    {
        foreach (var type in new[] { "Boolean", "Int8", "UInt8", "Int16", "UInt16", "UInt32", "Int64", "UInt64" })
        {
            foreach (var upcast in new[] { false, true })
            {
                foreach (var old in new[] { false, true })
                { yield return new TestCaseData(type, upcast, old).SetName("FieldGoal_" + type + (upcast ? "_Object" : "_Nominal") + (old ? "_Old" : "_Current")); }
            }
        }
    }

    private static async Task VerifyGoalAsync(string typeName, bool upcastStoreView, bool oldSnapshot)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var fieldType = typeName switch
        {
            "Boolean" => factory.BooleanType,
            "Int8" => factory.GetOrCreateIntegerType(8, signed: true),
            "UInt8" => factory.GetOrCreateIntegerType(8, signed: false),
            "Int16" => factory.GetOrCreateIntegerType(16, signed: true),
            "UInt16" => factory.GetOrCreateIntegerType(16, signed: false),
            "Int32" => factory.IntegerType,
            "UInt32" => factory.GetOrCreateIntegerType(32, signed: false),
            "Int64" => factory.GetOrCreateIntegerType(64, signed: true),
            "UInt64" => factory.GetOrCreateIntegerType(64, signed: false),
            _ => throw new InvalidOperationException("Undefined primitive field type.")
        };
        var isBoolean = typeName == "Boolean";
        IrTerm initialTerm = isBoolean ? factory.Boolean(false) : factory.Integer(fieldType, 0L);
        IrTerm storedTerm = isBoolean ? factory.Boolean(true) : factory.Integer(fieldType, 1L);
        IrValue initialValue = isBoolean ? factory.CreateBooleanValue(false) : factory.CreateIntegerValue(fieldType, 0L);
        var boxType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "BoxRef");
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), boxType, "field:Value", fieldType, false);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("owner:entry", boxType),
            factory.CreateVariable("owner:current", boxType), factory.CreateVariable("owner:old", boxType));
        var site = factory.CreateOperation("postcondition-field-view-write");
        IrTerm Read(IrVarId owner)
        { return factory.PureOpaque(field, factory.Variable(owner)); }
        IrTerm Equal(IrTerm left, IrTerm right)
        { return factory.Binary(IrBinaryOperator.Equal, left, right); }
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var target = upcastStoreView ? factory.Cast(factory.ObjectType, factory.Variable(parameter.Current))
            : factory.Variable(parameter.Current);
        builder.FieldStore(block, site, IrWriteRegion.Field, target, field, storedTerm);
        builder.Return(block, site);
        var requires = new[]
        {
            new PassiveContractClause(factory.Binary(IrBinaryOperator.NotEqual,
                factory.Variable(parameter.Entry), factory.Null(boxType)), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Entry), initialTerm), factory.Boolean(true), site)
        };
        var ensures = new PassiveContractClause(Equal(Read(oldSnapshot ? parameter.Old : parameter.Current),
            oldSnapshot ? initialTerm : storedTerm), factory.Boolean(true), site);
        var candidate = new PassiveCallableCandidate("postcondition-field-view", builder.Build(), [parameter], null,
            [.. requires], [ensures]);

        var owner = factory.CreateReferenceValue(boxType, new IrObjectState().WithField(field, initialValue));
        var inputs = new Dictionary<IrVarId, IrValue>
        {
            [parameter.Entry] = owner,
            [parameter.Current] = owner,
            [parameter.Old] = owner
        };
        var execution = new IrProgramInterpreter(factory).Execute(candidate.Program, inputs);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.Unsupported, Is.Null);
            Assert.That(execution.Exception, Is.Null);
        }
        var built = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: CancellationToken.None);
        var supported = !upcastStoreView || oldSnapshot;
        using (Assert.EnterMultipleScope())
        {
            Assert.That(built, Is.EqualTo(supported));
            Assert.That(plan == null, Is.EqualTo(!supported));
            Assert.That(failure, Is.EqualTo(supported ? WorkerClaimReason.None : WorkerClaimReason.UnsupportedBody));
        }
        if (supported)
        {
            using var solver = new PassiveCallableSolver(plan!);
            Assert.That((await solver.VerifyFeasibilityAsync(cancellationToken: CancellationToken.None)).Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));
            var verified = await solver.VerifyEnsuresAsync(0, cancellationToken: CancellationToken.None);
            Assert.That(verified.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(verified.Reason, Is.EqualTo(WorkerClaimReason.None));
        }
    }
}
