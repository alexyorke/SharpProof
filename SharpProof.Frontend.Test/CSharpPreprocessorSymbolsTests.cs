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
            ContractApiCatalog.ConditionalSymbol);
        var fromDirective = Parse(
            """
            #define SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """);
        var removedByDirective = Parse(
            """
            #undef SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """,
            ContractApiCatalog.ConditionalSymbol);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    fromOptions,
                    ContractApiCatalog.ConditionalSymbol),
                Is.True);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    fromDirective,
                    ContractApiCatalog.ConditionalSymbol),
                Is.True);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    removedByDirective,
                    ContractApiCatalog.ConditionalSymbol),
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
            """);
        var removed = Parse(
            """
            #define SHARPPROOF_CONTRACTS
            #undef SHARPPROOF_CONTRACTS
            internal static class Subject { }
            """);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    inactive,
                    ContractApiCatalog.ConditionalSymbol),
                Is.False);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    removed,
                    ContractApiCatalog.ConditionalSymbol),
                Is.False);
        }
    }

    private static SyntaxTree Parse(
        string source,
        params string[] symbols)
    {
        return CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(
                LanguageVersion.CSharp12,
                preprocessorSymbols: symbols));
    }
}
