namespace SharpProof.Frontend;

internal static class SyntaxSite
{
    internal static bool IsSame(SyntaxNode left, SyntaxNode right)
    {
        left = RepositoryParameterGuard.NotNull(left, nameof(left));

        right = RepositoryParameterGuard.NotNull(right, nameof(right));

        return left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
    }
}
