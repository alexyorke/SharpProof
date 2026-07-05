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
                PurityAnalysisEngine.LogDebug($"    [CtorInitRule] Operation syntax is ConstructorInitializer, but operation is not IInvocationOperation or TargetMethod is null. Kind: {operation.Kind}. Assuming Impure.");
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);
            }

            IMethodSymbol constructorSymbol = initializer.TargetMethod;
            PurityAnalysisEngine.LogDebug($"    [CtorInitRule] Found initializer call to: {constructorSymbol.ToDisplayString()}. Analyzing recursively.");

            foreach (var argument in initializer.Arguments)
            {
                PurityAnalysisEngine.LogDebug($"      [CtorInitRule] Checking argument: {argument.Syntax}");
                var argumentPurity = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                if (!argumentPurity.IsPure)
                {
                    PurityAnalysisEngine.LogDebug($"      [CtorInitRule] Argument '{argument.Syntax}' is Impure. Initializer is Impure.");
                    return argumentPurity;
                }
                PurityAnalysisEngine.LogDebug($"      [CtorInitRule] Argument '{argument.Syntax}' is Pure.");
            }
            PurityAnalysisEngine.LogDebug($"    [CtorInitRule] All arguments to initializer are Pure.");

            var constructorPurity = PurityAnalysisEngine.GetCalleePurity(constructorSymbol, context);

            if (!constructorPurity.IsPure)
            {
                PurityAnalysisEngine.LogDebug($"    [CtorInitRule] Target constructor '{constructorSymbol.ToDisplayString()}' determined IMPURE by recursive check. Result: Impure.");
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"    [CtorInitRule] Target constructor '{constructorSymbol.ToDisplayString()}' determined PURE by recursive check. Result: Pure.");
            }

            return constructorPurity.IsPure
                ? PurityAnalysisEngine.PurityAnalysisResult.Pure
                : constructorPurity.WithCallee(constructorSymbol, operation.Syntax);
        }
    }
}
