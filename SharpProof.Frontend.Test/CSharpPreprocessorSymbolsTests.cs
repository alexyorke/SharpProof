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
            ContractApiMetadata.ConditionalSymbol);
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
            ContractApiMetadata.ConditionalSymbol);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    fromOptions,
                    ContractApiMetadata.ConditionalSymbol),
                Is.True);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    fromDirective,
                    ContractApiMetadata.ConditionalSymbol),
                Is.True);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    removedByDirective,
                    ContractApiMetadata.ConditionalSymbol),
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
                    ContractApiMetadata.ConditionalSymbol),
                Is.False);
            Assert.That(
                CSharpPreprocessorSymbols.IsDefined(
                    removed,
                    ContractApiMetadata.ConditionalSymbol),
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
