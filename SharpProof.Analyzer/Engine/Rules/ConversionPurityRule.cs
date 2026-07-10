using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Configuration;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer.Engine.Rules
{
    internal class ConversionPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Conversion);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (operation is not IConversionOperation conversionOperation)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (conversionOperation.Operand == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var operandResult = PurityAnalysisEngine.CheckSingleOperation(conversionOperation.Operand, context, currentState);



            if (!operandResult.IsPure)
            {
                return operandResult;
            }

            if (conversionOperation.Operand.Type?.TypeKind == TypeKind.Dynamic ||
                conversionOperation.Type?.TypeKind == TypeKind.Dynamic)
            {
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


                if (IsPurityNeutralIntrinsicConversion(operatorMethod))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Pure;
                }

                if (RuleAnalysisHelper.IsStaticAbstractInterfaceMethod(operatorMethod, MethodKind.Conversion))
                {
                    return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        conversionOperation.Syntax,
                        PurityAnalysisEngine.PurityEvidence.Create(
                            "unknown_external_call",
                            nameof(ConversionPurityRule),
                            conversionOperation,
                            symbol: operatorMethod));
                }

                var operatorResult = PurityAnalysisEngine.GetCalleePurity(operatorMethod, context);

                if (!operatorResult.IsPure)
                {
                    return operatorResult.WithCallee(operatorMethod, conversionOperation.Syntax);
                }

                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }


            return operandResult;
        }

        private static bool IsPurityNeutralIntrinsicConversion(IMethodSymbol operatorMethod)
        {
            if (operatorMethod.Name != "op_Implicit" ||
                operatorMethod.Parameters.Length != 1)
            {
                return false;
            }

            if (operatorMethod.ContainingType?.SpecialType == SpecialType.System_String &&
                operatorMethod.Parameters[0].Type.SpecialType == SpecialType.System_String &&
                operatorMethod.ReturnType is INamedTypeSymbol stringSpanReturnType &&
                stringSpanReturnType.OriginalDefinition.ToDisplayString() == "System.ReadOnlySpan<T>" &&
                stringSpanReturnType.TypeArguments.Length == 1 &&
                stringSpanReturnType.TypeArguments[0].SpecialType == SpecialType.System_Char)
            {
                return true;
            }

            if (operatorMethod.ContainingType is not INamedTypeSymbol containingType ||
                operatorMethod.ReturnType is not INamedTypeSymbol returnType ||
                operatorMethod.Parameters[0].Type is not INamedTypeSymbol parameterType)
            {
                return false;
            }

            var containingTypeDefinition = containingType.OriginalDefinition.ToDisplayString();
            var parameterTypeDefinition = parameterType.OriginalDefinition.ToDisplayString();
            var returnTypeDefinition = returnType.OriginalDefinition.ToDisplayString();

            return containingTypeDefinition switch
            {
                "System.Span<T>" =>
                    parameterTypeDefinition == "System.Span<T>" &&
                    returnTypeDefinition == "System.ReadOnlySpan<T>",
                "System.Memory<T>" =>
                    parameterTypeDefinition == "System.Memory<T>" &&
                    returnTypeDefinition == "System.ReadOnlyMemory<T>",
                _ => false,
            };
        }
    }
}
