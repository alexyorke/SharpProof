using System.Globalization;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[TestFixture]
[NonParallelizable]
public sealed class Pr275CompiledFuzzEntryPointInputTests
{
    [TestCase("--help")]
    [TestCase("-h")]
    public void ActualCompiledEntryPointPrintsHelpWithoutRunningCampaign(string option)
    {
        System.ArgumentNullException.ThrowIfNull(option);
        var result = InvokeCompiledEntryPoint([option]);
        Assert.That(result.Exit, Is.Zero);
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Is.EqualTo("Usage: SharpProof.Fuzz [--cases N] [--seed N] [--max-parallelism 1..4]" + Environment.NewLine));
    }

    [TestCase("--cases")]
    [TestCase("--seed")]
    [TestCase("--max-parallelism")]
    public void ActualCompiledEntryPointRejectsMissingValues(string option)
    {
        System.ArgumentNullException.ThrowIfNull(option);
        AssertRejected([option], "Missing value for " + option + ".");
    }

    [TestCase("unknown")]
    [TestCase("--unknown")]
    [TestCase("--Cases")]
    public void ActualCompiledEntryPointRejectsUnknownOptions(string option)
    {
        System.ArgumentNullException.ThrowIfNull(option);
        AssertRejected([option, "1"], "Unknown option: " + option + ".");
    }

    [TestCase("--cases", "not-an-integer", "--cases requires a positive integer.")]
    [TestCase("--cases", "2147483648", "--cases requires a positive integer.")]
    [TestCase("--cases", "0", "--cases requires a positive integer.")]
    [TestCase("--cases", "-1", "--cases requires a positive integer.")]
    [TestCase("--cases", "1000001", "--cases cannot exceed the limit of 1000000.")]
    [TestCase("--max-parallelism", "not-an-integer", "--max-parallelism requires a positive integer.")]
    [TestCase("--max-parallelism", "2147483648", "--max-parallelism requires a positive integer.")]
    [TestCase("--max-parallelism", "0", "--max-parallelism requires a positive integer.")]
    [TestCase("--max-parallelism", "-1", "--max-parallelism requires a positive integer.")]
    [TestCase("--max-parallelism", "5", "--max-parallelism cannot exceed the limit of 4.")]
    [TestCase("--seed", "not-an-integer", "--seed requires a 32-bit integer.")]
    [TestCase("--seed", "2147483648", "--seed requires a 32-bit integer.")]
    public void ActualCompiledEntryPointRejectsMalformedValues(string option, string value, string message)
    {
        System.ArgumentNullException.ThrowIfNull(message);
        System.ArgumentNullException.ThrowIfNull(option);
        System.ArgumentNullException.ThrowIfNull(value);
        AssertRejected([option, value], message);
    }

    [Test]
    public void PublicParserRejectsMissingArgumentCollection()
    {
        Action parse = () => FuzzOptions.Parse(null!);
        Assert.That(Assert.Throws<ArgumentNullException>(parse)!.ParamName, Is.EqualTo("arguments"));
    }

    [Test]
    public void PublicUsageExceptionConstructorsRetainOrdinaryDiagnosticInputs()
    {
        Assert.That(new FuzzUsageException().InnerException, Is.Null);
        Assert.That(new FuzzUsageException("usage message").Message, Is.EqualTo("usage message"));
        var cause = new ArgumentException("ordinary parsing cause");
        var exception = new FuzzUsageException("usage message", cause);
        Assert.That(exception.Message, Is.EqualTo("usage message"));
        Assert.That(exception.InnerException, Is.SameAs(cause));
    }

    private static void AssertRejected(string[] arguments, string message)
    {
        System.ArgumentNullException.ThrowIfNull(arguments, nameof(arguments));
        System.ArgumentNullException.ThrowIfNull(message);
        var result = InvokeCompiledEntryPoint(arguments);
        Assert.That(result.Exit, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Is.EqualTo(message + Environment.NewLine +
            "Usage: SharpProof.Fuzz [--cases N] [--seed N] [--max-parallelism 1..4]" + Environment.NewLine));
    }

    private static (int Exit, string Output, string Error) InvokeCompiledEntryPoint(string[] arguments)
    {
        System.ArgumentNullException.ThrowIfNull(arguments, nameof(arguments));
        var entryPoint = typeof(FuzzOptions).Assembly.EntryPoint;
        Assert.That(entryPoint, Is.Not.Null);
        Assert.That(entryPoint!.ReturnType, Is.EqualTo(typeof(int)));
        var previousOutput = Console.Out;
        var previousError = Console.Error;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exit = (int)entryPoint.Invoke(null, [arguments])!;
            return (exit, output.ToString(), error.ToString());
        }
        finally
        {
            Console.SetOut(previousOutput);
            Console.SetError(previousError);
        }
    }
}
