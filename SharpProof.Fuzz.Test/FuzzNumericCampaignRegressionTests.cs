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
        var summary = await FuzzRunner.RunAsync(new FuzzOptions(24, 12345, 4), cancellationToken: CancellationToken.None);
        Assert.That(summary.Passed, Is.True, string.Join(Environment.NewLine, summary.Failures));
        var serialized = JsonSerializer.Serialize(summary);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized)));
        // Captured from the public campaign before removing the unused retry.
        Assert.That(digest, Is.EqualTo(
            "EC69FE2C4ADF80C846C14552582843573D7589AB06E57055D0CC80E8F79080EB"));
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
                factory, formula, CancellationToken.None, out var prepared), Is.True,
                "seed=" + seed);
            Assert.That(prepared, Is.Not.Null);
            sources.Append(new IrPrinter(factory).Print(generated.Term)).Append('\n');
        }
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sources.ToString())));
        Assert.That(digest, Is.EqualTo(
            "EE233F7FB9538285778CFB252D2E511C57F1903187028D1743511A5FE96450B8"));
    }
}
