using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Smt;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeOpaqueTermAdmissionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task LegalOpaquePredicateRemainsOutsideTheNativeBranchDomain(bool opaque)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var member = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "ExternalPredicate", factory.BooleanType, isStatic: true);
        IrTerm predicate = opaque ? factory.PureOpaque(member, receiver: null) : factory.Boolean(true);
        var builder = new IrProgramBuilder(factory);
        var entry = builder.CreateBlock("entry");
        var whenTrue = builder.CreateBlock("true");
        var whenFalse = builder.CreateBlock("false");
        var site = factory.CreateOperation("opaque-term-admission");
        builder.Branch(entry, site, predicate, whenTrue, whenFalse);
        builder.Return(whenTrue, site);
        builder.Return(whenFalse, site);
        var candidate = new PassiveCallableCandidate("opaque-term-admission", builder.Build(), [], null, [], []);
        var accepted = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted, Is.EqualTo(!opaque));
            Assert.That(plan == null, Is.EqualTo(opaque));
            Assert.That(failure, Is.EqualTo(opaque ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
        }
        var result = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate,
            WorkerEffectCapabilitySet.None, new WorkerBudgets());
        Assert.That(result.Outcome, opaque ? Is.Null : Is.TypeOf<ProvenOutcome>());
        Assert.That(result.Reason, Is.EqualTo(opaque ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
        // The maintained native backend independently rejects unsupported opaque
        // goals; this is a module admission test, not a source proof claim.
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(new VerificationQuery(factory, [],
            new Goal(factory, predicate, ProofDiagnosticKind.Precondition, new SourceLocationId(site.Value))));
        if (opaque)
        {
            Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(AbstentionReason.UnsupportedEncoding));
        }
        else
        { Assert.That(outcome, Is.TypeOf<ProvenOutcome>()); }
    }

    [TestCase("assume", false)]
    [TestCase("assume", true)]
    [TestCase("allocation", false)]
    [TestCase("allocation", true)]
    [TestCase("lock", false)]
    [TestCase("lock", true)]
    [TestCase("store", false)]
    [TestCase("store", true)]
    [TestCase("argument", false)]
    [TestCase("argument", true)]
    [TestCase("assignment", false)]
    [TestCase("assignment", true)]
    [TestCase("return", false)]
    [TestCase("return", true)]
    public async Task LegalExternalTermsHaveConsistentAdmissionAcrossOwnedInstructionOperands(string operand, bool opaque)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var termType = operand == "assume" ? factory.BooleanType
            : operand == "lock" ? factory.ObjectType : factory.IntegerType;
        var external = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
            "ExternalValue", termType, isStatic: true);
        var unsupported = factory.PureOpaque(external, receiver: null);
        var builder = new IrProgramBuilder(factory);
        var block = builder.CreateBlock("entry");
        var site = factory.CreateOperation("external-operand-admission");
        IrVarId? resultVariable = null;
        IrTerm? returnValue = null;
        IrTerm ordinary = operand == "assume" ? factory.Boolean(true) : factory.Integer(1);
        if (operand == "lock")
        {
            var receiver = factory.CreateVariable("receiver", factory.ObjectType);
            builder.Allocate(block, site, factory.ObjectType, receiver);
            ordinary = factory.Variable(receiver);
        }
        var expression = opaque ? unsupported : ordinary;
        switch (operand)
        {
            case "assume":
                builder.Assume(block, site, expression);
                break;
            case "allocation":
                var allocatedType = factory.GetOrCreateSequenceType(factory.IntegerType);
                var allocated = factory.CreateVariable("allocated", allocatedType);
                builder.Allocate(block, site, allocatedType, allocated, expression);
                break;
            case "lock":
                builder.Lock(block, site, expression);
                break;
            case "store":
                var storageType = factory.GetOrCreateSequenceType(factory.IntegerType);
                var storage = factory.CreateVariable("storage", storageType);
                builder.Allocate(block, site, storageType, storage, factory.Integer(1));
                // The fresh array is non-null and position0 is within its length1.
                builder.ElementStore(block, site, factory.Variable(storage), factory.Integer(0), expression);
                break;
            case "argument":
                var callee = factory.GetOrCreateMember(factory.CreateIdentity(), factory.ObjectType,
                    "ExternalCallee", factory.IntegerType, isStatic: true, [factory.IntegerType]);
                // No effect declaration is manufactured: an unspecified call
                // retains every may-effect, all permitted in this projection.
                builder.Call(block, site, null, callee, null, expression);
                break;
            case "assignment":
                var local = factory.CreateVariable("local", factory.IntegerType);
                builder.Assign(block, site, local, expression);
                break;
            case "return":
                resultVariable = factory.CreateVariable("result", factory.IntegerType);
                returnValue = expression;
                break;
            default:
                throw new InvalidOperationException("Undefined fixture operand.");
        }
        builder.Return(block, site, returnValue);
        var ensures = resultVariable is { } result
            ? new[] { new PassiveContractClause(factory.Binary(IrBinaryOperator.Equal, factory.Variable(result), factory.Integer(1)),
                factory.Boolean(true), site) }
            : [];
        var candidate = new PassiveCallableCandidate("external-operand-admission", builder.Build(), [], resultVariable, [], [.. ensures]);
        var accepted = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(accepted, Is.EqualTo(!opaque));
            Assert.That(plan == null, Is.EqualTo(opaque));
            Assert.That(failure, Is.EqualTo(opaque ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
        }
        var projection = await NativeEffectSiteVerifier.VerifyCapabilityProjectionAsync(candidate,
            WorkerEffectCapabilitySet.AllKnown, new WorkerBudgets());
        Assert.That(projection.Outcome, opaque ? Is.Null : Is.TypeOf<ProvenOutcome>());
        Assert.That(projection.Reason, Is.EqualTo(opaque ? WorkerClaimReason.UnsupportedBody : WorkerClaimReason.None));
        IrTerm goal = opaque ? termType == factory.BooleanType ? unsupported
            : termType == factory.ObjectType
                ? factory.Binary(IrBinaryOperator.NotEqual, unsupported, factory.Null(factory.ObjectType))
                : factory.Binary(IrBinaryOperator.Equal, unsupported, factory.Integer(1))
            : factory.Boolean(true);
        using var session = new CallableSolverSession(factory, new IrSmtBackendOptions());
        var outcome = await new ProofKernel(session).VerifyAsync(new VerificationQuery(factory, [],
            new Goal(factory, goal, ProofDiagnosticKind.Precondition, new SourceLocationId(site.Value))));
        if (opaque)
        {
            Assert.That(outcome, Is.TypeOf<UnknownOutcome>());
            Assert.That(((UnknownOutcome)outcome).Reason, Is.EqualTo(AbstentionReason.UnsupportedEncoding));
        }
        else
        { Assert.That(outcome, Is.TypeOf<ProvenOutcome>()); }
    }
}
