namespace SharpProof.Compiler.Semantics;

internal static class ContractRuntimePolicy
{
    private const string ConfigurationKey = "DefineConstants/#define";

    internal static bool IsReservedSymbolDefined(
        Compilation compilation,
        CancellationToken cancellationToken)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (CSharpPreprocessorSymbols.IsDefined(
                    tree,
                    ContractApiCatalog.ConditionalSymbol,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    internal static InvalidCompilerConfigurationValue InvalidConfiguration()
    {
        return new InvalidCompilerConfigurationValue(
            ConfigurationKey,
            ContractApiCatalog.ConditionalSymbol,
            "Ghost clauses do not check conditions; Result/Old throw when executed; remove SHARPPROOF_CONTRACTS before compiling.");
    }

}
