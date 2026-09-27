using static SharpProof.Ir.IrTermAnalysis;
using static SharpProof.Worker.PostconditionObligationBuilder;

namespace SharpProof.Worker;

// Decides [DoesNotThrow] and [AllowedExceptions] with Z3. Both hold when
// every reachable operation completes normally. A refutation counts only when
// the concrete program, run on the model's inputs, throws an exception the
// claim does not allow.
internal sealed class CallableEffectVerifier(
    ProofKernel kernel,
    AcyclicBlockPredicateExecutor executor,
    int maximumExpressionDepth)
{
    private const int MaximumLoopReplaySteps = 1_000_000;

    internal async Task<ImmutableDictionary<string, WorkerClaimResult>> VerifyAsync(
        CompilerCallablePreparation target,
        MethodResourceBudget resourceBudget,
        CallableEntryFeasibility entryFeasibility,
        CancellationToken cancellationToken)
    {
        // A compiler refutation already carries a replayed witness.
        var claims = target.EffectClaims
            .Where(static claim => claim.Outcome != WorkerClaimOutcome.Refuted &&
                claim.ContractKind is
                    WorkerEffectContractKind.DoesNotThrow or WorkerEffectContractKind.AllowedExceptions)
            .ToArray();
        if (claims.Length == 0 ||
            entryFeasibility.IsUnknown ||
            entryFeasibility.IsContradictory ||
            target.Body is not { Kind: CompilerPreparedBodyKind.Program, Program: { } program } prepared)
        {
            return ImmutableDictionary<string, WorkerClaimResult>.Empty;
        }

        var factory = target.Factory;
        var body = executor.Execute(
            target.Variables, factory, program, prepared.SpecCalls, prepared.SummaryCalls,
            prepared.ParameterBindings.ToImmutableDictionary(
                static item => item.Key, item => (IrTerm)factory.Variable(item.Value)),
            prepared.ParameterBindings,
            cancellationToken);
        if (!body.IsSuccess || body.NoThrow is not { } noThrow)
        {
            return ImmutableDictionary<string, WorkerClaimResult>.Empty;
        }

        var evidence = CallableEvidenceBuilder.Build(
            target, body, maximumExpressionDepth, cancellationToken);
        if (!evidence.IsSuccess ||
            GetDepth(noThrow) > maximumExpressionDepth ||
            !IsSupportedProofDomain(factory, noThrow) ||
            !resourceBudget.TryStartQuery())
        {
            return ImmutableDictionary<string, WorkerClaimResult>.Empty;
        }

        // Only entry facts and spec/user assumptions: the postcondition
        // evidence also assumes normal completion and defined return values,
        // which would make "no throw" hold vacuously.
        var entry = evidence.Evidence!;
        var assumptions = entry.Assumptions.Where(assumption =>
            entry.Preconditions.Contains(assumption) ||
            entry.EntryDomainAssumptions.Contains(assumption) ||
            assumption.Justification is SpecJustification or UserAssumedJustification);
        var query = new VerificationQuery(
            factory,
            assumptions,
            new Goal(factory, noThrow, ProofDiagnosticKind.EffectContract, new SourceLocationId(0)),
            entry.ReplayVariables);
        var outcome = await kernel.VerifyAsync(query, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (resourceBudget.IsExceeded)
        {
            return ImmutableDictionary<string, WorkerClaimResult>.Empty;
        }

        var results = ImmutableDictionary.CreateBuilder<string, WorkerClaimResult>(StringComparer.Ordinal);
        foreach (var claim in claims)
        {
            switch (outcome)
            {
                case ProvenOutcome proven:
                    var proofCore = CallableProofCore.Create(proven, entry.AssumptionLabels);
                    var provenResult = CallableClaimResultAssembler.Create(
                        target,
                        claim.ClaimId,
                        WorkerClaimOutcome.Proven,
                        WorkerClaimReason.None,
                        WorkerEffectEvidenceCertainty.CompleteMayEffectSummary);
                    provenResult.ProofCore = CallableProofCore.Merge(proofCore, ["z3:does-not-throw"]);
                    results[claim.ClaimId] = provenResult;
                    break;
                case RefutedOutcome refuted when
                    Replay(target, program, prepared, refuted, cancellationToken) is { } replayed &&
                    Violates(claim, replayed.Exception) &&
                    replayed.Witness is { } witness:
                    var refutedResult = CallableClaimResultAssembler.Create(
                        target,
                        claim.ClaimId,
                        WorkerClaimOutcome.Refuted,
                        WorkerClaimReason.None,
                        WorkerEffectEvidenceCertainty.DefiniteViolation,
                        projectAssumptions: false);
                    refutedResult.EffectWitness = witness;
                    results[claim.ClaimId] = refutedResult;
                    break;
            }
        }
        return results.ToImmutable();
    }

    // Implicit exceptions are fixed framework types, so their hierarchies are
    // known. An explicit throw's type is not in the IR.
    private static readonly ImmutableDictionary<IrExceptionKind, string[]> Hierarchies =
        new Dictionary<IrExceptionKind, string[]>
        {
            [IrExceptionKind.DivideByZero] = ["System.DivideByZeroException", "System.ArithmeticException"],
            [IrExceptionKind.Overflow] = ["System.OverflowException", "System.ArithmeticException"],
            [IrExceptionKind.NullReference] = ["System.NullReferenceException"],
            [IrExceptionKind.IndexOutOfRange] = ["System.IndexOutOfRangeException"],
            [IrExceptionKind.InvalidCast] = ["System.InvalidCastException"]
        }.ToImmutableDictionary();

    private static bool Violates(CompilerEffectClaimArtifact claim, IrExceptionKind? exception)
    {
        if (claim.ContractKind == WorkerEffectContractKind.DoesNotThrow)
        {
            return true;
        }

        return exception is { } kind &&
            !Hierarchies[kind].Concat(["System.SystemException", "System.Exception", "System.Object"])
                .Any(name => claim.Constraint.AllowedExceptionTypes.Any(allowed =>
                    allowed.EndsWith("::T:" + name, StringComparison.Ordinal)));
    }

    private static (WorkerEffectViolationWitness? Witness, IrExceptionKind? Exception)? Replay(
        CompilerCallablePreparation target,
        IrProgram program,
        CompilerPreparedBody prepared,
        RefutedOutcome refuted,
        CancellationToken cancellationToken)
    {
        var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
        foreach (var binding in prepared.ParameterBindings)
        {
            if (!refuted.Model.Assignments.TryGetValue(binding.Value, out var value))
            {
                return null;
            }

            initial[binding.Key] = value;
        }

        var hasLoops = IrBlockOrder.TryCutLoops(program, static _ => true, out _)
            is { BackEdges.IsEmpty: false };
        var execution = new IrProgramInterpreter(target.Factory).Execute(
            program,
            initial.ToImmutable(),
            hasLoops
                ? MaximumLoopReplaySteps
                : program.Blocks.Sum(static block => block.Instructions.Length),
            (call, receiver, arguments) =>
                CallableCounterexampleReplayer.ReplayRegisteredSpecCall(target, call, receiver, arguments),
            cancellationToken);
        var detail = execution switch
        {
            { Status: IrProgramExecutionStatus.Exception, Exception: { } exception } =>
                "System." + exception.Kind + "Exception",
            { Status: IrProgramExecutionStatus.AssumptionViolated } => "explicit throw",
            _ => null
        };
        if (detail == null)
        {
            return null;
        }

        var location = target.Entry.Location;
        var witness = new WorkerEffectViolationWitness
        {
            Kind = execution.Status == IrProgramExecutionStatus.Exception
                ? "implicit-exception"
                : "explicit-throw",
            Detail = detail,
            Effects = WorkerEffectSet.Throws,
            Location = new WorkerSourceLocation
            {
                Path = location.Path,
                Start = location.Start,
                Length = location.Length,
                Line = location.Line,
                Column = location.Column
            }
        };
        return (witness, execution.Exception?.Kind);
    }
}
