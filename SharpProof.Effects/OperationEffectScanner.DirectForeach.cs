using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed partial class OperationEffectScanner
{
    private readonly ImmutableArray<DirectForeachInfo> _directForeachInfos;

    private sealed record DirectForeachInfo(
        IForEachLoopOperation Operation,
        IMethodSymbol? GetEnumeratorMethod,
        IMethodSymbol? MoveNextMethod,
        IPropertySymbol? CurrentProperty,
        IMethodSymbol? DisposeMethod,
        ILocalSymbol? LoopVariable);

    private ImmutableArray<DirectForeachInfo> CreateDirectForeachInfos(
        IOperation root)
    {
        var result = ImmutableArray.CreateBuilder<DirectForeachInfo>();
        foreach (var forEach in root.DescendantsAndSelf()
                     .OfType<IForEachLoopOperation>())
        {
            if (forEach.Syntax is not CommonForEachStatementSyntax syntax)
            {
                continue;
            }

            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(
                    _session.Compilation,
                    syntax.SyntaxTree);
            if (!DirectForeachFacts.IsArrayOrString(model, syntax))
            {
                continue;
            }

            var info = model.GetForEachStatementInfo(syntax);
            result.Add(new DirectForeachInfo(
                forEach,
                info.GetEnumeratorMethod,
                info.MoveNextMethod,
                info.CurrentProperty,
                info.DisposeMethod,
                (forEach.LoopControlVariable as
                    IVariableDeclaratorOperation)?.Symbol));
        }

        return result.ToImmutable();
    }

    private bool TryScanDirectForeachProtocol(
        IOperation operation,
        out EffectSummary summary)
    {
        summary = EffectSummary.Empty;
        if (!operation.IsImplicit)
        {
            return false;
        }

        foreach (var info in _directForeachInfos)
        {
            if (!IsInsideDirectForeach(info, operation))
            {
                continue;
            }

            if (operation is IInvocationOperation invocation)
            {
                if (SameSymbol(
                        invocation.TargetMethod,
                        info.GetEnumeratorMethod))
                {
                    summary = ScanDirectForeachCollection(
                        info,
                        invocation);
                    return true;
                }

                if (SameSymbol(invocation.TargetMethod, info.MoveNextMethod) ||
                    SameSymbol(invocation.TargetMethod, info.DisposeMethod))
                {
                    return true;
                }
            }

            if (operation is IPropertyReferenceOperation property &&
                SameSymbol(property.Property, info.CurrentProperty))
            {
                return true;
            }

            if (operation is IConversionOperation conversion &&
                IsDirectForeachElementConversion(conversion, info))
            {
                return true;
            }
        }

        return false;
    }

    private EffectSummary ScanDirectForeachCollection(
        DirectForeachInfo info,
        IInvocationOperation origin)
    {
        var loweredCollection = origin.Instance ?? info.Operation.Collection;
        var collectionValue = DirectForeachFacts.GetCollectionValue(
            loweredCollection);
        var collection = ScanStep(collectionValue);
        var nullCheck = PotentialNullCheck(
            collectionValue,
            origin,
            FrameworkTypeMetadataNames.NullReferenceException);
        var collectionRead = EffectSummaryOperations.Read(
            _conversionOwnership.ClassifyRegion(collectionValue));
        return collection.Then(
            new EffectStep(
                EffectSummaryOperations.Join(
                    nullCheck.Summary,
                    collectionRead),
                nullCheck.CompletesNormally)).Summary;
    }

    private static bool SameSymbol(ISymbol? left, ISymbol? right)
    {
        return left != null && right != null &&
            SymbolEqualityComparer.Default.Equals(left, right);
    }

    private static bool IsInsideDirectForeach(
        DirectForeachInfo info,
        IOperation operation)
    {
        return operation.Syntax.SyntaxTree == info.Operation.Syntax.SyntaxTree &&
            info.Operation.Syntax.Span.Contains(operation.Syntax.Span);
    }

    private static bool IsDirectForeachElementConversion(
        IConversionOperation conversion,
        DirectForeachInfo info)
    {
        if (info.LoopVariable is not { } loopVariable)
        {
            return false;
        }

        if (conversion.Parent is ISimpleAssignmentOperation assignment &&
            assignment.Target is ILocalReferenceOperation local &&
            SymbolEqualityComparer.Default.Equals(local.Local, loopVariable))
        {
            return true;
        }

        return conversion.Syntax.SyntaxTree ==
                   info.Operation.LoopControlVariable.Syntax.SyntaxTree &&
               info.Operation.LoopControlVariable.Syntax.Span.Contains(
                   conversion.Syntax.Span);
    }
}
