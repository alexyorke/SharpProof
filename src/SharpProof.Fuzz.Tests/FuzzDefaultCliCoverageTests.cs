using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Fuzz;

namespace SharpProof.Fuzz.Test;

[NonParallelizable]
public sealed class FuzzDefaultCliCoverageTests
{
    [Test]
    [Category("Coverage")]
    public async Task DefaultExecutableCampaignReportsAllAgreementsAndRequiredCategories()
    {
        TestRepository.RequireCanonicalContainer();
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        var elapsed = Stopwatch.StartNew();
        int exitCode;
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            var entryPoint = typeof(FuzzRunner).Assembly.EntryPoint;
            Assert.That(entryPoint, Is.Not.Null);
            string[] arguments = [];
            var result = await Task.Run(() => entryPoint!.Invoke(null, [arguments]));
            exitCode = result switch
            {
                Task<int> task => await task,
                int completed => completed,
                _ => throw new InvalidOperationException("Unexpected executable entrypoint result.")
            };
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            elapsed.Stop();
            await TestContext.Progress.WriteLineAsync("Default Fuzz CLI elapsed milliseconds: " +
                elapsed.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
        }

        Assert.That(exitCode, Is.Zero, error.ToString());
        Assert.That(error.ToString(), Is.Empty);
        var summary = JsonSerializer.Deserialize<FuzzSummary>(output.ToString());
        Assert.That(summary, Is.Not.Null);
        Assert.That(summary!.Cases, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.Seed, Is.EqualTo(FuzzOptions.DefaultSeed));
        Assert.That(summary.MaximumParallelism, Is.EqualTo(FuzzOptions.DefaultMaximumParallelism));
        Assert.That(summary.Agreements, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.FrontendAgreements, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.SmtAgreements, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.PartialSmtAgreements, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.Abstentions, Is.Zero);
        Assert.That(summary.Failures, Is.Empty);
        Assert.That(summary.CoverageSatisfied, Is.True);
        Assert.That(summary.FrontendCoverage.HasExpandedCategories, Is.True);
        Assert.That(summary.TotalProgramCoverage.Cases, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.TotalProgramCoverage.Agreements, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.TotalProgramCoverage.HasExpandedCategories, Is.True);
        Assert.That(summary.MetadataProgramCoverage.Cases, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.MetadataProgramCoverage.Agreements, Is.EqualTo(FuzzOptions.DefaultCases));
        Assert.That(summary.MetadataProgramCoverage.HasExpandedCategories, Is.True);
        Assert.That(summary.Passed, Is.True);
    }
}
