using Microsoft.CodeAnalysis;
using NUnit.Framework;
using SharpProof.Ir;

namespace SharpProof.Contracts.Test;

[TestFixture]
public sealed class ClosedContractAttributeBindingBoundaryTests
{
    [TestCase("Positive", "int")]
    [TestCase("InRange(-2L, 2L)", "int")]
    [TestCase("NotNull", "string")]
    public void ExistingProjectionRejectsIncompatibleLegalValues(string attributeText, string sourceType)
    {
        ArgumentNullException.ThrowIfNull(attributeText);
        ArgumentNullException.ThrowIfNull(sourceType);
        var compilation = TestCompilation.Create("AttributeProjectionBoundary", $$"""
            using SharpProof.Attributes;
            public static class Target {
                public static {{sourceType}} Read([{{attributeText}}] {{sourceType}} value) => value;
            }
            """);
        using var image = new MemoryStream();
        Assert.That(compilation.Emit(image).Success, Is.True);
        var target = compilation.GetTypeByMetadataName("Target")!.GetMembers("Read").OfType<IMethodSymbol>().Single();
        Assert.That(SymbolEqualityComparer.Default.Equals(target.ContainingAssembly, compilation.Assembly), Is.True);
        var parameter = target.Parameters.Single();
        var attribute = parameter.GetAttributes().Single();
        Assert.That(attribute.ApplicationSyntaxReference, Is.Not.Null);
        var api = ContractApiSymbols.TryCreate(compilation)!;
        var legacyValidation = ClosedContractAttributeValidator.Validate(attribute, parameter.Type, parameter.RefKind, api.Selections);
        var totalValidation = ClosedContractAttributeValidator.Validate(attribute, parameter.Type, parameter.RefKind, api.Selections, includeUnsigned64: true);
        Assert.That(legacyValidation.IsValid, Is.True);
        Assert.That(totalValidation.IsValid, Is.True);

        var legacyFactory = new IrFactory();
        var legacyValue = legacyFactory.Variable(legacyFactory.CreateVariable("incompatible", legacyFactory.BooleanType));
        Assert.That(legacyFactory.GetTypeInfo(legacyValue.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
        Assert.That(legacyValidation.SupportsLegacyValue(legacyFactory, legacyValue), Is.False);
        var totalFactory = new IrFactory(IrExecutionSemantics.Total);
        var totalValue = totalFactory.Variable(totalFactory.CreateVariable("incompatible", totalFactory.BooleanType));
        Assert.That(totalFactory.GetTypeInfo(totalValue.Type).Kind, Is.EqualTo(IrTypeKind.Boolean));
        Assert.That(totalValidation.CreateTotalPredicate(totalFactory, totalValue, totalFactory.GetTypeInfo(totalValue.Type)), Is.Null);

        var legacyBound = new ContractBinder(compilation, new IrFactory()).Bind(target);
        Assert.That(legacyBound.IsSuccess, Is.True);
        Assert.That(legacyBound.Contracts!.Clauses, Has.Length.EqualTo(1));
        var factory = new IrFactory(IrExecutionSemantics.Total);
        var context = new SharpProof.Frontend.TotalLoweringContext(factory, target);
        var totalBound = new ContractBinder(compilation, factory).BindTotal(context);
        Assert.That(totalBound.IsSuccess, Is.True);
        Assert.That(totalBound.Clauses, Has.Length.EqualTo(1));
        Assert.That(totalBound.Clauses.Single().Kind, Is.EqualTo(BoundContractKind.Requires));
    }
}
