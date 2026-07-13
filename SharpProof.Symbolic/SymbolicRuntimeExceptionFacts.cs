using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Symbolic;

internal static class SymbolicRuntimeExceptionFacts
{
    internal static bool IsKnownEvidenceCategory(string category)
    {
        return string.Equals(category, ExceptionCategories.DirectThrow, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.Rethrow, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.SourceCallee, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.EffectSummary, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DynamicDispatch, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteDivideByZero, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteModuloByZero, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteNullDereference, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteWithNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteDeconstructionNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteAwaitNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteLockNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteThrowNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteNegativeArrayLength, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteNegativeStackAllocLength,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteNullableValueWithoutValue,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteUnboxNull, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteInvalidCast, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteCheckedIntegralOverflow, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteCheckedNumericConversionOverflow,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteArrayTypeMismatch, StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteIndexOutOfRange, StringComparison.Ordinal) ||
                string.Equals(category, ExceptionCategories.DefiniteIndexConstructionArgumentOutOfRange,
                    StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteArrayGetValueIndexOutOfRange,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteArgumentOutOfRangeGuard, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteInvalidClampBounds, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteRegexNullInput, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteRangeOutOfRange, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteCountIndexOutOfRange, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteInvalidCollectionCardinality,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteStringSubstringOutOfRange,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteStringRemoveOutOfRange, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteSliceOutOfRange, StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteMemoryExtensionsAsSpanOutOfRange,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteMemoryExtensionsAsMemoryOutOfRange,
                   StringComparison.Ordinal) ||
               string.Equals(category, ExceptionCategories.DefiniteSwitchExpressionNoMatch, StringComparison.Ordinal);
    }

    internal static bool TryGetThrowExpression(SyntaxNode throwNode, out ExpressionSyntax expression)
    {
        switch (throwNode)
        {
            case ThrowStatementSyntax { Expression: { } statementExpression }:
                expression = statementExpression;
                return true;
            case ThrowExpressionSyntax throwExpression:
                expression = throwExpression.Expression;
                return true;
            default:
                expression = null!;
                return false;
        }
    }

    internal static ITypeSymbol? GetThrownExceptionType(
        SyntaxNode throwNode,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        bool stopAtUntypedCatch)
    {
        if (!TryGetThrowExpression(throwNode, out var exceptionExpression))
            return GetRethrownExceptionType(throwNode, semanticModel, cancellationToken, stopAtUntypedCatch);

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
            if (!catchClause.Block.Span.Contains(throwNode.SpanStart)) continue;

            if (catchClause.Declaration == null)
            {
                if (stopAtUntypedCatch) return null;

                continue;
            }

            return semanticModel.GetTypeInfo(catchClause.Declaration.Type, cancellationToken).Type;
        }

        return null;
    }

    internal static class ExceptionTypes
    {
        internal const string Unknown = "unknown";
        internal const string Exception = "System.Exception";
        internal const string NullReferenceException = "System.NullReferenceException";
        internal const string DivideByZeroException = "System.DivideByZeroException";
        internal const string OverflowException = "System.OverflowException";
        internal const string ArgumentNullException = "System.ArgumentNullException";
        internal const string ArgumentException = "System.ArgumentException";
        internal const string InvalidOperationException = "System.InvalidOperationException";
        internal const string InvalidCastException = "System.InvalidCastException";
        internal const string ArrayTypeMismatchException = "System.ArrayTypeMismatchException";
        internal const string IndexOutOfRangeException = "System.IndexOutOfRangeException";
        internal const string ArgumentOutOfRangeException = "System.ArgumentOutOfRangeException";
        internal const string SwitchExpressionException = "System.Runtime.CompilerServices.SwitchExpressionException";
    }

    internal static class ExceptionCategories
    {
        internal const string DirectThrow = "direct_throw";
        internal const string Rethrow = "rethrow";
        internal const string SourceCallee = "source_callee";
        internal const string EffectSummary = "effect_summary";
        internal const string DynamicDispatch = "dynamic_dispatch";
        internal const string DefiniteThrowNull = "definite_throw_null";
        internal const string DefiniteDivideByZero = "definite_divide_by_zero";
        internal const string DefiniteModuloByZero = "definite_modulo_by_zero";
        internal const string DefiniteCheckedIntegralOverflow = "definite_checked_integral_overflow";
        internal const string DefiniteCheckedNumericConversionOverflow = "definite_checked_numeric_conversion_overflow";
        internal const string DefiniteNegativeArrayLength = "definite_negative_array_length";
        internal const string DefiniteNegativeStackAllocLength = "definite_negative_stackalloc_length";
        internal const string DefiniteNullDereference = "definite_null_dereference";
        internal const string DefiniteWithNull = "definite_with_null";
        internal const string DefiniteDeconstructionNull = "definite_deconstruction_null";
        internal const string DefiniteAwaitNull = "definite_await_null";
        internal const string DefiniteLockNull = "definite_lock_null";
        internal const string DefiniteNullableValueWithoutValue = "definite_nullable_value_without_value";
        internal const string DefiniteUnboxNull = "definite_unbox_null";
        internal const string DefiniteInvalidCast = "definite_invalid_cast";
        internal const string DefiniteArrayTypeMismatch = "definite_array_type_mismatch";
        internal const string DefiniteIndexOutOfRange = "definite_index_out_of_range";
        internal const string DefiniteIndexConstructionArgumentOutOfRange =
            "definite_index_construction_argument_out_of_range";
        internal const string DefiniteArrayGetValueIndexOutOfRange = "definite_array_get_value_index_out_of_range";
        internal const string DefiniteArgumentOutOfRangeGuard = "definite_argument_out_of_range_guard";
        internal const string DefiniteInvalidClampBounds = "definite_invalid_clamp_bounds";
        internal const string DefiniteRegexNullInput = "definite_regex_null_input";
        internal const string DefiniteRangeOutOfRange = "definite_range_out_of_range";
        internal const string DefiniteCountIndexOutOfRange = "definite_count_index_out_of_range";
        internal const string DefiniteInvalidCollectionCardinality = "definite_invalid_collection_cardinality";
        internal const string DefiniteStringSubstringOutOfRange = "definite_string_substring_out_of_range";
        internal const string DefiniteStringRemoveOutOfRange = "definite_string_remove_out_of_range";
        internal const string DefiniteSliceOutOfRange = "definite_slice_out_of_range";

        internal const string DefiniteMemoryExtensionsAsSpanOutOfRange =
            "definite_memory_extensions_as_span_out_of_range";

        internal const string DefiniteMemoryExtensionsAsMemoryOutOfRange =
            "definite_memory_extensions_as_memory_out_of_range";

        internal const string DefiniteSwitchExpressionNoMatch = "definite_switch_expression_no_match";
    }

    internal static class ExceptionSources
    {
        internal const string Throw = "throw";
        internal const string BinaryOperator = "binary_operator";
        internal const string CheckedOperator = "checked_operator";
        internal const string CheckedConversion = "checked_conversion";
        internal const string ArrayLength = "array_length";
        internal const string StackAllocLength = "stackalloc_length";
        internal const string NullReceiver = "null_receiver";
        internal const string AwaitExpression = "await_expression";
        internal const string LockReceiver = "lock_receiver";
        internal const string NullableValue = "nullable_value";
        internal const string Cast = "cast";
        internal const string ArrayStore = "array_store";
        internal const string ArrayIndex = "array_index";
        internal const string ArrayGetValue = "array_get_value";
        internal const string SpanSlice = "span_slice";
        internal const string RangeSlice = "range_slice";
        internal const string WithExpression = "with_expression";
        internal const string DeconstructionReceiver = "deconstruction_receiver";
        internal const string CountIndex = "count_index";
        internal const string CollectionOperation = "collection_operation";
        internal const string UnknownRuntimeHazardCandidate = "unknown_runtime_hazard_candidate";
        internal const string SwitchExpression = "switch_expression";
    }
}
