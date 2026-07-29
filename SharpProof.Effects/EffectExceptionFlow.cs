using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal static class EffectExceptionFlow
{
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

    internal static EffectThrowSet ResolveRethrow(IOperation operation)
    {
        for (var current = operation.Parent; current != null; current = current.Parent)
        {
            if (current is ICatchClauseOperation @catch)
            {
                return @catch.ExceptionType is INamedTypeSymbol type
                    ? EffectThrowSet.Create([type])
                    : EffectThrowSet.Unknown;
            }
        }

        return EffectThrowSet.Unknown;
    }

    private static EffectThrowSet KeepEscaping(
        EffectThrowSet thrown, SyntaxNode origin, Compilation compilation)
    {
        var known = thrown.Types;
        var includesUnknown = thrown.IncludesUnknown;
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(compilation, origin.SyntaxTree);
        foreach (var @try in origin.Ancestors().OfType<TryStatementSyntax>())
        {
            if (@try.Catches.Any(@catch => @catch.Filter?.Span.Contains(origin.Span) == true))
            {
                return EffectThrowSet.Empty;
            }

            var inBody = @try.Block.Span.Contains(origin.Span);
            var inHandler = @try.Catches.Any(@catch => @catch.Block.Span.Contains(origin.Span));
            if (inBody)
            {
                ApplyCatches(@try, model, ref known, ref includesUnknown);
            }

            if ((inBody || inHandler) &&
                @try.Finally is { } @finally &&
                model.AnalyzeControlFlow(@finally.Block) is
                {
                    Succeeded: true,
                    EndPointIsReachable: false
                })
            {
                return EffectThrowSet.Empty;
            }
        }
        return EffectThrowSet.Create(known, includesUnknown);
    }

    private static void ApplyCatches(
        TryStatementSyntax @try, SemanticModel model,
        ref ImmutableArray<INamedTypeSymbol> known, ref bool includesUnknown)
    {
        var exceptionType = model.Compilation.GetTypeByMetadataName(FrameworkTypeMetadataNames.Exception);
        foreach (var @catch in @try.Catches)
        {
            var caught = @catch.Declaration == null
                ? exceptionType
                : model.GetTypeInfo(@catch.Declaration.Type).Type as INamedTypeSymbol;
            if (caught == null ||
                @catch.Filter is { } filter &&
                model.GetConstantValue(filter.FilterExpression) is not { HasValue: true, Value: true } ||
                ContainsRethrow(@catch.Block))
            {
                continue;
            }

            known = [.. known.Where(type => !EffectTypeFacts.IsDerivedFrom(type, caught))];
            if (includesUnknown && @catch.Filter == null &&
                SymbolEqualityComparer.Default.Equals(caught, exceptionType))
            {
                includesUnknown = false;
            }
        }
    }

    private static bool ContainsRethrow(BlockSyntax block)
    {
        return block.DescendantNodes().Any(node =>
            node is ThrowStatementSyntax { Expression: null } &&
            !node.Ancestors()
                .TakeWhile(ancestor => !ReferenceEquals(ancestor, block))
                .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));
    }
}
