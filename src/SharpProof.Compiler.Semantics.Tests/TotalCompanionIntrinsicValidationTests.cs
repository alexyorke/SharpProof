using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Compiler.Semantics.Test;

[TestFixture]
internal sealed class TotalCompanionIntrinsicValidationTests
{
    [TestCase("_ = Contract.Result<int>();", ContractBindingFailure.ResultOutsideEnsures)]
    [TestCase("_ = Contract.Old(value);", ContractBindingFailure.OldOutsideEnsures)]
    [TestCase("", ContractBindingFailure.None)]
    public void CompanionDoesNotHideInvalidDirectIntrinsics(string statement, ContractBindingFailure expected)
    {
        System.ArgumentNullException.ThrowIfNull(statement);
        var compilation = TestCompilation.Create("TotalCompanionIntrinsics", $$"""
            using SharpProof.Attributes;
            public static class Target {
                public static int Read(int value) {
                    {{statement}}
                    return value;
                }
            }
            [ContractFor(typeof(Target))]
            public static class TargetContracts {
                public static int Read(int value) {
                    Contract.Requires(value > 0);
                    Contract.Ensures(Contract.Result<int>() == Contract.Old(value));
                    return value;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        var method = compilation.GetTypeByMetadataName("Target")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory();
        var context = new TotalLoweringContext(factory, method);
        var binder = new ContractBinder(compilation);
        var totalBinder = new TotalContractBinder(binder, factory);
        Assert.That(binder.Bind(method).Failure, Is.EqualTo(expected), "Legacy binding establishes the intrinsic policy.");
        var total = totalBinder.BindTotal(context);
        Assert.That(total.Failure, Is.EqualTo(expected));
        if (expected == ContractBindingFailure.None)
        {
            Assert.That(total.Clauses, Has.Length.EqualTo(2));
            Assert.That(total.Clauses.All(clause => clause.Evidence == BoundContractEvidence.Companion), Is.True);
        }
    }
}
