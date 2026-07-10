using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Generic;
using System.Collections.Immutable;
using SharpProof.Analyzer.Engine;

namespace SharpProof.Analyzer.Engine.Rules
{

    internal class ConstructorInitializerPurityRule : IPurityRule
    {
        public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.ConstructorBodyOperation);

        public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context, PurityAnalysisEngine.PurityAnalysisState currentState)
        {
            if (!(operation.Syntax is ConstructorInitializerSyntax))
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            if (!(operation is IInvocationOperation initializer) || initializer.TargetMethod == null)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }

            IMethodSymbol constructorSymbol = initializer.TargetMethod;

            foreach (var argument in initializer.Arguments)
            {
                var argumentPurity = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                if (!argumentPurity.IsPure)
                {
                    return argumentPurity;
                }
            }

            var constructorPurity = PurityAnalysisEngine.GetCalleePurity(constructorSymbol, context);

            if (!constructorPurity.IsPure)
            {
            }
            else
            {
            }

            return constructorPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : constructorPurity.WithCallee(constructorSymbol, operation.Syntax);
        }
    }
}
