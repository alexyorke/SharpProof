using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using NUnit.Framework;
using SharpProof.Symbolic;

namespace SharpProof.ToolingTest;

[TestFixture]
public sealed class SymbolicCliDiagnosticSelectorTests
{
    [Test]
    public void SelectRelevant_OrdersTargetThenFileThenProject_AndExcludesOtherTrees()
    {
        var tree = CSharpSyntaxTree.ParseText("class C { void M() { } }\n");
        var otherTree = CSharpSyntaxTree.ParseText("class Other { }\n");
        var targetSpan = new TextSpan(10, 4);
        var diagnostics = new[]
        {
            CreateDiagnostic("SP9003", Location.None),
            CreateDiagnostic("SP9002", Location.Create(tree, new TextSpan(20, 1))),
            CreateDiagnostic("SP9001", Location.Create(tree, targetSpan)),
            CreateDiagnostic("SP9004", Location.Create(otherTree, new TextSpan(0, 1)))
        };

        var selected = SymbolicCliDiagnosticSelector.SelectRelevant(
            diagnostics,
            tree,
            targetSpan.Start,
            line: 1);

        Assert.That(selected.Select(static item => item.Diagnostic.Id),
            Is.EqualTo(new[] { "SP9001", "SP9002", "SP9003" }));
        Assert.That(selected.Select(static item => item.IsTarget),
            Is.EqualTo(new[] { true, false, false }));
    }

    private static Diagnostic CreateDiagnostic(string id, Location location)
    {
        var descriptor = new DiagnosticDescriptor(
            id,
            id,
            id,
            "Test",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, location);
    }
}
