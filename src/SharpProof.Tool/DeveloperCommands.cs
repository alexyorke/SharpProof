using System.Text.Json;
using SharpProof.Reporting;

namespace SharpProof.Tool;

internal static class DeveloperCommands
{
    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                await output.WriteLineAsync(("SharpProof developer tools\n  doctor --project <csproj> [--verification portable|container]\n  explain --result <json> [--claim <id>]\n  compare --baseline <json> --current <json> [--fail-on-regression]\n  export-test --project <csproj> --result <json> --claim <id> --output <new-directory>\nEvidence paths: --request <json> --compiler-manifest <json>; comparison also accepts --baseline-request and --baseline-compiler-manifest. Exit codes: 0 success, 1 setup/regression failure, 2 invalid arguments/evidence.").AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }

            var options = CommandOptions.Parse(args);
            switch (args[0])
            {
                case "doctor":
                    options.Allow("project", "verification");
                    return await SetupDoctor.RunAsync(options.Required("project"), options.Get("verification"), output, cancellationToken).ConfigureAwait(false);
                case "explain":
                    options.Allow("result", "request", "compiler-manifest", "claim");
                    var evidence = EvidenceSet.Load(options.Required("result"), options.Get("request"), options.Get("compiler-manifest"), cancellationToken);
                    VerificationReport.Write(output, evidence.Response, options.Get("claim"));
                    return 0;
                case "compare":
                    options.Allow("baseline", "current", "request", "compiler-manifest", "baseline-request", "baseline-compiler-manifest", "fail-on-regression");
                    var baselinePath = options.Required("baseline");
                    var currentPath = options.Required("current");
                    var baseline = EvidenceSet.Load(baselinePath, options.Get("baseline-request"), options.Get("baseline-compiler-manifest"), cancellationToken);
                    var current = EvidenceSet.Load(currentPath, options.Get("request"), options.Get("compiler-manifest"), cancellationToken);
                    if (string.IsNullOrWhiteSpace(baseline.Artifact.Compilation.AssemblyIdentity) || string.IsNullOrWhiteSpace(baseline.Artifact.Compilation.TargetFramework) ||
                        baseline.Artifact.Compilation.AssemblyIdentity != current.Artifact.Compilation.AssemblyIdentity || baseline.Artifact.Compilation.TargetFramework != current.Artifact.Compilation.TargetFramework)
                    {
                        throw new InvalidDataException("Compare evidence for the same assembly identity and target framework.");
                    }
                    if (baseline.Artifact.SpecificationPackCatalogSha256 != current.Artifact.SpecificationPackCatalogSha256 ||
                        !baseline.Artifact.SpecificationPackIds.Order(StringComparer.Ordinal).SequenceEqual(current.Artifact.SpecificationPackIds.Order(StringComparer.Ordinal)))
                    {
                        throw new InvalidDataException("Compare evidence using the same specification-pack selection and catalog.");
                    }

                    if (baseline.Request.VerifyPolicy != current.Request.VerifyPolicy || baseline.Request.AssumptionPolicy != current.Request.AssumptionPolicy)
                    {
                        throw new InvalidDataException("Compare evidence using the same verification and assumption policies.");
                    }

                    var baselineBudgets = baseline.Request.Budgets;
                    var currentBudgets = current.Request.Budgets;
                    if (baselineBudgets.QueryRlimit != currentBudgets.QueryRlimit || baselineBudgets.MethodRlimit != currentBudgets.MethodRlimit ||
                        baselineBudgets.MethodWallTimeMilliseconds != currentBudgets.MethodWallTimeMilliseconds || baselineBudgets.ProjectWallTimeMilliseconds != currentBudgets.ProjectWallTimeMilliseconds ||
                        baselineBudgets.MaxParallelism != currentBudgets.MaxParallelism || baselineBudgets.MaximumExpressionDepth != currentBudgets.MaximumExpressionDepth)
                    {
                        await output.WriteLineAsync("NOTE Verification budgets differ; outcome changes may reflect different resource limits.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                    if (baseline.Response.Summary.Versions != current.Response.Summary.Versions)
                    {
                        await output.WriteLineAsync("NOTE Worker or API-spec versions differ; outcome changes may reflect changes in verification semantics.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    }

                    var changes = VerificationComparison.Compare(baseline.Response, current.Response);
                    foreach (var change in changes)
                    {
                        await output.WriteLineAsync(($"{(change.IsRegression ? "REGRESSION" : "CHANGE")} {change.Kind}: {change.Identity}").AsMemory(), cancellationToken).ConfigureAwait(false);
                    }

                    await output.WriteLineAsync(($"{changes.Count} changes; {changes.Count(static change => { global::SharpProof.Internal.InputValidation.Require(change, nameof(change)); return change.IsRegression; })} regressions. Existing verifier policy still applies.").AsMemory(), cancellationToken).ConfigureAwait(false);
                    return options.Has("fail-on-regression") && changes.Any(static change =>
                    {
                        global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
                        return change.IsRegression;
                    }) ? 1 : 0;
                case "export-test":
                    options.Allow("project", "result", "request", "compiler-manifest", "claim", "output");
                    var projectPath = options.Required("project");
                    var resultPath = options.Required("result");
                    var claimId = options.Required("claim");
                    var outputPath = options.Required("output");
                    var exported = EvidenceSet.Load(resultPath, options.Get("request"), options.Get("compiler-manifest"), cancellationToken);
                    await NUnitExporter.ExportAsync(projectPath, exported, claimId, outputPath, cancellationToken).ConfigureAwait(false);
                    await output.WriteLineAsync(("NUnit regression project exported. Consumer code was not executed. Run dotnet test in the canonical container.").AsMemory(), cancellationToken).ConfigureAwait(false);
                    return 0;
                default:
                    throw new ArgumentException("Unknown command. Run sharpproof --help.");
            }
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or BadImageFormatException)
        {
            await error.WriteLineAsync(("SharpProof: " + exception.Message).AsMemory(), cancellationToken).ConfigureAwait(false);
            return 2;
        }
    }
}

internal sealed class CommandOptions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
    internal static CommandOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new CommandOptions();
        for (var index = 1; index < args.Length; index++)
        {
            var key = args[index];
            if (!key.StartsWith("--", StringComparison.Ordinal) || key.Length == 2)
            {
                throw new ArgumentException("Expected a named --option.");
            }

            key = key[2..];
            var value = "true";
            if (key != "fail-on-regression")
            {
                if (++index == args.Length || args[index].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException("Missing value for --" + key);
                }

                value = args[index];
            }

            if (!options._values.TryAdd(key, value))
            {
                throw new ArgumentException("Duplicate option --" + key);
            }
        }

        return options;
    }

    internal void Allow(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var unknown = _values.Keys.FirstOrDefault(key =>
        {
            global::SharpProof.Internal.InputValidation.Require(key, nameof(key));
            return !keys.Contains(key, StringComparer.Ordinal);
        });
        if (unknown != null)
        {
            throw new ArgumentException("Unknown option --" + unknown);
        }
    }

    internal string? Get(string key)
    {
        global::SharpProof.Internal.InputValidation.Require(key, nameof(key));
        return _values.TryGetValue(key, out var value) ? value : null;
    }

    internal bool Has(string key)
    {
        global::SharpProof.Internal.InputValidation.Require(key, nameof(key));
        return _values.ContainsKey(key);
    }

    internal string Required(string key)
    {
        return Get(key) is { } value && !string.IsNullOrWhiteSpace(value) ? value : throw new ArgumentException("Required option --" + key);
    }
}
