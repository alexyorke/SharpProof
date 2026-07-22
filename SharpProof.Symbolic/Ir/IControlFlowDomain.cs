namespace SharpProof.Symbolic.Ir;

internal interface IControlFlowDomain<TState> {
    TState Transfer(TState state, IOperation operation);
    TState Refine(TState state, IOperation? condition, ControlFlowConditionKind kind, bool conditionalSuccessor);
    TState Merge(TState current, TState incoming);
    TState CompleteBlock(TState state, BasicBlock block);
    bool Equivalent(TState left, TState right);
}
