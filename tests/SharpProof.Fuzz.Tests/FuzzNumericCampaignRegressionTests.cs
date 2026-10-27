using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using SharpProof.Fuzz;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Fuzz.Test;

public sealed class FuzzNumericCampaignRegressionTests
{
    [Test]
    public async Task NumericCampaignRetainsItsFixedSeedResults()
    {
        var summary = await FuzzRunner.RunAsync(new FuzzOptions(24, 12345, 4), cancellationToken: TestContext.CurrentContext.CancellationToken);
        Assert.That(summary.Passed, Is.True, string.Join(Environment.NewLine, summary.Failures));
        var serialized = JsonSerializer.Serialize(summary);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        // Captured after the frontend oracle moved to the Total body lowerer.
        Assert.That(digest, Is.EqualTo(
            "F6EDE86E42140BA6B0C97C2D305CE122525B0DCCFD4A707BEDC3438F94816C27"));
    }

    [Test]
    public void GeneratedNumericFormulasAreDefinedOnTheirEntireFiniteDomain()
    {
        var sources = new StringBuilder();
        foreach (var seed in Enumerable.Range(0, 256).Concat([int.MinValue, int.MaxValue]))
        {
            var factory = new IrFactory(IrExecutionSemantics.Total);
            var generated = new WellSortedIrGenerator(factory, seed).NextArithmeticOrBoolean(3);
            var formula = generated.Term.Type == factory.BooleanType
                ? generated.Term
                : factory.Binary(IrBinaryOperator.Equal, generated.Term, factory.Integer(0));
            Assert.That(FiniteDomainSmtDifferentialOracle.TryPrepareForCampaign(
                factory, formula, TestContext.CurrentContext.CancellationToken, out var prepared), Is.True,
                "seed=" + seed);
            Assert.That(prepared, Is.Not.Null);
            sources.Append(new IrPrinter(factory).Print(generated.Term)).Append('\n');
        }
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sources.ToString())));
        Assert.That(digest, Is.EqualTo(
            "EE233F7FB9538285778CFB252D2E511C57F1903187028D1743511A5FE96450B8"));
    }
}
