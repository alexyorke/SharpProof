using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Frontend;

internal static class CSharpPreprocessorSymbols
{
    internal static ImmutableHashSet<string> GetDefined(
        SyntaxTree tree,
        CancellationToken cancellationToken = default)
    {
        tree = ArgumentNullGuard.NotNull(tree, nameof(tree));

        if (tree.Options is not CSharpParseOptions options)
        {
            return ImmutableHashSet<string>.Empty;
        }

        var defined = options.PreprocessorSymbolNames
            .ToImmutableHashSet(StringComparer.Ordinal)
            .ToBuilder();
        // C# permits #define and #undef only before the first token, so the
        // first token's leading trivia is the complete directive inventory.
        // Avoid walking every trivia node in an otherwise unannotated build.
        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } define:
                    defined.Add(define.Name.ValueText);
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undef:
                    defined.Remove(undef.Name.ValueText);
                    break;
            }
        }

        return defined.ToImmutable();
    }

    internal static bool IsDefined(
        SyntaxTree tree,
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException(
                "A preprocessor symbol is required.",
                nameof(symbol));
        }

        tree = ArgumentNullGuard.NotNull(tree, nameof(tree));
        if (tree.Options is not CSharpParseOptions options)
        {
            return false;
        }

        var defined = options.PreprocessorSymbolNames.Contains(symbol);
        foreach (var trivia in tree.GetRoot(cancellationToken).GetLeadingTrivia())
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (trivia.GetStructure())
            {
                case DefineDirectiveTriviaSyntax { IsActive: true } define
                    when string.Equals(
                        define.Name.ValueText,
                        symbol,
                        StringComparison.Ordinal):
                    defined = true;
                    break;
                case UndefDirectiveTriviaSyntax { IsActive: true } undef
                    when string.Equals(
                        undef.Name.ValueText,
                        symbol,
                        StringComparison.Ordinal):
                    defined = false;
                    break;
            }
        }

        return defined;
    }
}
