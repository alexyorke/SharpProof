using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class NativeExceptionEffectTests
{
    [TestCase("System.ArgumentException", true)]
    [TestCase("System.SystemException", true)]
    [TestCase("System.ArithmeticException", false)]
    public async Task InstanceDelegateFaultUsesOwnedDeclaredExceptionConstraints(string allowed, bool proven)
    {
        var source = "using SharpProof.Attributes; public class Receiver { public void Sink() {} } public static class C { " +
            "[AllowedExceptions(typeof(" + allowed + "))] public static System.Action Target(Receiver instance) { " +
            "return new System.Action(instance.Sink); } }";
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.Total!.ExceptionConstraints.Single().AllowedKinds.Contains(IrExceptionKind.Argument), Is.EqualTo(proven));
        var result = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(result.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        if (!proven)
        { Assert.That(result.ExceptionWitness!.Kind, Is.EqualTo(IrExceptionKind.Argument)); }
    }

    [TestCase("return x;", true)]
    [TestCase("return 10 / x;", false)]
    [TestCase("try { return checked(x + 1); } catch (System.OverflowException) { return 0; }", true)]
    [TestCase("Contract.Requires(x == 0); return 10 / x;", false)]
    [TestCase("Contract.Requires(x > 0 && x < 0); return 10 / x;", true)]
    public async Task SerializedEffectOnlyCallableQualifiesWithoutPostconditions(string body, bool proven)
    {
        var source = "using SharpProof.Attributes; public static class C { [DoesNotThrow] public static int Target(int x) { " + body + " } }";
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(CompilerTotalCallableArtifactTests.CreateArtifact(source));
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single();
        Assert.That(preparation.EffectClaims.Single().ContractKind, Is.EqualTo(WorkerEffectContractKind.DoesNotThrow));
        var native = await NativeExceptionEffectVerifier.VerifyAsync(preparation, new WorkerBudgets());
        Assert.That(native.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), native.Reason.ToString());
        if (preparation.EffectClaims.Single().Outcome == WorkerClaimOutcome.Proven)
        { Assert.That(native.Outcome, Is.TypeOf<ProvenOutcome>(), "Retain inherited compiler proofs."); }
        if (native.Outcome is RefutedOutcome)
        {
            using var image = new MemoryStream();
            var compilation = TestCompilation.Create("NativeEffectRuntime", source);
            Assert.That(compilation.Emit(image).Success, Is.True);
            image.Position = 0;
            var context = new System.Runtime.Loader.AssemblyLoadContext("NativeEffectOracle", isCollectible: true);
            try
            {
                var method = context.LoadFromStream(image).GetType("C")!.GetMethod("Target")!;
                var input = (int)native.EntryModel.Values.Single().IntegerNumericValue;
                var thrown = Assert.Throws<System.Reflection.TargetInvocationException>(new Action(() => method.Invoke(null, [input])));
                var candidate = PassiveCallableArtifactAdapter.Enroll(preparation)!;
                Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _), Is.True);
                var replay = plan!.ReplayException(native.EntryModel, CancellationToken.None);
                var expected = replay.Exception!.Kind == IrExceptionKind.Overflow ? typeof(OverflowException) : typeof(DivideByZeroException);
                Assert.That(thrown!.InnerException, Is.TypeOf(expected));
            }
            finally { context.Unload(); }
        }
    }

    [TestCase("return 10 / x;", false)]
    [TestCase("return x == 0 ? 0 : 10 / x;", true)]
    [TestCase("try { return 10 / x; } catch (System.DivideByZeroException) { return 0; }", true)]
    [TestCase("return checked(x + 1);", false)]
    [TestCase("while (x > 0) { x--; } return x;", true)]
    [TestCase("while (x > 0) { x--; } return 10 / x;", false)]
    public async Task SourceExceptionReachabilityUsesUncaughtExits(string body, bool proven)
    {
        var subject = PassiveSourceSubject.Create("class C { public static int Target(int x) { " + body + " } }");
        var candidate = subject.Enroll();
        Assert.That(candidate, Is.Not.Null);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate!, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyExceptionsAsync([]);
        Assert.That(result.Outcome, proven ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        if (!proven)
        {
            var replay = plan!.ReplayException(result.EntryModel, CancellationToken.None);
            Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Exception));
            Assert.That(replay.ConsumedApproximation, Is.False);
            Assert.That(result.ExceptionWitness?.Kind, Is.EqualTo(replay.Exception!.Kind));
            Assert.That(result.ExceptionWitness?.Site, Is.EqualTo(replay.Exception.Site));
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public async Task SharedExitPreservesKindAndPathCorrelation(bool allowOverflow, bool allowDivision)
    {
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var builder = new IrProgramBuilder(factory);
        var parameter = new PassiveParameterBinding(factory.CreateVariable("entry", factory.BooleanType),
            factory.CreateVariable("current", factory.BooleanType), factory.CreateVariable("old", factory.BooleanType));
        var site = factory.CreateOperation("joined-exceptions");
        var entry = builder.CreateBlock();
        var overflow = builder.CreateBlock();
        var division = builder.CreateBlock();
        var exit = builder.CreateBlock();
        builder.Branch(entry, site, factory.Variable(parameter.Current), overflow, division);
        builder.Throw(overflow, site, IrExceptionKind.Overflow, exit);
        builder.Throw(division, site, IrExceptionKind.DivideByZero, exit);
        builder.ExceptionalExit(exit, site);
        var candidate = new PassiveCallableCandidate("joined", builder.Build(), [parameter], null, [], []);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
        var allowed = ImmutableHashSet.CreateBuilder<IrExceptionKind>();
        if (allowOverflow)
        { allowed.Add(IrExceptionKind.Overflow); }
        if (allowDivision)
        { allowed.Add(IrExceptionKind.DivideByZero); }
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyExceptionsAsync(allowed.ToImmutable());
        Assert.That(result.Outcome, allowOverflow && allowDivision ? Is.TypeOf<ProvenOutcome>() : Is.TypeOf<RefutedOutcome>(), result.Reason.ToString());
        if (result.Outcome is RefutedOutcome)
        {
            var replay = plan!.ReplayException(result.EntryModel, CancellationToken.None);
            Assert.That(allowed.Contains(replay.Exception!.Kind), Is.False);
        }
    }

    [Test]
    public async Task AbstractionCannotProveAnOmittedCallEffect()
    {
        var subject = PassiveSourceSubject.Create("class C { public static int Target(int x) { return x; } }").Enroll()!;
        var candidate = new PassiveCallableCandidate(subject.CallableId, subject.Program, subject.Parameters,
            subject.Result, subject.Requires, subject.Ensures, isBodyAbstraction: true);
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out _), Is.True);
        using var solver = new PassiveCallableSolver(plan!);
        Assert.That((await solver.VerifyExceptionsAsync([])).Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        Assert.Throws<ArgumentException>(new Action(() => plan!.ExceptionQuery(ImmutableHashSet.Create((IrExceptionKind)99))));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ExceptionReplayRejectsOnlyConsumedApproximation(bool consumed)
    {
        var subject = new PassiveCallableVcTests.ScalarSubject();
        var entry = subject.Builder.CreateBlock();
        var thrown = subject.Builder.CreateBlock();
        var returned = subject.Builder.CreateBlock();
        var exit = subject.Builder.CreateBlock();
        subject.Builder.Havoc(entry, subject.Site, IrHavocKind.Variables, IrHavocOrigin.Approximation, subject.Parameter.Current);
        if (consumed)
        {
            subject.Builder.Branch(entry, subject.Site,
                subject.Factory.Binary(IrBinaryOperator.Equal, subject.Factory.Variable(subject.Parameter.Current), subject.Factory.Integer(0)), thrown, returned);
        }
        else
        { subject.Builder.Goto(entry, subject.Site, thrown); }
        subject.Builder.Throw(thrown, subject.Site, IrExceptionKind.Overflow, exit);
        subject.Builder.Return(returned, subject.Site, subject.Factory.Integer(0));
        subject.Builder.ExceptionalExit(exit, subject.Site);
        var candidate = subject.Candidate(subject.Factory.Boolean(true));
        Assert.That(PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure), Is.True, failure.ToString());
        using var solver = new PassiveCallableSolver(plan!);
        var result = await solver.VerifyExceptionsAsync([]);
        if (consumed)
        { Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.CounterexampleNotReplayable)); }
        else
        { Assert.That(result.Outcome, Is.TypeOf<RefutedOutcome>()); }
    }
}
