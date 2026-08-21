using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class ExceptionHandlerReachability(
    Compilation compilation,
    ManagedFlowResult? abstractFlow)
{
    private readonly Dictionary<CatchClauseSyntax, CatchReachability> _cache = new();
    private readonly INamedTypeSymbol? _exceptionType =
        compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
    private readonly INamedTypeSymbol? _nullReferenceExceptionType =
        compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.NullReferenceException);

    internal bool IsReachable(CatchClauseSyntax target, bool inFilter)
    {
        var reachability = GetReachability(target);
        return inFilter ? reachability.Filter : reachability.Handler;
    }

    private CatchReachability GetReachability(CatchClauseSyntax target)
    {
        if (_cache.TryGetValue(target, out var cached))
        {
            return cached;
        }
        if (target.Parent is not TryStatementSyntax @try)
        {
            return new CatchReachability(Filter: true, Handler: true);
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, @try.SyntaxTree);
        var protectedBlock = model.GetOperation(@try.Block);
        if (protectedBlock == null)
        {
            return new CatchReachability(Filter: true, Handler: true);
        }
        var potential = GetPotentialExceptions(protectedBlock);
        var filterReachable = potential.Unknown &&
            CanUnknownReach(target, @try, model) ||
            potential.Known.Any(type =>
                CanKnownReach(type, target, @try, model));
        var result = new CatchReachability(
            filterReachable,
            filterReachable &&
            GetFilterSelection(target, model) != CatchSelection.Never);
        _cache.Add(target, result);
        return result;
    }

    private PotentialExceptions GetPotentialExceptions(
        IOperation protectedBlock)
    {
        var known = ImmutableHashSet.CreateBuilder<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        var unknown = false;
        var remaining = new Stack<IOperation>();
        remaining.Push(protectedBlock);
        while (remaining.Count != 0)
        {
            var operation = remaining.Pop();
            if (operation is IAnonymousFunctionOperation or
                ILocalFunctionOperation)
            {
                continue;
            }
            if (operation is IThrowOperation thrown)
            {
                if (thrown.Exception is { } nullException &&
                    abstractFlow?.ProvesNull(thrown, nullException) == true &&
                    _nullReferenceExceptionType is { } nullReferenceException)
                {
                    known.Add(nullReferenceException);
                }
                else if (thrown.Exception is { } exception &&
                    DefiniteOperationFacts.UnwrapHarmlessValue(exception).Type
                    is INamedTypeSymbol type)
                {
                    known.Add(type);
                }
                else
                {
                    unknown = true;
                }
                continue;
            }
            if (CanThrowUnknown(operation))
            {
                unknown = true;
            }
            foreach (var child in operation.ChildOperations)
            {
                remaining.Push(child);
            }
        }
        return new PotentialExceptions(known.ToImmutable(), unknown);
    }

    private static bool CanThrowUnknown(IOperation operation)
    {
        return operation is
            IInvocationOperation or
            IDynamicInvocationOperation or
            IFunctionPointerInvocationOperation or
            IObjectCreationOperation or
            IArrayCreationOperation or
            IArrayElementReferenceOperation or
            IPropertyReferenceOperation or
            IEventAssignmentOperation or
            ILockOperation or
            IAwaitOperation or
            IConversionOperation { IsChecked: true } or
            IBinaryOperation
            {
                OperatorKind: BinaryOperatorKind.Divide or
                    BinaryOperatorKind.Remainder
            } or
            IIncrementOrDecrementOperation { IsChecked: true };
    }

    private static bool CanKnownReach(
        INamedTypeSymbol thrown,
        CatchClauseSyntax target,
        TryStatementSyntax @try,
        SemanticModel model)
    {
        foreach (var @catch in @try.Catches)
        {
            if (!CatchesKnownType(@catch, thrown, model))
            {
                continue;
            }
            if (@catch.Span == target.Span)
            {
                return true;
            }
            if (GetFilterSelection(@catch, model) == CatchSelection.Always)
            {
                return false;
            }
        }
        return false;
    }

    private bool CanUnknownReach(
        CatchClauseSyntax target,
        TryStatementSyntax @try,
        SemanticModel model)
    {
        foreach (var @catch in @try.Catches)
        {
            if (@catch.Span == target.Span)
            {
                return true;
            }
            if (CatchesAllExceptions(@catch, model) &&
                GetFilterSelection(@catch, model) == CatchSelection.Always)
            {
                return false;
            }
        }
        return false;
    }

    private static bool CatchesKnownType(
        CatchClauseSyntax @catch,
        INamedTypeSymbol thrown,
        SemanticModel model)
    {
        if (@catch.Declaration == null)
        {
            return true;
        }
        return model.GetTypeInfo(@catch.Declaration.Type).Type is
            INamedTypeSymbol caught &&
            EffectTypeFacts.IsDerivedFrom(thrown, caught);
    }

    private bool CatchesAllExceptions(
        CatchClauseSyntax @catch,
        SemanticModel model)
    {
        return @catch.Declaration == null ||
            _exceptionType != null &&
            SymbolEqualityComparer.Default.Equals(
                model.GetTypeInfo(@catch.Declaration.Type).Type,
                _exceptionType);
    }

    private static CatchSelection GetFilterSelection(
        CatchClauseSyntax @catch,
        SemanticModel model)
    {
        if (@catch.Filter == null)
        {
            return CatchSelection.Always;
        }
        return model.GetConstantValue(@catch.Filter.FilterExpression) switch
        {
            { HasValue: true, Value: true } => CatchSelection.Always,
            { HasValue: true, Value: false } => CatchSelection.Never,
            _ => CatchSelection.Maybe
        };
    }

    private readonly record struct CatchReachability(
        bool Filter,
        bool Handler);

    private readonly record struct PotentialExceptions(
        ImmutableHashSet<INamedTypeSymbol> Known,
        bool Unknown);

    private enum CatchSelection
    {
        Never,
        Maybe,
        Always
    }
}
