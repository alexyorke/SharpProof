using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.CompilerCollector;

namespace SharpProof.Worker.Test;

[TestFixture]
public sealed class MetadataTestSubjectReferenceTests
{
    private const string Caller = "public static class Subject { public static int Target(int value) => Library.Target(value); }";

    [TestCase(false)]
    [TestCase(true)]
    public void LibraryReferencesFollowTheExplicitOption(bool includeSharpProofReference)
    {
        using var subject = new MetadataTestSubject(
            "public static class Library { public static int Target(int value) => value; }", Caller,
            includeSharpProofReference: includeSharpProofReference);
        Assert.That(subject.LibraryCompilation.GetTypeByMetadataName("SharpProof.Attributes.Contract") is not null,
            Is.EqualTo(includeSharpProofReference));
        Assert.That(subject.InvokeRoot(7), Is.EqualTo(7));
    }

    [Test]
    public void OptedInLibraryRetainsGenuineMetadataParameterAttributes()
    {
        using var subject = new MetadataTestSubject(
            "using SharpProof.Attributes; public static class Library { public static int Target([Positive] int value) => value; }",
            Caller, includeSharpProofReference: true);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var attribute = method.Parameters.Single().GetAttributes().Single().AttributeClass!;
        Assert.That(attribute.ContainingAssembly.Name, Is.EqualTo("SharpProof.Attributes"));
        var body = new CompilerTotalIlBodyProvider(subject.Compilation, null).Resolve(method,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(body, Is.Not.Null);
        Assert.That(body!.ParameterAttributes.Single().Kind, Is.EqualTo("Positive"));
    }

    [Test]
    public void DefaultReferenceFreeLibraryKeepsLookalikeAttributesLocal()
    {
        using var subject = new MetadataTestSubject("""
            namespace SharpProof.Attributes {
                public sealed class PositiveAttribute : System.Attribute { }
            }
            public static class Library {
                public static int Target([SharpProof.Attributes.Positive] int value) => value;
            }
            """, Caller);
        Assert.That(subject.LibraryCompilation.GetTypeByMetadataName("SharpProof.Attributes.Contract"), Is.Null);
        var method = subject.Compilation.GetTypeByMetadataName("Library")!.GetMembers("Target").OfType<IMethodSymbol>().Single();
        var attribute = method.Parameters.Single().GetAttributes().Single().AttributeClass!;
        Assert.That(attribute.ContainingAssembly.Name, Is.EqualTo("TypedIlLibrary"));
        var body = new CompilerTotalIlBodyProvider(subject.Compilation, null).Resolve(method,
            TestContext.CurrentContext.CancellationToken);
        Assert.That(body, Is.Null, "A local lookalike must not become authenticated SharpProof evidence.");
    }
}
