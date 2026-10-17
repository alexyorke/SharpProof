namespace SharpProof.Analyzer;

internal static class InvalidContractArgumentDiagnostics
{
    internal static Diagnostic Create(ContractIntrinsicViolation violation)
    {
        if (violation.Operation is null)
        { throw new System.ArgumentException("Value must be initialized.", nameof(violation)); }

        var (argument, reason) =
            AnalyzerDiagnosticCatalog.DescribeIntrinsicViolation(
                violation.Failure,
                violation.IsOld);
        return Create(
            violation.IsOld ? "Contract.Old" : "Contract.Result",
            argument,
            reason,
            violation.Operation.Syntax.GetLocation());
    }

    internal static Diagnostic Create(string attributeName, string argument, string reason, Location location)
    {
        if (argument is null)
        {
            throw new System.ArgumentNullException(nameof(argument));
        }
        if (attributeName is null)
        {
            throw new System.ArgumentNullException(nameof(attributeName));
        }
        if (location is null)
        {
            throw new System.ArgumentNullException(nameof(location));
        }
        if (reason is null)
        {
            throw new System.ArgumentNullException(nameof(reason));
        }
        return Diagnostic.Create(
                GeneratedDiagnosticDescriptors.InvalidContractArgumentRule,
                location,
                attributeName,
                argument,
                reason);
    }
}
