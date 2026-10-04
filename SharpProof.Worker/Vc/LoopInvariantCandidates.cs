namespace SharpProof.Worker;

// Candidate loop invariants. Candidates are untrusted guesses: the solver keeps
// only those whose every checkpoint the kernel proves.
internal static class LoopInvariantCandidates
{
    internal const int MaximumPerLoop = 48;

    // Each integer the loop carries is compared with the integers it reads but
    // does not write, the other integers it carries, zero and the constants of
    // its conditions; each boolean it carries is guessed true and false.
    internal static ImmutableArray<PassiveLoopCutter.Invariant> Generate(PassiveCallableCandidate candidate,
        ImmutableArray<PassiveLoopCutter.Loop> loops, CancellationToken cancellationToken)
    {
        var factory = candidate.Factory;
        var invariants = ImmutableArray.CreateBuilder<PassiveLoopCutter.Invariant>();
        foreach (var loop in loops)
        {
            var written = loop.Writes.ToHashSet();
            var read = new HashSet<IrVarId>();
            var constants = new HashSet<IrTerm>();
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
                    if (term == null)
                    { continue; }
                    read.UnionWith(IrTraversal.CollectVariables(term));
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
            var anchors = read.Where(variable => !written.Contains(variable)).OrderBy(variable => variable.Value)
                .Select(variable => factory.Variable(variable)).Concat(constants.OrderBy(constant => constant.Id.Value)).ToArray();
            var conditions = new List<IrTerm>();
            var tracked = loop.Writes.Where(entering.Contains).OrderBy(variable => variable.Value).ToArray();
            foreach (var variable in tracked)
            {
                var type = factory.GetVariableInfo(variable).Type;
                var value = factory.Variable(variable);
                if (type == factory.BooleanType)
                {
                    conditions.Add(value);
                    conditions.Add(factory.Unary(IrUnaryOperator.Not, value));
                    continue;
                }
                if (factory.GetTypeInfo(type) is not { Kind: IrTypeKind.Integer, Width: > 0 })
                { continue; }
                var others = anchors.Concat(tracked.Where(other => other.Value > variable.Value)
                    .Select(other => factory.Variable(other))).Append(factory.Integer(type, 0L));
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
}
