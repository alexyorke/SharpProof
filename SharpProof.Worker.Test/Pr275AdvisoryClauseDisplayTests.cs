using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Analyzer;

namespace SharpProof.Worker.Test;

public sealed class Pr275AdvisoryClauseDisplayTests
{
    private const string Source = """
        #nullable enable
        using System;
        using System.Diagnostics.CodeAnalysis;
        public static class Fixture {
            public static string Echo([NotNull()] string? value) {
                ArgumentNullException.ThrowIfNull(value);
                return value;
            }
            private static void Need(bool condition) { }
            public static void Target(int value) {
                Need(condition:
                    value
                    >
                    0);
            }
        }
        """;

    [TestCase(false)]
    [TestCase(true)]
    public void MissingSourceLocationHasUnknownDisplay(bool useNone)
    {
        Assert.That(AdvisoryClauseDisplay.Text(useNone ? Location.None : null), Is.EqualTo("<unknown>"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActualParameterAttributeAndListNameTheParameter(bool useList)
    {
        var root = EmittedRoot();
        var parameter = root.DescendantNodes().OfType<ParameterSyntax>().Single(static node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Identifier.ValueText == "value" && node.AttributeLists.Count == 1));
        var list = parameter.AttributeLists.Single();
        var location = useList ? list.GetLocation() : list.Attributes.Single().GetLocation();
        Assert.That(AdvisoryClauseDisplay.Text(location), Is.EqualTo("[NotNull()] value"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActualInvocationAndNamedArgumentDisplayTheSameCondition(bool useArgument)
    {
        var root = EmittedRoot();
        var invocation = root.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(static node => node is null ? throw new System.ArgumentNullException(nameof(node)) : (node.Expression is IdentifierNameSyntax { Identifier.ValueText: "Need" }));
        var location = useArgument ? invocation.ArgumentList.Arguments.Single().GetLocation() : invocation.GetLocation();
        Assert.That(AdvisoryClauseDisplay.Text(location), Is.EqualTo("value > 0"));
    }

    [Test]
    public void ActualOtherSyntaxKeepsItsText()
    {
        var root = EmittedRoot();
        var expression = root.DescendantNodes().OfType<ReturnStatementSyntax>().Single().Expression!;
        Assert.That(AdvisoryClauseDisplay.Text(expression.GetLocation()), Is.EqualTo("value"));
    }

    private static SyntaxNode EmittedRoot()
    {
        var compilation = TestCompilation.Create("Pr275AdvisoryClauseDisplay", Source, includeSharpProofReference: false);
        TestCompilation.AssertNoErrors(compilation);
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream);
        Assert.That(emitted.Diagnostics.Where(static diagnostic => diagnostic is null ? throw new System.ArgumentNullException(nameof(diagnostic)) : (diagnostic.Severity == DiagnosticSeverity.Error)), Is.Empty);
        Assert.That(emitted.Success, Is.True);
        return compilation.SyntaxTrees.Single().GetRoot();
    }
}
