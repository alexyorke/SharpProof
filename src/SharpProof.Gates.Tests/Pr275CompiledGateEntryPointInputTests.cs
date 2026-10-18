using System.Globalization;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
[NonParallelizable]
public sealed class Pr275CompiledGateEntryPointInputTests
{
    [TestCase("unknown")]
    [TestCase("help")]
    [TestCase("--help")]
    [TestCase("CORPUS")]
    public void ActualCompiledEntryPointRejectsUnknownCommands(string command)
    {
        System.ArgumentNullException.ThrowIfNull(command);
        var result = InvokeCompiledEntryPoint([command]);
        Assert.That(result.Exit, Is.EqualTo(2));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("Usage: SharpProof.Gates"));
    }

    [TestCase("exception-oracle", "--unexpected", "1", "ArgumentException")]
    [TestCase("allocation-oracle", "--unexpected", "1", "ArgumentException")]
    [TestCase("purity-oracle", "--unexpected", "1", "ArgumentException")]
    [TestCase("capability-oracle", "--unexpected", "1", "ArgumentException")]
    [TestCase("effectcontract-oracle", "--unexpected", "1", "ArgumentException")]
    [TestCase("exception-oracle", "--limit", "not-an-integer", "FormatException")]
    [TestCase("allocation-oracle", "--limit", "not-an-integer", "FormatException")]
    [TestCase("purity-oracle", "--limit", "not-an-integer", "FormatException")]
    [TestCase("capability-oracle", "--limit", "not-an-integer", "FormatException")]
    [TestCase("effectcontract-oracle", "--limit", "not-an-integer", "FormatException")]
    [TestCase("exception-oracle", "--limit", "2147483648", "OverflowException")]
    [TestCase("allocation-oracle", "--limit", "2147483648", "OverflowException")]
    [TestCase("purity-oracle", "--limit", "2147483648", "OverflowException")]
    [TestCase("capability-oracle", "--limit", "2147483648", "OverflowException")]
    [TestCase("effectcontract-oracle", "--limit", "2147483648", "OverflowException")]
    public void ActualCompiledEntryPointRejectsMalformedOracleArguments(
        string command, string option, string value, string exceptionType)
    {
        System.ArgumentNullException.ThrowIfNull(command);
        System.ArgumentNullException.ThrowIfNull(exceptionType);
        System.ArgumentNullException.ThrowIfNull(option);
        System.ArgumentNullException.ThrowIfNull(value);
        var result = InvokeCompiledEntryPoint([command, option, value]);
        Assert.That(result.Exit, Is.EqualTo(1));
        Assert.That(result.Output, Is.Empty);
        Assert.That(result.Error, Does.Contain("System." + exceptionType));
        if (exceptionType == "ArgumentException")
        {
            Assert.That(result.Error, Does.Contain("Use exception-oracle, allocation-oracle"));
        }
        var usage = InvokeCompiledEntryPoint(["unknown"]);
        Assert.That(usage.Exit, Is.EqualTo(2));
        Assert.That(usage.Error, Does.Contain("Usage: SharpProof.Gates"));
    }

    private static (int Exit, string Output, string Error) InvokeCompiledEntryPoint(string[] arguments)
    {
        System.ArgumentNullException.ThrowIfNull(arguments, nameof(arguments));
        var entryPoint = typeof(CorpusGate).Assembly.EntryPoint;
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
