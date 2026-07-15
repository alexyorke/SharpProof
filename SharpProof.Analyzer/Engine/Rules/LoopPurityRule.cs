using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Analyzer.Engine.Rules;

internal class LoopPurityRule : IPurityRule
{
    public IEnumerable<OperationKind> ApplicableOperationKinds => ImmutableArray.Create(OperationKind.Loop);

    public PurityAnalysisEngine.PurityAnalysisResult CheckPurity(IOperation operation, PurityAnalysisContext context,
        PurityAnalysisEngine.PurityAnalysisState currentState)
    {
        if (!(operation is ILoopOperation loopOperation))
            return PurityAnalysisEngine.PurityAnalysisResult.Impure(operation.Syntax);


        if (loopOperation is IForLoopOperation forLoopOperation)
        {
            foreach (var beforeOperation in forLoopOperation.Before)
            {
                var beforeResult = PurityAnalysisEngine.CheckSingleOperation(beforeOperation, context, currentState);
                if (!beforeResult.IsPure) return beforeResult;
            }

            if (forLoopOperation.Condition != null)
            {
                var conditionResult =
                    PurityAnalysisEngine.CheckSingleOperation(forLoopOperation.Condition, context, currentState);
                if (!conditionResult.IsPure) return conditionResult;
            }
        }
        else if (loopOperation is IWhileLoopOperation whileLoopOperation &&
                 whileLoopOperation.Condition != null)
        {
            var conditionResult =
                PurityAnalysisEngine.CheckSingleOperation(whileLoopOperation.Condition, context, currentState);
            if (!conditionResult.IsPure) return conditionResult;
        }

        if (HasStaticallyUnreachableBody(loopOperation)) return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        if (loopOperation is IForEachLoopOperation forEachLoopOperation)
        {
            var collectionResult =
                PurityAnalysisEngine.CheckSingleOperation(forEachLoopOperation.Collection, context, currentState);
            if (!collectionResult.IsPure) return collectionResult;

            var enumeratorResult = CheckForEachEnumeratorPurity(forEachLoopOperation.Collection, context);
            if (!enumeratorResult.IsPure) return enumeratorResult;
        }


        if (loopOperation.Body != null)
            foreach (var bodyOp in loopOperation.Body.DescendantsAndSelf())
            {
                var opResult = PurityAnalysisEngine.CheckSingleOperation(bodyOp, context, currentState);
                if (!opResult.IsPure) return opResult;
            }

        if (loopOperation is IForLoopOperation reachableForLoopOperation)
            foreach (var atLoopBottomOperation in reachableForLoopOperation.AtLoopBottom)
            {
                var atLoopBottomResult =
                    PurityAnalysisEngine.CheckSingleOperation(atLoopBottomOperation, context, currentState);
                if (!atLoopBottomResult.IsPure) return atLoopBottomResult;
            }


        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static bool HasStaticallyUnreachableBody(ILoopOperation loopOperation)
    {
        return loopOperation switch
        {
            IWhileLoopOperation whileLoop => whileLoop.ConditionIsTop && IsCompileTimeFalse(whileLoop.Condition),
            IForLoopOperation forLoop => IsCompileTimeFalse(forLoop.Condition),
            _ => false
        };
    }

    private static bool IsCompileTimeFalse(IOperation? conditionOperation)
    {
        if (conditionOperation == null) return false;

        var constantValue = conditionOperation.ConstantValue;
        return constantValue.HasValue &&
               constantValue.Value is bool boolValue &&
               !boolValue;
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckForEachEnumeratorPurity(
        IOperation collectionOperation,
        PurityAnalysisContext context)
    {
        return CheckForEachEnumeratorPurity(collectionOperation, context, false);
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckForEachAsyncEnumeratorPurity(
        IOperation collectionOperation,
        PurityAnalysisContext context)
    {
        return CheckForEachEnumeratorPurity(collectionOperation, context, true);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckForEachEnumeratorPurity(
        IOperation collectionOperation,
        PurityAnalysisContext context,
        bool isAsync)
    {
        var unwrappedCollection =
            PurityAnalysisEngine.SkipImplicitConversions(collectionOperation) ?? collectionOperation;
        if (unwrappedCollection.Type == null)
            return MissingEnumeratorEvidence(unwrappedCollection.Syntax, null, "missing_collection_type");

        if (!isAsync && unwrappedCollection.Type is IArrayTypeSymbol)
            return PurityAnalysisEngine.PurityAnalysisResult.Pure;

        var getEnumerators = (isAsync
                ? EnumeratorRuntimeMemberClassifier.EnumerateGetAsyncEnumeratorImplementations(
                    unwrappedCollection.Type)
                : EnumeratorRuntimeMemberClassifier.EnumerateGetEnumeratorImplementations(
                    unwrappedCollection.Type))
            .ToArray();
        if (getEnumerators.Length == 0)
            return MissingEnumeratorEvidence(
                unwrappedCollection.Syntax,
                unwrappedCollection.Type,
                isAsync ? "missing_get_async_enumerator" : "missing_get_enumerator");

        foreach (var getEnumerator in getEnumerators)
        {
            var enumeratorPurity =
                PurityCalleeResolver.GetCalleePurityAtUse(getEnumerator, unwrappedCollection.Syntax, context);
            if (!enumeratorPurity.IsPure) return enumeratorPurity;

            var runtimeMemberPurity = CheckForEachEnumeratorRuntimeMemberPurity(
                getEnumerator.ReturnType,
                unwrappedCollection.Syntax,
                context,
                isAsync);
            if (!runtimeMemberPurity.IsPure) return runtimeMemberPurity;
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    internal static PurityAnalysisEngine.PurityAnalysisResult CheckForEachEnumeratorRuntimeMemberPurity(
        ITypeSymbol enumeratorType,
        SyntaxNode foreachSyntax,
        PurityAnalysisContext context)
    {
        return CheckForEachEnumeratorRuntimeMemberPurity(enumeratorType, foreachSyntax, context, false);
    }

    private static PurityAnalysisEngine.PurityAnalysisResult CheckForEachEnumeratorRuntimeMemberPurity(
        ITypeSymbol enumeratorType,
        SyntaxNode foreachSyntax,
        PurityAnalysisContext context,
        bool isAsync)
    {
        var runtimeMembers = (isAsync
                ? EnumeratorRuntimeMemberClassifier.EnumerateAsyncRuntimeMembers(enumeratorType)
                : EnumeratorRuntimeMemberClassifier.EnumerateRuntimeMembers(enumeratorType))
            .ToArray();
        if (runtimeMembers.Length == 0)
            return MissingEnumeratorEvidence(
                foreachSyntax,
                enumeratorType,
                isAsync ? "missing_async_enumerator_runtime_member" : "missing_enumerator_runtime_member");

        foreach (var runtimeMember in runtimeMembers)
        {
            var memberPurity =
                PurityCalleeResolver.GetCalleePurityAtUse(runtimeMember, foreachSyntax, context);
            if (!memberPurity.IsPure &&
                !EnumeratorRuntimeMemberClassifier.IsLocalEnumeratorStateMutation(
                    runtimeMember,
                    enumeratorType,
                    context.SemanticModel.Compilation))
                return memberPurity;

            if (isAsync && runtimeMember.Name is ("MoveNextAsync" or "DisposeAsync"))
            {
                var awaitablePurity = AwaitPurityRule.CheckAwaitablePatternMembers(
                    runtimeMember.ReturnType,
                    foreachSyntax,
                    context);
                if (!awaitablePurity.IsPure) return awaitablePurity;
            }
        }

        return PurityAnalysisEngine.PurityAnalysisResult.Pure;
    }

    private static PurityAnalysisEngine.PurityAnalysisResult MissingEnumeratorEvidence(
        SyntaxNode syntax,
        ISymbol? symbol,
        string reason)
    {
        return PurityAnalysisEngine.ImpureResult(
            syntax,
            "unknown_external_call",
            nameof(LoopPurityRule),
            symbol,
            reason);
    }

}
