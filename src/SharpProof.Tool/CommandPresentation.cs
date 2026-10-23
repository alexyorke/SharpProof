using System.Collections.Immutable;
using System.Globalization;
using SharpProof.Reporting;
using SharpProof.Worker.Protocol;

namespace SharpProof.Tool;

// Command output is a separate, versioned projection. It never supplies worker proof authority.
internal sealed class CommandPresentation
{
    internal const string SchemaName = "SharpProof.CommandPresentation";
    internal const int CurrentSchemaVersion = 1;
    internal const string RetainedEvidenceScope = "Retained verification evidence only; current source and source freshness are not established.";

    private CommandPresentation(string command, int exitCode, string text, string scope,
        ImmutableArray<CommandOutcome> outcomes, ImmutableArray<CommandIssue> issues,
        ImmutableArray<CommandGeneratedFile> generatedFiles, ImmutableArray<string> notes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentOutOfRangeException.ThrowIfNegative(exitCode);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        RepositoryParameterGuard.ThrowIfMissing(outcomes.IsDefault, nameof(outcomes));
        RepositoryParameterGuard.ThrowIfMissing(issues.IsDefault, nameof(issues));
        RepositoryParameterGuard.ThrowIfMissing(generatedFiles.IsDefault, nameof(generatedFiles));
        RepositoryParameterGuard.ThrowIfMissing(notes.IsDefault, nameof(notes));
        Command = command;
        ExitCode = exitCode;
        Text = text;
        Scope = scope;
        Outcomes = outcomes;
        Issues = issues;
        GeneratedFiles = generatedFiles;
        Notes = notes;
        var messages = ImmutableArray.CreateBuilder<string>();
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } message)
        {
            messages.Add(message);
        }
        Messages = messages.ToImmutable();
    }

    public string Schema { get; } = SchemaName;
    public int SchemaVersion { get; } = CurrentSchemaVersion;
    public string Command { get; }
    public int ExitCode { get; }
    public string Status => ExitCode switch { 0 => "completed", 1 => "findings", 130 => "canceled", _ => "failed" };
    public string Scope { get; }
    public ImmutableArray<string> Messages { get; }
    public ImmutableArray<string> Notes { get; }
    public ImmutableArray<CommandOutcome> Outcomes { get; }
    public ImmutableArray<CommandIssue> Issues { get; }
    public ImmutableArray<CommandGeneratedFile> GeneratedFiles { get; }
    internal string Text { get; }

    internal static CommandPresentation CreateMessage(string command, int exitCode, string text, string? scope = null,
        IReadOnlyList<CommandIssue>? issues = null, IReadOnlyList<CommandGeneratedFile>? generatedFiles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(text);
        var projectedIssues = ImmutableArray.CreateBuilder<CommandIssue>();
        var outcomes = ImmutableArray.CreateBuilder<CommandOutcome>();
        if (command == "doctor")
        {
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } line)
            {
                if (line.StartsWith("PASS ", StringComparison.Ordinal) || line.StartsWith("FAIL ", StringComparison.Ordinal))
                {
                    var passed = line.StartsWith("PASS ", StringComparison.Ordinal);
                    var message = line.Substring(5);
                    outcomes.Add(new("check", message, passed ? "Pass" : "Fail", string.Empty));
                    if (!passed)
                    {
                        projectedIssues.Add(new("error", "doctor.check_failed", message));
                    }
                }
                else if (line.StartsWith("WARN ", StringComparison.Ordinal))
                {
                    projectedIssues.Add(new("warning", "doctor.warning", line.Substring(5)));
                }
            }
        }
        return new(command, exitCode, text, scope ?? DefaultScope(command), outcomes.ToImmutable(),
            issues is null ? projectedIssues.ToImmutable() : Capture(issues), Capture(generatedFiles), []);
    }

    internal static CommandPresentation CreateExplain(WorkerVerifyResponse response, string? claimId, int exitCode, string text)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(text);
        var claims = response.Manifest.Claims.ToDictionary(static claim =>
        {
            ArgumentNullException.ThrowIfNull(claim);
            return claim.ClaimId;
        }, StringComparer.Ordinal);
        if (claimId != null && !claims.ContainsKey(claimId))
        {
            throw new ArgumentException("The requested claim is absent from this result.", nameof(claimId));
        }
        var callables = response.Manifest.Callables.ToDictionary(static callable =>
        {
            ArgumentNullException.ThrowIfNull(callable);
            return callable.CallableId;
        }, StringComparer.Ordinal);
        var outcomes = ImmutableArray.CreateBuilder<CommandOutcome>();
        var issues = ImmutableArray.CreateBuilder<CommandIssue>();
        outcomes.Add(new("run", "run", response.RunStatus.ToString(), response.FailureReason.ToString(), details:
        [
            new("requestHash", response.RequestHash), new("inputHash", response.InputHash),
            new("cacheStatus", response.Summary.CacheStatus.ToString()),
            new("elapsedMilliseconds", response.Summary.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture))
        ]));
        foreach (var result in response.ClaimResults)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (claimId != null && result.ClaimId != claimId)
            {
                continue;
            }
            var claim = claims[result.ClaimId];
            var location = CommandLocation.From(claim.Location);
            var details = ImmutableArray.CreateBuilder<CommandDetail>();
            details.Add(new("callableId", claim.CallableId));
            details.Add(new("claimKind", claim.Kind.ToString()));
            details.Add(new("vacuity", result.Vacuity.ToString()));
            details.Add(new("effectCertainty", result.EffectCertainty.ToString()));
            if (result.Presentation is { } presentation)
            {
                details.Add(new("displayName", presentation.Name));
                for (var index = 0; index < presentation.ParameterNames.Length; index++)
                {
                    details.Add(new("parameter:" + index.ToString(CultureInfo.InvariantCulture), presentation.ParameterNames[index]));
                }
            }
            if (result.Diagnostic is { } diagnostic)
            {
                var diagnosticLocation = CommandLocation.From(diagnostic.Location);
                details.Add(new("diagnosticStage", diagnostic.Stage));
                details.Add(new("diagnosticCode", diagnostic.Code));
                details.Add(new("diagnosticLocation", diagnosticLocation.Display(), diagnosticLocation));
            }
            foreach (var value in result.Model)
            {
                ArgumentNullException.ThrowIfNull(value);
                details.Add(new("model:" + value.Variable, value.Value + " (" + value.Kind + ")"));
            }
            AddAssumptions(details, result.Assumptions);
            if (result.EffectWitness is { } witness)
            {
                var witnessLocation = CommandLocation.From(witness.Location);
                details.Add(new("effectWitnessKind", witness.Kind));
                details.Add(new("effectWitnessDetail", witness.Detail));
                details.Add(new("effectWitnessLocation", witnessLocation.Display(), witnessLocation));
            }
            if (result.Outcome == WorkerClaimOutcome.Unknown)
            {
                var explanation = ReasonCatalog.Explain(result.Reason);
                details.Add(new("nextAction", explanation.NextAction));
                details.Add(new("help", ReasonExplanation.Documentation));
                issues.Add(new("warning", "claim." + result.Reason, explanation.Description, location));
            }
            else if (result.Outcome == WorkerClaimOutcome.Refuted)
            {
                issues.Add(new("error", "claim.refuted", "Recorded claim is Refuted.", location));
            }
            if (result.Vacuity != WorkerVacuityKind.None)
            {
                issues.Add(new("warning", "claim.vacuity", "Review vacuity: " + result.Vacuity, location));
            }
            outcomes.Add(new("claim", result.ClaimId, result.Outcome.ToString(), result.Reason.ToString(), location, details: details.ToImmutable()));
        }
        foreach (var result in response.CallableResults)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (claimId != null && claims[claimId].CallableId != result.CallableId)
            {
                continue;
            }
            var location = CommandLocation.From(callables[result.CallableId].Location);
            var details = ImmutableArray.CreateBuilder<CommandDetail>();
            AddAssumptions(details, result.Assumptions);
            if (result.Coverage == WorkerCallableCoverage.Incomplete)
            {
                var explanation = ReasonCatalog.Explain(result.Reason);
                details.Add(new("nextAction", explanation.NextAction));
                issues.Add(new("warning", "coverage." + result.Reason, explanation.Description, location));
            }
            outcomes.Add(new("callable", result.CallableId, result.Coverage.ToString(), result.Reason.ToString(), location, details: details.ToImmutable()));
        }
        foreach (var error in response.Errors)
        {
            ArgumentNullException.ThrowIfNull(error);
            issues.Add(new("error", error.Code, error.Message));
        }
        return new("explain", exitCode, text, RetainedEvidenceScope, outcomes.ToImmutable(), issues.ToImmutable(), [], []);
    }

    internal static CommandPresentation CreateCompare(IReadOnlyList<VerificationChange> changes, IReadOnlyList<string> notes, int exitCode, string text)
    {
        ArgumentNullException.ThrowIfNull(changes);
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(text);
        var outcomes = ImmutableArray.CreateBuilder<CommandOutcome>();
        var issues = ImmutableArray.CreateBuilder<CommandIssue>();
        foreach (var change in changes)
        {
            ArgumentNullException.ThrowIfNull(change);
            outcomes.Add(new("change", change.Identity, change.IsRegression ? "Regression" : "Change", change.Kind, isRegression: change.IsRegression));
            if (change.IsRegression)
            {
                issues.Add(new("warning", "comparison.regression", change.Kind + ": " + change.Identity));
            }
        }
        return new("compare", exitCode, text, RetainedEvidenceScope, outcomes.ToImmutable(), issues.ToImmutable(), [], Capture(notes));
    }

    private static ImmutableArray<T> Capture<T>(IReadOnlyList<T>? values) where T : class
    {
        if (values is null)
        {
            return [];
        }
        var result = ImmutableArray.CreateBuilder<T>(values.Count);
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value);
            result.Add(value);
        }
        return result.ToImmutable();
    }

    private static void AddAssumptions(ImmutableArray<CommandDetail>.Builder details, ImmutableArray<WorkerAssumptionEvidence> assumptions)
    {
        ArgumentNullException.ThrowIfNull(details);
        RepositoryParameterGuard.ThrowIfMissing(assumptions.IsDefault, nameof(assumptions));
        foreach (var assumption in assumptions)
        {
            ArgumentNullException.ThrowIfNull(assumption);
            details.Add(new("assumption:" + assumption.Id, assumption.Kind + (assumption.Used ? " (used)" : " (declared)")));
        }
    }

    private static string DefaultScope(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        return command switch
        {
            "doctor" => "Project setup inspection only; no restore, build, solver, or application execution.",
            "init" => "Starter file plan or application only; no restore, build, solver, or consumer application execution.",
            "export-test" => "Generated regression test source from retained evidence; consumer application code was not executed.",
            _ => "Command output only."
        };
    }
}

internal sealed class CommandOutcome
{
    internal CommandOutcome(string kind, string id, string outcome, string reason, CommandLocation? location = null,
        bool? isRegression = null, IReadOnlyList<CommandDetail>? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);
        ArgumentNullException.ThrowIfNull(reason);
        Kind = kind;
        Id = id;
        Outcome = outcome;
        Reason = reason;
        Location = location;
        IsRegression = isRegression;
        var values = ImmutableArray.CreateBuilder<CommandDetail>();
        if (details != null)
        {
            foreach (var detail in details)
            {
                ArgumentNullException.ThrowIfNull(detail);
                values.Add(detail);
            }
        }
        Details = values.ToImmutable();
    }
    public string Kind { get; }
    public string Id { get; }
    public string Outcome { get; }
    public string Reason { get; }
    public CommandLocation? Location { get; }
    public bool? IsRegression { get; }
    public ImmutableArray<CommandDetail> Details { get; }
}

internal sealed class CommandDetail
{
    internal CommandDetail(string name, string value, CommandLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        Name = name;
        Value = value;
        Location = location;
    }
    public string Name { get; }
    public string Value { get; }
    public CommandLocation? Location { get; }
}

internal sealed class CommandIssue
{
    internal CommandIssue(string severity, string code, string message, CommandLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(severity);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);
        if (severity is not ("info" or "warning" or "error"))
        {
            throw new ArgumentException("Issue severity must be info, warning, or error.", nameof(severity));
        }
        Severity = severity;
        Code = code;
        Message = message;
        Location = location;
    }
    public string Severity { get; }
    public string Code { get; }
    public string Message { get; }
    public CommandLocation? Location { get; }
}

internal sealed class CommandGeneratedFile
{
    internal CommandGeneratedFile(string path, bool written = true, bool changed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Written = written;
        Changed = changed;
    }
    public string Path { get; }
    public bool Written { get; }
    public bool Changed { get; }
}

internal sealed class CommandLocation
{
    internal CommandLocation(string path, int line, int column, int start = 0, int length = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(column);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Path = path;
        Line = line;
        Column = column;
        Start = start;
        Length = length;
    }
    public string Path { get; }
    public int Line { get; }
    public int Column { get; }
    public int Start { get; }
    public int Length { get; }
    internal string Display()
    {
        return FormattableString.Invariant($"{Path}({Line},{Column})");
    }
    internal static CommandLocation From(WorkerSourceLocation location)
    {
        ArgumentNullException.ThrowIfNull(location);
        return new(location.Path, location.Line, location.Column, location.Start, location.Length);
    }
}
