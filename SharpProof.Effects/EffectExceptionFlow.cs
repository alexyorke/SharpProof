using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class EffectExceptionFlow
{
    private static readonly ConditionalWeakTable<
        Compilation, CatchFlowCache> CatchFlows = new();

    internal static EffectThrowSet ResolveThrownException(
        IThrowOperation thrown,
        EffectAnalysisSession session,
        ManagedFlowResult? abstractFlow)
    {
        if (thrown.Exception == null)
        {
            return EffectThrowSet.Unknown;
        }

        if (HasUnencodableExceptionType(thrown))
        {
            return EffectThrowSet.Unknown;
        }

        if (abstractFlow?.ProvesNull(thrown, thrown.Exception) == true)
        {
            return session.ResolveExceptionSet(
                FrameworkTypeMetadataNames.NullReferenceException);
        }

        var exceptions = session.ResolveThrownException(thrown.Exception);
        if (abstractFlow?.ProvesNonNull(thrown, thrown.Exception) == true ||
            DefiniteOperationFacts.IsDefinitelyNonNull(thrown.Exception))
        {
            return exceptions;
        }

        return exceptions.Union(
            session.ResolveExceptionSet(
                FrameworkTypeMetadataNames.NullReferenceException));
    }

    internal static EffectSummary CreateThrowSummary(
        IThrowOperation thrown,
        EffectThrowSet exceptions)
    {
        return EffectSummaryOperations.Throw(
            exceptions,
            !exceptions.IsEmpty && HasUnencodableExceptionType(thrown)
                ? EffectUncertainty.UnsupportedOperation
                : EffectUncertainty.None);
    }

    internal static EffectSummary CreateExceptionConstructionThrowSummary(
        EffectSummary construction,
        IThrowOperation thrown,
        EffectThrowSet exceptions)
    {
        return EffectSummaryOperations.ExceptionConstructionThrow(
            construction,
            exceptions,
            !exceptions.IsEmpty && HasUnencodableExceptionType(thrown)
                ? EffectUncertainty.UnsupportedOperation
                : EffectUncertainty.None);
    }

    private static bool HasUnencodableExceptionType(IThrowOperation thrown)
    {
        var exception = thrown.Exception;
        while (exception is IConversionOperation
            { IsImplicit: true, OperatorMethod: null } conversion)
        {
            exception = conversion.Operand;
        }

        if (exception?.Type is not INamedTypeSymbol named)
        {
            return false;
        }

        return named.TypeKind == TypeKind.Error ||
            DocumentationCommentId.CreateReferenceId(named) is not { Length: > 0 };
    }

    internal static EffectSummary KeepEscaping(
        EffectSummary summary, IOperation origin, Compilation compilation)
    {
        if (summary.IsBottom || summary.Throws.IsEmpty)
        {
            return summary;
        }

        if (!string.Equals(origin.Syntax.Language, LanguageNames.CSharp, StringComparison.Ordinal))
        {
            return summary;
        }

        var escaping = KeepEscaping(summary.Throws, origin.Syntax, compilation);
        return escaping == summary.Throws
            ? summary
            : EffectSummaryOperations.WithThrows(summary, escaping);
    }

    internal static EffectThrowSet KeepEscapingThroughTry(
        EffectThrowSet thrown,
        TryStatementSyntax @try,
        Compilation compilation)
    {
        var known = thrown.Types;
        var includesUnknown = thrown.IncludesUnknown;
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, @try.SyntaxTree);
        ApplyCatches(
            @try,
            model,
            ref known,
            ref includesUnknown,
            includeRethrows: false);
        return EffectThrowSet.Create(known, includesUnknown);
    }

    private static EffectThrowSet KeepEscaping(
        EffectThrowSet thrown, SyntaxNode origin, Compilation compilation)
    {
        var known = thrown.Types;
        var includesUnknown = thrown.IncludesUnknown;
        SemanticModel? model = null;
        // Stop at a lambda or local-function boundary, as ContainsRethrow does.
        // A throw inside a nested callable does not unwind into a try that
        // lexically encloses the callable -- it unwinds wherever the callable is
        // invoked -- so those try statements must not be treated as catching it.
        // Currently a no-op because nested-callable bodies are never scanned;
        // this keeps it sound if that changes.
        foreach (var @try in origin.Ancestors()
                     .TakeWhile(static ancestor =>
                         ancestor is not AnonymousFunctionExpressionSyntax and
                         not LocalFunctionStatementSyntax)
                     .OfType<TryStatementSyntax>())
        {
            if (@try.Catches.Any(@catch => @catch.Filter?.Span.Contains(origin.Span) == true))
            {
                return EffectThrowSet.Empty;
            }

            var inBody = @try.Block.Span.Contains(origin.Span);
            var inHandler = @try.Catches.Any(@catch => @catch.Block.Span.Contains(origin.Span));
            if (inBody)
            {
                model ??= SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(compilation, origin.SyntaxTree);
                ApplyCatches(
                    @try,
                    model,
                    ref known,
                    ref includesUnknown,
                    includeRethrows: true);
            }

            if ((inBody || inHandler) && @try.Finally is { } @finally)
            {
                model ??= SharpProof.Frontend.Host.CompilationModelProvider
                    .GetSemanticModel(compilation, origin.SyntaxTree);
                if (model.AnalyzeControlFlow(@finally.Block) is
                    {
                        Succeeded: true,
                        EndPointIsReachable: false
                    })
                {
                    return EffectThrowSet.Empty;
                }
            }
        }
        return EffectThrowSet.Create(known, includesUnknown);
    }

    private static void ApplyCatches(
        TryStatementSyntax @try, SemanticModel model,
        ref ImmutableArray<INamedTypeSymbol> known,
        ref bool includesUnknown,
        bool includeRethrows)
    {
        var catches = GetCatchFlows(@try, model, includeRethrows);

        known = [.. known.Where(type => CanEscape(catches, type, null))];
        if (includesUnknown)
        {
            includesUnknown = CanEscape(
                catches,
                null,
                model.Compilation.GetTypeByMetadataName(
                    FrameworkTypeMetadataNames.Exception));
        }
    }

    private static ImmutableArray<CatchFlow> GetCatchFlows(
        TryStatementSyntax @try,
        SemanticModel model,
        bool includeRethrows)
    {
        var cache = CatchFlows.GetValue(
            model.Compilation,
            static _ => new CatchFlowCache());
        var selected = includeRethrows
            ? cache.WithRethrows
            : cache.WithoutRethrows;
        return selected.GetOrAdd(
            @try,
            _ => CreateCatchFlows(@try, model, includeRethrows));
    }

    private static ImmutableArray<CatchFlow> CreateCatchFlows(
        TryStatementSyntax @try,
        SemanticModel model,
        bool includeRethrows)
    {
        var exceptionType = model.Compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.Exception);
        return @try.Catches.Select(@catch =>
        {
            var caught = @catch.Declaration == null
                ? exceptionType
                : model.GetTypeInfo(@catch.Declaration.Type).Type as INamedTypeSymbol;
            var filter = GetFilterSelection(@catch.Filter, model);
            return new CatchFlow(
                caught,
                filter,
                includeRethrows && ContainsRethrow(@catch.Block));
        }).ToImmutableArray();
    }

    private static bool CanEscape(
        ImmutableArray<CatchFlow> catches,
        INamedTypeSymbol? thrown,
        INamedTypeSymbol? exceptionType)
    {
        var canReachNext = true;
        var canEscape = false;
        foreach (var @catch in catches)
        {
            var typeSelection = thrown is { } knownType
                ? GetTypeSelection(knownType, @catch.Caught)
                : GetUnknownTypeSelection(@catch.Caught, exceptionType);
            var selection = Combine(typeSelection, @catch.Filter);
            if (selection == CatchSelection.Never)
            {
                continue;
            }

            canEscape |= @catch.ContainsRethrow;
            if (selection == CatchSelection.Always)
            {
                canReachNext = false;
                break;
            }
        }

        return canEscape || canReachNext;
    }

    private static CatchSelection GetUnknownTypeSelection(
        INamedTypeSymbol? caught,
        INamedTypeSymbol? exceptionType)
    {
        return caught != null &&
            exceptionType != null &&
            SymbolEqualityComparer.Default.Equals(caught, exceptionType)
            ? CatchSelection.Always
            : CatchSelection.Maybe;
    }

    private static CatchSelection GetTypeSelection(
        INamedTypeSymbol thrown,
        INamedTypeSymbol? caught)
    {
        if (caught == null)
        {
            return CatchSelection.Maybe;
        }

        return EffectTypeFacts.GetExceptionCatchSelection(thrown, caught);
    }

    private static CatchSelection GetFilterSelection(
        CatchFilterClauseSyntax? filter,
        SemanticModel model)
    {
        if (filter == null)
        {
            return CatchSelection.Always;
        }

        return CatchFilterFacts.GetConstantSelection(filter, model) switch
        {
            true => CatchSelection.Always,
            false => CatchSelection.Never,
            _ => CatchSelection.Maybe
        };
    }

    private static CatchSelection Combine(
        CatchSelection left,
        CatchSelection right)
    {
        if (left == CatchSelection.Never ||
            right == CatchSelection.Never)
        {
            return CatchSelection.Never;
        }

        return left == CatchSelection.Always &&
               right == CatchSelection.Always
            ? CatchSelection.Always
            : CatchSelection.Maybe;
    }

    private static bool ContainsRethrow(BlockSyntax block)
    {
        return block.DescendantNodes().Any(node =>
            node is ThrowStatementSyntax { Expression: null } &&
            node.Ancestors()
                .TakeWhile(static ancestor =>
                    ancestor is not AnonymousFunctionExpressionSyntax and
                    not LocalFunctionStatementSyntax)
                .OfType<CatchClauseSyntax>()
                .FirstOrDefault()?.Block == block);
    }

    private readonly record struct CatchFlow(
        INamedTypeSymbol? Caught,
        CatchSelection Filter,
        bool ContainsRethrow);

    private sealed class CatchFlowCache
    {
        internal readonly ConcurrentDictionary<
            TryStatementSyntax, ImmutableArray<CatchFlow>> WithoutRethrows =
            new(ReferenceComparer<TryStatementSyntax>.Instance);

        internal readonly ConcurrentDictionary<
            TryStatementSyntax, ImmutableArray<CatchFlow>> WithRethrows =
            new(ReferenceComparer<TryStatementSyntax>.Instance);
    }

}
