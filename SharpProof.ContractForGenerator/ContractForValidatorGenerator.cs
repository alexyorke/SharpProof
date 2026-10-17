using Microsoft.CodeAnalysis;

namespace SharpProof.ContractForGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ContractForValidatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        RepositoryParameterGuard.RequireInitialized(context.Equals(default(IncrementalGeneratorInitializationContext)), nameof(context), "Value must be initialized.");

        // ContractFor companions are reconciled by SharpProof.Analyzer after
        // all generators have contributed their syntax trees. Keeping this
        // entry point as an empty incremental generator preserves the package
        // loading role without allowing partial, heuristic source ownership
        // to produce duplicate or provenance-incomplete diagnostics.
    }

}
