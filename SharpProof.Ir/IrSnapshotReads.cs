namespace SharpProof.Ir;

internal static class IrSnapshotReads
{
    // Snapshot provenance follows the selected reference value, rather than
    // every variable in its guard or array index.
    internal static (bool Success, IrTerm? Selector, ImmutableDictionary<IrId, IrTerm> Memo) TrySelector(IrFactory factory, IrTerm owner, Func<IrVarId, bool> snapshot,
        Func<IrTerm, IrTerm> rewriteGuard, Func<bool> spend,
        ImmutableDictionary<IrId, IrTerm> memo, CancellationToken cancellationToken)
    {
        ArgumentNullGuard.NotNull(factory, nameof(factory));
        ArgumentNullGuard.NotNull(owner, nameof(owner));
        ArgumentNullGuard.NotNull(snapshot, nameof(snapshot));
        ArgumentNullGuard.NotNull(rewriteGuard, nameof(rewriteGuard));
        ArgumentNullGuard.NotNull(spend, nameof(spend));
        ArgumentNullGuard.NotNull(memo, nameof(memo));
        var pending = ImmutableStack<(IrTerm Term, bool Expanded)>.Empty;
        pending = pending.Push((owner, false));
        while (!pending.IsEmpty)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!spend())
            { return (false, null, memo); }
            var (term, expanded) = pending.Peek();
            pending = pending.Pop();
            if (memo.ContainsKey(term.Id))
            { continue; }
            if (term is IrVariableTerm variable)
            { memo = memo.Add(term.Id, factory.Boolean(snapshot(variable.Variable))); continue; }
            if (term is IrNullTerm or IrStringTerm or IrEmptyArrayTerm)
            { memo = memo.Add(term.Id, factory.Boolean(false)); continue; }
            IrTerm? child = term switch
            {
                IrCastTerm cast => cast.Operand,
                IrSequenceAccessTerm access => access.Sequence,
                IrOpaqueTerm field when IrFieldSites.IsFieldRead(factory, field) => field.Receiver,
                _ => null
            };
            if (term is not IrConditionalTerm && child == null)
            { return (false, null, memo); }
            if (!expanded)
            {
                pending = pending.Push((term, true));
                if (term is IrConditionalTerm branches)
                {
                    pending = pending.Push((branches.WhenFalse, false));
                    pending = pending.Push((branches.WhenTrue, false));
                }
                else
                { pending = pending.Push((child!, false)); }
                continue;
            }
            if (term is IrConditionalTerm conditional)
            {
                var whenTrue = memo[conditional.WhenTrue.Id];
                var whenFalse = memo[conditional.WhenFalse.Id];
                memo = memo.Add(term.Id, whenTrue.Id == whenFalse.Id ? whenTrue
                    : factory.Conditional(rewriteGuard(conditional.Condition), whenTrue, whenFalse));
            }
            else
            { memo = memo.Add(term.Id, memo[child!.Id]); }
        }
        return (true, memo[owner.Id], memo);
    }
}
