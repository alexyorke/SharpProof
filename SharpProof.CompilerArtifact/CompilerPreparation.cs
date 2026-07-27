namespace SharpProof.CompilerArtifact;
#pragma warning disable IDE0055 // Compact preparation DTOs preserve the fixed production-size ceiling.
internal enum CompilerContractKind { Requires, Ensures, Assume }
internal enum CompilerContractEvidence { CompilerBoundInvocation, ClosedAttribute, Companion }
internal enum CompilerVariableRole { Receiver, Parameter, Result, PreState }
internal sealed record CompilerCallablePreparation(IrFactory Factory, WorkerCallableManifestEntry Entry,
    ImmutableArray<CompilerPreparedClause> Clauses, ImmutableArray<CompilerCanonicalVariable> Variables,
    WorkerClaimReason FailureReason, CompilerPreparedBody? Body) {
    internal bool IsSuccess => FailureReason == WorkerClaimReason.None;
}
internal sealed record CompilerPreparedClause(CompilerContractKind Kind, IrTerm Condition,
    CompilerContractEvidence Evidence, string? ClaimId, string? AssumptionId);
internal readonly record struct CompilerIntegerInterval(long Minimum, long Maximum);
internal sealed record CompilerCanonicalVariable(CompilerVariableRole Role, int Ordinal, IrVarId Variable,
    IrVarId? CurrentStateVariable, CompilerIntegerInterval? SourceIntegerInterval, string ModelLabel);
internal enum CompilerPreparedBodyKind { Trivial, Program }
internal sealed record CompilerPreparedBody(CompilerPreparedBodyKind Kind, IrProgram? Program,
    ImmutableDictionary<IrVarId, IrVarId> ParameterBindings,
    ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> SpecCalls) {
    internal static CompilerPreparedBody Trivial() => new(CompilerPreparedBodyKind.Trivial, null,
        ImmutableDictionary<IrVarId, IrVarId>.Empty, ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall>.Empty);
    internal static CompilerPreparedBody ProgramBody(IrProgram program, ImmutableDictionary<IrVarId, IrVarId> parameterBindings,
        ImmutableDictionary<IrInstructionId, CompilerPreparedSpecCall> specCalls) =>
        new(CompilerPreparedBodyKind.Program, program ?? throw new ArgumentNullException(nameof(program)),
            parameterBindings, specCalls);
}
internal sealed record CompilerPreparedSpecCall(IrInstructionId Instruction, string CallIdentity,
    string WitnessIdentifier, bool ConsumesMemoryHavoc);
