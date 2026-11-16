namespace SharpProof.Analyzer;

internal static class AnalyzerDiagnosticCatalog
{
    internal static (string Argument, string Reason) DescribeIntrinsicViolation(
        ContractBindingFailure failure, ContractIntrinsic intrinsic)
    {
        return (failure, intrinsic) switch
        {
            (ContractBindingFailure.NestedOld, _) => (
                "<nesting>",
                "Contract." + intrinsic + " cannot be nested inside Contract.Old"),
            (ContractBindingFailure.InvalidIntrinsicSignature, ContractIntrinsic.Old) => (
                "<signature>",
                "expected exactly one value argument"),
            (ContractBindingFailure.InvalidIntrinsicSignature, ContractIntrinsic.ValueAtReturn) => (
                "<signature>",
                "expected an out parameter of the enclosing callable"),
            (ContractBindingFailure.InvalidIntrinsicSignature, _) => (
                "<signature>",
                "expected a result type matching the callable return type"),
            _ => ("<placement>",
                "expected use inside Contract.Ensures")
        };
    }

    internal static string DescribePlacement(ContractClausePlacement placement)
    {
        return placement switch
        {
            ContractClausePlacement.Conditional =>
                "expected an unconditional prologue statement",
            ContractClausePlacement.Unreachable =>
                "expected a reachable prologue statement",
            ContractClausePlacement.Late =>
                "expected the clause before every non-contract statement",
            _ => "expected a direct prologue statement"
        };
    }
}
