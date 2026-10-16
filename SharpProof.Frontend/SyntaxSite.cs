namespace SharpProof.Frontend;

internal static class SyntaxSite
{
    internal static bool IsSame(SyntaxNode left, SyntaxNode right)
    {
        if (left is null)
        {
            throw new System.ArgumentNullException(nameof(left));
        }

        if (right is null)
        {
            throw new System.ArgumentNullException(nameof(right));
        }

        return left.SyntaxTree == right.SyntaxTree && left.Span == right.Span;
    }
}
