using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Reporting;
using SharpProof.Tool;
using SharpProof.Worker.Protocol;

namespace SharpProof.DeveloperExperience.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CommandPresentationFormattingTests
{
    [TestCase(null, CommandOutputFormat.Text)]
    [TestCase("text", CommandOutputFormat.Text)]
    [TestCase("json", CommandOutputFormat.Json)]
    [TestCase("markdown", CommandOutputFormat.Markdown)]
    public void FormatSelectionIsExplicitAndDefaultsToLegacyText(string? value, CommandOutputFormat expected)
    {
        Assert.That(CommandPresentationFormatter.ParseFormat(value), Is.EqualTo(expected));
    }

    [TestCase("")]
    [TestCase(" ")]
    [TestCase("JSON")]
    [TestCase("xml")]
    public void UnsupportedFormatsAreArgumentErrors(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var error = Assert.Throws<ArgumentException>(new Action(() => { _ = CommandPresentationFormatter.ParseFormat(value); }));
        Assert.That(error!.ParamName, Is.EqualTo("format"));
    }

    [TestCase("")]
    [TestCase("one line without a terminator")]
    [TestCase("first\r\n\r\nlast\r\n")]
    public async Task TextOutputPreservesEveryLegacyCharacter(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var presentation = CommandPresentation.CreateMessage("init", 0, text);
        Assert.That(await RenderAsync(presentation, CommandOutputFormat.Text), Is.EqualTo(text));
    }

    [TestCase("doctor")]
    [TestCase("explain")]
    [TestCase("compare")]
    [TestCase("export-test")]
    [TestCase("init")]
    public async Task JsonIsOneVersionedDocumentSeparateFromTheWorkerProtocol(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        var presentation = CommandPresentation.CreateMessage(command, 0, "Recorded message\n");
        var output = await RenderAsync(presentation, CommandOutputFormat.Json);
        using var json = JsonDocument.Parse(output);
        var root = json.RootElement;
        Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("SharpProof.CommandPresentation"));
        Assert.That(root.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(root.GetProperty("command").GetString(), Is.EqualTo(command));
        Assert.That(root.GetProperty("exitCode").GetInt32(), Is.Zero);
        Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("completed"));
        Assert.That(root.GetProperty("messages")[0].GetString(), Is.EqualTo("Recorded message"));
        Assert.That(root.TryGetProperty("protocolVersion", out _), Is.False);
        Assert.That(root.TryGetProperty("text", out _), Is.False);
        Assert.That(output, Does.StartWith("{").And.EndWith("}\n"));
    }

    [TestCase(0, "completed")]
    [TestCase(1, "findings")]
    [TestCase(2, "failed")]
    [TestCase(130, "canceled")]
    public async Task StatusDescribesTheCommandExitRatherThanClaimProof(int exitCode, string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var presentation = CommandPresentation.CreateMessage("custom", exitCode, string.Empty);
        using var json = JsonDocument.Parse(await RenderAsync(presentation, CommandOutputFormat.Json));
        Assert.That(json.RootElement.GetProperty("status").GetString(), Is.EqualTo(expected));
        var markdown = await RenderAsync(presentation, CommandOutputFormat.Markdown);
        Assert.That(markdown, Does.Contain("Status: " + expected).And.Not.Contain("## Output").And.Not.Contain("## Outcomes").And.Not.Contain("## Issues").And.Not.Contain("## Generated files"));
    }

    [Test]
    public async Task DoctorChecksAndWarningsRemainExplicitAcrossFormats()
    {
        const string text = "PASS Profile\nFAIL Features: choose supported features\nWARN Restore again\nUnclassified context\n\n";
        var presentation = CommandPresentation.CreateMessage("doctor", 1, text);
        Assert.That(presentation.Outcomes.Select(static outcome =>
        {
            ArgumentNullException.ThrowIfNull(outcome);
            return outcome.Outcome;
        }), Is.EqualTo(ImmutableArray.Create("Pass", "Fail")));
        Assert.That(presentation.Issues.Select(static issue =>
        {
            ArgumentNullException.ThrowIfNull(issue);
            return issue.Code;
        }), Is.EqualTo(ImmutableArray.Create("doctor.check_failed", "doctor.warning")));
        Assert.That(presentation.Scope, Does.Contain("no restore, build, solver"));
        Assert.That(await RenderAsync(presentation, CommandOutputFormat.Text), Is.EqualTo(text));
        using var json = JsonDocument.Parse(await RenderAsync(presentation, CommandOutputFormat.Json));
        Assert.That(json.RootElement.GetProperty("outcomes")[1].GetProperty("outcome").GetString(), Is.EqualTo("Fail"));
        Assert.That(json.RootElement.GetProperty("issues")[1].GetProperty("severity").GetString(), Is.EqualTo("warning"));
        var markdown = await RenderAsync(presentation, CommandOutputFormat.Markdown);
        Assert.That(markdown, Does.Contain("## Outcomes").And.Contain("## Issues").And.Contain("choose supported features").And.Contain("Unclassified context").And.Contain("> \n"));
    }

    [Test]
    public async Task FilesAndExplicitIssuesAreCopiedAndKeepPlannedWrittenAndUnchangedStates()
    {
        var location = new CommandLocation("Source.cs", 3, 4, 5, 6);
        var issues = new List<CommandIssue> { new("info", "init.plan", "Planned safely", location) };
        var files = new List<CommandGeneratedFile>
        {
            new("Planned.cs", written: false), new("Written.cs"), new("Existing.cs", written: false, changed: false)
        };
        var presentation = CommandPresentation.CreateMessage("init", 0, "File plan\n", "Explicit scope", issues, files);
        issues.Clear();
        files.Clear();
        using var json = JsonDocument.Parse(await RenderAsync(presentation, CommandOutputFormat.Json));
        var root = json.RootElement;
        Assert.That(root.GetProperty("scope").GetString(), Is.EqualTo("Explicit scope"));
        Assert.That(root.GetProperty("issues")[0].GetProperty("location").GetProperty("start").GetInt32(), Is.EqualTo(5));
        Assert.That(root.GetProperty("issues")[0].GetProperty("location").GetProperty("length").GetInt32(), Is.EqualTo(6));
        var generated = root.GetProperty("generatedFiles");
        Assert.That(generated.GetArrayLength(), Is.EqualTo(3));
        Assert.That(generated[0].GetProperty("written").GetBoolean(), Is.False);
        Assert.That(generated[0].GetProperty("changed").GetBoolean(), Is.True);
        Assert.That(generated[1].GetProperty("written").GetBoolean(), Is.True);
        Assert.That(generated[2].GetProperty("changed").GetBoolean(), Is.False);
        var markdown = await RenderAsync(presentation, CommandOutputFormat.Markdown);
        Assert.That(markdown, Does.Contain("Planned\\.cs | true | false").And.Contain("Written\\.cs | true | true").And.Contain("Existing\\.cs | false | false").And.Contain("Source\\.cs\\(3,4\\)"));
    }

    [Test]
    public async Task MarkdownEscapesRecordedMarkupAndTableBoundaries()
    {
        const string text = "[link](target) ![image](target) <script>x</script> & x|y \\path `code` *bold* _name_ {x} #+-!\r\nsecond";
        var presentation = CommandPresentation.CreateMessage("init", 0, text, generatedFiles: [new CommandGeneratedFile("a|b\n[x].cs", written: false)]);
        var markdown = await RenderAsync(presentation, CommandOutputFormat.Markdown);
        Assert.That(markdown, Does.Contain("\\[link\\]\\(target\\)").And.Contain("\\!\\[image\\]").And.Contain("&lt;script&gt;x&lt;/script&gt;").And.Contain("&amp;").And.Contain("x\\|y").And.Contain("\\\\path").And.Contain("\\`code\\`").And.Contain("\\*bold\\*").And.Contain("\\_name\\_").And.Contain("\\{x\\}").And.Contain("\\#\\+\\-\\!"));
        Assert.That(markdown, Does.Contain("a\\|b<br>\\[x\\]\\.cs").And.Not.Contain("<script>").And.Not.Contain("\r"));
    }

    [TestCase(WorkerClaimOutcome.Proven)]
    [TestCase(WorkerClaimOutcome.Refuted)]
    [TestCase(WorkerClaimOutcome.Unknown)]
    public async Task ExplainPreservesRecordedOutcomeReasonIdentityAndLocation(WorkerClaimOutcome outcome)
    {
        using var fixture = new StoredEvidenceFixture();
        var response = fixture.Response;
        if (outcome != WorkerClaimOutcome.Unknown)
        {
            response.ClaimResults = [response.ClaimResults[0] with { Outcome = outcome, Reason = WorkerClaimReason.None, Diagnostic = null }];
            response.CallableResults = [response.CallableResults[0] with { Coverage = WorkerCallableCoverage.Complete, Reason = WorkerCallableCoverageReason.None }];
        }
        if (outcome == WorkerClaimOutcome.Refuted)
        {
            response.ClaimResults[0].Model = [new() { Variable = "parameter:0", Kind = "Integer", Value = "-1" }];
        }
        using var legacy = new StringWriter(CultureInfo.InvariantCulture);
        VerificationReport.Write(legacy, response);
        var presentation = CommandPresentation.CreateExplain(response, null, 0, legacy.ToString());
        Assert.That(await RenderAsync(presentation, CommandOutputFormat.Text), Is.EqualTo(legacy.ToString()));
        using var json = JsonDocument.Parse(await RenderAsync(presentation, CommandOutputFormat.Json));
        var claims = json.RootElement.GetProperty("outcomes").EnumerateArray().Where(static entry => { global::SharpProof.Internal.InputValidation.Require(entry, nameof(entry)); return entry.GetProperty("kind").GetString() == "claim"; }).ToImmutableArray();
        Assert.That(claims, Has.Length.EqualTo(1));
        Assert.That(claims[0].GetProperty("id").GetString(), Is.EqualTo("claim:captured"));
        Assert.That(claims[0].GetProperty("outcome").GetString(), Is.EqualTo(outcome.ToString()));
        Assert.That(claims[0].GetProperty("reason").GetString(), Is.EqualTo(response.ClaimResults[0].Reason.ToString()));
        Assert.That(claims[0].GetProperty("location").GetProperty("path").GetString(), Is.EqualTo("Evidence.cs"));
        Assert.That(claims[0].GetProperty("location").GetProperty("line").GetInt32(), Is.EqualTo(1));
        Assert.That(claims[0].GetProperty("location").GetProperty("column").GetInt32(), Is.EqualTo(1));
        Assert.That(presentation.Scope, Is.EqualTo(CommandPresentation.RetainedEvidenceScope));
        var markdown = await RenderAsync(presentation, CommandOutputFormat.Markdown);
        Assert.That(markdown, Does.Contain(outcome.ToString()).And.Contain("claim:captured").And.Contain("Evidence\\.cs\\(1,1\\)"));
        if (outcome == WorkerClaimOutcome.Refuted)
        {
            Assert.That(claims[0].GetProperty("details").EnumerateArray().Any(static detail => { global::SharpProof.Internal.InputValidation.Require(detail, nameof(detail)); return detail.GetProperty("name").GetString() == "model:parameter:0" && detail.GetProperty("value").GetString() == "-1 (Integer)"; }), Is.True);
        }
        if (outcome == WorkerClaimOutcome.Unknown)
        {
            Assert.That(presentation.Outcomes[1].Details.Any(static detail =>
            {
                ArgumentNullException.ThrowIfNull(detail);
                return detail.Name == "diagnosticLocation" && detail.Location?.Path == "Evidence.cs";
            }), Is.True);
            Assert.That(presentation.Issues, Has.Length.EqualTo(2));
        }
    }

    [Test]
    public async Task ExplainCarriesVacuityAssumptionsAndEffectWitnessLocations()
    {
        using var fixture = new StoredEvidenceFixture(includeMetadata: false);
        var response = fixture.Response;
        response.ClaimResults = [response.ClaimResults[0] with
        {
            Outcome = WorkerClaimOutcome.Proven, Reason = WorkerClaimReason.None, Vacuity = WorkerVacuityKind.ContradictoryPreconditions,
            ProofCore = ["requires:0"], Assumptions = [new() { Id = "entry", Kind = WorkerAssumptionKind.Precondition, Used = true }]
        }];
        response.CallableResults[0].Assumptions = [new() { Id = "declared", Kind = WorkerAssumptionKind.UserAssume }];
        var vacuous = CommandPresentation.CreateExplain(response, null, 0, "Proven with vacuity\n");
        Assert.That(vacuous.Issues.Any(static issue =>
        {
            ArgumentNullException.ThrowIfNull(issue);
            return issue.Code == "claim.vacuity";
        }), Is.True);
        var markdown = await RenderAsync(vacuous, CommandOutputFormat.Markdown);
        Assert.That(markdown, Does.Contain("Precondition \\(used\\)").And.Contain("UserAssume \\(declared\\)").And.Contain("## Outcome details"));
        response.Manifest.Claims = [response.Manifest.Claims[0] with { Kind = WorkerClaimKind.Effect, Evidence = WorkerClaimEvidence.Attribute, EffectContractKind = WorkerEffectContractKind.EnforcePure }];
        response.ClaimResults = [response.ClaimResults[0] with
        {
            Outcome = WorkerClaimOutcome.Refuted, Vacuity = WorkerVacuityKind.None, ProofCore = [], Assumptions = [],
            EffectCertainty = WorkerEffectEvidenceCertainty.DefiniteViolation,
            EffectWitness = new() { Kind = "Write", Detail = "Writes state", Effects = WorkerEffectSet.WritesStaticState, Location = new() { Path = "Witness.cs", Start = 9, Length = 2, Line = 4, Column = 5 } }
        }];
        var refuted = CommandPresentation.CreateExplain(response, null, 0, "Refuted effect\n");
        using var json = JsonDocument.Parse(await RenderAsync(refuted, CommandOutputFormat.Json));
        var details = json.RootElement.GetProperty("outcomes")[1].GetProperty("details");
        var witness = details.EnumerateArray().Single(static detail => { global::SharpProof.Internal.InputValidation.Require(detail, nameof(detail)); return detail.GetProperty("name").GetString() == "effectWitnessLocation"; });
        Assert.That(witness.GetProperty("location").GetProperty("path").GetString(), Is.EqualTo("Witness.cs"));
        Assert.That(witness.GetProperty("location").GetProperty("start").GetInt32(), Is.EqualTo(9));
        Assert.That(witness.GetProperty("location").GetProperty("length").GetInt32(), Is.EqualTo(2));
    }

    [Test]
    public void ClaimFilterKeepsItsCallableAndRunErrorsWithoutOtherClaimRows()
    {
        using var fixture = new StoredEvidenceFixture();
        var response = fixture.Response;
        response.Manifest.Claims = [response.Manifest.Claims[0], response.Manifest.Claims[0] with { ClaimId = "other", CallableId = "M:Other.Run" }];
        response.Manifest.Callables = [response.Manifest.Callables[0], response.Manifest.Callables[0] with { CallableId = "M:Other.Run", ClaimIds = ["other"] }];
        response.ClaimResults = [response.ClaimResults[0], response.ClaimResults[0] with { ClaimId = "other" }];
        response.CallableResults = [response.CallableResults[0], response.CallableResults[0] with { CallableId = "M:Other.Run" }];
        response.Errors = [new() { Code = "worker.infrastructure", Message = "Recorded run failure" }];
        var presentation = CommandPresentation.CreateExplain(response, "claim:captured", 0, "Filtered\n");
        Assert.That(presentation.Outcomes, Has.Length.EqualTo(3));
        Assert.That(presentation.Outcomes.Any(static outcome =>
        {
            ArgumentNullException.ThrowIfNull(outcome);
            return outcome.Id == "other" || outcome.Id == "M:Other.Run";
        }), Is.False);
        Assert.That(presentation.Issues.Any(static issue =>
        {
            ArgumentNullException.ThrowIfNull(issue);
            return issue.Code == "worker.infrastructure" && issue.Location == null;
        }), Is.True);
        Assert.Throws<ArgumentException>(new Action(() => { _ = CommandPresentation.CreateExplain(response, "missing", 0, string.Empty); }));
    }

    [Test]
    public async Task ComparisonKeepsRegressionsNotesAndAnEmptyNoChangeResultDistinct()
    {
        var changes = ImmutableArray.Create(new VerificationChange("Added proof", "claim:added", false), new VerificationChange("Proven -> Unknown", "claim:lost", true));
        var presentation = CommandPresentation.CreateCompare(changes, ImmutableArray.Create("Budgets differ | review"), 1, "Two changes\n");
        using var json = JsonDocument.Parse(await RenderAsync(presentation, CommandOutputFormat.Json));
        Assert.That(json.RootElement.GetProperty("outcomes")[0].GetProperty("isRegression").GetBoolean(), Is.False);
        Assert.That(json.RootElement.GetProperty("outcomes")[1].GetProperty("isRegression").GetBoolean(), Is.True);
        Assert.That(json.RootElement.GetProperty("notes")[0].GetString(), Is.EqualTo("Budgets differ | review"));
        Assert.That(json.RootElement.GetProperty("issues").GetArrayLength(), Is.EqualTo(1));
        var markdown = await RenderAsync(presentation, CommandOutputFormat.Markdown);
        Assert.That(markdown, Does.Contain("## Notes").And.Contain("Budgets differ \\| review").And.Contain("claim:lost").And.Contain("true"));
        var empty = CommandPresentation.CreateCompare([], [], 0, string.Empty);
        Assert.That(await RenderAsync(empty, CommandOutputFormat.Markdown), Does.Not.Contain("## Notes").And.Not.Contain("## Outcomes").And.Not.Contain("## Issues"));
    }

    [Test]
    public void ModelFactoriesRejectInvalidArgumentsAndCopyDetailCollections()
    {
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateMessage(null!, 0, string.Empty); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = CommandPresentation.CreateMessage("init", -1, string.Empty); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateMessage("init", 0, null!); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = CommandPresentation.CreateMessage("init", 0, string.Empty, " "); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateMessage("init", 0, string.Empty, issues: [null!]); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateMessage("init", 0, string.Empty, generatedFiles: [null!]); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateExplain(null!, null, 0, string.Empty); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateExplain(new WorkerVerifyResponse(), null, 0, null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateCompare(null!, [], 0, string.Empty); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateCompare([], null!, 0, string.Empty); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateCompare([], [], 0, null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateCompare([null!], [], 0, string.Empty); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandPresentation.CreateCompare([], [null!], 0, string.Empty); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandOutcome(null!, "id", "outcome", "reason"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandOutcome("kind", null!, "outcome", "reason"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandOutcome("kind", "id", null!, "reason"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandOutcome("kind", "id", "outcome", null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandOutcome("kind", "id", "outcome", "reason", details: [null!]); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = new CommandDetail(" ", "value"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandDetail("name", null!); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = new CommandIssue("unknown", "code", "message"); }));
        Assert.Throws<ArgumentException>(new Action(() => { _ = new CommandIssue("info", " ", "message"); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandIssue("info", "code", null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandGeneratedFile(null!); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = new CommandLocation(null!, 1, 1); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = new CommandLocation("file", 0, 1); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = new CommandLocation("file", 1, 0); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = new CommandLocation("file", 1, 1, -1); }));
        Assert.Throws<ArgumentOutOfRangeException>(new Action(() => { _ = new CommandLocation("file", 1, 1, length: -1); }));
        Assert.Throws<ArgumentNullException>(new Action(() => { _ = CommandLocation.From(null!); }));
        var details = new List<CommandDetail> { new("name", "value") };
        var outcome = new CommandOutcome("kind", "id", "outcome", "reason", details: details);
        details.Clear();
        Assert.That(outcome.Details, Has.Length.EqualTo(1));
        var explicitIssue = new CommandIssue("info", "explicit", "Supplied");
        var doctor = CommandPresentation.CreateMessage("doctor", 1, "FAIL Check\n", issues: [explicitIssue]);
        Assert.That(doctor.Issues, Is.EqualTo(ImmutableArray.Create(explicitIssue)));
        Assert.That(doctor.Outcomes, Has.Length.EqualTo(1));
    }

    [Test]
    public void InvalidOrCanceledWritesLeaveTheOutputEmpty()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        var presentation = CommandPresentation.CreateMessage("init", 0, "No partial output");
        var token = TestContext.CurrentContext.CancellationToken;
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () => await CommandPresentationFormatter.WriteAsync(null!, presentation, CommandOutputFormat.Json, token)));
        Assert.ThrowsAsync<ArgumentNullException>(new Func<Task>(async () => await CommandPresentationFormatter.WriteAsync(output, null!, CommandOutputFormat.Json, token)));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(new Func<Task>(async () => await CommandPresentationFormatter.WriteAsync(output, presentation, (CommandOutputFormat)999, token)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(new Func<Task>(async () => await CommandPresentationFormatter.WriteAsync(output, presentation, CommandOutputFormat.Json, cancellation.Token)));
        Assert.That(output.ToString(), Is.Empty);
    }

    [Test]
    public async Task StructuredNumbersAndSourceCoordinatesDoNotDependOnCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            using var fixture = new StoredEvidenceFixture();
            var response = fixture.Response with { Summary = fixture.Response.Summary with { ElapsedMilliseconds = 1234 } };
            var presentation = CommandPresentation.CreateExplain(response, null, 0, "Retained\n");
            using var json = JsonDocument.Parse(await RenderAsync(presentation, CommandOutputFormat.Json));
            var elapsed = json.RootElement.GetProperty("outcomes")[0].GetProperty("details").EnumerateArray().Single(static detail => { global::SharpProof.Internal.InputValidation.Require(detail, nameof(detail)); return detail.GetProperty("name").GetString() == "elapsedMilliseconds"; });
            Assert.That(elapsed.GetProperty("value").GetString(), Is.EqualTo("1234"));
            Assert.That(new CommandLocation("Source.cs", 1234, 56).Display(), Is.EqualTo("Source.cs(1234,56)"));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static async Task<string> RenderAsync(CommandPresentation presentation, CommandOutputFormat format)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        global::SharpProof.Internal.InputValidation.Require(format, nameof(format));
        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await CommandPresentationFormatter.WriteAsync(writer, presentation, format, TestContext.CurrentContext.CancellationToken);
        return writer.ToString();
    }
}
