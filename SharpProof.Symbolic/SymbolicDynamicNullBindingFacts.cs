using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic
{
    internal static class SymbolicDynamicNullBindingFacts
    {
        internal const string RuntimeBinderExceptionType = "Microsoft.CSharp.RuntimeBinder.RuntimeBinderException";
        internal const string MemberCategory = "definite_dynamic_member_null_binding";
        internal const string IndexCategory = "definite_dynamic_index_null_binding";
        internal const string InvocationCategory = "definite_dynamic_invocation_null_binding";
        internal const string MemberSource = "dynamic_member";
        internal const string IndexSource = "dynamic_index";
        internal const string InvocationSource = "dynamic_invocation";

        internal static bool IsDynamicNullBindingCategory(string category)
        {
            return string.Equals(category, MemberCategory, StringComparison.Ordinal) ||
                string.Equals(category, InvocationCategory, StringComparison.Ordinal) ||
                string.Equals(category, IndexCategory, StringComparison.Ordinal);
        }

        internal static bool TryGetDynamicNullBindingShape(
            SyntaxNode node,
            Func<ExpressionSyntax, ExpressionSyntax> unwrapExpression,
            out SyntaxNode site,
            out ExpressionSyntax receiver,
            out string category,
            out string source)
        {
            site = null!;
            receiver = null!;
            category = string.Empty;
            source = string.Empty;

            switch (node)
            {
                case MemberAccessExpressionSyntax memberAccess:
                    if (memberAccess.Parent is InvocationExpressionSyntax { Expression: var invocationExpression } &&
                        ReferenceEquals(invocationExpression, memberAccess))
                    {
                        return false;
                    }

                    site = memberAccess;
                    receiver = memberAccess.Expression;
                    category = MemberCategory;
                    source = MemberSource;
                    return true;

                case ElementAccessExpressionSyntax elementAccess:
                    site = elementAccess;
                    receiver = elementAccess.Expression;
                    category = IndexCategory;
                    source = IndexSource;
                    return true;

                case InvocationExpressionSyntax invocation:
                    var unwrappedInvocationExpression = unwrapExpression(invocation.Expression);
                    site = invocation;
                    receiver = unwrappedInvocationExpression is MemberAccessExpressionSyntax memberInvocation
                        ? memberInvocation.Expression
                        : invocation.Expression;
                    category = InvocationCategory;
                    source = InvocationSource;
                    return true;

                default:
                    return false;
            }
        }
    }
}
