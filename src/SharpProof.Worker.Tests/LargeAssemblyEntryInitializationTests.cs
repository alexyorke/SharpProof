using System.Text;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Entry initialization once walked at most 4,096 assembly symbols looking for
// a module initializer and treated a truncated walk as one. Every callable of
// a larger assembly (NodaTime has about 4,500) was then Unknown with no
// reason the analyzer could name. The search is now exact.
[TestFixture]
internal sealed class LargeAssemblyEntryInitializationTests
{
    [TestCase("[EnforcePure]")]
    [TestCase("[DoesNotThrow]")]
    public async Task ATrivialBodyInALargeAssemblyIsProven(string attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        var claim = await VerifyAsync(attribute, moduleInitializer: false);
        Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Proven), claim.Reason.ToString());
    }

    // A module initializer past the old bound still makes entry effects
    // incomplete.
    [Test]
    public async Task AModuleInitializerAfterManySymbolsStillBlocksPurity()
    {
        var claim = await VerifyAsync("[EnforcePure]", moduleInitializer: true);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(claim.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
            Assert.That(claim.Reason, Is.EqualTo(WorkerClaimReason.UnsupportedBody));
        }
    }

    private static async Task<WorkerClaimResult> VerifyAsync(string attribute, bool moduleInitializer)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        var source = new StringBuilder("using SharpProof.Attributes; public static class Subject { ")
            .Append(attribute).Append(" public static bool Target(int year) => (year & 3) == 0; } ");
        for (var type = 0; type < 60; type++)
        {
            source.Append("public static class Filler").Append(type).Append(" { ");
            for (var member = 0; member < 80; member++)
            { source.Append("public static int M").Append(member).Append("() => ").Append(member).Append("; "); }
            source.Append("} ");
        }
        if (moduleInitializer)
        { source.Append("public static class Startup { [System.Runtime.CompilerServices.ModuleInitializer] public static void Run() { } }"); }
        using var project = WorkerTests.TestProject.Create(source.ToString());
        project.UseNetCoreReferencePack();
        var request = project.CreateRequest(cacheEnabled: false, targetFramework: "net9.0");
        using var worker = SharpProofWorker.Create(request.Budgets);
        var response = await worker.VerifyAsync(request, cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(response.Errors.Select(static error => error.Code + ": " + error.Message), Is.Empty);
        return response.ClaimResults.Single();
    }
}
