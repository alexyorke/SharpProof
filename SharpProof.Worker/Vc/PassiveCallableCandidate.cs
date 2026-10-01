namespace SharpProof.Worker;

internal readonly record struct PassiveParameterBinding(IrVarId Entry, IrVarId Current, IrVarId Old);

internal readonly record struct PassiveContractClause(IrTerm Value, IrTerm Safe, OperationId Operation);

// Enrolled only by a decoded candidate artifact or the trusted source tooling
// adapter. No source symbols, mutable artifacts, or arbitrary query labels cross
// this boundary. The candidate has no effect on the production worker route.
internal sealed class PassiveCallableCandidate
{
    internal PassiveCallableCandidate(string callableId, IrProgram program,
        ImmutableArray<PassiveParameterBinding> parameters, IrVarId? result,
        ImmutableArray<PassiveContractClause> requires, ImmutableArray<PassiveContractClause> ensures)
    {
        if (string.IsNullOrWhiteSpace(callableId))
        { throw new ArgumentException("A callable identity is required.", nameof(callableId)); }
        Program = ArgumentNullGuard.NotNull(program, nameof(program));
        if (program.Factory.Semantics != IrExecutionSemantics.Total)
        { throw new ArgumentException("A passive candidate requires Total IR.", nameof(program)); }
        if (parameters.IsDefault || requires.IsDefault || ensures.IsDefault)
        { throw new ArgumentException("Candidate arrays must be initialized."); }
        var entries = new HashSet<IrVarId>();
        var identities = new HashSet<IrVarId>();
        foreach (var parameter in parameters)
        {
            var type = program.Factory.GetVariableInfo(parameter.Entry).Type;
            if (!entries.Add(parameter.Entry) || !identities.Add(parameter.Entry) ||
                !identities.Add(parameter.Current) || !identities.Add(parameter.Old) ||
                program.Factory.GetVariableInfo(parameter.Current).Type != type ||
                program.Factory.GetVariableInfo(parameter.Old).Type != type)
            { throw new ArgumentException("Canonical parameter bindings must be distinct and type consistent.", nameof(parameters)); }
        }
        if (result is { } resultVariable)
        {
            program.Factory.GetVariableInfo(resultVariable);
            if (!identities.Add(resultVariable))
            { throw new ArgumentException("The result must have its own canonical identity.", nameof(result)); }
        }
        var allowed = parameters.SelectMany(parameter => new[] { parameter.Entry, parameter.Current, parameter.Old }).ToHashSet();
        if (result is { } allowedResult)
        { allowed.Add(allowedResult); }
        foreach (var clause in requires.Concat(ensures))
        {
            IrFactory.RequireBooleanTerm(program.Factory, clause.Value, nameof(requires));
            IrFactory.RequireBooleanTerm(program.Factory, clause.Safe, nameof(requires));
            program.Factory.GetOperationInfo(clause.Operation);
            if (IrTraversal.CollectVariables([clause.Value, clause.Safe]).Any(variable => !allowed.Contains(variable)))
            { throw new ArgumentException("Clauses may refer only to owned canonical bindings.", nameof(requires)); }
        }
        if (requires.Any(clause => IrTraversal.CollectVariables([clause.Value, clause.Safe])
                .Any(variable => !entries.Contains(variable))))
        { throw new ArgumentException("Requires must refer only to entry inputs.", nameof(requires)); }
        CallableId = callableId;
        Parameters = parameters;
        Result = result;
        Requires = requires;
        Ensures = ensures;
    }

    internal string CallableId { get; }
    internal IrProgram Program { get; }
    internal IrFactory Factory => Program.Factory;
    internal ImmutableArray<PassiveParameterBinding> Parameters { get; }
    internal IrVarId? Result { get; }
    internal ImmutableArray<PassiveContractClause> Requires { get; }
    internal ImmutableArray<PassiveContractClause> Ensures { get; }
}
