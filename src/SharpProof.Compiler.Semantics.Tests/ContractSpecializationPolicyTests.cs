using System.Collections.Immutable;
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
        var arguments = wrongArity ? ImmutableArray<ITypeSymbol>.Empty
            : ImmutableArray.Create<ITypeSymbol>(compilation.GetSpecialType(SpecialType.System_String));
        var annotations = arguments.Select(static argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : argument.NullableAnnotation).ToImmutableArray();
        if (wrongArity)
        {
            Assert.Throws<ArgumentException>(new Action(() => definition.Construct(arguments, annotations)));
        }
        var success = ContractSpecializationPolicy.TrySpecialize(
            (definition, arguments, annotations),
            static state => state.definition.Construct(state.arguments, state.annotations),
            out INamedTypeSymbol specialized);
        Assert.That(success, Is.EqualTo(!wrongArity));
        if (wrongArity)
        {
            Assert.That(specialized, Is.Null);
            return;
        }
        Assert.That(SymbolEqualityComparer.Default.Equals(
            specialized, definition.Construct(arguments, annotations)), Is.True);
        Assert.That(specialized.OriginalDefinition, Is.SameAs(definition));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MethodConstructionRetainsTheSdkArityPolicy(bool wrongArity)
    {
        var compilation = CreateCompilation();
        var ownerDefinition = compilation.GetTypeByMetadataName("Target`1")!;
        var ownerArguments = ImmutableArray.Create<ITypeSymbol>(compilation.GetSpecialType(SpecialType.System_String));
        var owner = ownerDefinition.Construct(ownerArguments,
            ownerArguments.Select(static argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : argument.NullableAnnotation).ToImmutableArray());
        var definition = owner.GetMembers("Echo").OfType<IMethodSymbol>().Single();
        var arguments = wrongArity ? ImmutableArray<ITypeSymbol>.Empty
            : ImmutableArray.Create<ITypeSymbol>(compilation.GetSpecialType(SpecialType.System_Int32));
        var annotations = arguments.Select(static argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : argument.NullableAnnotation).ToImmutableArray();
        if (wrongArity)
        {
            Assert.Throws<ArgumentException>(new Action(() => definition.Construct(arguments, annotations)));
        }
        var success = ContractSpecializationPolicy.TrySpecialize(
            (definition, arguments, annotations),
            static state => state.definition.Construct(state.arguments, state.annotations),
            out IMethodSymbol specialized);
        Assert.That(success, Is.EqualTo(!wrongArity));
        if (wrongArity)
        {
            Assert.That(specialized, Is.Null);
            return;
        }
        Assert.That(SymbolEqualityComparer.Default.Equals(
            specialized, definition.Construct(arguments, annotations)), Is.True);
        Assert.That(specialized.ContainingType, Is.SameAs(owner));
        Assert.That(specialized.Parameters[0].Type.SpecialType, Is.EqualTo(SpecialType.System_String));
        Assert.That(specialized.ReturnType.SpecialType, Is.EqualTo(SpecialType.System_Int32));
    }

    [Test]
    public void ReconstructingAnActualConstructedTypeKeepsTheSdkInvalidOperationException()
    {
        var compilation = CreateCompilation();
        var arguments = ImmutableArray.Create<ITypeSymbol>(compilation.GetSpecialType(SpecialType.System_String));
        var annotations = arguments.Select(static argument => argument is null ? throw new System.ArgumentNullException(nameof(argument)) : argument.NullableAnnotation).ToImmutableArray();
        var constructed = compilation.GetTypeByMetadataName("Target`1")!.Construct(arguments, annotations);
        Assert.Throws<InvalidOperationException>(new Action(() => constructed.Construct(arguments, annotations)));
        Assert.Throws<InvalidOperationException>(new Action(() => ContractSpecializationPolicy.TrySpecialize(
            (constructed, arguments, annotations),
            static state => state.constructed.Construct(state.arguments, state.annotations),
            out INamedTypeSymbol _)));
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
