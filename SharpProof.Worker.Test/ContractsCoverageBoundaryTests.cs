using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Contracts;
using SharpProof.Frontend;
using SharpProof.Ir;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class ContractsCoverageBoundaryTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void MetadataBindingRequiresTheRuntimeContractApi(bool includeContractApi)
    {
        var compilation = CreateMetadataConsumer(
            "public static class Library { public static int Target(int value) => value; }",
            includeContractApi);
        var result = Bind(compilation);

        Assert.That(result.Failure, Is.EqualTo(includeContractApi
            ? ContractBindingFailure.None
            : ContractBindingFailure.ContractApiUnavailable));
        Assert.That(result.Clauses, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MetadataBindingDoesNotBorrowSourceAttributeEvidence(bool metadata)
    {
        const string source = "using SharpProof.Attributes; public static class Library { public static int Target([Positive] int value) => value; }";
        var compilation = metadata
            ? CreateMetadataConsumer(source, includeContractApi: true)
            : TestCompilation.Create("ContractsSourceBoundary", source);
        var result = Bind(compilation);

        Assert.That(result.Failure, Is.EqualTo(metadata
            ? ContractBindingFailure.None
            : ContractBindingFailure.UnsupportedTarget));
        Assert.That(result.Clauses, Has.Length.EqualTo(metadata ? 1 : 0));
        if (metadata)
        {
            Assert.That(result.Clauses[0].Attribute.ApplicationSyntaxReference, Is.Null);
            Assert.That(result.Clauses[0].ParameterOrdinal, Is.Zero);
        }
    }

    [TestCase("int", true)]
    [TestCase("System.DateTime", false)]
    public void MetadataBindingRetainsItsScalarSignatureBoundary(string parameterType, bool scalar)
    {
        var compilation = CreateMetadataConsumer(
            $"public static class Library {{ public static int Target({parameterType} value) => 0; }}",
            includeContractApi: true);
        var result = Bind(compilation);

        Assert.That(result.Failure, Is.EqualTo(scalar
            ? ContractBindingFailure.None
            : ContractBindingFailure.UnsupportedTarget));
        Assert.That(result.Clauses, Is.Empty);
    }

    private static TotalMetadataRequiresBindingResult Bind(CSharpCompilation compilation)
    {
        var method = compilation.GetTypeByMetadataName("Library")!
            .GetMembers("Target").OfType<IMethodSymbol>().Single();
        var factory = new IrFactory(IrExecutionSemantics.Total);
        return new ContractBinder(compilation, factory)
            .BindTotalMetadataRequires(new TotalLoweringContext(factory, method));
    }

    private static CSharpCompilation CreateMetadataConsumer(string source, bool includeContractApi)
    {
        var library = TestCompilation.Create("ContractsMetadataBoundaryLibrary", source);
        using var image = new MemoryStream();
        var emitted = library.Emit(image);
        Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
        var references = includeContractApi
            ? TestMetadataReferences.WithSharpProof
            : TestMetadataReferences.WithoutSharpProof;
        var compilation = CSharpCompilation.Create("ContractsMetadataBoundaryConsumer",
            [CSharpSyntaxTree.ParseText("public static class Consumer { }")],
            references.Add(MetadataReference.CreateFromImage(image.ToArray())),
            TestCompilation.CreateOptions(OutputKind.DynamicallyLinkedLibrary));
        TestCompilation.AssertNoErrors(compilation);
        return compilation;
    }
}
