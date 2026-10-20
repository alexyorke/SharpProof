using System.Collections.Immutable;
using SharpProof.Specs;

namespace SharpProof.Effects.Test;

[TestFixture]
public sealed class ResolvedApiSpecNullabilityContractTests
{
    [Test]
    public void TryGetPublishesItsShippedNonNullOutputAnnotation()
    {
        var owner = typeof(ResolvedApiSpecTable);
        var method = owner.GetMethod(nameof(ResolvedApiSpecTable.TryGet));
        Assert.That(method, Is.Not.Null);
        Assert.That(method!.IsPublic, Is.True);
        Assert.That(method.IsStatic, Is.False);
        Assert.That(method.ReturnType, Is.EqualTo(typeof(bool)));
        var parameters = method.GetParameters().ToImmutableArray();
        Assert.That(parameters, Has.Length.EqualTo(2));
        var output = parameters[1];
        Assert.That(output.Name, Is.EqualTo("spec"));
        Assert.That(output.IsOut, Is.True);
        Assert.That(output.ParameterType, Is.EqualTo(typeof(ResolvedApiSpec).MakeByRefType()));

        var annotations = output.GetCustomAttributes(inherit: false)
            .Where(attribute => attribute is null ? throw new System.ArgumentNullException(nameof(attribute)) : (attribute.GetType().FullName ==
                "System.Diagnostics.CodeAnalysis.NotNullWhenAttribute"))
            .ToImmutableArray();
        Assert.That(annotations, Has.Length.EqualTo(1));
        var annotation = annotations[0];
        var attributeType = annotation.GetType();
        Assert.That(owner.Assembly.GetName().Name, Is.EqualTo("SharpProof.Effects"));
        Assert.That(attributeType.Assembly, Is.SameAs(owner.Assembly));
        Assert.That(attributeType.Module, Is.SameAs(owner.Module));
        Assert.That(attributeType.IsPublic, Is.False);
        Assert.That(attributeType.IsSealed, Is.True);

        var returnValue = attributeType.GetProperty(
            "ReturnValue", BindingFlags.Public | BindingFlags.Instance);
        Assert.That(returnValue, Is.Not.Null);
        Assert.That(returnValue!.PropertyType, Is.EqualTo(typeof(bool)));
        Assert.That(returnValue.GetMethod, Is.Not.Null);
        Assert.That(returnValue.GetMethod!.IsPublic, Is.True);
        Assert.That(returnValue.SetMethod, Is.Null);
        Assert.That(returnValue.GetValue(annotation), Is.EqualTo(true));

        var usage = attributeType.GetCustomAttribute<AttributeUsageAttribute>();
        Assert.That(usage, Is.Not.Null);
        Assert.That(usage!.ValidOn, Is.EqualTo(AttributeTargets.Parameter));
        Assert.That(usage.Inherited, Is.False);
        Assert.That(usage.AllowMultiple, Is.False);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TryGetHonorsItsNonNullOutputContract(bool knownSymbol)
    {
        var compilation = TestCompilation.Create(
            "ResolvedApiSpecNullability", "public class C { public void Missing() {} }");
        Assert.That(compilation.GetDiagnostics().Where(diagnostic =>
            diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        var table = new ApiSpecResolver(ApiSpecTable.Default).Resolve(compilation);
        Assert.That(table.Specs, Is.Not.Empty);
        var expected = table.Specs.First(spec => spec is null ? throw new System.ArgumentNullException(nameof(spec)) : (spec.Symbol is IMethodSymbol));
        ISymbol symbol = knownSymbol
            ? expected.Symbol
            : compilation.GetTypeByMetadataName("C")!.GetMembers("Missing").Single();

        var found = table.TryGet(symbol, out var actual);
        Assert.That(found, Is.EqualTo(knownSymbol));
        if (knownSymbol)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual, Is.SameAs(expected));
        }
        else
        {
            Assert.That(actual, Is.Null);
        }
    }
}
