namespace SharpProof.Analyzer;

internal static class InvalidContractArgumentDiagnostics
{
    internal static Diagnostic Create(ContractIntrinsicViolation violation)
    {
        var (argument, reason) =
            AnalyzerDiagnosticCatalog.DescribeIntrinsicViolation(
                violation.Failure,
                violation.IsOld);
        return Create(
            violation.IsOld ? "Contract.Old" : "Contract.Result",
            argument,
            reason,
            violation.Invocation.Syntax.GetLocation());
    }

    internal static Diagnostic Create(string attributeName, string argument, string reason, Location location)
    {
        return Diagnostic.Create(
                GeneratedDiagnosticDescriptors.InvalidContractArgumentRule,
                location,
                attributeName,
                argument,
                reason);
    }
}
