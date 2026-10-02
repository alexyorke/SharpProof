namespace SharpProof.Worker;

internal readonly record struct PassiveParameterBinding(IrVarId Entry, IrVarId Current, IrVarId Old);

internal readonly record struct PassiveContractClause(IrTerm Value, IrTerm Safe, OperationId Operation);

internal readonly record struct PassiveCallPrecondition(IrInstructionId Marker, IrTerm Value, IrTerm Safe);

// Enrolled only by a decoded candidate artifact or the trusted source tooling
// adapter. No source symbols, mutable artifacts, or arbitrary query labels cross
// this boundary. The candidate has no effect on the production worker route.
internal sealed class PassiveCallableCandidate
{
    internal PassiveCallableCandidate(string callableId, IrProgram program,
        ImmutableArray<PassiveParameterBinding> parameters, IrVarId? result,
        ImmutableArray<PassiveContractClause> requires, ImmutableArray<PassiveContractClause> ensures, bool isBodyAbstraction = false,
        ImmutableArray<PassiveCallPrecondition> callPreconditions = default)
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
        IsBodyAbstraction = isBodyAbstraction;
        CallPreconditions = callPreconditions.IsDefault ? [] : callPreconditions;
        if (CallPreconditions.IsEmpty)
        {
            if (program.Blocks.SelectMany(block => block.Instructions).OfType<IrAssignInstruction>().Any(marker =>
                IrCallPreconditionMarker.IsReservedName(program.Factory.GetString(program.Factory.GetVariableInfo(marker.Target).Name))))
            { throw new ArgumentException("Reserved call-precondition markers require owned obligation rows.", nameof(callPreconditions)); }
            return;
        }
        var instructions = program.Blocks.SelectMany(block => block.Instructions).ToDictionary(instruction => instruction.Id);
        var markers = new HashSet<IrInstructionId>();
        var markerTargets = new HashSet<IrVarId>();
        var writers = instructions.Values.SelectMany(IrInstructionFacts.WrittenVariables)
            .GroupBy(variable => variable).ToDictionary(group => group.Key, group => group.Count());
        var reads = IrTraversal.CollectVariables(instructions.Values.SelectMany(IrInstructionFacts.ReadTerms)
            .Concat(requires.Concat(ensures).SelectMany(clause => new[] { clause.Value, clause.Safe })));
        foreach (var clause in CallPreconditions)
        {
            IrFactory.RequireBooleanTerm(program.Factory, clause.Value, nameof(callPreconditions));
            IrFactory.RequireBooleanTerm(program.Factory, clause.Safe, nameof(callPreconditions));
            if (isBodyAbstraction || !markers.Add(clause.Marker) || !instructions.TryGetValue(clause.Marker, out var instruction) ||
                instruction is not IrAssignInstruction marker || program.Factory.GetVariableInfo(marker.Target).Type != program.Factory.BooleanType ||
                !markerTargets.Add(marker.Target) || identities.Contains(marker.Target) || reads.Contains(marker.Target) || writers[marker.Target] != 1 ||
                marker.Value.Id != program.Factory.Binary(IrBinaryOperator.AndAlso, clause.Safe, clause.Value).Id)
            { throw new ArgumentException("Call preconditions must own distinct guarded assignments in the original body.", nameof(callPreconditions)); }
        }
        if (instructions.Values.OfType<IrAssignInstruction>().Any(marker =>
            IrCallPreconditionMarker.IsReservedName(program.Factory.GetString(program.Factory.GetVariableInfo(marker.Target).Name)) &&
            !markers.Contains(marker.Id)))
        { throw new ArgumentException("Reserved call-precondition markers require owned obligation rows.", nameof(callPreconditions)); }
    }

    internal string CallableId { get; }
    internal IrProgram Program { get; }
    internal bool IsBodyAbstraction { get; }
    internal IrFactory Factory => Program.Factory;
    internal ImmutableArray<PassiveParameterBinding> Parameters { get; }
    internal IrVarId? Result { get; }
    internal ImmutableArray<PassiveContractClause> Requires { get; }
    internal ImmutableArray<PassiveContractClause> Ensures { get; }
    internal ImmutableArray<PassiveCallPrecondition> CallPreconditions { get; }
}
