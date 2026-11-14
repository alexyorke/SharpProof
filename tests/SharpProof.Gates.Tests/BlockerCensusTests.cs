using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using SharpProof.Gates.Corpus;

namespace SharpProof.Gates.Test;

[TestFixture]
public sealed class BlockerCensusTests
{
    [Test]
    public void EveryMethodHasAtLeastOneRowAndExactBodiesStandAlone()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var source = document with
        {
            Methods = [.. document.Methods.Where(static method => method.SourceId == "nodatime").Take(40)]
        };
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(source, cancellationToken, purity: true);
        var declarations = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(static declaration => OpenSourceCorpusRunner.CorpusMethodId(declaration) != null)
            .ToArray();

        var rows = declarations.Select(declaration => BlockerCensus.Blockers(compilation, declaration, cancellationToken)).ToArray();

        Assert.That(declarations, Has.Length.EqualTo(40));
        Assert.That(rows, Has.All.Not.Empty);
        Assert.That(rows.Where(static row => row.Contains("(lowers exactly)")), Has.All.Length.EqualTo(1));
        Assert.That(rows.Any(static row => row.Contains("(lowers exactly)")), Is.True);
        Assert.That(rows.Any(static row => !row.Contains("(lowers exactly)")), Is.True);
    }

    [Test]
    public void EffectSitesAreReportedOnlyForBodiesThatLowerExactly()
    {
        var cancellationToken = TestContext.CurrentContext.CancellationToken;
        var document = OpenSourceCorpusCatalog.Load(RepositoryLayout.FindRoot());
        var source = document with
        {
            Methods = [.. document.Methods.Where(static method => method.SourceId == "humanizer").Take(60)]
        };
        var compilation = OpenSourceCorpusRunner.PrepareExceptionProbe(source, cancellationToken, purity: true);
        var exact = compilation.SyntaxTrees
            .SelectMany(tree => tree.GetRoot(cancellationToken).DescendantNodes().OfType<MethodDeclarationSyntax>())
            .Where(static declaration => OpenSourceCorpusRunner.CorpusMethodId(declaration) != null)
            .Where(declaration => BlockerCensus.Blockers(compilation, declaration, cancellationToken).Contains("(lowers exactly)"))
            .ToArray();

        var rows = exact.Select(declaration => BlockerCensus.EffectSites(compilation, declaration, cancellationToken)).ToArray();

        Assert.That(exact, Is.Not.Empty);
        Assert.That(rows, Has.All.Not.Empty);
        Assert.That(rows.Where(static row => row.Contains("(no purity-relevant effect site)")), Has.All.Length.EqualTo(1));
        Assert.That(rows.SelectMany(static row => row), Has.None.EqualTo("(does not lower exactly)"));
    }
}
