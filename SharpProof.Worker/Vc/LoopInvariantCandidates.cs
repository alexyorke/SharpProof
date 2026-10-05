namespace SharpProof.Worker;

// Candidate loop invariants. Candidates are untrusted guesses: the solver keeps
// only those whose every checkpoint the kernel proves.
internal static class LoopInvariantCandidates
{
    internal const int MaximumPerLoop = 48;

    // Each integer the loop carries (in a variable or a field it stores to) is
    // compared with the integers it reads but does not write, the other
    // integers it carries, zero and the constants of its conditions; each
    // boolean it carries is guessed true and false.
    internal static ImmutableArray<PassiveLoopCutter.Invariant> Generate(PassiveCallableCandidate candidate,
        ImmutableArray<PassiveLoopCutter.Loop> loops, CancellationToken cancellationToken)
    {
        var factory = candidate.Factory;
        var invariants = ImmutableArray.CreateBuilder<PassiveLoopCutter.Invariant>();
        foreach (var loop in loops)
        {
            var (tracked, anchors) = State(candidate, loop, cancellationToken);
            var conditions = new List<IrTerm>();
            for (var ordinal = 0; ordinal < tracked.Length; ordinal++)
            {
                var value = tracked[ordinal];
                var type = value.Type;
                if (type == factory.BooleanType)
                {
                    conditions.Add(value);
                    conditions.Add(factory.Unary(IrUnaryOperator.Not, value));
                    continue;
                }
                if (factory.GetTypeInfo(type) is not { Kind: IrTypeKind.Integer, Width: > 0 })
                { continue; }
                var others = anchors.Concat(tracked.Skip(ordinal + 1)).Append(factory.Integer(type, 0L));
                foreach (var other in others.Where(other => other.Type == type).Distinct())
                {
                    conditions.Add(factory.Binary(IrBinaryOperator.LessThanOrEqual, value, other));
                    conditions.Add(factory.Binary(IrBinaryOperator.LessThanOrEqual, other, value));
                    conditions.Add(factory.Binary(IrBinaryOperator.Equal, value, other));
                }
            }
            invariants.AddRange(conditions.Where(condition => condition is not IrBooleanTerm).Distinct()
                .Take(MaximumPerLoop).Select(condition => new PassiveLoopCutter.Invariant(loop.Header, condition)));
        }
        return invariants.ToImmutable();
    }

    // The state a loop carries (its writes that are also set before it, and
    // the fields it stores to) and its anchors (what it reads but does not
    // write, those fields as the callable entered, and its conditions'
    // constants).
    internal static (IrTerm[] Tracked, IrTerm[] Anchors) State(PassiveCallableCandidate candidate,
        PassiveLoopCutter.Loop loop, CancellationToken cancellationToken)
    {
        var factory = candidate.Factory;
        var written = loop.Writes.ToHashSet();
        var read = new HashSet<IrVarId>();
        var constants = new HashSet<IrTerm>();
        var lengths = new List<IrTerm>();
        var copies = new Dictionary<IrVarId, IrTerm?>();
        foreach (var block in loop.Blocks.OrderBy(block => block.Value))
        {
            foreach (var instruction in candidate.Program.GetBlock(block).Instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IrTerm? term = instruction switch
                {
                    IrAssignInstruction assign => assign.Value,
                    IrBranchInstruction branch => branch.Condition,
                    IrAssumeInstruction assume => assume.Condition,
                    IrReturnInstruction returned => returned.Value,
                    _ => null
                };
                if (instruction is IrAssignInstruction copy)
                {
                    // A variable the loop only ever sets to one unwritten
                    // variable is that variable wherever the loop reads it.
                    var source = copy.Value is IrVariableTerm { Variable: var original } && !written.Contains(original) ? copy.Value : null;
                    copies[copy.Target] = copies.TryGetValue(copy.Target, out var previous) && previous?.Id != source?.Id ? null : source;
                }
                else if (instruction is IrHavocInstruction havoc)
                {
                    foreach (var variable in havoc.Variables)
                    { copies[variable] = null; }
                }
                if (term == null)
                { continue; }
                read.UnionWith(IrTraversal.CollectVariables(term));
                _ = IrTraversal.Any(term, child =>
                {
                    if (child is IrLengthTerm)
                    { lengths.Add(child); }
                    return false;
                });
                if (instruction is IrBranchInstruction)
                {
                    _ = IrTraversal.Any(term, child =>
                    {
                        if (child is IrIntegerTerm)
                        { constants.Add(child); }
                        return false;
                    });
                }
            }
        }
        // Only state that enters the loop can be checked on its entry edges:
        // a variable the loop writes must also be set before it.
        var entering = new HashSet<IrVarId>(candidate.Parameters.SelectMany(parameter => new[] { parameter.Current, parameter.Old }));
        foreach (var block in candidate.Program.Blocks.Where(block => !loop.Blocks.Contains(block.Id)))
        {
            foreach (var instruction in block.Instructions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (instruction is IrAssignInstruction assign)
                { entering.Add(assign.Target); }
            }
        }
        // A field read through an input's Old snapshot is its entry value.
        var snapshots = candidate.Parameters.SelectMany(parameter => new[]
        {
            (parameter.Entry, (IrTerm)factory.Variable(parameter.Old)), (parameter.Current, factory.Variable(parameter.Old))
        }).ToDictionary(pair => pair.Item1, pair => pair.Item2);
        var entered = loop.Fields.Select(field => IrSubstitution.Substitute(factory, field, snapshots)).Where(field => !loop.Fields.Contains(field));
        // The length of an array the loop does not replace is fixed.
        var resolved = copies.Where(pair => pair.Value != null).ToDictionary(pair => pair.Key, pair => pair.Value!);
        var fixedLengths = lengths.Select(length => IrSubstitution.Substitute(factory, length, resolved))
            .Where(length => IrTraversal.CollectVariables(length).All(variable => !written.Contains(variable))).Distinct();
        var anchors = read.Where(variable => !written.Contains(variable)).OrderBy(variable => variable.Value)
            .Select(variable => (IrTerm)factory.Variable(variable)).Concat(entered).Concat(fixedLengths)
            .Concat(constants.OrderBy(constant => constant.Id.Value)).ToArray();
        return ([.. loop.Writes.Where(entering.Contains).OrderBy(variable => variable.Value)
            .Select(variable => (IrTerm)factory.Variable(variable)).Concat(loop.Fields)], anchors);
    }
}
