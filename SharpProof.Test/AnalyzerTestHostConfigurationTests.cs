using System.Collections.Immutable;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Test
{
    [TestFixture]
    public sealed class AnalyzerTestHostConfigurationTests
    {
        [Test]
        public async Task CachedGlobalOptions_PreserveNewlineDelimitedValues()
        {
            var diagnostics = await AnalyzerTestHost.GetDiagnosticsAsync(@"
using SharpProof.Attributes;

public class TestClass
{
    [EnforcePure]
    public void CallsFirst()
    {
        First();
    }

    [EnforcePure]
    public void CallsSecond()
    {
        Second();
    }

    private void First()
    {
    }

    private void Second()
    {
    }
}",
                ImmutableDictionary<string, string>.Empty.Add(
                    "sharpproof_known_impure_methods",
                    "TestClass.First()\nTestClass.Second()"));

            var symbols = diagnostics
                .Where(diagnostic => diagnostic.Id == SharpProofDiagnostics.PurityNotVerifiedId)
                .Select(diagnostic => diagnostic.Properties[SharpProofDiagnostics.ImpuritySymbolProperty])
                .ToArray();

            Assert.That(symbols, Has.Some.Contains("TestClass.First"));
            Assert.That(symbols, Has.Some.Contains("TestClass.Second"));
        }
    }
}
