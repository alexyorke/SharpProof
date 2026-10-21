using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace SharpProof.Frontend.Test;

[TestFixture]
public sealed class CSharpPreprocessorSymbolsTests
{
    [Test]
    public void ParseOptionAndActiveDirectiveStateAreCombined()
    {
        var fromOptions = Parse(
            "internal static class Subject { }",
            [ContractApiCatalog.ConditionalSymbol]);
        var fromDirective = Parse(
            """
            #define SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """, []);
        var removedByDirective = Parse(
            """
            #undef SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """,
            [ContractApiCatalog.ConditionalSymbol]);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    fromOptions,
                    ContractApiCatalog.ConditionalSymbol, TestContext.CurrentContext.CancellationToken),
                Is.True);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    fromDirective,
                    ContractApiCatalog.ConditionalSymbol, TestContext.CurrentContext.CancellationToken),
                Is.True);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    removedByDirective,
                    ContractApiCatalog.ConditionalSymbol, TestContext.CurrentContext.CancellationToken),
                Is.False);
        }
    }

    [Test]
    public void InactiveAndRemovedDefinitionsDoNotLeak()
    {
        var inactive = Parse(
            """
            #if NEVER
            #define SHARPPROOF_CONTRACTS
            #endif
            internal static class Subject { }
            """, []);
        var removed = Parse(
            """
            #define SHARPPROOF_CONTRACTS
            #undef SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """, []);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    inactive,
                    ContractApiCatalog.ConditionalSymbol, TestContext.CurrentContext.CancellationToken),
                Is.False);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    removed,
                    ContractApiCatalog.ConditionalSymbol, TestContext.CurrentContext.CancellationToken),
                Is.False);
        }
    }

    private static SyntaxTree Parse(
        string source,
        ImmutableArray<string> symbols)
    {
        if (symbols.IsDefault)
        { throw new ArgumentNullException(nameof(symbols)); }
        System.ArgumentNullException.ThrowIfNull(source);
        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: symbols));
    }
}
