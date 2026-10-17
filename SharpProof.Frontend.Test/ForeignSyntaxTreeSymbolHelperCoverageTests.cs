using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class ForeignSyntaxTreeSymbolHelperCoverageTests
{
    private static readonly KeyValuePair<string, object>[] DefinedSymbols =
        [new(ContractApiCatalog.ConditionalSymbol, true)];

    [Test]
    public void ActualVisualBasicTreeDoesNotSupplyCSharpConditionalSymbols()
    {
        var options = VisualBasicParseOptions.Default.WithPreprocessorSymbols(DefinedSymbols);
        var tree = VisualBasicSyntaxTree.ParseText("Public Class Subject\nEnd Class", options);
        Assert.That(tree.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error), Is.Empty);
        Assert.That(tree.Options.Language, Is.EqualTo(LanguageNames.VisualBasic));
        Assert.That(tree.Options.PreprocessorSymbolNames, Does.Contain(ContractApiCatalog.ConditionalSymbol));
        Assert.That(options.PreprocessorSymbols.Single().Value, Is.EqualTo(true));
        Assert.That(CSharpPreprocessorSymbols.GetDefined(tree, TestContext.CurrentContext.CancellationToken), Is.Empty);
        Assert.That(CSharpPreprocessorSymbols.IsDefined(tree, ContractApiCatalog.ConditionalSymbol, TestContext.CurrentContext.CancellationToken), Is.False);
    }
}
