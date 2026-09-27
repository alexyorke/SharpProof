using System.Collections.Immutable;
using System.Globalization;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;
namespace SharpProof.CompilerArtifact;

internal enum CompilerCallableReplayStatus
{
    Refuted,
    PostconditionUndefined,
    UnsupportedRegisteredCall,
    // The counterexample came from a loop-cut abstraction and the concrete
    // run does not reproduce it.
    NotReplayable,
    Failed
}

internal static class CompilerCallablePostconditionReplay
{
    private const int MaximumLoopReplaySteps = 1_000_000;

    internal static CompilerCallableReplayStatus Replay(
        CompilerCallablePreparation target,
        ImmutableDictionary<IrVarId, IrValue> model,
        IrTerm postcondition,
        bool rejectUnexpectedReturnValue,
        CancellationToken cancellationToken,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>,
            IrValue?>? callHost = null)
    {
        if (target.Body is not { } body)
        {
            return CompilerCallableReplayStatus.Failed;
        }

        var factory = target.Factory;
        var final = model.ToBuilder();
        var results = target.Variables.Where(static variable =>
            variable.Role == CompilerVariableRole.Result).ToArray();
        var hasLoops = false;
        if (body.Kind == CompilerPreparedBodyKind.Program)
        {
            if (body.Program is not { } program ||
                !ReferenceEquals(program.Factory, factory))
            {
                return CompilerCallableReplayStatus.Failed;
            }

            var initial = ImmutableDictionary.CreateBuilder<IrVarId, IrValue>();
            foreach (var binding in body.ParameterBindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!model.TryGetValue(binding.Value, out var value))
                {
                    return CompilerCallableReplayStatus.Failed;
                }

                initial[binding.Key] = value;
            }

            var maximumSteps = program.Blocks.Sum(
                static block => (long)block.Instructions.Length);
            if (maximumSteps is < 1 or > CompilerPreparedBody.MaximumInstructions)
            {
                return CompilerCallableReplayStatus.Failed;
            }

            hasLoops = IrBlockOrder.TryCutLoops(program, static _ => true, out _)
                is { BackEdges.IsEmpty: false };
            var execution = new IrProgramInterpreter(factory).Execute(
                program,
                initial.ToImmutable(),
                hasLoops ? MaximumLoopReplaySteps : (int)maximumSteps,
                callHost,
                cancellationToken);
            if (execution.Status != IrProgramExecutionStatus.Returned)
            {
                return execution is
                {
                    Status: IrProgramExecutionStatus.Unsupported,
                    Instruction: IrCallInstruction call
                } && (body.SpecCalls.ContainsKey(call.Id) ||
                      body.SummaryCalls.ContainsKey(call.Id))
                    ? CompilerCallableReplayStatus.UnsupportedRegisteredCall
                    : hasLoops
                        ? CompilerCallableReplayStatus.NotReplayable
                        : CompilerCallableReplayStatus.Failed;
            }

            foreach (var binding in body.ParameterBindings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!execution.Values.TryGetValue(binding.Key, out var value))
                {
                    return CompilerCallableReplayStatus.Failed;
                }

                final[binding.Value] = value;
            }

            if (results.Length > 1 ||
                results.Length == 0 && rejectUnexpectedReturnValue &&
                execution.ReturnValue != null ||
                results.Length == 1 &&
                (execution.ReturnValue == null ||
                 execution.ReturnValue.Type != factory.GetVariableInfo(
                     results[0].Variable).Type))
            {
                return CompilerCallableReplayStatus.Failed;
            }

            if (results.Length == 1)
            {
                final[results[0].Variable] = execution.ReturnValue!;
            }
        }
        else if (body.Kind != CompilerPreparedBodyKind.Trivial ||
                 body.Program != null ||
                 !body.ParameterBindings.IsEmpty ||
                 !body.SpecCalls.IsEmpty ||
                 !body.SummaryCalls.IsEmpty ||
                 results.Length != 0)
        {
            return CompilerCallableReplayStatus.Failed;
        }

        foreach (var variable in target.Variables.Where(static variable =>
                     variable.Role == CompilerVariableRole.PreState))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!variable.CurrentStateVariable.HasValue ||
                !model.TryGetValue(variable.CurrentStateVariable.Value, out var value) ||
                value.Type != factory.GetVariableInfo(variable.Variable).Type)
            {
                return CompilerCallableReplayStatus.Failed;
            }

            final[variable.Variable] = value;
        }

        foreach (var variable in target.Variables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (final.TryGetValue(variable.Variable, out var value) &&
                !CompilerSourceIntegerDomain.Contains(
                    variable.SourceIntegerInterval,
                    value))
            {
                return CompilerCallableReplayStatus.Failed;
            }
        }

        var evaluated = new IrInterpreter(factory).Evaluate(
            postcondition,
            final,
            cancellationToken);
        if (evaluated.Status == IrEvaluationStatus.Exception)
        {
            return CompilerCallableReplayStatus.PostconditionUndefined;
        }

        return evaluated.Status == IrEvaluationStatus.Value &&
               evaluated.Value is { Kind: IrValueKind.Boolean, Boolean: false }
            ? CompilerCallableReplayStatus.Refuted
            : hasLoops
                ? CompilerCallableReplayStatus.NotReplayable
                : CompilerCallableReplayStatus.Failed;
    }
}
