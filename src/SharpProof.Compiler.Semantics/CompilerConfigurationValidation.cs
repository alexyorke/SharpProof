namespace SharpProof.Compiler.Semantics;

internal sealed class CompilerConfigurationIssue
{
    internal CompilerConfigurationIssue(InvalidCompilerConfigurationValue value, Location location)
    {
        if (value.Key == null || value.Value == null || value.Reason == null)
        {
            throw new ArgumentException("A configuration issue requires a key, value, and reason.", nameof(value));
        }
        Value = value;
        Location = ArgumentNullGuard.NotNull(location, nameof(location));
    }
    internal InvalidCompilerConfigurationValue Value { get; }
    internal Location Location { get; }
}

internal static class CompilerConfigurationValidation
{
    internal static ImmutableArray<CompilerConfigurationIssue> GetIssues(
        Compilation compilation, AnalyzerOptions analyzerOptions,
        CompilerConfiguration configuration, CancellationToken cancellationToken)
    {
        compilation = ArgumentNullGuard.NotNull(compilation, nameof(compilation));
        analyzerOptions = ArgumentNullGuard.NotNull(analyzerOptions, nameof(analyzerOptions));
        configuration = ArgumentNullGuard.NotNull(configuration, nameof(configuration));
        var issues = ImmutableArray.CreateBuilder<CompilerConfigurationIssue>();
        foreach (var invalidValue in configuration.InvalidConfigurationValues)
        {
            issues.Add(new(invalidValue, Location.None));
        }
        // A failed options provider cannot be queried again safely. Other
        // global configuration errors are independent from tree-local values,
        // so continue collecting those values instead of hiding them behind
        // the first global diagnostic.
        var optionsProviderFailed = configuration.InvalidConfigurationValues
            .Any(static invalidValue => string.Equals(
                invalidValue.Key,
                "AnalyzerConfigOptionsProvider",
                StringComparison.Ordinal));
        if (optionsProviderFailed)
        {
            return issues.ToImmutable();
        }

        try
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var options = analyzerOptions.AnalyzerConfigOptionsProvider
                    .GetOptions(tree);
                var invalidValues =
                    CompilerConfiguration.GetInvalidTreeConfigurationValues(
                        options,
                        analyzerOptions.AnalyzerConfigOptionsProvider.GlobalOptions);
                foreach (var invalidValue in invalidValues)
                {
                    issues.Add(new(invalidValue, Location.Create(tree, new TextSpan(0, 0))));
                }
            }
        }
        catch (AggregateException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            issues.Add(new(CompilerConfiguration.ProviderFailure(exception), Location.None));
        }

        return issues.ToImmutable();
    }
}
