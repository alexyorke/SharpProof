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
            var source = ReadFileCached(Path.Combine(
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
