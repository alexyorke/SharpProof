using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Verify;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class Pr275BulkWorkerApiTests
{
    private const string DisjointCallPreconditionSource = """
        using SharpProof.Attributes;
        public sealed class LeftInput { }
        public sealed class RightInput { }
        public static class Subject
        {
            public static int Target(LeftInput left, RightInput right, int value)
            {
                Contract.Ensures(Contract.Result<int>() == value);
                return Helper(value);
            }

            private static int Helper(int value)
            {
                Contract.Requires(value >= 0);
                return value;
            }
        }
        """;

    [Test]
    public void PreparedNestedSequenceResultIsRejectedByPassivePlanConstruction()
    {
        var candidate = CreatePreparedTargetCandidate("""
            using SharpProof.Attributes;
            public static class Subject
            {
                public static int[][] Target(int[][] values)
                {
                    Contract.Ensures(true);
                    return values;
                }
            }
            """);
        var resultType = candidate.Factory.GetTypeInfo(
            candidate.Factory.GetVariableInfo(candidate.Result!.Value).Type);
        Assert.That(resultType.Kind, Is.EqualTo(IrTypeKind.Sequence));
        Assert.That(resultType.ElementType, Is.Not.Null);
        Assert.That(candidate.Factory.GetTypeInfo(resultType.ElementType!.Value).Kind,
            Is.EqualTo(IrTypeKind.Sequence));

        AssertUnsupportedBody(candidate);
    }

    [Test]
    public void PreparedNestedSequenceParameterIsRejectedByPassivePlanConstruction()
    {
        var candidate = CreatePreparedTargetCandidate("""
            using SharpProof.Attributes;
            public static class Subject
            {
                public static int Target(int[][] values)
                {
                    Contract.Ensures(Contract.Result<int>() == 0);
                    return 0;
                }
            }
            """);
        var parameterType = candidate.Factory.GetTypeInfo(
            candidate.Factory.GetVariableInfo(candidate.Parameters.Single().Entry).Type);
        Assert.That(parameterType.Kind, Is.EqualTo(IrTypeKind.Sequence));
        Assert.That(parameterType.ElementType, Is.Not.Null);
        Assert.That(candidate.Factory.GetTypeInfo(parameterType.ElementType!.Value).Kind,
            Is.EqualTo(IrTypeKind.Sequence));

        AssertUnsupportedBody(candidate);
    }

    [Test]
    public void NestedSequenceLengthCannotDrivePassiveAllocationPlanning()
    {
        var candidate = CreatePreparedTargetCandidate("""
            using SharpProof.Attributes;
            public static class Subject
            {
                public static int Target()
                {
                    Contract.Ensures(Contract.Result<int>() == 0);
                    int[][] nested = new int[1][];
                    int[] values = new int[nested.Length];
                    return 0;
                }
            }
            """);
        var hasNestedLength = candidate.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrAllocationInstruction>()
            .Any(allocation => allocation.Length is { } length &&
                ContainsNestedSequence(candidate, length));
        Assert.That(hasNestedLength, Is.True);

        AssertUnsupportedBody(candidate);
    }

    [Test]
    public void NestedSequenceAssumptionCannotEnterPassivePlan()
    {
        var candidate = CreatePreparedTargetCandidate("""
            using SharpProof.Attributes;
            public static class Subject
            {
                public static int Target()
                {
                    Contract.Ensures(Contract.Result<int>() == 0);
                    int[][] nested = new int[1][];
                    Contract.Assume(nested.Length > 0);
                    return 0;
                }
            }
            """);
        var hasNestedAssumption = candidate.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrAssumeInstruction>()
            .Any(assume => ContainsNestedSequence(candidate, assume.Condition));
        Assert.That(hasNestedAssumption, Is.True);

        AssertUnsupportedBody(candidate);
    }

    [Test]
    public void StaticOpaqueCallWithNestedSequenceArgumentIsRejectedByPassivePlan()
    {
        var candidate = CreatePreparedTargetCandidate("""
            using SharpProof.Attributes;
            public static class Subject
            {
                public static int Target()
                {
                    Contract.Ensures(Contract.Result<int>() == 0);
                    System.GC.KeepAlive(new int[1][]);
                    return 0;
                }
            }
            """);
        var hasUnsupportedArgumentCall = candidate.Program.Blocks
            .SelectMany(static block => block.Instructions)
            .OfType<IrCallInstruction>()
            .Any(call => call.Receiver is null && call.Target is null &&
                call.Arguments.Any(argument => ContainsNestedSequence(candidate, argument)));
        Assert.That(hasUnsupportedArgumentCall, Is.True);

        AssertUnsupportedBody(candidate);
    }

    [Test]
    public void PreparedPlanPropertiesAndCallQueriesStayBoundToOwnedCallPreconditions()
    {
        var candidate = CreatePreparedCallPreconditionCandidate();
        Assert.That(candidate.CallPreconditions, Is.Not.Empty);
        var plan = BuildPlan(candidate);
        var queries = plan.CallPreconditionQueries(0);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(plan.Factory, Is.SameAs(candidate.Factory));
            Assert.That(plan.EnsuresCount, Is.EqualTo(candidate.Ensures.Length));
            Assert.That(plan.CallPreconditionCount,
                Is.EqualTo(candidate.CallPreconditions.Length));
            Assert.That(plan.CallableId, Is.EqualTo(candidate.CallableId));
            Assert.That(queries, Is.Not.Empty);
            Assert.That(queries.All(query =>
                query.Goal.Diagnostic == ProofDiagnosticKind.Precondition), Is.True);
        }
    }

    [Test]
    public void CallPreconditionQueriesRejectNegativeAndPastEndOrdinals()
    {
        var plan = BuildPlan(CreatePreparedCallPreconditionCandidate());

        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.CallPreconditionQueries(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.CallPreconditionQueries(plan.CallPreconditionCount)));
    }

    [Test]
    public void CallPreconditionReplayRejectsNegativeAndPastEndOrdinals()
    {
        var plan = BuildPlan(CreatePreparedCallPreconditionCandidate());
        var missingInputs = ImmutableDictionary<IrVarId, IrValue>.Empty;

        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.ReplayCallPrecondition(-1, missingInputs, CancellationToken.None)));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() =>
            plan.ReplayCallPrecondition(plan.CallPreconditionCount,
                missingInputs, CancellationToken.None)));
    }

    [Test]
    public void CallPreconditionReplayDeclinesWhenOwnedEntryConstraintsCannotBeChecked()
    {
        var candidate = CreatePreparedDisjointCallPreconditionCandidate();
        Assert.That(candidate.EntryConstraints, Is.Not.Empty);
        Assert.That(candidate.CallPreconditions, Is.Not.Empty);
        var plan = BuildPlan(candidate);

        var witness = plan.ReplayCallPrecondition(
            0,
            ImmutableDictionary<IrVarId, IrValue>.Empty,
            CancellationToken.None);

        Assert.That(witness, Is.Null);
    }

    private static PassiveCallableCandidate CreatePreparedTargetCandidate(string source)
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single(static item =>
            item.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(preparation.Total, Is.Not.Null, preparation.FailureReason.ToString());
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        return candidate!;
    }

    private static PassiveCallableCandidate CreatePreparedCallPreconditionCandidate()
    {
        var source = GoldenTest.Load("worker", "reachable-call-precondition-artifact").Source;
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(source);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single(static item =>
            item.Total is { CallPreconditions.Length: > 0 });
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        return candidate!;
    }

    private static PassiveCallableCandidate CreatePreparedDisjointCallPreconditionCandidate()
    {
        var artifact = CompilerTotalCallableArtifactTests.CreateArtifact(
            DisjointCallPreconditionSource);
        var json = CompilerManifestArtifactJson.SerializeProducerValidated(artifact);
        CompilerManifestArtifactJson.DeserializePrepared(json, out var preparations);
        var preparation = preparations.Single(static item =>
            item.Entry.CallableId.Contains("Target", StringComparison.Ordinal));
        Assert.That(preparation.Total, Is.Not.Null, preparation.FailureReason.ToString());
        Assert.That(preparation.Total!.CallPreconditions, Is.Not.Empty);
        Assert.That(preparation.Total.DisjointInputs, Is.Not.Empty);
        var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
        Assert.That(candidate, Is.Not.Null);
        return candidate!;
    }

    private static PassiveCallableVcPlan BuildPlan(PassiveCallableCandidate candidate)
    {
        var success = PassiveCallableVcBuilder.TryBuild(candidate,
            out var plan, out var failure);
        Assert.That(success, Is.True, failure.ToString());
        Assert.That(plan, Is.Not.Null);
        return plan!;
    }

    private static void AssertUnsupportedBody(PassiveCallableCandidate candidate)
    {
        var success = PassiveCallableVcBuilder.TryBuild(candidate,
            out var plan, out var failure);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(success, Is.False);
            Assert.That(plan, Is.Null);
            Assert.That(failure, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    private static bool ContainsNestedSequence(
        PassiveCallableCandidate candidate,
        IrTerm root)
    {
        var info = candidate.Factory.GetTypeInfo(root.Type);
        if (info.Kind == IrTypeKind.Sequence &&
            info.ElementType is { } element &&
            candidate.Factory.GetTypeInfo(element).Kind == IrTypeKind.Sequence)
        {
            return true;
        }
        return root switch
        {
            IrOpaqueTerm opaque =>
                opaque.Receiver is { } receiver && ContainsNestedSequence(candidate, receiver) ||
                opaque.Arguments.Any(argument => ContainsNestedSequence(candidate, argument)),
            IrUnaryTerm unary => ContainsNestedSequence(candidate, unary.Operand),
            IrBinaryTerm binary => ContainsNestedSequence(candidate, binary.Left) ||
                ContainsNestedSequence(candidate, binary.Right),
            IrConditionalTerm conditional => ContainsNestedSequence(candidate, conditional.Condition) ||
                ContainsNestedSequence(candidate, conditional.WhenTrue) ||
                ContainsNestedSequence(candidate, conditional.WhenFalse),
            IrCastTerm cast => ContainsNestedSequence(candidate, cast.Operand),
            IrLengthTerm length => ContainsNestedSequence(candidate, length.Value),
            IrSequenceAccessTerm access => ContainsNestedSequence(candidate, access.Sequence) ||
                ContainsNestedSequence(candidate, access.Index),
            _ => false
        };
    }
}
