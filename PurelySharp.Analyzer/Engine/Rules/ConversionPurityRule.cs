using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using PurelySharp.Analyzer.Configuration;
using System.Collections.Generic;
using System.Collections.Immutable;
using PurelySharp.Analyzer.Engine;

namespace PurelySharp.Analyzer.Engine.Rules
{
    internal class ConversionPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Conversion);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IConversionOperation conversionOperation)
            {
                PurityAnalysisEngine.LogDebug($"WARNING: ConversionPurityRule called with unexpected operation type: {operation.Kind}. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            PurityAnalysisEngine.LogDebug($"    [ConversionRule] Checking operand of type {conversionOperation.Operand?.Kind} for conversion: {operation.Syntax}");
            if (conversionOperation.Operand == null)
            {
                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Conversion operand is null. Assuming Pure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var operandResult = PurityAnalysisEngine.CheckSingleOperation(conversionOperation.Operand, context, currentState);

            PurityAnalysisEngine.LogDebug($"    [ConversionRule] Operand Result: IsPure={operandResult.IsPure}");


            if (!operandResult.IsPure)
            {
                return operandResult;
            }

            if (conversionOperation.Operand.Type?.TypeKind == TypeKind.Dynamic ||
                conversionOperation.Type?.TypeKind == TypeKind.Dynamic)
            {
                PurityAnalysisEngine.LogDebug("    [ConversionRule] Dynamic conversion detected. Conservatively treating as Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    conversionOperation.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "dynamic_dispatch",
                        nameof(ConversionPurityRule),
                        conversionOperation));
            }


            if (conversionOperation.Conversion.IsUserDefined && conversionOperation.Conversion.MethodSymbol != null)
            {
                IMethodSymbol operatorMethod = conversionOperation.Conversion.MethodSymbol;
                PurityAnalysisEngine.LogDebug($"    [ConversionRule] User-defined conversion found: {operatorMethod.ToDisplayString()}");
                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Conversion Syntax: {conversionOperation.Syntax}");
                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Conversion Op Kind: {conversionOperation.Kind}");

                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Checking operator method: {operatorMethod.ToDisplayString()}");
                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Operator Method Containing Type: {operatorMethod.ContainingType?.ToDisplayString()}");
                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Operator Method Return Type: {operatorMethod.ReturnType?.ToDisplayString()}");
                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Operator Method Param Count: {operatorMethod.Parameters.Length}");

                if (IsStaticAbstractInterfaceConversion(operatorMethod))
                {
                    PurityAnalysisEngine.LogDebug($"    [ConversionRule] Static abstract interface conversion '{operatorMethod.Name}' has unresolved dispatch targets. Conversion is Impure.");
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        conversionOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(ConversionPurityRule),
                            conversionOperation,
                            symbol: operatorMethod));
                }

                var operatorResult = PurityAnalysisEngine.GetCalleePurity(operatorMethod, context);

                PurityAnalysisEngine.LogDebug($"    [ConversionRule] Operator Method Result: IsPure={operatorResult.IsPure}");
                if (!operatorResult.IsPure)
                {
                    return operatorResult.WithCallee(operatorMethod, conversionOperation.Syntax);
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            return operandResult;
        }

        private static bool IsStaticAbstractInterfaceConversion(IMethodSymbol methodSymbol)
        {
            return methodSymbol.IsStatic &&
                methodSymbol.IsAbstract &&
                methodSymbol.MethodKind == MethodKind.Conversion &&
                methodSymbol.ContainingType?.TypeKind == TypeKind.Interface;
        }
    }
}
