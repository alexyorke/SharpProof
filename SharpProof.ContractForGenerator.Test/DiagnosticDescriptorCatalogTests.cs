using NUnit.Framework;
using SharpProof.TestSupport;

namespace SharpProof.ContractForGenerator.Test;

[TestFixture]
public sealed class DiagnosticDescriptorCatalogTests
{
    [Test]
    public void RuntimeDescriptorsMatchTheAuthoritativeCatalog()
    {
        DiagnosticDescriptorCatalogAssertions.AssertOutput(
            "contractForGenerator",
            typeof(ContractForValidatorGenerator).Assembly);
    }
}
