namespace SharpProof.Ir;

internal static class IrSnapshotReads
{
    // Snapshot provenance follows the selected reference value, rather than
    // every variable in its guard or array index.
    internal static bool TrySelector(IrFactory factory, IrTerm owner, Func<IrVarId, bool> snapshot,
        Func<IrTerm, IrTerm> rewriteGuard, Func<bool> spend,
        Dictionary<IrId, IrTerm> memo, out IrTerm selector, CancellationToken cancellationToken)
    {
        selector = null!;
        var pending = new Stack<(IrTerm Term, bool Expanded)>();
        pending.Push((owner, false));
        while (pending.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!spend())
            { return false; }
            var (term, expanded) = pending.Pop();
            if (memo.ContainsKey(term.Id))
            { continue; }
            if (term is IrVariableTerm variable)
            { memo.Add(term.Id, factory.Boolean(snapshot(variable.Variable))); continue; }
            if (term is IrNullTerm or IrStringTerm or IrEmptyArrayTerm)
            { memo.Add(term.Id, factory.Boolean(false)); continue; }
            IrTerm? child = term switch
            {
                IrCastTerm cast => cast.Operand,
                IrSequenceAccessTerm access => access.Sequence,
                IrOpaqueTerm field when IrFieldSites.IsFieldRead(factory, field) => field.Receiver,
                _ => null
            };
            if (term is not IrConditionalTerm && child == null)
            { return false; }
            if (!expanded)
            {
                pending.Push((term, true));
                if (term is IrConditionalTerm branches)
                {
                    pending.Push((branches.WhenFalse, false));
                    pending.Push((branches.WhenTrue, false));
                }
                else
                { pending.Push((child!, false)); }
                continue;
            }
            if (term is IrConditionalTerm conditional)
            {
                var whenTrue = memo[conditional.WhenTrue.Id];
                var whenFalse = memo[conditional.WhenFalse.Id];
                memo.Add(term.Id, whenTrue.Id == whenFalse.Id ? whenTrue
                    : factory.Conditional(rewriteGuard(conditional.Condition), whenTrue, whenFalse));
            }
            else
            { memo.Add(term.Id, memo[child!.Id]); }
        }
        selector = memo[owner.Id];
        return true;
    }
}
