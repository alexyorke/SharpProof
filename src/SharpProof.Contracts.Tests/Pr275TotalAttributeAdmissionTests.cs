using NUnit.Framework;
using SharpProof.Frontend;
using SharpProof.Ir;
using SharpProof.Testing;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class Pr275TotalAttributeAdmissionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void AnActualCompilationWithoutAnnotationsRetainsTheApiUnavailableBoundary(bool requiresOnly)
    {
        var compilation = TestCompilation.Create("Pr275MissingContractApi",
            "public static class Subject { public static int Target(int value) => value; }",
            includeSharpProofReference: false).WithReferences(TestMetadataReferences.WithoutSharpProof);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        Assert.That(compilation.GetTypeByMetadataName("SharpProof.Attributes.Contract"), Is.Null);
        var target = compilation.GetTypeByMetadataName("Subject")!.GetMembers("Target")
            .OfType<Microsoft.CodeAnalysis.IMethodSymbol>().Single();
        Assert.That(target.ContainingAssembly, Is.SameAs(compilation.Assembly));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new TotalLoweringContext(factory, target);
        var binder = new ContractBinder(compilation, factory);
        var binding = requiresOnly ? binder.BindTotalRequires(context) : binder.BindTotal(context);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(binding.IsSuccess, Is.False);
            Assert.That(binding.Failure, Is.EqualTo(ContractBindingFailure.ContractApiUnavailable));
            Assert.That(binding.Clauses, Is.Empty);
        }
    }
}
