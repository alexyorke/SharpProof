using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using SharpProof.Worker.Protocol;

namespace SharpProof.Tool;

internal static class ToolApplication
{
    internal const string Scope = "Stored evidence only: current source, request authority, and proof freshness are not established.";
    internal const string Help = """
        SharpProof optional diagnostics tool
        Usage:
          dotnet sharpproof doctor [--json]
          dotnet sharpproof explain <result.json> [--file <exact recorded path>] [--line <positive line>] [--json]
          dotnet sharpproof doctor --project <csproj> [--verification portable|container]
          dotnet sharpproof explain --result <result.json> [--claim <id>]
          dotnet sharpproof compare --baseline <result.json> --current <result.json> [--fail-on-regression]
          dotnet sharpproof export-test --project <csproj> --result <result.json> --claim <id> --output <new-directory>
          dotnet sharpproof --help
        doctor without --project discovers SDK/runtime information without evaluating projects. With --project it checks evaluated configuration and existing restore evidence. Neither mode restores, builds, or loads the solver.
        explain validates the stored worker protocol and explains recorded outcomes. It does not verify current source.
        File matching is ordinal against the exact recorded path; --line matches the recorded claim/callable start line.
        Install this tool optionally. A normal SharpProof PackageReference already enables build verification.
        """;

    internal static async Task<int> RunAsync(
        ImmutableArray<string> args, TextWriter output, TextWriter error, Func<CancellationToken, Task<string>> discoverSdk,
        CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfMissing(args.IsDefault, nameof(args));
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(discoverSdk);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (args is ["compare", ..] or ["export-test", ..] ||
                args is ["doctor", ..] && args is not (["doctor"] or ["doctor", "--json"]) ||
                args is ["explain", var firstOption, ..] && firstOption.StartsWith("--", StringComparison.Ordinal))
            {
                return await DeveloperCommands.RunAsync([.. args], output, error, cancellationToken).ConfigureAwait(false);
            }
            if (args.Length == 0 || args is ["--help"])
            {
                await WriteLineAsync(output, Help, cancellationToken).ConfigureAwait(false);
                return 0;
            }
            if (args[0] == "doctor")
            {
                if (args.Length > 2 || args.Length == 2 && args[1] != "--json")
                {
                    throw new ArgumentException("doctor accepts only --json.");
                }
                var sdk = await discoverSdk(cancellationToken).ConfigureAwait(false);
                var report = new
                {
                    scope = "SDK discovery only; no project evaluation, restore, build, or solver execution.",
                    sdk = sdk.Trim(),
                    runtime = Environment.Version.ToString(),
                    rid = RuntimeInformation.RuntimeIdentifier,
                    osArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                    processArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                    next = "Build your consumer with SharpProof to check its actual configuration and native solver."
                };
                await WriteLineAsync(output, args.Length == 2 ? JsonSerializer.Serialize(report) :
                    $"SharpProof doctor: SDK={report.sdk}; runtime={report.runtime}; RID={report.rid}; " +
                    $"OS={report.osArchitecture}; process={report.processArchitecture}.\n{report.scope}\n{report.next}", cancellationToken).ConfigureAwait(false);
                return 0;
            }
            if (args[0] != "explain" || args.Length < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Expected doctor or explain <result.json>. Use --help for syntax.");
            }
            return await ExplainAsync(args, output, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or
            JsonException or InvalidDataException or InvalidOperationException or Win32Exception or TimeoutException)
        {
            await WriteLineAsync(error, "SharpProof: " + exception.Message, cancellationToken).ConfigureAwait(false);
            return 2;
        }
    }

    private static async Task<int> ExplainAsync(ImmutableArray<string> args, TextWriter output, CancellationToken cancellationToken)
    {
        RepositoryParameterGuard.ThrowIfMissing(args.IsDefault, nameof(args));
        ArgumentNullException.ThrowIfNull(output);
        string? file = null;
        int? line = null;
        var json = false;
        var seen = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        for (var index = 2; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option))
            {
                throw new ArgumentException("Duplicate option: " + option);
            }
            if (option == "--json")
            {
                json = true;
            }
            else if ((option == "--file" || option == "--line") && ++index < args.Length)
            {
                if (option == "--file")
                {
                    file = args[index];
                }
                else if (int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                {
                    line = parsed;
                }
                else
                {
                    throw new ArgumentException("--line requires a positive integer.");
                }
            }
            else
            {
                throw new ArgumentException("Unknown or incomplete option: " + option);
            }
        }
        var response = WorkerProtocolJson.DeserializeResponse(
            await WorkerProtocolJson.ReadUtf8FileAsync(args[1], cancellationToken).ConfigureAwait(false));
        var validation = WorkerProtocolJson.Validate(response);
        if (!validation.IsValid)
        {
            throw new InvalidDataException("The stored result does not satisfy the SharpProof worker protocol.");
        }
        ArgumentNullException.ThrowIfNull(response);
        var entryBuffer = ImmutableArray.CreateBuilder<ExplanationEntry>();
        var claimBuffer = ImmutableDictionary.CreateBuilder<string, WorkerClaimManifestEntry>(StringComparer.Ordinal);
        foreach (var claim in response.Manifest.Claims)
        {
            ArgumentNullException.ThrowIfNull(claim);
            RepositoryParameterGuard.RequireUniqueKey(claimBuffer.ContainsKey(claim.ClaimId), claim.ClaimId);
            claimBuffer.Add(claim.ClaimId, claim);
        }
        var callableBuffer = ImmutableDictionary.CreateBuilder<string, WorkerCallableManifestEntry>(StringComparer.Ordinal);
        foreach (var callable in response.Manifest.Callables)
        {
            ArgumentNullException.ThrowIfNull(callable);
            RepositoryParameterGuard.RequireUniqueKey(callableBuffer.ContainsKey(callable.CallableId), callable.CallableId);
            callableBuffer.Add(callable.CallableId, callable);
        }
        var claims = claimBuffer.ToImmutable();
        var callables = callableBuffer.ToImmutable();
        foreach (var result in response.ClaimResults)
        {
            ArgumentNullException.ThrowIfNull(result);
            var claim = claims[result.ClaimId];
            Add(entryBuffer, claim.Location, file, line, "claim", result.ClaimId,
                result.Outcome.ToString(), result.Reason.ToString(), result.Vacuity.ToString(), result.Assumptions.Length);
        }
        foreach (var result in response.CallableResults)
        {
            ArgumentNullException.ThrowIfNull(result);
            var callable = callables[result.CallableId];
            Add(entryBuffer, callable.Location, file, line, "callable", result.CallableId,
                result.Coverage.ToString(), result.Reason.ToString(), "NotApplicable", result.Assumptions.Length);
        }
        var entries = entryBuffer.ToImmutable();
        if (json)
        {
            await WriteLineAsync(output, JsonSerializer.Serialize(new
            {
                scope = Scope,
                resultPath = Path.GetFullPath(args[1]),
                runStatus = response.RunStatus.ToString(),
                failureReason = response.FailureReason.ToString(),
                selectedCallables = response.Manifest.Callables.Length,
                selectedClaims = response.Manifest.Claims.Length,
                elapsedMilliseconds = response.Summary.ElapsedMilliseconds,
                cache = response.Summary.CacheStatus.ToString(),
                errors = response.Errors,
                entries,
                help = "https://github.com/alexyorke/SharpProof/blob/master/docs/unknown-reasons.md"
            }), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await WriteLineAsync(output, Scope, cancellationToken).ConfigureAwait(false);
            await WriteLineAsync(output,
                FormattableString.Invariant($"Run={response.RunStatus} ({response.FailureReason}); ") +
                FormattableString.Invariant($"selected callables={response.Manifest.Callables.Length}, claims={response.Manifest.Claims.Length}; ") +
                FormattableString.Invariant($"elapsed={response.Summary.ElapsedMilliseconds} ms; cache={response.Summary.CacheStatus}."), cancellationToken).ConfigureAwait(false);
            foreach (var entry in entries)
            {
                await WriteLineAsync(output,
                    FormattableString.Invariant($"{entry.Path}({entry.Line},{entry.Column}): {entry.Kind} {entry.Id}: ") +
                    FormattableString.Invariant($"{entry.Outcome} ({entry.Reason}); vacuity={entry.Vacuity}; recorded assumptions={entry.Assumptions}."), cancellationToken).ConfigureAwait(false);
            }
            foreach (var failure in response.Errors)
            {
                ArgumentNullException.ThrowIfNull(failure);
                await WriteLineAsync(output, failure.Code + ": " + failure.Message, cancellationToken).ConfigureAwait(false);
            }
            await WriteLineAsync(output, "Matching entries: " + entries.Length.ToString(CultureInfo.InvariantCulture) +
                ". Reason guidance: https://github.com/alexyorke/SharpProof/blob/master/docs/unknown-reasons.md", cancellationToken).ConfigureAwait(false);
        }
        return 0;
    }

    private static async Task WriteLineAsync(TextWriter writer, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(text);
        await writer.WriteLineAsync(text.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static void Add(ImmutableArray<ExplanationEntry>.Builder entries, WorkerSourceLocation location, string? file, int? line,
        string kind, string id, string outcome, string reason, string vacuity, int assumptions)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(location);
        ArgumentNullException.ThrowIfNull(kind);
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(reason);
        ArgumentNullException.ThrowIfNull(vacuity);
        if ((file is null || string.Equals(file, location.Path, StringComparison.Ordinal)) && (line is null || line == location.Line))
        {
            entries.Add(new(location.Path, location.Line, location.Column, kind, id, outcome, reason, vacuity, assumptions));
        }
    }

    private sealed record ExplanationEntry
    {
        internal ExplanationEntry(string path, int line, int column, string kind, string id,
            string outcome, string reason, string vacuity, int assumptions)
        {
            ArgumentNullException.ThrowIfNull(path);
            ArgumentNullException.ThrowIfNull(kind);
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(outcome);
            ArgumentNullException.ThrowIfNull(reason);
            ArgumentNullException.ThrowIfNull(vacuity);
            Path = path;
            Line = line;
            Column = column;
            Kind = kind;
            Id = id;
            Outcome = outcome;
            Reason = reason;
            Vacuity = vacuity;
            Assumptions = assumptions;
        }

        public string Path { get; }
        public int Line { get; }
        public int Column { get; }
        public string Kind { get; }
        public string Id { get; }
        public string Outcome { get; }
        public string Reason { get; }
        public string Vacuity { get; }
        public int Assumptions { get; }
    }
}
