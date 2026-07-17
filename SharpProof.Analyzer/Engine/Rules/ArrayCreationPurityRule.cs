using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules;

internal class ArrayCreationPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.ArrayCreation);

    public PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        if (!(operation is IArrayCreationOperation arrayCreation)) return PurityAnalysisResult.Pure;


        var isParamsArray = arrayCreation.Parent is IArgumentOperation argumentOperation &&
                            argumentOperation.Parameter != null &&
                            argumentOperation.Parameter.IsParams;

        if (isParamsArray)
        {
            var paramsDimensionsResult = CheckDimensionSizes(arrayCreation, context, currentState);
            if (!paramsDimensionsResult.IsPure) return paramsDimensionsResult;

            var paramsInitializerResult =
                CheckInitializerElements(arrayCreation, context, currentState);
            if (!paramsInitializerResult.IsPure) return paramsInitializerResult;

            return PurityAnalysisResult.Pure;
        }

        var dimensionsResult = CheckDimensionSizes(arrayCreation, context, currentState);
        if (!dimensionsResult.IsPure) return dimensionsResult;

        var initializerResult = CheckInitializerElements(arrayCreation, context, currentState);
        if (!initializerResult.IsPure) return initializerResult;

        if (RuleAnalysisHelper.IsFreshLocalArrayInitialization(arrayCreation)) return PurityAnalysisResult.Pure;

        if (IsTransientImmutableArrayFactoryArgument(arrayCreation)) return PurityAnalysisResult.Pure;

        return PurityAnalysisResult.Impure(
            arrayCreation.Syntax,
            PurityEvidence.Create(
                "mutable_state_write",
                nameof(ArrayCreationPurityRule),
                arrayCreation,
                arrayCreation.Syntax,
                arrayCreation.Type,
                "array_creation"));
    }

    private static bool IsTransientImmutableArrayFactoryArgument(IArrayCreationOperation arrayCreation)
    {
        var current = arrayCreation.Parent;

        while (current is IConversionOperation conversionOperation) current = conversionOperation.Parent;

        if (current is not IArgumentOperation argumentOperation ||
            argumentOperation.Parent is not IInvocationOperation invocationOperation)
            return false;

        var method = invocationOperation.TargetMethod?.OriginalDefinition;
        return method?.Name == "CreateRange" &&
               method.ContainingType?.OriginalDefinition.ToDisplayString() ==
               "System.Collections.Immutable.ImmutableArray";
    }

    private static PurityAnalysisResult CheckDimensionSizes(
        IArrayCreationOperation arrayCreation,
        PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        foreach (var dimensionSize in arrayCreation.DimensionSizes)
        {
            var dimensionResult = CheckSingleOperation(dimensionSize, context, currentState);
            if (!dimensionResult.IsPure) return dimensionResult;
        }

        return PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisResult CheckInitializerElements(
        IArrayCreationOperation arrayCreation,
        PurityAnalysisContext context,
        PurityAnalysisState currentState)
    {
        if (arrayCreation.Initializer == null) return PurityAnalysisResult.Pure;

        foreach (var elementValue in arrayCreation.Initializer.ElementValues)
        {
            var elementPurity = CheckSingleOperation(elementValue, context, currentState);
            if (!elementPurity.IsPure) return elementPurity;
        }

        return PurityAnalysisResult.Pure;
    }
}
