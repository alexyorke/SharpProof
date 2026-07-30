using NUnit.Framework;

namespace SharpProof.Specs.Test;

[TestFixture]
public sealed class SpecIdentifierTests
{
    [Test]
    public void IdentifiersHaveStableScopedValueSemantics()
    {
        var templates = ApiSpecTable.Default.Templates;
        var spec = templates[0].Id;
        var sameSpec = templates[0].Id;
        var otherSpec = templates[1].Id;
        var variables = templates
            .First(static template =>
                template.Variables.Length >= 2)
            .Variables;
        var variable = variables[0].Id;
        var sameVariable = variables[0].Id;
        var otherVariable = variables[1].Id;

        using (Assert.EnterMultipleScope())
        {
            Assert.That(spec.Equals((object)sameSpec), Is.True);
            Assert.That(spec != otherSpec, Is.True);
            Assert.That(
                spec.ToString(),
                Is.EqualTo("spec" + spec.Value));
            Assert.That(variable.IsDefault, Is.False);
            Assert.That(variable.Equals((object)sameVariable), Is.True);
            Assert.That(variable == sameVariable, Is.True);
            Assert.That(variable != otherVariable, Is.True);
            Assert.That(
                variable.ToString(),
                Is.EqualTo(
                    variable.Spec.ToString() +
                    ".var" +
                    variable.Value));
        }
    }
}
