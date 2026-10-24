using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class PrivatePreviewCommandRoutingTests
{
    [TestCase("init --verification unsupported --project missing.csproj", "--verification must be portable or container.")]
    [TestCase("init --consumer-image image --project missing.csproj", "--consumer-image applies only to container initialization.")]
    [TestCase("init --apply --apply --project missing.csproj", "Duplicate option --apply")]
    [TestCase("init --apply false --project missing.csproj", "Expected a named --option")]
    [TestCase("init --project", "Missing value for --project")]
    [TestCase("init --apply", "Required option --project")]
    [TestCase("doctor --project missing.csproj --apply", "Unknown option --apply")]
    [TestCase("compare --format json --format markdown", "Duplicate option --format")]
    [TestCase("explain result.json --format json", "Unknown or incomplete option: --format")]
    public async Task NewSyntaxRejectsAmbiguousFlagsBeforePerformingWork(string command, string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(diagnostic);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var result = await ToolApplication.RunAsync(command.Split(' ').ToImmutableArray(), output, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken);
        Assert.That(result, Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain(diagnostic));
    }

    [TestCase("init")]
    [TestCase("doctor")]
    [TestCase("explain")]
    [TestCase("compare")]
    [TestCase("export-test")]
    public async Task UnsupportedFormatsAreRejectedBeforeProjectsOrEvidenceAreRead(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync([command, "--format", "xml"], output, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain("format").And.Contain("text").And.Contain("json").And.Contain("markdown"));
    }

    [TestCase("text")]
    [TestCase("json")]
    [TestCase("markdown")]
    public async Task InvalidEvidenceLeavesMachineOutputEmptyAndDiagnosticsOnStderr(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        var missing = Path.Combine(Path.GetTempPath(), "sharpproof-missing-" + Guid.NewGuid().ToString("N"), "result.json");
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["explain", "--format", format, "--result", missing], output, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.StartWith("SharpProof: ").And.Contain("Required evidence"));
    }

    [TestCase("text")]
    [TestCase("json")]
    [TestCase("markdown")]
    public async Task NamedDoctorSetupFindingsKeepExitOneAndCleanFormattedOutput(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        using var project = new CommandProjectFixture();
        await File.WriteAllTextAsync(project.ProjectPath, "<Project />", TestContext.CurrentContext.CancellationToken);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["doctor", "--project", project.ProjectPath, "--verification", "portable", "--format", format],
            output, error, UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.EqualTo(1));
        Assert.That(error.ToString(), Is.Empty);
        if (format == "json")
        {
            using var document = JsonDocument.Parse(output.ToString());
            Assert.That(document.RootElement.GetProperty("command").GetString(), Is.EqualTo("doctor"));
            Assert.That(document.RootElement.GetProperty("exitCode").GetInt32(), Is.EqualTo(1));
            Assert.That(document.RootElement.GetProperty("outcomes").EnumerateArray().Any(outcome =>
                outcome.GetProperty("outcome").GetString() == "Fail"), Is.True);
        }
        else
        {
            Assert.That(output.ToString(), Does.Contain("FAIL"));
            if (format == "markdown")
            { Assert.That(output.ToString(), Does.Contain("# SharpProof doctor")); }
        }
    }
    [TestCase(false)]
    [TestCase(true)]
    public async Task InitRoutesPreviewAndExplicitApplyIntoOneJsonDocument(bool apply)
    {
        using var project = new CommandProjectFixture();
        var arguments = new List<string> { "init", "--format", "json", "--project", project.ProjectPath };
        if (apply)
        { arguments.Add("--apply"); }
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync([.. arguments], output, error, UnexpectedDiscovery,
            TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
        Assert.That(error.ToString(), Is.Empty);
        using var document = JsonDocument.Parse(output.ToString());
        var result = document.RootElement;
        Assert.That(result.GetProperty("schema").GetString(), Is.EqualTo("SharpProof.CommandPresentation"));
        Assert.That(result.GetProperty("schemaVersion").GetInt32(), Is.EqualTo(1));
        Assert.That(result.GetProperty("command").GetString(), Is.EqualTo("init"));
        Assert.That(result.GetProperty("exitCode").GetInt32(), Is.Zero);
        var files = result.GetProperty("generatedFiles").EnumerateArray().ToArray();
        Assert.That(files, Is.Not.Empty);
        Assert.That(files.Any(file => file.GetProperty("changed").GetBoolean()), Is.True);
        Assert.That(files.All(file => file.GetProperty("written").GetBoolean() == apply), Is.True);
        if (apply)
        {
            foreach (var file in files)
            { Assert.That(File.Exists(file.GetProperty("path").GetString()), Is.True); }
        }
        else
        {
            Assert.That(await File.ReadAllTextAsync(project.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(CommandProjectFixture.Source));
            Assert.That(Directory.GetFiles(project.Root, "*", SearchOption.AllDirectories), Has.Length.EqualTo(1));
        }
    }

    [Test]
    public async Task InitDefaultTextAndExplicitTextAreIdenticalPreviews()
    {
        using var project = new CommandProjectFixture();
        using var first = new StringWriter(CultureInfo.InvariantCulture);
        using var second = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["init", "--project", project.ProjectPath], first, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
        Assert.That(await ToolApplication.RunAsync(["init", "--project", project.ProjectPath, "--format", "text"], second, error,
            UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.Zero, error.ToString());
        Assert.That(first.ToString(), Is.Not.Empty.And.EqualTo(second.ToString()));
        Assert.That(error.ToString(), Is.Empty);
        Assert.That(await File.ReadAllTextAsync(project.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(CommandProjectFixture.Source));
    }

    [Test]
    public async Task LegacyDoctorJsonKeepsItsOriginalSchema()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["doctor", "--json"], output, error,
            static cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult("10.0.402");
            }, TestContext.CurrentContext.CancellationToken), Is.Zero);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.That(document.RootElement.GetProperty("sdk").GetString(), Is.EqualTo("10.0.402"));
        Assert.That(document.RootElement.TryGetProperty("schema", out _), Is.False);
        Assert.That(error.ToString(), Is.Empty);
    }

    [Test]
    public async Task InitCancellationPreventsAnyPreviewOrMutation()
    {
        using var project = new CommandProjectFixture();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await DeveloperCommands.RunAsync(["init", "--apply", "--project", project.ProjectPath, "--format", "json"],
            output, error, cancellation.Token), Is.EqualTo(130));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.Empty);
        Assert.That(await File.ReadAllTextAsync(project.ProjectPath, TestContext.CurrentContext.CancellationToken), Is.EqualTo(CommandProjectFixture.Source));
    }

    private static Task<string> UnexpectedDiscovery(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new AssertionException("Legacy SDK discovery must not run for named developer commands.");
    }

    private sealed class CommandProjectFixture : IDisposable
    {
        internal const string Source = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n";
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpproof-command-" + Guid.NewGuid().ToString("N"));
        internal string ProjectPath { get; }

        internal CommandProjectFixture()
        {
            Directory.CreateDirectory(Root);
            ProjectPath = Path.Combine(Root, "Consumer.csproj");
            File.WriteAllText(ProjectPath, Source);
        }

        public void Dispose()
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
