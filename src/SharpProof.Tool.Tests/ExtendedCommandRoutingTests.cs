using System.Collections.Immutable;
using System.Globalization;
using NUnit.Framework;
using SharpProof.Tool;

namespace SharpProof.Tool.Test;

[TestFixture]
public sealed class ExtendedCommandRoutingTests
{
    [TestCase("doctor --verification portable", "Required option --project")]
    [TestCase("doctor --verification portable --project", "Missing value for --project")]
    [TestCase("doctor --json --project <missing>", "Missing value for --json")]
    [TestCase("explain --claim claim.0", "Required option --result")]
    [TestCase("explain --claim claim.0 --result", "Missing value for --result")]
    [TestCase("explain --result <missing> --result <missing>", "Duplicate option --result")]
    [TestCase("explain --result <missing> --unknown value", "Unknown option --unknown")]
    [TestCase("explain <missing> --result <missing>", "Unknown or incomplete option: --result")]
    [TestCase("compare --baseline <missing>", "Required option --current")]
    [TestCase("compare --baseline <missing> --current <missing> --fail-on-regression true", "Expected a named --option")]
    [TestCase("export-test --project <missing> --result <missing> --claim claim.0", "Required option --output")]
    public async Task InvalidSyntaxIsRejectedBeforeEvidenceAccess(string command, string diagnostic)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(diagnostic);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(Arguments(command), output, error, UnexpectedDiscovery,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain(diagnostic));
    }

    [TestCase("doctor --project <missing> --verification unsupported")]
    [TestCase("doctor --verification unsupported --project <missing>")]
    public async Task ProjectDoctorAcceptsNamedOptionsInEitherOrder(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(Arguments(command), output, error, UnexpectedDiscovery,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain("--verification must be portable or container."));
    }

    [TestCase("explain --result <missing> --claim claim.0")]
    [TestCase("explain --claim claim.0 --result <missing>")]
    public async Task NamedExplainAcceptsNamedOptionsInEitherOrder(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(Arguments(command), output, error, UnexpectedDiscovery,
            TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain("Required evidence must be a nonempty regular file"));
    }

    [TestCase("")]
    [TestCase(" ")]
    public async Task BlankRequiredValuesAreRejectedBeforeEvidenceAccess(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await ToolApplication.RunAsync(["compare", "--baseline", value, "--current", "missing.json"],
            output, error, UnexpectedDiscovery, TestContext.CurrentContext.CancellationToken), Is.EqualTo(2));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Does.Contain("Required option --baseline"));
    }

    [TestCase("doctor --verification portable --project <missing>")]
    [TestCase("explain --claim claim.0 --result <missing>")]
    [TestCase("compare --baseline <missing> --current <missing>")]
    [TestCase("export-test --project <missing> --result <missing> --claim claim.0 --output <missing>")]
    public async Task ExtendedEntrypointsHonorCancellationBeforeDispatch(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        Assert.That(await Program.RunMainAsync(Arguments(command), cancellation.Token), Is.EqualTo(130));
    }

    [Test]
    public async Task DeveloperCommandsHonorCancellationBeforeReadingEvidence()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        Assert.That(await DeveloperCommands.RunAsync(["explain", "--result", "missing.json"], output, error,
            cancellation.Token), Is.EqualTo(130));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.Empty);
    }

    private static ImmutableArray<string> Arguments(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var missing = Path.Combine(Path.GetTempPath(), "sharpproof-missing-evidence-" + Guid.NewGuid().ToString("N"));
        return command.Split(' ').Select(argument =>
        {
            ArgumentNullException.ThrowIfNull(argument);
            return argument == "<missing>" ? missing : argument;
        }).ToImmutableArray();
    }

    private static Task<string> UnexpectedDiscovery(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new AssertionException("SDK discovery must not start for extended commands.");
    }
}
