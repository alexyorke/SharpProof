using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.ProofCore.Smt;
using SharpProof.Symbolic;
using SharpProof.Symbolic.Smt;

namespace SharpProof.Test;

[TestFixture]
public sealed class SmtFormulaVersionRewriterTests
{
    [Test]
    public void RewriteSymbolVersions_DoesNotRewritePrefixSiblingVariableName()
    {
        var symbol = GetParameterSymbol("myField");
        var baseName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        var formula = new SmtVariable(baseName + "B@v2", SmtValueKind.Int);

        var rewritten = SmtFormulaVersionRewriter.RewriteSymbolVersions(
            formula,
            CreateVersions(symbol, 0),
            CreateVersions(symbol, 1));

        Assert.That(rewritten, Is.EqualTo(formula));
    }

    [Test]
    public void RewriteSymbolVersions_RewritesElementAccessForTargetVariable()
    {
        var symbol = GetParameterSymbol("myField");
        var baseName = SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition);
        var formula = new SmtVariable(baseName + "[0]", SmtValueKind.Int);

        var rewritten = SmtFormulaVersionRewriter.RewriteSymbolVersions(
            formula,
            CreateVersions(symbol, 0),
            CreateVersions(symbol, 1));

        Assert.That(rewritten, Is.EqualTo(new SmtVariable(baseName + "@v1[0]", SmtValueKind.Int)));
    }

    private static ImmutableDictionary<ISymbol, int> CreateVersions(ISymbol symbol, int version)
    {
        return ImmutableDictionary<ISymbol, int>
            .Empty
            .WithComparers(SymbolEqualityComparer.Default)
            .Add(symbol.OriginalDefinition, version);
    }

    private static IParameterSymbol GetParameterSymbol(string parameterName)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(@"
public sealed class TestClass
{
    public void TestMethod(int myField)
    {
    }
}");
        var compilation = CSharpCompilation.Create(
            "SmtFormulaVersionRewriterTests",
            new[] { syntaxTree },
            AnalyzerTestHost.GetTrustedPlatformReferences());
        var semanticModel = compilation.GetSemanticModel(syntaxTree);
        var method = syntaxTree
            .GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Single();
        return method.ParameterList.Parameters
            .Select(parameter => semanticModel.GetDeclaredSymbol(parameter))
            .OfType<IParameterSymbol>()
            .Single(parameter => parameter.Name == parameterName);
    }
}