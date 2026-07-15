namespace SharpProof.ProofCore.Smt;

internal delegate TStatus SmtFactCollector<TStatus>(SmtFormula formula, ref bool changed);

internal static class SmtFactFixedPoint
{
    internal static TStatus Collect<TStatus>(
        IReadOnlyList<SmtFormula> conditions,
        TStatus success,
        SmtFactCollector<TStatus> collect)
    {
        var iterationLimit = Math.Max(1, conditions.Count * 4);
        var changed = false;
        do
        {
            changed = false;
            foreach (var condition in conditions)
            {
                var status = collect(condition, ref changed);
                if (!EqualityComparer<TStatus>.Default.Equals(status, success)) return status;
            }

            iterationLimit--;
        } while (changed && iterationLimit > 0);

        return success;
    }
}
