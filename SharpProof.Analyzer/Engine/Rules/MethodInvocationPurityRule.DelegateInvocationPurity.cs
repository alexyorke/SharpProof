using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal partial class MethodInvocationPurityRule
{
    private static bool TryCheckDelegateInvocationPurity(
        IInvocationOperation invocationOperation,
        IMethodSymbol invokedMethodSymbol,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
        if (invokedMethodSymbol.Name != "Invoke" ||
            invokedMethodSymbol.ContainingType?.TypeKind != TypeKind.Delegate)
            return false;


        if (invocationOperation.Instance == null)
        {
            result = PurityAnalysisEngine.ImpureResult(
                invocationOperation,
                "unresolved_delegate_target",
                nameof(MethodInvocationPurityRule),
                invokedMethodSymbol);
            return true;
        }

        var delegateInstanceOp = invocationOperation.Instance;

        var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(
            delegateInstanceOp,
            currentState,
            context.CancellationToken,
            context.SemanticModel);
        if (potentialTargets != null)
        {
            if (potentialTargets.Value.IsUnresolved || potentialTargets.Value.MethodSymbols.IsEmpty)
            {
                result = PurityAnalysisEngine.ImpureResult(
                    delegateInstanceOp,
                    "unresolved_delegate_target",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
            }
            else
            {
                result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
                foreach (var targetMethod in potentialTargets.Value.MethodSymbols)
                {
                    var targetPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
                    if (!targetPurity.IsPure)
                    {
                        if (CanTreatFreshMutableObjectReturningNestedCallableInvocationAsPure(targetMethod,
                                targetPurity)) continue;

                        result = targetPurity.WithCallee(targetMethod, invocationOperation.Syntax);
                        break;
                    }
                }
            }
        }
        else
        {
            result = PurityAnalysisEngine.ImpureResult(
                delegateInstanceOp,
                "unresolved_delegate_target",
                nameof(MethodInvocationPurityRule),
                invokedMethodSymbol);
        }

        if (result.IsPure)
            foreach (var argument in invocationOperation.Arguments)
            {
                var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                if (!argumentResult.IsPure)
                {
                    result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                        argumentResult.ImpureSyntaxNode ?? argument.Value.Syntax,
                        argumentResult.Evidence);
                    return true;
                }
            }

        return true;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckDelegateArgumentTargetPurity(
        IArgumentOperation argument,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (argument.Parameter?.Type?.TypeKind != TypeKind.Delegate)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(
            argument.Value,
            currentState,
            context.CancellationToken,
            context.SemanticModel);
        if (potentialTargets == null ||
            potentialTargets.Value.IsUnresolved ||
            potentialTargets.Value.MethodSymbols.Count == 0)
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                argument.Value.Syntax,
                PurityAnalysisEngine.PurityEvidence.Create(
                    "unresolved_delegate_target",
                    nameof(MethodInvocationPurityRule),
                    argument,
                    argument.Value.Syntax,
                    PurityAnalysisEngine.TryResolveSymbol(argument.Value) ?? argument.Parameter));

        foreach (var targetMethod in potentialTargets.Value.MethodSymbols)
        {
            var targetPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
            if (!targetPurity.IsPure) return targetPurity.WithCallee(targetMethod, argument.Value.Syntax);
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static bool TryCheckKnownDelegateInvokingBclInvocationPurity(
        IInvocationOperation invocationOperation,
        IMethodSymbol methodSymbol,
        PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState,
        out PurityAnalysisEngine.PurityAnalysisResult result)
    {
        result = PurityAnalysisEngine.PurityAnalysisResult.Pure;
        if (!IsKnownDelegateInvokingBclMethod(methodSymbol)) return false;

        foreach (var argument in invocationOperation.Arguments)
        {
            var delegateTargetResult = CheckDelegateArgumentTargetPurity(argument, context, currentState);
            if (!delegateTargetResult.IsPure)
            {
                result = delegateTargetResult;
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownDelegateInvokingBclMethod(IMethodSymbol methodSymbol)
    {
        var typeDefinition = methodSymbol.ContainingType?.OriginalDefinition.ToDisplayString();
        return typeDefinition switch
        {
            "System.Collections.Generic.List<T>" => methodSymbol.Name is
                "ConvertAll" or
                "Exists" or
                "Find" or
                "FindAll" or
                "FindIndex" or
                "FindLast" or
                "FindLastIndex" or
                "ForEach" or
                "RemoveAll" or
                "TrueForAll",
            "System.Array" => methodSymbol.Name is
                "ConvertAll" or
                "Exists" or
                "Find" or
                "FindAll" or
                "FindIndex" or
                "FindLast" or
                "FindLastIndex" or
                "ForEach" or
                "TrueForAll",
            _ => false
        };
    }
}