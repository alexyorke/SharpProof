using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpProof.Reporting;

namespace SharpProof.Tool;

internal static class DeveloperCommands
{
    internal static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        return RunAsync(args, output, error, SdkProbe.ReadAsync, cancellationToken);
    }

    internal static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error,
        Func<CancellationToken, Task<string>> discoverSdk, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(discoverSdk);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (args.Length == 0 || args is ["--help"] or ["-h"])
            {
                await output.WriteLineAsync(("SharpProof developer tools\n  init --project <csproj> [--verification portable|container] [--apply] [--consumer-image <digest-pinned-image>]\n  doctor [--project <csproj> [--verification portable|container]]\n  explain --result <json> [--claim <id>]\n  compare --baseline <json> --current <json> [--fail-on-regression]\n  export-test --project <csproj> --result <json> --claim <id> --output <new-directory> [--workspace <root>]\nNamed commands accept --format text|json|markdown (default text). Init previews setup unless --apply is present. Evidence paths: --request <json> --compiler-manifest <json>; comparison also accepts --baseline-request and --baseline-compiler-manifest. Exit codes: 0 success, 1 setup/regression failure, 2 invalid arguments/evidence.").AsMemory(), cancellationToken).ConfigureAwait(false);
                return 0;
            }

            var options = CommandOptions.Parse(args);
            var format = CommandPresentationFormatter.ParseFormat(options.Get("format"));
            using var rendered = new StringWriter(CultureInfo.InvariantCulture) { NewLine = output.NewLine };
            CommandPresentation presentation;
            int exitCode;
            switch (args[0])
            {
                case "init":
                    options.Allow("project", "verification", "apply", "consumer-image", "format");
                    var initialized = await SetupInitializer.InitializeAsync(options.Required("project"), options.Get("verification") ?? "portable",
                        options.Has("apply"), options.Get("consumer-image"), cancellationToken).ConfigureAwait(false);
                    await rendered.WriteAsync(initialized.Message.AsMemory(), cancellationToken).ConfigureAwait(false);
                    var generated = initialized.Files.Select(static file =>
                    {
                        ArgumentNullException.ThrowIfNull(file);
                        return new CommandGeneratedFile(file.Path, file.Written, file.Changed);
                    }).ToArray();
                    exitCode = 0;
                    presentation = CommandPresentation.CreateMessage("init", exitCode, rendered.ToString(),
                        "Project setup plan; no restore, build, proof, or consumer execution.", generatedFiles: generated);
                    break;
                case "doctor" when !options.Has("project"):
                    options.Allow("format");
                    var sdk = (await discoverSdk(cancellationToken).ConfigureAwait(false)).Trim();
                    await rendered.WriteLineAsync(FormattableString.Invariant(
                        $"SDK={sdk}; runtime={Environment.Version}; RID={RuntimeInformation.RuntimeIdentifier}; OS={RuntimeInformation.OSArchitecture}; process={RuntimeInformation.ProcessArchitecture}.").AsMemory(), cancellationToken).ConfigureAwait(false);
                    await rendered.WriteLineAsync("SDK discovery only; no project evaluation, restore, build, or solver execution.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    await rendered.WriteLineAsync("Build your consumer with SharpProof, or pass --project, to check its actual configuration and native solver.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    exitCode = 0;
                    presentation = CommandPresentation.CreateMessage("doctor", exitCode, rendered.ToString(),
                        "SDK discovery only; no project evaluation, restore, build, or solver execution.");
                    break;
                case "doctor":
                    options.Allow("project", "verification", "format");
                    exitCode = await SetupDoctor.RunAsync(options.Required("project"), options.Get("verification"), rendered, cancellationToken).ConfigureAwait(false);
                    presentation = CommandPresentation.CreateMessage("doctor", exitCode, rendered.ToString());
                    break;
                case "explain":
                    options.Allow("result", "request", "compiler-manifest", "claim", "format");
                    var explained = EvidenceSet.LoadExplainable(options.Required("result"), options.Get("request"), options.Get("compiler-manifest"), cancellationToken);
                    VerificationReport.Write(rendered, explained, options.Get("claim"));
                    exitCode = 0;
                    presentation = CommandPresentation.CreateExplain(explained, options.Get("claim"), exitCode, rendered.ToString());
                    break;
                case "compare":
                    options.Allow("baseline", "current", "request", "compiler-manifest", "baseline-request", "baseline-compiler-manifest", "fail-on-regression", "format");
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

                    var notes = new List<string>();
                    var baselineBudgets = baseline.Request.Budgets;
                    var currentBudgets = current.Request.Budgets;
                    if (baselineBudgets.QueryRlimit != currentBudgets.QueryRlimit || baselineBudgets.MethodRlimit != currentBudgets.MethodRlimit ||
                        baselineBudgets.MethodWallTimeMilliseconds != currentBudgets.MethodWallTimeMilliseconds || baselineBudgets.ProjectWallTimeMilliseconds != currentBudgets.ProjectWallTimeMilliseconds ||
                        baselineBudgets.MaxParallelism != currentBudgets.MaxParallelism || baselineBudgets.MaximumExpressionDepth != currentBudgets.MaximumExpressionDepth)
                    {
                        notes.Add("NOTE Verification budgets differ; outcome changes may reflect different resource limits.");
                    }
                    if (baseline.Response.Summary.Versions != current.Response.Summary.Versions)
                    {
                        notes.Add("NOTE Worker or API-spec versions differ; outcome changes may reflect changes in verification semantics.");
                    }

                    var changes = VerificationComparison.Compare(baseline.Response, current.Response);
                    foreach (var note in notes)
                    {
                        await rendered.WriteLineAsync(note.AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                    foreach (var change in changes)
                    {
                        await rendered.WriteLineAsync(($"{(change.IsRegression ? "REGRESSION" : "CHANGE")} {change.Kind}: {change.Identity}").AsMemory(), cancellationToken).ConfigureAwait(false);
                    }
                    await rendered.WriteLineAsync(($"{changes.Count} changes; {changes.Count(static change => { global::SharpProof.Internal.InputValidation.Require(change, nameof(change)); return change.IsRegression; })} regressions. Existing verifier policy still applies.").AsMemory(), cancellationToken).ConfigureAwait(false);
                    exitCode = options.Has("fail-on-regression") && changes.Any(static change =>
                    {
                        global::SharpProof.Internal.InputValidation.Require(change, nameof(change));
                        return change.IsRegression;
                    }) ? 1 : 0;
                    presentation = CommandPresentation.CreateCompare(changes, notes, exitCode, rendered.ToString());
                    break;
                case "export-test":
                    options.Allow("project", "result", "request", "compiler-manifest", "claim", "output", "workspace", "format");
                    var projectPath = options.Required("project");
                    var resultPath = options.Required("result");
                    var claimId = options.Required("claim");
                    var outputPath = options.Required("output");
                    var exported = EvidenceSet.Load(resultPath, options.Get("request"), options.Get("compiler-manifest"), cancellationToken);
                    var workspacePath = options.Get("workspace");
                    await NUnitExporter.ExportAsync(projectPath, exported, claimId, outputPath, workspacePath, cancellationToken).ConfigureAwait(false);
                    await rendered.WriteLineAsync("NUnit regression project exported. Consumer code was not executed. Run dotnet test in the canonical container.".AsMemory(), cancellationToken).ConfigureAwait(false);
                    var exportedFiles = new List<CommandGeneratedFile>
                    {
                        new(Path.GetFullPath(Path.Combine(outputPath, "Regression.cs"))),
                        new(Path.GetFullPath(Path.Combine(outputPath, "SharpProof.Regression.csproj")))
                    };
                    if (workspacePath != null)
                    { exportedFiles.Add(new(Path.GetFullPath(Path.Combine(outputPath, "README.md")))); }
                    exitCode = 0;
                    presentation = CommandPresentation.CreateMessage("export-test", exitCode, rendered.ToString(),
                        "Generated from validated stored evidence; consumer code was not executed.", generatedFiles: exportedFiles);
                    break;
                default:
                    throw new ArgumentException("Unknown command. Run sharpproof --help.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            await CommandPresentationFormatter.WriteAsync(output, presentation, format, cancellationToken).ConfigureAwait(false);
            return exitCode;
        }
        catch (OperationCanceledException) { return 130; }
        catch (Exception exception) when (exception is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or BadImageFormatException or Win32Exception or TimeoutException)
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
            if (key is not ("fail-on-regression" or "apply"))
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
