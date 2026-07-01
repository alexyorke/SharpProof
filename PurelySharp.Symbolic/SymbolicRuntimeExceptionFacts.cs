using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PurelySharp.Symbolic
{
    internal static class SymbolicRuntimeExceptionFacts
    {
        internal static class ExceptionTypes
        {
            internal const string Unknown = "unknown";
            internal const string Exception = "System.Exception";
            internal const string NullReferenceException = "System.NullReferenceException";
            internal const string DivideByZeroException = "System.DivideByZeroException";
            internal const string OverflowException = "System.OverflowException";
            internal const string ArgumentNullException = "System.ArgumentNullException";
            internal const string InvalidOperationException = "System.InvalidOperationException";
            internal const string InvalidCastException = "System.InvalidCastException";
            internal const string ArrayTypeMismatchException = "System.ArrayTypeMismatchException";
            internal const string IndexOutOfRangeException = "System.IndexOutOfRangeException";
            internal const string ArgumentOutOfRangeException = "System.ArgumentOutOfRangeException";
        }

        internal static class ExceptionCategories
        {
            internal const string DirectThrow = "direct_throw";
            internal const string Rethrow = "rethrow";
            internal const string SourceCallee = "source_callee";
            internal const string EffectSummary = "effect_summary";
            internal const string DefiniteThrowNull = "definite_throw_null";
            internal const string DefiniteDivideByZero = "definite_divide_by_zero";
            internal const string DefiniteCheckedIntegralOverflow = "definite_checked_integral_overflow";
            internal const string DefiniteNegativeArrayLength = "definite_negative_array_length";
            internal const string DefiniteNullDereference = "definite_null_dereference";
            internal const string DefiniteAwaitNull = "definite_await_null";
            internal const string DefiniteLockNull = "definite_lock_null";
            internal const string DefiniteNullableValueWithoutValue = "definite_nullable_value_without_value";
            internal const string DefiniteUnboxNull = "definite_unbox_null";
            internal const string DefiniteInvalidCast = "definite_invalid_cast";
            internal const string DefiniteArrayTypeMismatch = "definite_array_type_mismatch";
            internal const string DefiniteIndexOutOfRange = "definite_index_out_of_range";
            internal const string DefiniteRangeOutOfRange = "definite_range_out_of_range";
        }

        internal static class ExceptionSources
        {
            internal const string Throw = "throw";
            internal const string BinaryOperator = "binary_operator";
            internal const string CheckedOperator = "checked_operator";
            internal const string CheckedConversion = "checked_conversion";
            internal const string ArrayLength = "array_length";
            internal const string NullReceiver = "null_receiver";
            internal const string AwaitExpression = "await_expression";
            internal const string LockReceiver = "lock_receiver";
            internal const string NullableValue = "nullable_value";
            internal const string Cast = "cast";
            internal const string ArrayStore = "array_store";
            internal const string ArrayIndex = "array_index";
            internal const string SpanSlice = "span_slice";
            internal const string RangeSlice = "range_slice";
        }

        internal static bool IsKnownEvidenceCategory(string category)
        {
            return string.Equals(category, ExceptionCategories.DirectThrow, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.Rethrow, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.SourceCallee, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.EffectSummary, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteDivideByZero, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteNullDereference, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteAwaitNull, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteLockNull, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteThrowNull, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteNegativeArrayLength, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteNullableValueWithoutValue, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteUnboxNull, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteInvalidCast, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteCheckedIntegralOverflow, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteArrayTypeMismatch, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteIndexOutOfRange, System.StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteRangeOutOfRange, System.StringComparison.Ordinal);
        }

        internal static ITypeSymbol? GetThrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool stopAtUntypedCatch)
        {
            ExpressionSyntax? exceptionExpression = throwNode switch
            {
                ThrowStatementSyntax statement => statement.Expression,
                ThrowExpressionSyntax expression => expression.Expression,
                _ => null
            };

            if (exceptionExpression == null)
            {
                return GetRethrownExceptionType(throwNode, semanticModel, cancellationToken, stopAtUntypedCatch);
            }

            var typeInfo = semanticModel.GetTypeInfo(exceptionExpression, cancellationToken);
            return typeInfo.Type ?? typeInfo.ConvertedType;
        }

        private static ITypeSymbol? GetRethrownExceptionType(
            SyntaxNode throwNode,
            SemanticModel semanticModel,
            CancellationToken cancellationToken,
            bool stopAtUntypedCatch)
        {
            foreach (var catchClause in throwNode.Ancestors().OfType<CatchClauseSyntax>())
            {
                if (!catchClause.Block.Span.Contains(throwNode.SpanStart))
                {
                    continue;
                }

                if (catchClause.Declaration == null)
                {
                    if (stopAtUntypedCatch)
                    {
                        return null;
                    }

                    continue;
                }

                return semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
            }

            return null;
        }
    }
}
