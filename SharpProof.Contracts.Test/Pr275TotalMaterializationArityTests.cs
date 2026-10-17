using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class Pr275TotalMaterializationArityTests
{
    [TestCase("Zero", 0, false)]
    [TestCase("Zero", 0, true)]
    [TestCase("Two", 2, false)]
    [TestCase("Two", 2, true)]
    public void ActualOrdinaryInvocationsRejectRawClauseArityWithoutEnrollment(
        string methodName, int argumentCount, bool requiresOnly)
    {
        System.ArgumentNullException.ThrowIfNull(methodName);
        var compilation = TestCompilation.Create("Pr275RawClauseArity", """
            public static class Subject
            {
                public static bool Zero() { Ordinary.Zero(); return true; }
                public static bool Two() { Ordinary.Two(true, false); return true; }
            }
            public static class Ordinary
            {
                public static void Zero() { }
                public static void Two(bool first, bool second) { }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers(methodName)
            .OfType<IMethodSymbol>().Single();
        Assert.That(target.ContainingAssembly, Is.SameAs(compilation.Assembly));
        var resolution = EffectiveContractSourceResolver.ForCompilation(compilation).Resolve(target);
        Assert.That(resolution.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(resolution.Inventory.Clauses, Is.Empty);
        var invocation = resolution.Inventory.ImplementationBody!.DescendantsAndSelf()
            .OfType<IInvocationOperation>().Single();
        Assert.That(invocation.SemanticModel!.Compilation, Is.SameAs(compilation));
        Assert.That(invocation.Arguments, Has.Length.EqualTo(argumentCount));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        Assert.That(api, Is.Not.Null);
        Assert.That(api.Clauses.GetClauseKind(invocation.TargetMethod), Is.Null);
        var publicResult = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(publicResult.Failure, Is.EqualTo(ContractBindingFailure.None));
        Assert.That(publicResult.Clauses, Is.Empty);
        var rejected = TotalContractInvocationMaterialization.Bind(context, api,
            new ContractIntrinsicValidator(api), resolution,
            [new ContractInvocationCandidate(BoundContractKind.Requires, invocation)], requiresOnly);
        Assert.That(rejected.Failure, Is.EqualTo(ContractBindingFailure.InvalidIntrinsicSignature));
        Assert.That(rejected.Clauses, Is.Empty);
    }
}
