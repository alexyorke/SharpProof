namespace SharpProof.Frontend;

internal static class SyntaxSite
{
    internal static bool IsSame(SyntaxNode left, SyntaxNode right)
    {
        return left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
    }
}
