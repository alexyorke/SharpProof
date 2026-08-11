namespace SharpProof.ContractForGenerator;

[Generator(LanguageNames.CSharp)]
public sealed class ContractForValidatorGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var candidates = context.SyntaxProvider.ForAttributeWithMetadataName(
                ContractSelectionInventory.ContractForMetadataName,
                static (_, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                },
                static (attributeContext, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return attributeContext.TargetSymbol as INamedTypeSymbol;
                })
            .Where(static candidate => candidate != null)
            .Select(static (candidate, _) => candidate!)
            .Collect()
            .WithTrackingName("ContractForCandidates");
        context.RegisterSourceOutput(
            context.CompilationProvider
                .Combine(candidates)
                .Combine(context.AnalyzerConfigOptionsProvider)
                .WithTrackingName("ContractForValidationInput"),
            static (output, value) => Execute(
                output,
                value.Left.Left,
                value.Left.Right,
                value.Right));
    }

    private static void Execute(
        SourceProductionContext context,
        Compilation compilation,
        ImmutableArray<INamedTypeSymbol> candidates,
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (candidates.IsDefaultOrEmpty || IsProfileOff(optionsProvider))
        {
            return;
        }

        var handwrittenCandidates = candidates
            .Where(candidate => candidate.Locations.All(location =>
                location.SourceTree is not { } tree ||
                !AnalyzerGeneratedCodePolicy.IsGenerated(
                    tree,
                    compilation,
                    context.CancellationToken)))
            .ToImmutableArray();
        if (handwrittenCandidates.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var diagnostic in
                 ContractForValidationEngine.Validate(
                     compilation,
                     handwrittenCandidates,
                     context.CancellationToken))
        {
            context.ReportDiagnostic(diagnostic);
        }
    }

    private static bool IsProfileOff(
        AnalyzerConfigOptionsProvider optionsProvider)
    {
        try
        {
            var options = optionsProvider.GlobalOptions;
            foreach (var key in new[]
                     {
                         SharpProofConfigurationCatalog.ProfileKey,
                         "build_property." +
                         SharpProofConfigurationCatalog.ProfileKey,
                         "build_property." +
                         SharpProofConfigurationCatalog.ProfileBuildPropertyName
                     })
            {
                if (options.TryGetValue(key, out var value) &&
                    string.Equals(
                        value.Trim(),
                        SharpProofConfigurationCatalog.ProfileOff,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            return true;
        }
    }
}
