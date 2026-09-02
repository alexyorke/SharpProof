using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractIntrinsicValidationTests
{
    [Test]
    public void ResultInsideOldMapsToNestedFailureForDirectContract()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public static class Target {
                public static int Read(int value) {
                    Contract.Ensures(
                        Contract.Old(Contract.Result<int>()) == value);
                    return value;
                }
            }
            """;

        Assert.That(
            Bind(source, "Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.NestedOld));
    }

    [Test]
    public void ResultInsideOldMapsToNestedFailureForCompanionContract()
    {
        const string source =
            """
            using SharpProof.Attributes;

            public interface Target {
                int Read(int value);
            }

            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int Read(Target receiver, int value) {
                    Contract.Ensures(
                        Contract.Old(Contract.Result<int>()) == value);
                    return value;
                }
            }
            """;

        Assert.That(
            Bind(source, "Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.NestedOld));
    }

    [Test]
    public void IndirectIntrinsicCallsFailClosed()
    {
        const string source =
            """
            using System;
            using SharpProof.Attributes;

            public static class Target {
                public static int Read(int value) {
                    Func<int> result = Contract.Result<int>;
                    Func<int, int> old = Contract.Old<int>;
                    return result() + old(value);
                }
            }
            """;

        Assert.That(
            Bind(source, "Target", "Read").Failure,
            Is.EqualTo(ContractBindingFailure.ResultOutsideEnsures));
    }

    private static ContractBindingResult Bind(
        string source,
        string typeName,
        string methodName)
    {
        var compilation = ContractTestCompilation.Create(
            "ContractIntrinsicValidation",
            source);

        var method = compilation.GetTypeByMetadataName(typeName)!
            .GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Single();
        return new ContractBinder(compilation, new IrFactory()).Bind(method);
    }
}
