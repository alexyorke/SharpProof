namespace SharpProof.Analyzer;

internal static class ContractRuntimePolicy
{
    private const string ConfigurationKey = "DefineConstants/#define";

    internal static bool IsRuntimeEvaluationEnabled(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CSharpPreprocessorSymbols.IsDefined(
                    tree,
                    ContractApiMetadata.ConditionalSymbol,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    internal static InvalidAnalyzerConfigurationValue InvalidConfiguration()
    {
        return new InvalidAnalyzerConfigurationValue(
            ConfigurationKey,
            ContractApiMetadata.ConditionalSymbol,
            "the reserved symbol enables runtime evaluation of ghost " +
            "contracts; remove it before SharpProof analysis");
    }

    internal static void ThrowIfRuntimeEvaluationEnabled(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        if (IsRuntimeEvaluationEnabled(compilation, cancellationToken))
        {
            throw new InvalidOperationException(
                ContractApiMetadata.ConditionalSymbol +
                " enables runtime evaluation of ghost contracts and is " +
                "not supported during SharpProof verification.");
        }
    }
}
