using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeFieldReferenceViewTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PublicFieldStoresRespectReferenceViewAndObjectIdentity(bool upcastStoreView)
    {
        var scenario = CreateScenario(upcastStoreView);
        var execution = new IrProgramInterpreter(scenario.Factory).Execute(scenario.Candidate.Program,
            scenario.InitialValues);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
            Assert.That(execution.GetCurrentValue(scenario.Observed)?.Integer, Is.EqualTo(1));
            Assert.That(execution.ConsumedApproximation, Is.False);
            Assert.That(execution.Unsupported, Is.Null);
            Assert.That(execution.Exception, Is.Null);
        }

        var built = PassiveCallableVcBuilder.TryBuild(scenario.Candidate, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (upcastStoreView)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(built, Is.False);
                Assert.That(plan, Is.Null);
                Assert.That(failure, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
            }
            return;
        }

        Assert.That(built, Is.True, failure.ToString());
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.None));
        using var solver = new PassiveCallableSolver(plan!);
        var feasibility = await solver.VerifyFeasibilityAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(feasibility.Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible));

        var current = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var old = await solver.VerifyEnsuresAsync(1, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(current.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(current.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(old.Outcome, Is.TypeOf<ProvenOutcome>());
            Assert.That(old.Reason, Is.EqualTo(WorkerClaimReason.None));
            Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0));
        }
    }

    [TestCaseSource(nameof(AdditionalPrimitiveCases))]
    public async Task PublicFieldStoresPreserveTypedPrimitiveSnapshots(string typeName, bool upcastStoreView)
    {
        var scenario = CreateTypedScenario(typeName, upcastStoreView);
        var execution = new IrProgramInterpreter(scenario.Factory).Execute(scenario.Candidate.Program,
            scenario.InitialValues);
        var observed = execution.GetCurrentValue(scenario.Observed)
            ?? throw new InvalidOperationException("The typed field read did not populate its observation local.");
        using (Assert.EnterMultipleScope())
        {
            Assert.That(execution.Status, Is.EqualTo(IrProgramExecutionStatus.Returned), typeName);
            Assert.That(observed.Type, Is.EqualTo(scenario.FieldType), typeName);
            Assert.That(execution.ConsumedApproximation, Is.False, typeName);
            Assert.That(execution.Unsupported, Is.Null, typeName);
            Assert.That(execution.Exception, Is.Null, typeName);
            if (scenario.IsBoolean)
            {
                Assert.That(observed.Kind, Is.EqualTo(IrValueKind.Boolean), typeName);
                Assert.That(observed.Boolean, Is.True, typeName);
            }
            else
            {
                Assert.That(observed.Kind, Is.EqualTo(IrValueKind.Integer), typeName);
                Assert.That(observed.IntegerWidth, Is.EqualTo(scenario.IntegerWidth), typeName);
                Assert.That(observed.IntegerSigned, Is.EqualTo(scenario.IntegerSigned), typeName);
                Assert.That(observed.IntegerNumericValue, Is.EqualTo(System.Numerics.BigInteger.One), typeName);
            }
        }

        var built = PassiveCallableVcBuilder.TryBuild(scenario.Candidate, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken);
        if (upcastStoreView)
        {
            using (Assert.EnterMultipleScope())
            {
                Assert.That(built, Is.False, typeName);
                Assert.That(plan, Is.Null, typeName);
                Assert.That(failure, Is.EqualTo(WorkerClaimReason.UnsupportedBody), typeName);
            }
            return;
        }

        Assert.That(built, Is.True, failure.ToString());
        Assert.That(failure, Is.EqualTo(WorkerClaimReason.None), typeName);
        using var solver = new PassiveCallableSolver(plan!);
        var feasibility = await solver.VerifyFeasibilityAsync(cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(feasibility.Kind, Is.EqualTo(PassiveCallableFeasibilityKind.Feasible), typeName);
        var current = await solver.VerifyEnsuresAsync(0, cancellationToken: TestContext.CurrentContext.CancellationToken);
        var old = await solver.VerifyEnsuresAsync(1, cancellationToken: TestContext.CurrentContext.CancellationToken);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(current.Outcome, Is.TypeOf<ProvenOutcome>(), typeName);
            Assert.That(current.Reason, Is.EqualTo(WorkerClaimReason.None), typeName);
            Assert.That(old.Outcome, Is.TypeOf<ProvenOutcome>(), typeName);
            Assert.That(old.Reason, Is.EqualTo(WorkerClaimReason.None), typeName);
            Assert.That(solver.ConsumedResourceCount, Is.GreaterThan(0), typeName);
        }
    }

    private static IEnumerable<TestCaseData> AdditionalPrimitiveCases()
    {
        foreach (var typeName in new[] { "Boolean", "Int8", "UInt8", "Int16", "UInt16", "UInt32", "Int64", "UInt64" })
        {
            foreach (var upcastStoreView in new[] { false, true })
            {
                var viewName = upcastStoreView ? "ObjectUpcast" : "SameView";
                yield return new TestCaseData(typeName, upcastStoreView)
                    .SetName("PublicFieldStoresPreserveTypedPrimitiveSnapshots_" + typeName + "_" + viewName);
            }
        }
    }

    private static TypedScenario CreateTypedScenario(string typeName, bool upcastStoreView)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var (fieldType, isBoolean, width, signed) = typeName switch
        {
            "Boolean" => (factory.BooleanType, true, 0, false),
            "Int8" => (factory.GetOrCreateIntegerType(8, signed: true), false, 8, true),
            "UInt8" => (factory.GetOrCreateIntegerType(8, signed: false), false, 8, false),
            "Int16" => (factory.GetOrCreateIntegerType(16, signed: true), false, 16, true),
            "UInt16" => (factory.GetOrCreateIntegerType(16, signed: false), false, 16, false),
            "UInt32" => (factory.GetOrCreateIntegerType(32, signed: false), false, 32, false),
            "Int64" => (factory.GetOrCreateIntegerType(64, signed: true), false, 64, true),
            "UInt64" => (factory.GetOrCreateIntegerType(64, signed: false), false, 64, false),
            _ => throw new InvalidOperationException("Undefined field value type.")
        };
        IrValue initialValue = isBoolean ? factory.CreateBooleanValue(false)
            : signed ? factory.CreateIntegerValue(fieldType, 0L)
            : factory.CreateIntegerValue(fieldType, 0UL);
        IrTerm initialTerm = isBoolean ? factory.Boolean(false)
            : signed ? factory.Integer(fieldType, 0L)
            : factory.Integer(fieldType, 0UL);
        IrTerm storedTerm = isBoolean ? factory.Boolean(true)
            : signed ? factory.Integer(fieldType, 1L)
            : factory.Integer(fieldType, 1UL);
        var boxType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "BoxRef");
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), boxType, "field:Value",
            fieldType, isStatic: false);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("owner:entry", boxType),
            factory.CreateVariable("owner:current", boxType), factory.CreateVariable("owner:old", boxType));
        var site = factory.CreateOperation("typed-field-view-write");
        IrTerm Read(IrVarId owner)
        { return factory.PureOpaque(field, factory.Variable(owner)); }
        IrTerm Equal(IrTerm left, IrTerm right)
        { return factory.Binary(IrBinaryOperator.Equal, left, right); }

        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var target = upcastStoreView
            ? factory.Cast(factory.ObjectType, factory.Variable(parameter.Current))
            : factory.Variable(parameter.Current);
        builder.FieldStore(block, site, IrWriteRegion.Field, target, field, storedTerm);
        var observed = factory.CreateVariable("observed", fieldType);
        builder.Assign(block, site, observed, Read(parameter.Current));
        builder.Return(block, site);
        var requires = new[]
        {
            new PassiveContractClause(factory.Binary(IrBinaryOperator.NotEqual,
                factory.Variable(parameter.Entry), factory.Null(boxType)), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Entry), initialTerm), factory.Boolean(true), site)
        };
        var ensures = new[]
        {
            new PassiveContractClause(Equal(Read(parameter.Current), storedTerm), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Old), initialTerm), factory.Boolean(true), site)
        };
        var candidate = new PassiveCallableCandidate("field-reference-view-" + typeName, builder.Build(), [parameter], null,
            [.. requires], [.. ensures]);
        var owner = factory.CreateReferenceValue(boxType, new IrObjectState().WithField(field, initialValue));
        var inputs = new Dictionary<IrVarId, IrValue>
        {
            [parameter.Entry] = owner,
            [parameter.Current] = owner,
            [parameter.Old] = owner
        };
        return new(factory, candidate, observed, fieldType, isBoolean, width, signed, inputs);
    }

    private static Scenario CreateScenario(bool upcastStoreView)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var boxType = factory.GetOrCreateReferenceType(factory.CreateIdentity(), "BoxRef");
        var field = factory.GetOrCreateMember(factory.CreateIdentity(), boxType, "field:Value",
            factory.IntegerType, isStatic: false);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("owner:entry", boxType),
            factory.CreateVariable("owner:current", boxType), factory.CreateVariable("owner:old", boxType));
        var site = factory.CreateOperation("field-view-write");
        IrTerm Read(IrVarId owner)
        { return factory.PureOpaque(field, factory.Variable(owner)); }
        IrTerm Equal(IrTerm left, IrTerm right)
        { return factory.Binary(IrBinaryOperator.Equal, left, right); }

        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var target = upcastStoreView
            ? factory.Cast(factory.ObjectType, factory.Variable(parameter.Current))
            : factory.Variable(parameter.Current);
        builder.FieldStore(block, site, IrWriteRegion.Field, target, field, factory.Integer(1));
        var observed = factory.CreateVariable("observed", factory.IntegerType);
        builder.Assign(block, site, observed, Read(parameter.Current));
        builder.Return(block, site);
        var requires = new[]
        {
            new PassiveContractClause(factory.Binary(IrBinaryOperator.NotEqual,
                factory.Variable(parameter.Entry), factory.Null(boxType)), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Entry), factory.Integer(0)), factory.Boolean(true), site)
        };
        var ensures = new[]
        {
            new PassiveContractClause(Equal(Read(parameter.Current), factory.Integer(1)), factory.Boolean(true), site),
            new PassiveContractClause(Equal(Read(parameter.Old), factory.Integer(0)), factory.Boolean(true), site)
        };
        var candidate = new PassiveCallableCandidate("field-reference-view", builder.Build(), [parameter], null,
            [.. requires], [.. ensures]);
        var objectState = new IrObjectState().WithField(field, factory.CreateIntegerValue(0));
        var owner = factory.CreateReferenceValue(boxType, objectState);
        var inputs = new Dictionary<IrVarId, IrValue>
        {
            [parameter.Entry] = owner,
            [parameter.Current] = owner,
            [parameter.Old] = owner
        };
        return new(factory, candidate, observed, inputs);
    }

    private sealed record Scenario(IrFactory Factory, PassiveCallableCandidate Candidate, IrVarId Observed,
        IReadOnlyDictionary<IrVarId, IrValue> InitialValues);

    private sealed record TypedScenario(IrFactory Factory, PassiveCallableCandidate Candidate, IrVarId Observed,
        IrTypeId FieldType, bool IsBoolean, int IntegerWidth, bool IntegerSigned,
        IReadOnlyDictionary<IrVarId, IrValue> InitialValues);
}
