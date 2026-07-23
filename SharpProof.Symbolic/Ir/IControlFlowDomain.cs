namespace SharpProof.Symbolic.Ir;

internal interface IControlFlowDomain<TState> {
    void SetControlFlowGraph(ControlFlowGraph graph, PointsToAnalysisResult? pointsToAnalysisResult);
    TState Transfer(TState state, IOperation operation);
    TState Refine(TState state, IOperation? condition, ControlFlowConditionKind kind, bool conditionalSuccessor,
        BasicBlock source);
    TState Merge(TState current, TState incoming);
    TState CompleteBlock(TState state, BasicBlock block);
    bool Equivalent(TState left, TState right);
    bool IsUnreachable(TState state);
    string GetKey(TState state);
}
