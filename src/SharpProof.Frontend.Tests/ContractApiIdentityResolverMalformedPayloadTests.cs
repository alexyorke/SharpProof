using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ContractApiIdentityResolverMalformedPayloadTests
{
    [Test]
    public void MalformedFileBackedPayloadIsReportedAsUnreadable()
    {
        var attributesAssembly =
            typeof(SharpProof.Attributes.Contract).Assembly;
        using var temporary = new TempDirectory("SharpProofMalformedPayload-");
        var path = Path.Combine(
            temporary.FullName,
            "SharpProof.Attributes.dll");
        File.WriteAllBytes(path, [0x4d, 0x5a, 0x01, 0x02, 0x03, 0x04]);

        var reference = MetadataReference.CreateFromImage(
            File.ReadAllBytes(attributesAssembly.Location),
            filePath: path);
        var compilation = CSharpCompilation.Create(
            "MalformedContractPayload",
            [CSharpSyntaxTree.ParseText("public static class Subject { }")],
            TestMetadataReferences.WithoutSharpProof.Add(reference),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var resolver = ContractApiIdentityResolver.ForCompilation(compilation);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(resolver.Contract, Is.Null);
            Assert.That(resolver.UnreadableContractApiReason, Is.Not.Null);
        }
    }
}
