using Microsoft.CodeAnalysis;
using NUnit.Framework;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ContractSpecializationPolicyTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void NamedConstructionRetainsTheSdkArityPolicy(bool wrongArity)
    {
        var compilation = CreateCompilation();
        var definition = compilation.GetTypeByMetadataName("Target`1")!;
        var arguments = wrongArity ? Array.Empty<ITypeSymbol>()
            : new ITypeSymbol[] { compilation.GetSpecialType(SpecialType.System_String) };
        if (wrongArity)
        {
            Assert.Throws<ArgumentException>(() => definition.Construct(arguments));
        }
        var success = ContractSpecializationPolicy.TrySpecialize(
            (definition, arguments),
            static state => state.definition.Construct(state.arguments),
            out INamedTypeSymbol specialized);
        Assert.That(success, Is.EqualTo(!wrongArity));
        if (wrongArity)
        {
            Assert.That(specialized, Is.Null);
            return;
        }
        Assert.That(SymbolEqualityComparer.Default.Equals(
            specialized, definition.Construct(arguments)), Is.True);
        Assert.That(specialized.OriginalDefinition, Is.SameAs(definition));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MethodConstructionRetainsTheSdkArityPolicy(bool wrongArity)
    {
        var compilation = CreateCompilation();
        var owner = compilation.GetTypeByMetadataName("Target`1")!
            .Construct(compilation.GetSpecialType(SpecialType.System_String));
        var definition = owner.GetMembers("Echo").OfType<IMethodSymbol>().Single();
        var arguments = wrongArity ? Array.Empty<ITypeSymbol>()
            : new ITypeSymbol[] { compilation.GetSpecialType(SpecialType.System_Int32) };
        if (wrongArity)
        {
            Assert.Throws<ArgumentException>(() => definition.Construct(arguments));
        }
        var success = ContractSpecializationPolicy.TrySpecialize(
            (definition, arguments),
            static state => state.definition.Construct(state.arguments),
            out IMethodSymbol specialized);
        Assert.That(success, Is.EqualTo(!wrongArity));
        if (wrongArity)
        {
            Assert.That(specialized, Is.Null);
            return;
        }
        Assert.That(SymbolEqualityComparer.Default.Equals(
            specialized, definition.Construct(arguments)), Is.True);
        Assert.That(specialized.ContainingType, Is.SameAs(owner));
        Assert.That(specialized.Parameters[0].Type.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(specialized.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
    }

    [Test]
    public void ReconstructingAnActualConstructedTypeKeepsTheSdkInvalidOperationException()
    {
        var compilation = CreateCompilation();
        var arguments = new ITypeSymbol[] { compilation.GetSpecialType(SpecialType.System_String) };
        var constructed = compilation.GetTypeByMetadataName("Target`1")!.Construct(arguments);
        Assert.Throws<InvalidOperationException>(() => constructed.Construct(arguments));
        Assert.Throws<InvalidOperationException>(() => ContractSpecializationPolicy.TrySpecialize(
            (constructed, arguments),
            static state => state.constructed.Construct(state.arguments),
            out INamedTypeSymbol _));
    }

    private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation CreateCompilation()
    {
        var compilation = TestCompilation.Create("SpecializationPolicy", """
            public class Target<T> {
                public U Echo<U>(T value, U returned) => returned;
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        return compilation;
    }
}
