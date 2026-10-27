using System.Numerics;

namespace SharpProof.Verify;

/// <summary>The concrete IR and contract state checked before a callable is refuted.</summary>
internal sealed class CallableReplayContext(
    IrProgram? program,
    bool isTrivial,
    ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
    ImmutableDictionary<IrVarId, IrVarId?> preStateBindings,
    ImmutableArray<IrVarId> resultVariables,
    IrTerm postcondition,
    ImmutableDictionary<IrVarId, (BigInteger Minimum, BigInteger Maximum)> integerDomains,
    int maximumSteps,
    ImmutableHashSet<IrInstructionId> registeredCalls,
    Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? callHost = null)
{
    public CallableReplayContext(
        IrProgram? program, bool isTrivial,
        ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
        ImmutableDictionary<IrVarId, IrVarId?> preStateBindings,
        ImmutableArray<IrVarId> resultVariables, IrTerm postcondition,
        ImmutableDictionary<IrVarId, (BigInteger Minimum, BigInteger Maximum)> integerDomains,
        int maximumSteps, ImmutableHashSet<IrInstructionId> registeredCalls,
        IrTerm? postconditionGuard, IrProgramReplayOptions? replayOptions,
        Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? callHost = null)
        : this(program, isTrivial, parameterBindings, preStateBindings, resultVariables,
            postcondition, integerDomains, maximumSteps, registeredCalls, callHost)
    {

        RepositoryParameterGuard.RequireInitialized(resultVariables.IsDefault, nameof(resultVariables), "Array must be initialized.");
        integerDomains = RepositoryParameterGuard.NotNull(integerDomains, nameof(integerDomains));
        parameterBindings = RepositoryParameterGuard.NotNull(parameterBindings, nameof(parameterBindings));
        postcondition = RepositoryParameterGuard.NotNull(postcondition, nameof(postcondition));
        preStateBindings = RepositoryParameterGuard.NotNull(preStateBindings, nameof(preStateBindings));
        registeredCalls = RepositoryParameterGuard.NotNull(registeredCalls, nameof(registeredCalls));
        PostconditionGuard = postconditionGuard;
        ReplayOptions = replayOptions;
    }

    internal IrProgram? Program { get; } = program;
    internal bool IsTrivial { get; } = isTrivial;
    internal ImmutableDictionary<IrVarId, IrVarId> ParameterBindings { get; } = ArgumentNullGuard.NotNull(parameterBindings, nameof(parameterBindings));
    internal ImmutableDictionary<IrVarId, IrVarId?> PreStateBindings { get; } = ArgumentNullGuard.NotNull(preStateBindings, nameof(preStateBindings));
    internal ImmutableArray<IrVarId> ResultVariables { get; } = (resultVariables.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(resultVariables)) : resultVariables);
    internal IrTerm Postcondition { get; } = ArgumentNullGuard.NotNull(postcondition, nameof(postcondition));
    internal ImmutableDictionary<IrVarId, (BigInteger Minimum, BigInteger Maximum)> IntegerDomains { get; } = ArgumentNullGuard.NotNull(integerDomains, nameof(integerDomains));
    internal int MaximumSteps { get; } = maximumSteps;
    internal ImmutableHashSet<IrInstructionId> RegisteredCalls { get; } = ArgumentNullGuard.NotNull(registeredCalls, nameof(registeredCalls));
    internal Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? CallHost { get; } = callHost;
    public IrTerm? PostconditionGuard { get; }
    public IrProgramReplayOptions? ReplayOptions { get; }
    internal ImmutableArray<IrTerm> EntryConstraints { get; } = [];

    internal CallableReplayContext(CallableReplayContext context, ImmutableArray<IrTerm> entryConstraints)
        : this(context.Program, context.IsTrivial, context.ParameterBindings, context.PreStateBindings,
            context.ResultVariables, context.Postcondition, context.IntegerDomains, context.MaximumSteps,
            context.RegisteredCalls, context.PostconditionGuard, context.ReplayOptions, context.CallHost)
    { context = RepositoryParameterGuard.NotNull(context, nameof(context)); RepositoryParameterGuard.RequireInitialized(entryConstraints.IsDefault, nameof(entryConstraints), "Array must be initialized."); EntryConstraints = entryConstraints; }
}
