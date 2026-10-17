namespace SharpProof.Analyzer;

internal static class InvalidContractArgumentDiagnostics
{
    internal static Diagnostic Create(ContractIntrinsicViolation violation)
    {
        violation = RepositoryParameterGuard.ValidateInitialized(violation, violation.Operation is null, nameof(violation), "Value must be initialized.");

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
        argument = RepositoryParameterGuard.NotNull(argument, nameof(argument));
        attributeName = RepositoryParameterGuard.NotNull(attributeName, nameof(attributeName));
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        reason = RepositoryParameterGuard.NotNull(reason, nameof(reason));
        return Diagnostic.Create(
                GeneratedDiagnosticDescriptors.InvalidContractArgumentRule,
                location,
                attributeName,
                argument,
                reason);
    }
}
