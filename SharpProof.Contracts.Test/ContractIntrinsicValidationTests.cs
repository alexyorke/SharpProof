using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractIntrinsicValidationTests
{
    [Test]
    public void ResultInsideOldMapsToNestedFailureForDirectContract()
    {
        Assert.That(
            Bind(ContractIntrinsicValidationFixtures.DirectContract,
                "Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.NestedOld));
    }

    [Test]
    public void ResultInsideOldMapsToNestedFailureForCompanionContract()
    {
        Assert.That(
            Bind(ContractIntrinsicValidationFixtures.CompanionContract,
                "Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.NestedOld));
    }

    [Test]
    public void IndirectIntrinsicCallsFailClosed()
    {
        Assert.That(
            Bind(ContractIntrinsicValidationFixtures.IndirectIntrinsicCalls,
                "Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.ResultOutsideEnsures));
    }

    private static ContractBindingResult Bind(
        string source,
        string typeName,
        string methodName)
    {
        var compilation = TestCompilation.Create(
            "ContractIntrinsicValidation",
            source);

        var method = compilation.GetTypeByMetadataName(typeName)!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
        return new ContractBinder(compilation, new IrFactory()).Bind(method);
    }
}
