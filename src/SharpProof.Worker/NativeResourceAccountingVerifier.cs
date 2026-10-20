using System.Numerics;

namespace SharpProof.Worker;

internal static class NativeResourceAccountingVerifier
{
    internal const string ModelId = WorkerResourceModels.Accounting;

    internal static WorkerResourceAccountingEvidence[] Evidence(ResourceProgram program, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        var pending = new Stack<ResourceProgram>();
        var result = new List<WorkerResourceAccountingEvidence>();
        pending.Push(program);
        var remaining = ResourceExpression.MaximumNodes;
        while (pending.Count != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { throw new ResourceExpressionLimitException(); }
            var current = pending.Pop();
            if (current.Kind == ResourceProgramKind.CreditLoop)
            {
                var budget = Budget(current, cancellation);
                var credit = current.Credit!;
                if (result.Count >= 16)
                { throw new ResourceExpressionLimitException(); }
                result.Add(new()
                {
                    InitialCredit = credit.Initial.ToString(CultureInfo.InvariantCulture),
                    DepositPerIteration = credit.Deposit.ToString(CultureInfo.InvariantCulture),
                    CounterWidth = credit.CounterWidth,
                    CheckedDeposit = credit.CheckedDeposit,
                    MaximumSpend = budget.ToDisplayString()
                });
            }
            foreach (var child in current.Children)
            { pending.Push(child); }
        }
        return [.. result];
    }

    internal static ResourceExpression Budget(ResourceProgram program, CancellationToken cancellation)
    {
        program = ArgumentNullGuard.NotNull(program, nameof(program));
        cancellation.ThrowIfCancellationRequested();
        if (program.Kind != ResourceProgramKind.CreditLoop || program.Credit is not { } credit || program.Rank is not { } rank ||
            program.Children.Length != 2 || program.RepeatKind != ResourceRepeatKind.Loop || credit.CounterWidth is not (32 or 64))
        { throw new InvalidDataException("MalformedResourceCredit"); }
        var maximum = (BigInteger.One << (credit.CounterWidth - 1)) - 1;
        if (credit.Initial < 0 || credit.Initial > maximum || credit.Deposit <= 0 || credit.Deposit > maximum)
        { throw new InvalidDataException("ResourceCreditDomainUnproved"); }
        var count = NativeResourceProgramVerifier.CheckRank(rank, cancellation);
        var budget = ResourceExpression.Add(ResourceExpression.Literal(credit.Initial),
            ResourceExpression.Multiply(count, ResourceExpression.Literal(credit.Deposit)));
        if (!credit.CheckedDeposit && (budget.Maximum is not { } bound || bound > maximum))
        { throw new InvalidDataException("ResourceCreditMayOverflow"); }
        return budget;
    }
}
