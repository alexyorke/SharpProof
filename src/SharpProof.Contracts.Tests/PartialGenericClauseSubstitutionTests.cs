using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class PartialGenericClauseSubstitutionTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PartialDefinitionAndImplementationBindTheSameReferenceClause(bool constructed)
    {
        var compilation = TestCompilation.Create("PartialGenericClauseSubstitution", """
            using System.Collections.Generic;
            using SharpProof.Attributes;
            public static partial class Target {
                public static partial bool Check<T>(List<T> value);
                public static partial bool Check<T>(List<T> value) {
                    Contract.Requires(value != null);
                    return true;
                }
            }
            """);
        TestCompilation.AssertNoErrors(compilation);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var definition = compilation.GetTypeByMetadataName("Target")!
            .GetMembers("Check").OfType<IMethodSymbol>().Single();
        var implementation = definition.PartialImplementationPart!;
        Assert.That(definition.IsPartialDefinition, Is.True);
        Assert.That(implementation, Is.Not.Null);
        Assert.That(SymbolEqualityComparer.Default.Equals(implementation!.PartialDefinitionPart, definition), Is.True);
        Assert.That(definition.ContainingAssembly, Is.SameAs(compilation.Assembly));
        Assert.That(implementation.ContainingAssembly, Is.SameAs(compilation.Assembly));
        Assert.That(definition.Parameters[0].Type.IsReferenceType, Is.True);
        Assert.That(implementation.Parameters[0].Type.IsReferenceType, Is.True);

        var target = definition;
        var source = implementation;
        if (constructed)
        {
            var argument = compilation.GetSpecialType(SpecialType.System_String);
            target = target.Construct(argument);
            source = source.Construct(argument);
        }
        Assert.That(SymbolEqualityComparer.Default.Equals(target.Parameters[0].Type, source.Parameters[0].Type),
            Is.EqualTo(constructed));

        var fromImplementation = new ContractBinder(compilation, new IrFactory()).Bind(source);
        Assert.That(fromImplementation.IsSuccess, Is.True, fromImplementation.Failure.ToString());
        Assert.That(fromImplementation.Contracts!.Clauses, Has.Length.EqualTo(1));
        Assert.That(fromImplementation.Contracts.Clauses.Single().Kind, Is.EqualTo(BoundContractKind.Requires));

        var fromDefinition = new ContractBinder(compilation, new IrFactory()).Bind(target);
        Assert.That(fromDefinition.IsSuccess, Is.True, fromDefinition.Failure.ToString());
        Assert.That(fromDefinition.Contracts!.Clauses, Has.Length.EqualTo(1));
        Assert.That(fromDefinition.Contracts.Clauses.Single().Kind, Is.EqualTo(BoundContractKind.Requires));
    }
}
