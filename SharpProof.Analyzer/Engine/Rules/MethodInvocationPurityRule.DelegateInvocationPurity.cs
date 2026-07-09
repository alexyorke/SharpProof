using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Analyzer.Engine;
using SharpProof.Analyzer.Engine.Analysis;
using SharpProof.Symbolic;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace SharpProof.Analyzer.Engine.Rules
{

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
            {
                return false;
            }

            PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] === Simplified Delegate Invocation Check Start ===");
            PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Invoked Symbol: {invokedMethodSymbol.ContainingType.Name}.Invoke()");

            if (invocationOperation.Instance == null)
            {
                PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] Instance is NULL (static delegate?). Assuming impure.");
                result = PurityAnalysisEngine.ImpureResult(
                    invocationOperation,
                    "unresolved_delegate_target",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
                return true;
            }

            IOperation delegateInstanceOp = invocationOperation.Instance;
            PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Analyzing Delegate Instance Op: {delegateInstanceOp.Kind} | Syntax: {delegateInstanceOp.Syntax}");

            var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(
                delegateInstanceOp,
                currentState,
                context.CancellationToken,
                context.SemanticModel);
            if (potentialTargets != null)
            {
                PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Resolved {potentialTargets.Value.MethodSymbols.Count} target(s) for delegate invocation.");
                if (potentialTargets.Value.IsUnresolved || potentialTargets.Value.MethodSymbols.IsEmpty)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> Resolved target set is empty or explicitly unresolved. Treating as unresolved delegate target.");
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
                        PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Checking Potential Target: {targetMethod.ToDisplayString()}");
                        var targetPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
                        PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Potential Target Purity Result: IsPure={targetPurity.IsPure}");
                        if (!targetPurity.IsPure)
                        {
                            if (CanTreatFreshMutableObjectReturningNestedCallableInvocationAsPure(targetMethod, targetPurity))
                            {
                                PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> PURE target deferred to caller return/ownership analysis.");
                                continue;
                            }

                            PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> IMPURE target found. Invocation is impure.");
                            result = targetPurity.WithCallee(targetMethod, invocationOperation.Syntax);
                            break;
                        }
                    }
                }
            }
            else
            {
                PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] --> IMPURE (Could not resolve delegate targets for {delegateInstanceOp.Kind}). Fallback to SP0002 at instance op.");
                result = PurityAnalysisEngine.ImpureResult(
                    delegateInstanceOp,
                    "unresolved_delegate_target",
                    nameof(MethodInvocationPurityRule),
                    invokedMethodSymbol);
            }

            PurityAnalysisEngine.LogDebug($"  [MIR-DEL-S] Final Result for Delegate Invocation: IsPure={result.IsPure}");
            PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] === Simplified Delegate Invocation Check End ===");
            if (result.IsPure)
            {
                foreach (var argument in invocationOperation.Arguments)
                {
                    var argumentResult = PurityAnalysisEngine.CheckSingleOperation(argument.Value, context, currentState);
                    if (!argumentResult.IsPure)
                    {
                        PurityAnalysisEngine.LogDebug("  [MIR-DEL-S] --> IMPURE (Delegate invocation argument is impure)");
                        result = PurityAnalysisEngine.PurityAnalysisResult.Impure(
                            argumentResult.ImpureSyntaxNode ?? argument.Value.Syntax,
                            argumentResult.Evidence);
                        return true;
                    }
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
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Pure;
            }

            var potentialTargets = PurityAnalysisEngine.ResolvePotentialTargets(
                argument.Value,
                currentState,
                context.CancellationToken,
                context.SemanticModel);
            if (potentialTargets == null ||
                potentialTargets.Value.IsUnresolved ||
                potentialTargets.Value.MethodSymbols.Count == 0)
            {
                return PurityAnalysisEngine.PurityAnalysisResult.Impure(
                    argument.Value.Syntax,
                    PurityAnalysisEngine.PurityEvidence.Create(
                        "unresolved_delegate_target",
                        nameof(MethodInvocationPurityRule),
                        argument,
                        syntaxNode: argument.Value.Syntax,
                        symbol: PurityAnalysisEngine.TryResolveSymbol(argument.Value) ?? argument.Parameter));
            }

            foreach (var targetMethod in potentialTargets.Value.MethodSymbols)
            {
                var targetPurity = PurityAnalysisEngine.GetCalleePurity(targetMethod, context);
                if (!targetPurity.IsPure)
                {
                    return targetPurity.WithCallee(targetMethod, argument.Value.Syntax);
                }
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
            if (!IsKnownDelegateInvokingBclMethod(methodSymbol))
            {
                return false;
            }

            foreach (var argument in invocationOperation.Arguments)
            {
                var delegateTargetResult = CheckDelegateArgumentTargetPurity(argument, context, currentState);
                if (!delegateTargetResult.IsPure)
                {
                    PurityAnalysisEngine.LogDebug("  [MIR] --> IMPURE (delegate-invoking BCL argument target was impure or unresolved)");
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
}
