using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using SharpProof.Analyzer.Engine;
using static SharpProof.Analyzer.Engine.PurityAnalysisEngine;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class ArrayCreationPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.ArrayCreation);

        public PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisState currentState)
        {
            if (!(operation is IArrayCreationOperation arrayCreation))
            {
                return PurityAnalysisResult.Pure;
            }

            LogDebug($"ArrayCreationRule: Analyzing {arrayCreation.Syntax}");


            bool isParamsArray = arrayCreation.Parent is IArgumentOperation argumentOperation &&
                                argumentOperation.Parameter != null &&
                                argumentOperation.Parameter.IsParams;

            if (isParamsArray)
            {



                var paramsDimensionsResult = CheckDimensionSizes(arrayCreation, context, currentState);
                if (!paramsDimensionsResult.IsPure)
                {
                    return paramsDimensionsResult;
                }

                var paramsInitializerResult = CheckInitializerElements(arrayCreation, context, currentState, "'params' array");
                if (!paramsInitializerResult.IsPure)
                {
                    return paramsInitializerResult;
                }

                LogDebug($"    [ArrCreateRule] 'params' array creation itself treated as PURE.");
                return PurityAnalysisResult.Pure;
            }
            else
            {
                var dimensionsResult = CheckDimensionSizes(arrayCreation, context, currentState);
                if (!dimensionsResult.IsPure)
                {
                    return dimensionsResult;
                }

                var initializerResult = CheckInitializerElements(arrayCreation, context, currentState, "array");
                if (!initializerResult.IsPure)
                {
                    return initializerResult;
                }

                if (IsFreshLocalArrayInitialization(arrayCreation))
                {
                    LogDebug($"    [ArrCreateRule] Array creation '{arrayCreation.Syntax}' assigned to a fresh local array. Treating as PURE.");
                    return PurityAnalysisResult.Pure;
                }

                if (IsTransientImmutableArrayFactoryArgument(arrayCreation))
                {
                    LogDebug($"    [ArrCreateRule] Array creation '{arrayCreation.Syntax}' is a transient immutable-array factory source. Treating as PURE.");
                    return PurityAnalysisResult.Pure;
                }

                LogDebug($"    [ArrCreateRule] Array creation '{arrayCreation.Syntax}' is IMPURE (mutable allocation, not for params).");
                return PurityAnalysisResult.Impure(
                    arrayCreation.Syntax,
                    PurityEvidence.Create(
                        "mutable_state_write",
                        ruleName: nameof(ArrayCreationPurityRule),
                        operation: arrayCreation,
                        syntaxNode: arrayCreation.Syntax,
                        symbol: arrayCreation.Type,
                        catalogSource: "array_creation"));
            }
        }

        private static bool IsFreshLocalArrayInitialization(IArrayCreationOperation arrayCreation)
        {
            IOperation? current = arrayCreation.Parent;

            if (current is IConversionOperation conversionOperation)
            {
                current = conversionOperation.Parent;
            }

            if (current is IVariableInitializerOperation variableInitializer &&
                variableInitializer.Parent is IVariableDeclaratorOperation variableDeclarator &&
                variableDeclarator.Symbol.Type is IArrayTypeSymbol)
            {
                return true;
            }

            if (current is IAssignmentOperation assignmentOperation &&
                assignmentOperation.Target is ILocalReferenceOperation localReference &&
                localReference.Type is IArrayTypeSymbol)
            {
                return true;
            }

            return false;
        }

        private static bool IsTransientImmutableArrayFactoryArgument(IArrayCreationOperation arrayCreation)
        {
            IOperation? current = arrayCreation.Parent;

            while (current is IConversionOperation conversionOperation)
            {
                current = conversionOperation.Parent;
            }

            if (current is not IArgumentOperation argumentOperation ||
                argumentOperation.Parent is not IInvocationOperation invocationOperation)
            {
                return false;
            }

            var method = invocationOperation.TargetMethod?.OriginalDefinition;
            return method?.Name == "CreateRange" &&
                method.ContainingType?.OriginalDefinition.ToDisplayString() == "System.Collections.Immutable.ImmutableArray";
        }

        private static PurityAnalysisResult CheckDimensionSizes(
            IArrayCreationOperation arrayCreation,
            PurityAnalysisContext context,
            PurityAnalysisState currentState)
        {
            foreach (var dimensionSize in arrayCreation.DimensionSizes)
            {
                var dimensionResult = CheckSingleOperation(dimensionSize, context, currentState);
                if (!dimensionResult.IsPure)
                {
                    LogDebug($"    [ArrCreateRule] Array dimension '{dimensionSize.Syntax}' is IMPURE. Operation is Impure.");
                    return dimensionResult;
                }
            }

            return PurityAnalysisResult.Pure;
        }

        private static PurityAnalysisResult CheckInitializerElements(
            IArrayCreationOperation arrayCreation,
            PurityAnalysisContext context,
            PurityAnalysisState currentState,
            string description)
        {
            if (arrayCreation.Initializer == null)
            {
                LogDebug($"    [ArrCreateRule] {description} has no initializer elements to check.");
                return PurityAnalysisResult.Pure;
            }

            foreach (var elementValue in arrayCreation.Initializer.ElementValues)
            {
                var elementPurity = CheckSingleOperation(elementValue, context, currentState);
                if (!elementPurity.IsPure)
                {
                    LogDebug($"    [ArrCreateRule] {description} initializer element '{elementValue.Syntax}' is IMPURE. Operation is Impure.");
                    return elementPurity;
                }
            }

            LogDebug($"    [ArrCreateRule] All {description} initializer elements are Pure.");
            return PurityAnalysisResult.Pure;
        }

    }
}
