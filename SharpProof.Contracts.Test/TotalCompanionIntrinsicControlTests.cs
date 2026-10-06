using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class TotalCompanionIntrinsicControlTests
{
    [TestCase("_ = Contract.Result<int>();")]
    [TestCase("_ = Contract.Old(value);")]
    [TestCase("void Nested() { _ = Contract.Result<int>(); }")]
    public void RequiresOnlyPreservesCompanionPreconditions(string statement)
    {
        var compilation = TestCompilation.Create("TotalCompanionControls", $$"""
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
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, method);
        var result = new ContractBinder(compilation, factory).BindTotalRequires(context);
        Assert.That(result.IsSuccess, Is.True, result.Failure.ToString());
        Assert.That(result.Clauses, Has.Length.EqualTo(1));
        Assert.That(result.Clauses[0].Kind, Is.EqualTo(BoundContractKind.Requires));
        Assert.That(result.Clauses[0].Evidence, Is.EqualTo(BoundContractEvidence.Companion));
    }
}
