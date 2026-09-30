using System.Numerics;

namespace SharpProof.Verify;

/// <summary>The concrete IR and contract state checked before a callable is refuted.</summary>
public sealed class CallableReplayContext(
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
    internal IrProgram? Program { get; } = program;
    internal bool IsTrivial { get; } = isTrivial;
    internal ImmutableDictionary<IrVarId, IrVarId> ParameterBindings { get; } = parameterBindings;
    internal ImmutableDictionary<IrVarId, IrVarId?> PreStateBindings { get; } = preStateBindings;
    internal ImmutableArray<IrVarId> ResultVariables { get; } = resultVariables;
    internal IrTerm Postcondition { get; } = postcondition;
    internal ImmutableDictionary<IrVarId, (BigInteger Minimum, BigInteger Maximum)> IntegerDomains { get; } = integerDomains;
    internal int MaximumSteps { get; } = maximumSteps;
    internal ImmutableHashSet<IrInstructionId> RegisteredCalls { get; } = registeredCalls;
    internal Func<IrCallInstruction, IrValue?, ImmutableArray<IrValue>, IrValue?>? CallHost { get; } = callHost;
}
