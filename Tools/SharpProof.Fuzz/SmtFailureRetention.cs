using SharpProof.Ir;

namespace SharpProof.Fuzz;

internal static class SmtFailureRetention
{
    public static async Task<FuzzFailure> RetainFiniteDomainAsync(
        IrFactory factory,
        IrTerm formula,
        int caseIndex,
        int caseSeed,
        CancellationToken cancellationToken = default)
    {
        var minimized = await IrStructuralShrinker.MinimizeAsync(
            factory,
            formula,
            async (candidate, cancellation) =>
                (await FiniteDomainSmtDifferentialOracle.CompareAsync(
                    factory, candidate, cancellation).ConfigureAwait(false)).Status !=
                FuzzOracleStatus.Agreement,
            cancellationToken).ConfigureAwait(false);
        var result = await FiniteDomainSmtDifferentialOracle.CompareAsync(
            factory, minimized, cancellationToken).ConfigureAwait(false);
        var printer = new IrPrinter(factory);
        return new FuzzFailure(caseIndex, caseSeed, "finite-domain-smt",
            printer.Print(formula), printer.Print(minimized), result.Detail);
    }

    public static async Task<FuzzFailure> RetainPartialTermAsync(
        IrFactory factory,
        PartialTermSmtCase generated,
        int caseIndex,
        int caseSeed,
        CancellationToken cancellationToken = default)
    {
        var result = await PartialTermSmtDifferentialOracle.CompareAsync(
            factory, generated, cancellationToken).ConfigureAwait(false);
        var printed = new IrPrinter(factory).Print(generated.Formula);
        return new FuzzFailure(caseIndex, caseSeed, "partial-term-smt",
            printed, printed, result.Detail);
    }
}
