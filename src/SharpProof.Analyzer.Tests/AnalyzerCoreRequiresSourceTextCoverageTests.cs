using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;

namespace SharpProof.Analyzer.Test;

[TestFixture]
public sealed class AnalyzerCoreRequiresSourceTextCoverageTests
{
    [Test]
    public void AParsedNamedArgumentUsesItsConditionWithNormalizedWhitespace()
    {
        var root = Parsed("""
            public static class Fixture
            {
                private static void Need(bool condition) { }
                public static void Target(int value)
                {
                    Need(condition:
                        value
                        >
                        0);
                }
            }
            """);
        var argument = root.DescendantNodes().OfType<ArgumentSyntax>().Single();
        Assert.That(argument.NameColon!.Name.Identifier.ValueText, Is.EqualTo("condition"));
        Assert.That(AdvisoryRequiresDiagnostics.ClauseText(argument.GetLocation()), Is.EqualTo("value > 0"));
        Assert.That(AdvisoryRequiresDiagnostics.ClauseText(argument.Parent!.Parent!.GetLocation()), Is.EqualTo("value > 0"));
    }

    [Test]
    public void ParsedAttributeAndListLocationsNameTheSameConstrainedParameter()
    {
        var root = Parsed("""
            using SharpProof.Attributes;
            public static class Fixture
            {
                public static int Target([InRange(0, 10)] int value) => value;
            }
            """);
        var list = root.DescendantNodes().OfType<AttributeListSyntax>().Single();
        Assert.That(list.Parent, Is.InstanceOf<ParameterSyntax>());
        var attribute = list.Attributes.Single();
        Assert.That(AdvisoryRequiresDiagnostics.ClauseText(attribute.GetLocation()), Is.EqualTo("[InRange(0, 10)] value"));
        Assert.That(AdvisoryRequiresDiagnostics.ClauseText(list.GetLocation()), Is.EqualTo("[InRange(0, 10)] value"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AMissingSourceLocationRetainsTheUnknownPlaceholder(bool absent)
    {
        Location? location = absent ? null : Location.None;
        Assert.That(AdvisoryRequiresDiagnostics.ClauseText(location), Is.EqualTo("<unknown>"));
    }

    [Test]
    public void AParsedIdentifierRetainsItsSourceText()
    {
        var root = Parsed("public static class Fixture { public static int Target(int value) => value; }");
        var identifier = root.DescendantNodes().OfType<IdentifierNameSyntax>().Single();
        Assert.That(AdvisoryRequiresDiagnostics.ClauseText(identifier.GetLocation()), Is.EqualTo("value"));
    }

    private static SyntaxNode Parsed(string source)
    {
        System.ArgumentNullException.ThrowIfNull(source);
        var compilation = AnalyzerTestHost.CreateCompilation(source, []);
        Assert.That(compilation.GetDiagnostics().Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        return compilation.SyntaxTrees.Single().GetRoot();
    }
}
