using NUnit.Framework;

namespace SharpProof.Test;

public sealed partial class ArchitectureReductionTests
{
    public sealed record SourceShapeExpectation(
        string RelativePath,
        string[] Required,
        string[] Forbidden,
        int MaxLines = 0);

    public sealed record SourceShapeScenario(
        string Name,
        SourceShapeExpectation[] Expectations);

    private static readonly SourceShapeScenario[] SymbolicIrShapeScenarios =
    [
        new("SymbolicIrLowerer_DelegatesRegexLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicRegexLowerer.TryLowerRegexMatchSuccessCondition(",
                    "SymbolicRegexLowerer.TryLowerRegexMatchesCountComparison("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicStringLowerer.cs",
                ["SymbolicRegexLowerer.TryLowerRegexInvocationPredicate("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicRegexLowerer.cs",
                ["internal static class SymbolicRegexLowerer",
                    "internal static bool TryLowerRegexInvocationPredicate(",
                    "SymbolicSourcePredicateLowerer.CountLocalSymbolReferences("],
                ["partial class SymbolicRegexLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesObjectLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicKnownApiLowerer.cs",
                ["SymbolicObjectLowerer.TryLowerObjectReferenceEqualsInvocation"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs", [],
                ["private static bool TryLowerObjectReferenceEqualsInvocation"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicObjectLowerer.cs",
                ["internal static bool TryLowerObjectReferenceEqualsInvocation",
                    "internal static class SymbolicObjectLowerer",
                    "ir.known-api.object.reference-equals"])
        ]),
        new("SymbolicIrLowerer_DelegatesPatternLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicPatternLowerer.TryLowerBinaryPatternCondition(isPatternExpression"],
                ["private static bool TryLowerBinaryPatternCondition",
                    "private static bool TryLowerTypeTestCondition",
                    "private static PatternSyntax UnwrapPattern"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicPatternLowerer.cs",
                ["internal static class SymbolicPatternLowerer",
                    "internal static bool TryLowerBinaryPatternCondition",
                    "internal static bool TryLowerTypeTestCondition",
                    "private static PatternSyntax UnwrapPattern",
                    "ir.pattern.type.test"],
                ["partial class SymbolicPatternLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesSourcePredicateLoweringWithSharedBudget",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicSourcePredicateLowerer.TryLowerSourceBooleanInvocation("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicMemberLowerer.cs",
                ["SymbolicSourcePredicateLowerer.TryLowerReturnedBoolean("],
                ["const int MaxSourcePredicateInlineDepth"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicSourcePredicateLowerer.cs",
                ["internal static class SymbolicSourcePredicateLowerer",
                    "internal static bool TryLowerSourceBooleanInvocation(",
                    "SymbolicLoweringContext.MaxSourcePredicateInlineDepth"],
                ["partial class SymbolicSourcePredicateLowerer"], 2000),
            Shape("SharpProof.Symbolic/Ir/SymbolicLoweringContext.cs",
                ["internal const int MaxSourcePredicateInlineDepth = 8;",
                    "SymbolicStateValueFacts.ImplicitThisVariableName"])
        ]),
        new("SymbolicIrLowerer_DelegatesTupleLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicTupleLowerer.TryLowerTupleEqualityCondition(binaryExpression"],
                ["private static bool TryLowerTupleEqualityCondition",
                    "private static bool TryLowerTupleElementMemberTerm",
                    "private static bool TryLowerTupleElementTerms"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicMemberLowerer.cs",
                ["SymbolicTupleLowerer.TryLowerTupleElementMemberTerm(memberAccess"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicTupleLowerer.cs",
                ["internal static class SymbolicTupleLowerer",
                    "internal static bool TryLowerTupleEqualityCondition",
                    "internal static bool TryLowerTupleElementMemberTerm",
                    "private static bool TryLowerTupleElementTerms",
                    "ir.tuple.equality.element"],
                ["partial class SymbolicTupleLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesNullableLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs", [],
                ["private static bool TryLowerNullableHasValueTerm",
                    "private static bool TryLowerNullableValueTerm"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicMemberLowerer.cs",
                ["SymbolicNullableLowerer.TryLowerNullableHasValueTerm(memberAccess.Expression",
                    "SymbolicNullableLowerer.TryLowerNullableValueTerm(memberAccess.Expression"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicNullableLowerer.cs",
                ["internal static class SymbolicNullableLowerer",
                    "internal static bool TryLowerNullableHasValueTerm",
                    "internal static bool TryLowerNullableValueTerm",
                    "internal static bool TryLowerNullableGetValueOrDefaultInvocation",
                    "SymbolicIrLowerer.LowerArrayTotalLengthTerm(conditionalAccess.Expression"],
                ["partial class SymbolicNullableLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesIndexingLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["TryLowerElementAccessTerm(elementAccess"],
                ["private static bool TryLowerElementAccessTerm",
                    "private static bool TryGetBuiltInElementAccessElementType",
                    "private static bool TryLowerArrayDimensionLengthTerm",
                    "private static bool TryLowerArrayGetLengthInvocation",
                    "private static bool TryLowerArrayBoundInvocation",
                    "private static bool TryLowerArrayTotalLengthTerm",
                    "private static bool TryCreateBuiltInLengthReferenceTerm"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicIndexingLowerer.cs",
                ["internal static bool TryLowerElementAccessTerm",
                    "private static bool TryGetBuiltInElementAccessElementType",
                    "internal static bool TryLowerArrayGetLengthInvocation",
                    "internal static bool TryLowerArrayBoundInvocation",
                    "internal static bool TryLowerArrayDimensionLengthTerm",
                    "internal static bool TryLowerArrayTotalLengthTerm",
                    "internal static bool TryCreateBuiltInLengthReferenceTerm",
                    "internal static class SymbolicIndexingLowerer",
                    "TryCreateArrayTotalLengthReferenceTerm(reference, multiDimensionalArray, out term)",
                    "new SymbolicArrayDimensionLengthTerm"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicKnownApiLowerer.cs",
                ["nameof(Array.GetLength)", "nameof(Array.GetLongLength)",
                    "nameof(Array.GetLowerBound)", "nameof(Array.GetUpperBound)"])
        ]),
        new("SymbolicIrLowerer_DelegatesConversionLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicConversionLowerer.TryLowerSupportedConversionTerm(expression",
                    "SymbolicConversionLowerer.TryLowerReferenceAsTerm(asExpression"],
                ["private static bool TryLowerIdentityPreservingAsTerm",
                    "private static bool IsIdentityPreservingReferenceConversion",
                    "private static bool TryLowerSupportedConversionTerm"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicConversionLowerer.cs",
                ["internal static class SymbolicConversionLowerer",
                    "private static bool TryLowerIdentityPreservingAsTerm",
                    "internal static bool TryLowerReferenceAsTerm",
                    "private static bool IsIdentityPreservingReferenceConversion",
                    "internal static bool TryLowerSupportedConversionTerm"],
                ["partial class SymbolicConversionLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesStringLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["internal static partial class SymbolicIrLowerer",
                    "SymbolicStringLowerer.TryLowerStringSearchComparison(",
                    "SymbolicStringLowerer.TryLowerPrefixSubstringComparison(",
                    "SymbolicStringLowerer.TryLowerStringEqualityCondition(",
                    "SymbolicStringLowerer.TryLowerStringExpressionTerm("],
                ["private static bool TryLowerRegexIsMatchInvocation",
                    "private static bool TryLowerStringPredicateInvocation",
                    "private static bool TryLowerStringEqualityCondition",
                    "private static bool TryCreateStringEqualityCondition",
                    "private static bool TryLowerStringStaticValueMember",
                    "private static bool TryCreateStringContentReferenceTerm",
                    "private static bool IsSystemStringType"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicKnownApiLowerer.cs",
                ["SymbolicStringLowerer.TryLowerStringStaticValueMember(memberSymbol, out term)"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicStringLowerer.cs",
                ["internal static class SymbolicStringLowerer",
                    "internal static bool TryLowerRegexIsMatchInvocation",
                    "internal static bool TryLowerStringPredicateInvocation",
                    "internal static bool TryLowerStringEqualityCondition",
                    "private static bool TryCreateStringEqualityCondition",
                    "internal static bool TryLowerStringStaticValueMember",
                    "internal static bool TryCreateStringContentReferenceTerm",
                    "private static bool IsSystemStringType"],
                ["partial class SymbolicStringLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesMemberLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicMemberLowerer.TryLowerMemberTerm(memberAccess"],
                ["private static bool TryLowerMemberTerm",
                    "private static bool TryGetInstanceMemberValueKind",
                    "private static bool IsBuiltInSpanOrMemoryType"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicMemberLowerer.cs",
                ["internal static class SymbolicMemberLowerer",
                    "internal static bool TryLowerMemberTerm",
                    "private static bool TryGetInstanceMemberValueKind",
                    "SymbolicTypeFacts.IsBuiltInSpanOrMemoryType(receiverType)",
                    "new SymbolicCountTerm",
                    "new SymbolicIntegerConstantTerm(arrayType.Rank)"],
                ["private static bool IsBuiltInSpanOrMemoryType",
                    "partial class SymbolicMemberLowerer"], 2000),
            Shape("SharpProof.Symbolic/Ir/SymbolicIndexingLowerer.cs",
                ["new SymbolicLengthTerm", "TryLowerArrayTotalLengthTerm("])
        ]),
        new("SymbolicIrLowerer_DelegatesNumericLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicKnownApiLowerer.cs",
                ["SymbolicNumericLowerer.TryLowerBigIntegerStaticValueMember(memberSymbol, out term)"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicNumericLowerer.IsBigIntegerType("],
                ["private static bool TryLowerBigIntegerStaticValueMember",
                    "private static bool IsBigIntegerType"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicNumericLowerer.cs",
                ["internal static class SymbolicNumericLowerer",
                    "internal static bool TryLowerBigIntegerStaticValueMember",
                    "internal static bool IsBigIntegerType"],
                ["partial class SymbolicNumericLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesCompletedAsyncLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicAsyncLowerer.TryGetKnownCompletedAsyncResultExpression("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicNullableLowerer.cs",
                ["SymbolicAsyncLowerer.TryGetKnownCompletedAsyncResultExpression("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicAsyncLowerer.cs",
                ["internal static class SymbolicAsyncLowerer",
                    "internal static bool TryGetKnownCompletedAsyncResultExpression(",
                    "private static bool IsKnownFromResultFactory("],
                ["partial class SymbolicAsyncLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesReferenceLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.Boundary.cs",
                ["SymbolicReferenceLowerer.TryLowerReferenceTerm(expression"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicStringLowerer.cs",
                ["SymbolicReferenceLowerer.TryLowerReferenceTerm("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicReferenceLowerer.cs",
                ["internal static class SymbolicReferenceLowerer",
                    "internal static bool TryLowerReferenceTerm(",
                    "internal static bool TryLowerReferenceConditionalAccessTerm("],
                ["partial class SymbolicReferenceLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesTypeClassificationToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicTypeLowerer.TryGetSymbolType(symbol",
                    "SymbolicTypeLowerer.TryGetValueKind(symbolType"],
                ["private static bool TryGetSymbolType", "private static bool TryGetValueKind",
                    "private static bool IsIntegerSmtType",
                    "private static bool IsSupportedTupleCarrierType"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicTypeLowerer.cs",
                ["internal static class SymbolicTypeLowerer",
                    "internal static bool TryGetSymbolType",
                    "internal static bool TryGetValueKind",
                    "internal static bool IsIntegerSmtType",
                    "private static bool IsSupportedTupleCarrierType"],
                ["partial class SymbolicTypeLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_DelegatesOperatorLoweringToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicOperatorLowerer.TryGetRelationOperator(binaryExpression.Kind()",
                    "SymbolicOperatorLowerer.TryGetBinaryTermOperator(binary.Kind()",
                    "SymbolicOperatorLowerer.CanCompareTerms(left, right, relationOperator)"],
                ["private static bool CanCompareTerms", "private static bool IsEqualityExpression",
                    "private static bool TryGetRelationOperator",
                    "private static bool TryGetBinaryTermOperator"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicOperatorLowerer.cs",
                ["internal static class SymbolicOperatorLowerer",
                    "internal static bool CanCompareTerms", "internal static bool IsEqualityExpression",
                    "internal static bool TryGetRelationOperator",
                    "internal static bool TryGetRelationalPatternOperator",
                    "internal static bool TryGetBinaryTermOperator"],
                ["partial class SymbolicOperatorLowerer"], 2000),
            Shape("SharpProof.Symbolic/Ir/SymbolicPatternLowerer.cs",
                ["SymbolicOperatorLowerer.TryGetRelationalPatternOperator("]),
            Shape("SharpProof.Symbolic/SymbolicProgramPointFacts.cs",
                ["SymbolicOperatorLowerer.TryGetRelationOperator(",
                    "SymbolicOperatorLowerer.TryGetRelationalPatternOperator("],
                ["TryGetInlineAssignmentComparisonRelationOperator",
                    "TryGetIrRelationalPatternOperator"])
        ]),
        new("SymbolicIrLowerer_DelegatesKnownApiDispatchToDedicatedCatalog",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["SymbolicKnownApiLowerer.TryLowerKnownApiInvocation(knownInvocation, context, out condition)",
                    "SymbolicKnownApiLowerer.TryLowerKnownApiInvocationTerm(invocation, context, out term)"],
                ["KnownApiLowerings =", "private static bool TryLowerKnownApiInvocation(",
                    "private static bool TryLowerKnownApiInvocationTerm(",
                    "private static bool TryLowerKnownStaticValueMember("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicMemberLowerer.cs",
                ["SymbolicKnownApiLowerer.TryLowerKnownStaticValueMember(memberAccess, context, out term)"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicKnownApiLowerer.cs",
                ["KnownApiLowerings =", "KnownApiTermLowerings",
                    "internal static class SymbolicKnownApiLowerer", "\"System.Math\"",
                    "TryLowerIntegralMathMinMaxInvocation", "TryLowerIntegralMathAbsInvocation",
                    "TryLowerIntegralMathClampInvocation",
                    "internal static bool TryLowerKnownApiInvocation(",
                    "internal static bool TryLowerKnownApiInvocationTerm(",
                    "internal static bool TryLowerKnownStaticValueMember("],
                ["partial class SymbolicKnownApiLowerer"], 2000)
        ]),
        new("SymbolicIrLowerer_KeepsConditionFactoriesInDedicatedPartial",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["CreateFactCondition(", "CreateRelationCondition("],
                ["private static SymbolicCondition CreateFactCondition",
                    "private static SymbolicCondition CreateRelationCondition",
                    "private static SymbolicCondition CreateReferenceIsNullCondition"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.Conditions.cs",
                ["internal static SymbolicCondition CreateFactCondition",
                    "internal static SymbolicCondition CreateRelationCondition",
                    "internal static SymbolicCondition CreateReferenceIsNullCondition",
                    "SymbolicFact.Exact(atom, node, provenance)"])
        ]),
        new("SymbolicIrLowerer_DelegatesSharedValueFactsToDedicatedCollaborator",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIrLowerer.cs",
                ["UnwrapExpression(expression)", "TryGetIntegralConstant(constantValue.Value"],
                ["private static bool TryGetStableVariableSymbol",
                    "private static bool TryGetIntegralConstant",
                    "private static ExpressionSyntax UnwrapExpression"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicLoweringValueFacts.cs",
                ["internal static bool TryGetStableVariableSymbol",
                    "internal static bool TryGetIntegralConstant",
                    "internal static ExpressionSyntax UnwrapExpression",
                    "internal static class SymbolicLoweringValueFacts"]),
            Shape("SharpProof.Symbolic/SymbolicLoopStateTransfer.cs",
                ["SymbolicLoweringValueFacts.TryGetIntegralConstant"],
                ["SymbolicAssignmentStateTransfer.TryGetIntegralConstant"]),
            Shape("SharpProof.Symbolic/SymbolicComplexityService.cs",
                ["SymbolicLoweringValueFacts.TryGetIntegralConstant"],
                ["case ulong ulongValue"])
        ]),
        new("RuntimeHazardStableNullDereferences_UseTypedIrExceptionPreconditions",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateNullDereferenceTrigger",
                    "SymbolicExceptionPreconditionKind.NullDereference",
                    "TryCreateIrRelationalExceptionPreconditionTrigger",
                    "CreateUnsupportedExceptionPreconditionTrigger(",
                    "!TryCreateNullDereferenceTrigger(receiver"],
                ["IsStableIrReferenceSubject", "TryTranslateNullCondition(receiver",
                    "\"ir.runtime-hazard.null-dereference.formula-fallback\""])
        ]),
        new("RuntimeHazardUnboxNull_UsesTypedIrExceptionPrecondition",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateUnboxNullTrigger", "SymbolicExceptionPreconditionKind.UnboxNull",
                    "ir.runtime-hazard.unbox-null"],
                ["TryTranslateNullCondition(expression",
                    "\"ir.runtime-hazard.unbox-null.formula-fallback\""])
        ]),
        new("RuntimeHazardStableArgumentNull_UsesTypedIrExceptionPreconditions",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateArgumentNullTrigger", "SymbolicExceptionPreconditionKind.ArgumentNull",
                    "ir.runtime-hazard.argument-null", "!TryCreateArgumentNullTrigger(expression"],
                ["IsStableIrReferenceSubject", "TryTranslateNullCondition(expression",
                    "\"ir.runtime-hazard.argument-null.formula-fallback\""])
        ]),
        new("RuntimeHazardNullableValue_UsesTypedIrExceptionPrecondition",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateNullableValueWithoutValueTrigger",
                    "SymbolicExceptionPreconditionKind.NullableValueWithoutValue",
                    "SymbolicSemanticPipeline.LowerNullableHasValueTerm",
                    "CreateUnsupportedExceptionPreconditionTrigger",
                    "!TryCreateNullableValueWithoutValueTrigger("],
                ["SymbolicIrLowerer.TryLowerNullableHasValueTerm",
                    "CSharpSmtFormulaTranslator.TryTranslateNullableHasValue(",
                    "ir.runtime-hazard.nullable-value.without-value.formula-fallback"])
        ]),
        new("RuntimeHazardInvalidReferenceCast_UsesTypedIrTypeTestPrecondition",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateRuntimeReferenceInvalidCastTrigger",
                    "SymbolicExceptionPreconditionKind.InvalidCast",
                    "ir.runtime-hazard.invalid-cast.non-null", "new SymbolicTypeTestAtom",
                    "SymbolicRuntimeTypeFacts.TryGetRuntimeTypeTestKey",
                    "TryCreateReferenceNullCondition(",
                    "\"ir.runtime-hazard.reference.non-null.guard\"",
                    "CreateUnsupportedExceptionPreconditionTrigger"],
                ["CSharpSmtFormulaTranslator.TryCreateRuntimeTypeTestFormula(",
                    "\"ir.runtime-hazard.invalid-cast.formula-fallback\""]),
            Shape("SharpProof.Symbolic/SymbolicRuntimeHazardSyntaxCandidateFactory.cs", [],
                ["private static bool TryCreateRuntimeReferenceCastMismatchTrigger"]),
            Shape("SharpProof.Symbolic/SymbolicRuntimeHazardIrTriggerFactory.cs",
                ["internal static bool TryCreateExactRuntimeInvalidCastTrigger",
                    "internal static bool TryCreateRuntimeReferenceInvalidCastTrigger",
                    "internal static bool TryCreateReferenceNullCondition"],
                ["private static RuntimeHazardTrigger CreateInvalidCastTypedProjectionTrigger"])
        ]),
        new("RuntimeHazardDirectThrow_UsesIrExceptionPreconditionTrigger",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateDirectThrowTrigger", "SymbolicExceptionPreconditionKind.DirectThrow",
                    "ir.runtime-hazard.direct-throw"]),
            Shape("SharpProof.Symbolic/SymbolicRuntimeHazardSyntaxCandidateFactory.cs",
                ["if (!TryCreateDirectThrowTrigger(throwNode, out var directTrigger))",
                    "TryCreateDirectThrowTrigger(throwNode"],
                ["new RuntimeHazardTrigger(new Smt"]),
            Shape("SharpProof.Symbolic/SymbolicRuntimeHazardIrTriggerFactory.cs",
                ["internal static bool TryCreateDirectThrowTrigger"])
        ]),
        new("RuntimeHazardSwitchExpressionNoMatch_PreservesIrExceptionPreconditionWhenLowerable",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateSwitchExpressionNoMatchCandidate",
                    "CreateUnsupportedExceptionPreconditionTrigger",
                    "SymbolicExceptionPreconditionKind.SwitchExpressionNoMatch",
                    "TryCreateSwitchExpressionArmSymbolicCondition",
                    "ir.runtime-hazard.switch-expression.no-match",
                    "ExceptionTypes.SwitchExpressionException",
                    "ExceptionCategories.DefiniteSwitchExpressionNoMatch"])
        ]),
        new("RuntimeHazardDynamicNullBinding_UsesTypedIrExceptionPrecondition",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateDynamicNullBindingTrigger",
                    "SymbolicExceptionPreconditionKind.DynamicNullBinding",
                    "ir.runtime-hazard.dynamic-null-binding",
                    "TryCreateOptionalReferenceSubject"],
                ["ir.runtime-hazard.dynamic-null-binding.formula-fallback",
                    "!TryTranslateNullCondition(receiver, semanticModel, cancellationToken, out var trigger)"])
        ]),
        new("RuntimeHazardDivideByZero_UsesTypedIrProjection",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateIrExceptionPreconditionTrigger",
                    "SymbolicExceptionPreconditionKind.DivideByZero",
                    "TryCreateNumericZeroCondition(",
                    "ir.runtime-hazard.divide-by-zero.unsupported",
                    "CreateUnsupportedExceptionPreconditionTrigger"],
                ["ir.runtime-hazard.divide-by-zero.formula-fallback",
                    "trigger = new RuntimeHazardTrigger(formula);",
                    "TryTranslateZeroCondition(binaryExpression.Right",
                    "TryTranslateZeroCondition(assignment.Right"])
        ]),
        new("RuntimeHazardSimpleIndexing_UsesTypedIrBoundsPrecondition",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateIrElementAccessOutOfRangeTrigger",
                    "SymbolicExceptionPreconditionKind.IndexOutOfRange",
                    "SymbolicExceptionPreconditionKind.ArgumentOutOfRange",
                    "SymbolicSemanticPipeline.LowerBuiltInElementAccessInRangeCondition(",
                    "new SymbolicNotCondition(inRangeCondition)",
                    "ir.runtime-hazard.index.out-of-range.unsupported",
                    "CreateUnsupportedExceptionPreconditionTrigger"],
                ["new SymbolicBoundsAtom",
                    "CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange(",
                    "ir.runtime-hazard.index.out-of-range.formula-fallback",
                    "trigger = new RuntimeHazardTrigger(new SmtUnaryFormula(SmtUnaryOperator.Not, inRangeFormula));"])
        ]),
        new("RuntimeHazardIndexFallback_UsesTypedProjectionWithoutFormulaFallback",
        [
            Shape("SharpProof.Symbolic/SymbolicRuntimeHazardIrTriggerFactory.cs",
                ["ir.runtime-hazard.index.out-of-range.unsupported"],
                ["\"ir.runtime-hazard.index.out-of-range.formula-fallback\""])
        ]),
        new("AnalyzerExceptionSites_UseSharedTypedElementAccessRangeHelper",
        [
            Shape("SharpProof.Analyzer/ExceptionSiteClassifier.RangeAccess.cs",
                ["SymbolicSemanticPipeline.LowerBuiltInElementAccessOutOfRangeCondition(",
                    "SymbolicSemanticPipeline.LowerSubsequenceInRangeCondition("],
                ["SymbolicReachabilityService.TryCreateBuiltInElementAccessInRangeCondition(",
                    "CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange(",
                    "CSharpSmtFormulaTranslator.CreateSubsequenceInRangeFormula("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicSemanticPipeline.cs",
                ["LowerBuiltInElementAccessInRangeCondition(",
                    "SymbolicIrLowerer.LowerBuiltInElementAccessInRangeCondition(",
                    "SymbolicIrLowerer.LowerSubsequenceInRangeCondition("],
                ["CSharpSmtFormulaTranslator.TryTranslateBuiltInElementAccessInRange("]),
            Shape("SharpProof.Symbolic/Ir/SymbolicIndexingLowerer.cs",
                ["internal static bool TryCreateSubsequenceInRangeCondition("])
        ]),
        new("ElementAccessRangeHelper_UsesIrMultidimensionalBoundsBeforeLegacyFallback",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicSemanticPipeline.cs",
                ["LowerBuiltInElementAccessInRangeCondition(",
                    "SymbolicIrLowerer.LowerBuiltInElementAccessInRangeCondition(",
                    "SymbolicIrLowerer.LowerArrayElementBoundsCondition("],
                ["CSharpSmtFormulaTranslator"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicIndexingLowerer.cs",
                ["internal static bool TryCreateBuiltInElementAccessInRangeCondition(",
                    "TryResolveBuiltInRangeLengthShape(", "TryResolveBuiltInIndexLengthShape(",
                    "ApplyWellFormedPrecondition(", "RequiresNonNegativeValue",
                    "internal static bool TryCreateArrayElementBoundsCondition(",
                    "TryLowerArrayDimensionLengthTerm(arrayExpression, dimension, context, out var length)",
                    "new SymbolicBoundsAtom("])
        ]),
        new("ElementAccessLengthTerms_AreLoweredBySharedIrLowerers",
        [
            Shape("SharpProof.Symbolic/Ir/SymbolicIndexingLowerer.cs",
                ["internal static bool TryLowerBuiltInLengthTerm(",
                    "TryLowerDirectRangeAccessResultLengthTerm(",
                    "TryLowerBuiltInViewResultLengthTerm(",
                    "TryLowerBuiltInSliceInvocationResultLengthTerm(",
                    "TryLowerMemoryExtensionsViewResultLengthTerm(",
                    "TryResolveBuiltInRangeLengthShape(", "TryResolveBuiltInIndexLengthShape(",
                    "TryResolveAssignedLengthShape<TShape>(",
                    "TryGetShapeAssignmentFromPrecedingStatement(",
                    "SymbolicStringLengthLowerer.TryLowerStringInvocationResultLengthTerm(",
                    "internal static bool TryCreateBuiltInLengthReferenceTerm(",
                    "type is not IArrayTypeSymbol &&", "HasCountBackedIntIndexer(type)",
                    "term = new SymbolicCountTerm(reference);",
                    "TryCreateStringContentReferenceTerm(reference, out var stringContent)",
                    "CreateLengthTerm(reference, out term)"],
                ["TryResolveAssignedRangeLengthShape(", "TryResolveAssignedIndexLengthShape(",
                    "private static bool TryLowerStringInvocationResultLengthTerm("], 2000),
            Shape("SharpProof.Symbolic/Ir/SymbolicStringLengthLowerer.cs",
                ["internal static bool TryLowerStringInvocationResultLengthTerm("], [], 2000),
            Shape("SharpProof.Symbolic/Ir/SymbolicSemanticPipeline.cs",
                ["LowerBuiltInLengthTerm(", "ProjectBuiltInLengthTerm("]),
            Shape("SharpProof.Symbolic/SymbolicRuntimeHazardIrTriggerFactory.cs",
                ["SymbolicSemanticPipeline.LowerBuiltInLengthTerm(elementAccess.Expression, context)",
                    "SymbolicSemanticPipeline.LowerBuiltInElementAccessOutOfRangeCondition("],
                ["new SymbolicCountTerm("])
        ]),
        new("RuntimeHazardSlicing_UsesTypedProjectionWhenFormulaLowers",
        [
            Shape("@runtime-hazard-candidates",
                ["TryCreateSlicingArgumentOutOfRangeCandidate",
                    "SymbolicSemanticPipeline.LowerSubsequenceInRangeCondition(",
                    "TryCreateIrExceptionPreconditionTrigger(",
                    "SymbolicExceptionPreconditionKind.ArgumentOutOfRange",
                    "CreateUnsupportedExceptionPreconditionTrigger",
                    "ir.runtime-hazard.slicing.argument-out-of-range.unsupported"],
                ["SymbolicIrLowerer.TryCreateSubsequenceInRangeCondition(",
                    "CSharpSmtFormulaTranslator.CreateSubsequenceInRangeFormula",
                    "ir.runtime-hazard.slicing.argument-out-of-range.fallback"]),
            Shape("SharpProof.Symbolic/Ir/SymbolicIndexingLowerer.cs",
                ["provenance + \".count-within-remaining-length\"",
                    "provenance + \".addition-does-not-overflow\""])
        ])
    ];

    private static SourceShapeExpectation Shape(
        string relativePath,
        string[] required,
        string[]? forbidden = null,
        int maxLines = 0)
    {
        return new SourceShapeExpectation(relativePath, required, forbidden ?? [], maxLines);
    }

    private static IEnumerable<TestCaseData> SymbolicIrShapeScenarioData()
    {
        return SymbolicIrShapeScenarios.Select(static scenario =>
            new TestCaseData(scenario).SetName(scenario.Name));
    }

    [TestCaseSource(nameof(SymbolicIrShapeScenarioData))]
    public void SymbolicIrLowererShapeScenariosPreserveArchitecture(SourceShapeScenario scenario)
    {
        var repositoryRoot = FindRepositoryRoot();
        foreach (var expectation in scenario.Expectations)
        {
            var source = expectation.RelativePath == "@runtime-hazard-candidates"
                ? ReadRuntimeHazardCandidateSources(repositoryRoot)
                : ReadFileCached(Path.Combine(
                    repositoryRoot,
                    expectation.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            Assert.Multiple(() =>
            {
                foreach (var required in expectation.Required)
                    Assert.That(source, Does.Contain(required), expectation.RelativePath);
                foreach (var forbidden in expectation.Forbidden)
                    Assert.That(source, Does.Not.Contain(forbidden), expectation.RelativePath);
                if (expectation.MaxLines > 0)
                    Assert.That(source.Split('\n'), Has.Length.LessThanOrEqualTo(expectation.MaxLines + 1),
                        expectation.RelativePath);
            });
        }
    }
}
