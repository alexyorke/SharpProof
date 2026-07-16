using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using SharpProof.Symbolic.Ir;

namespace SharpProof.Symbolic;

internal readonly record struct SymbolicMutationInventoryEntry(
    SyntaxNode Source,
    ExpressionSyntax? Target,
    ExpressionSyntax? Exposure);

internal sealed class SymbolicMutationInventory(
    SyntaxNode root,
    SemanticModel semanticModel,
    CancellationToken cancellationToken,
    ImmutableArray<SymbolicMutationInventoryEntry> entries)
{
    internal static SymbolicMutationInventory Create(
        SyntaxNode root, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var entries = ImmutableArray.CreateBuilder<SymbolicMutationInventoryEntry>();
        foreach (var node in CSharpSyntaxFacts.DescendantNodesInExecution(root))
        {
            if (SymbolMutationFacts.TryGetMutationTarget(node, out var target))
                entries.Add(new(node, target, null));
            switch (node)
            {
                case InvocationExpressionSyntax invocation:
                    if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
                        entries.Add(new(node, null, memberAccess.Expression));
                    foreach (var argument in invocation.ArgumentList.Arguments)
                        entries.Add(new(node, null, argument.Expression));
                    break;
                case ObjectCreationExpressionSyntax { ArgumentList: { } arguments }:
                    foreach (var argument in arguments.Arguments)
                        entries.Add(new(node, null, argument.Expression));
                    break;
            }
        }
        return new(root, semanticModel, cancellationToken, entries.ToImmutable());
    }

    internal bool MutatesSymbol(ISymbol symbol) =>
        entries.Any(entry => entry.Target is { } target && References(target, symbol));

    internal bool MutatesAny(IReadOnlyCollection<ISymbol> symbols, bool exactTargets = false) =>
        symbols.Count != 0 && entries.Any(entry => entry.Target is { } target &&
            (exactTargets ? ExactTargetMatchesAny(target, symbols) : symbols.Any(symbol => References(target, symbol))));

    internal bool ExposesSymbol(ISymbol symbol, bool mutableOnly) =>
        (!mutableOnly || IsMutableReference(SymbolicFactFactory.GetTrackedSymbolType(symbol))) &&
        entries.Any(entry => entry.Exposure is { } exposure && References(exposure, symbol));

    internal bool InvalidatesSymbol(ISymbol symbol, bool mutableExposures) =>
        MutatesSymbol(symbol) || ExposesSymbol(symbol, mutableExposures);

    internal IEnumerable<SyntaxNode> MutationSources(ISymbol symbol) =>
        entries.Where(entry => entry.Target is { } target && References(target, symbol))
            .Select(static entry => entry.Source);

    internal bool MutatesBetween(int after, int before, ISymbol symbol) =>
        entries.Any(entry => entry.Target is { } target &&
            !ReferenceEquals(entry.Source, root) && entry.Source.SpanStart > after &&
            entry.Source.SpanStart < before && References(target, symbol));

    internal SymbolicNestedMutationInvalidationPlan ToInvalidationPlan()
    {
        var steps = ImmutableArray.CreateBuilder<SymbolicMutationInvalidationStep>();
        var unsupported = false;
        foreach (var entry in entries)
        {
            if (entry.Target is { } target)
            {
                var targets = LowerTargetInvalidations(target, semanticModel, cancellationToken);
                if (targets.IsDefaultOrEmpty) unsupported = true;
                else steps.Add(new(targets, target.Span, "operation-transfer.mutation-invalidation"));
                continue;
            }
            if (entry.Exposure is not { } exposure) continue;
            foreach (var symbol in SymbolMutationFacts.GetReferencedLocalAndParameterSymbols(
                         exposure, semanticModel, cancellationToken))
                if (IsMutableReference(SymbolicFactFactory.GetTrackedSymbolType(symbol)))
                    steps.Add(new(
                        ImmutableArray.Create(ForSymbol(symbol)), entry.Source.Span,
                        "operation-transfer.reference-invalidation"));
        }
        return new(steps.ToImmutable(), unsupported);
    }

    internal bool TryCollectLocalOrParameterInvalidations(
        ISet<string> keys, ImmutableArray<SymbolicInvalidationTarget>.Builder targets)
    {
        foreach (var entry in entries)
        {
            if (entry.Target == null) continue;
            if (!SymbolMutationFacts.TryGetLocalOrParameterSymbol(
                    entry.Target, semanticModel, cancellationToken, out var symbol))
                return false;
            var key = SymbolicFactFactory.GetSmtVariableName(symbol);
            if (keys.Add(key)) targets.Add(new(key));
        }
        return true;
    }

    internal static ImmutableArray<SymbolicInvalidationTarget> LowerTargetInvalidations(
        ExpressionSyntax target, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var invalidations = ImmutableArray.CreateBuilder<SymbolicInvalidationTarget>();
        var symbol = GetMutatedSymbol(target, semanticModel, cancellationToken);
        if (symbol is ILocalSymbol or IParameterSymbol)
            invalidations.Add(ForSymbol(symbol));
        else if (symbol is IFieldSymbol or IPropertySymbol &&
                 SymbolicStateInvalidator.IsCurrentInstanceMemberReference(target, semanticModel, cancellationToken))
            invalidations.Add(new(
                SymbolicStateValueFacts.ImplicitThisVariableName + "." + symbol.Name,
                SymbolicInvalidationMatchKind.VariableOrMember));

        var receiver = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(target) switch
        {
            ElementAccessExpressionSyntax element => element.Expression,
            MemberAccessExpressionSyntax member => member.Expression,
            _ => null
        };
        var receiverSymbol = receiver == null ? null : semanticModel.GetSymbolInfo(
            CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(receiver),
            cancellationToken).Symbol?.OriginalDefinition;
        if (receiverSymbol is ILocalSymbol or IParameterSymbol)
            invalidations.Add(ForSymbol(receiverSymbol));
        return invalidations.ToImmutable();
    }

    private bool ExactTargetMatchesAny(ExpressionSyntax target, IReadOnlyCollection<ISymbol> symbols)
    {
        target = CSharpSyntaxFacts.UnwrapParenthesesAndNullableSuppression(target);
        if (target is TupleExpressionSyntax tuple)
            return tuple.Arguments.Any(argument => symbols.Any(symbol => References(argument.Expression, symbol)));
        var targetSymbol = GetMutatedSymbol(target, semanticModel, cancellationToken)?.OriginalDefinition;
        return targetSymbol != null && symbols.Any(symbol =>
            SymbolEqualityComparer.Default.Equals(symbol.OriginalDefinition, targetSymbol));
    }

    private bool References(SyntaxNode node, ISymbol symbol) =>
        SymbolMutationFacts.ExpressionReferencesSymbol(node, symbol, semanticModel, cancellationToken);

    internal static ISymbol? GetMutatedSymbol(
        ExpressionSyntax target, SemanticModel semanticModel, CancellationToken cancellationToken)
    {
        var symbol = semanticModel.GetSymbolInfo(target, cancellationToken).Symbol;
        if (symbol != null) return SymbolicStateInvalidator.NormalizeMutatedSymbol(symbol);
        return semanticModel.GetOperation(target, cancellationToken) switch
        {
            IFieldReferenceOperation field => field.Field,
            IPropertyReferenceOperation property => property.Property,
            _ => null
        };
    }

    private static SymbolicInvalidationTarget ForSymbol(ISymbol symbol) =>
        new(SymbolicFactFactory.GetSmtVariableName(symbol.OriginalDefinition));

    private static bool IsMutableReference(ITypeSymbol? type) =>
        type is IArrayTypeSymbol || type?.IsReferenceType == true && type.SpecialType != SpecialType.System_String;
}
