using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SharpProof.Specs;

namespace SharpProof.Effects;

/// <summary>
/// Projects a source summary to the synchronous effects of invoking deferred
/// async and iterator methods. Call-site recognition uses syntax because CFG
/// operation copies do not retain complete <see cref="IOperation.Parent"/>
/// chains.
/// </summary>
internal sealed class DeferredInvocationEffectPolicy
{
    private const int MaximumSyntaxScanNodes = 4096;
    private const int MaximumTransparentParentheses = 256;

    private readonly IMethodSymbol _caller;
    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol? _task;
    private readonly INamedTypeSymbol? _genericTask;
    private readonly INamedTypeSymbol? _valueTask;
    private readonly INamedTypeSymbol? _genericValueTask;
    private readonly ResolvedApiSpecTable _apiSpecs;
    private readonly Dictionary<IMethodSymbol, bool> _iterators =
        new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IMethodSymbol, bool> _synchronousInitializers =
        new(SymbolEqualityComparer.Default);
    private HashSet<ILocalSymbol>? _usedLocals;
    private bool _usedLocalsComplete = true;

    internal DeferredInvocationEffectPolicy(
        Compilation compilation,
        IMethodSymbol caller,
        ResolvedApiSpecTable apiSpecs)
    {
        _compilation = compilation;
        _caller = caller;
        _apiSpecs = apiSpecs;
        _task = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.Task);
        _genericTask = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.TaskOfT);
        _valueTask = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ValueTask);
        _genericValueTask = compilation.GetTypeByMetadataName(
            FrameworkTypeMetadataNames.ValueTaskOfT);
    }

    internal EffectSummary Project(
        EffectCallSite call,
        EffectSummary summary)
    {
        if (IsIterator(call.Target))
        {
            return HasSynchronousTypeInitialization(call.Target) ||
                !IsIteratorResultUnused(call.Origin.Syntax)
                    ? summary
                    : EffectSummaryOperations.Allocate(
                        EffectAllocationKind.Managed);
        }

        return IsAsyncTaskLike(call.Target) &&
            !HasSynchronousTypeInitialization(call.Target) &&
            IsTaskResultUnobserved(call.Origin.Syntax)
                ? EffectSummaryOperations.Join(
                    EffectSummaryOperations.WithThrows(
                        summary,
                        EffectThrowSet.Empty),
                    EffectSummaryOperations.Allocate(
                        EffectAllocationKind.Managed))
                : summary;
    }

    private bool IsAsyncTaskLike(IMethodSymbol method)
    {
        return method.IsAsync &&
            method.ReturnType is INamedTypeSymbol returnType &&
            IsTaskType(returnType);
    }

    private bool IsTaskType(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition;
        return Matches(original, _task) ||
            Matches(original, _genericTask) ||
            Matches(original, _valueTask) ||
            Matches(original, _genericValueTask);
    }

    private static bool Matches(
        INamedTypeSymbol actual,
        INamedTypeSymbol? expected)
    {
        return expected != null && SymbolEqualityComparer.Default.Equals(
            actual,
            expected);
    }

    private bool IsIterator(IMethodSymbol method)
    {
        if (_iterators.TryGetValue(method, out var cached))
        {
            return cached;
        }

        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            var visited = 0;
            foreach (var node in declaration.DescendantNodes(
                    node => ReferenceEquals(node, declaration) ||
                        node is not (LocalFunctionStatementSyntax or
                            AnonymousFunctionExpressionSyntax)))
            {
                if (++visited > MaximumSyntaxScanNodes)
                {
                    _iterators[method] = false;
                    return false;
                }
                if (node is YieldStatementSyntax)
                {
                    _iterators[method] = true;
                    return true;
                }
            }
        }

        _iterators[method] = false;
        return false;
    }

    private bool HasSynchronousTypeInitialization(IMethodSymbol method)
    {
        if (_synchronousInitializers.TryGetValue(method, out var cached))
        {
            return cached;
        }

        var result = EffectMethodNodeBuilder.CanTriggerOwnTypeInitialization(
                method) &&
            EffectMethodNodeBuilder.HasPotentialStaticInitialization(
                method.ContainingType,
                _apiSpecs);
        _synchronousInitializers[method] = result;
        return result;
    }

    private bool IsTaskResultUnobserved(SyntaxNode origin)
    {
        var value = LiftParentheses(origin);
        switch (value.Parent)
        {
            case ReturnStatementSyntax returnStatement:
                return SameExpression(returnStatement.Expression, value);
            case ArrowExpressionClauseSyntax arrow:
                return SameExpression(arrow.Expression, value);
            default:
                return IsIteratorResultUnused(value);
        }
    }

    private bool IsIteratorResultUnused(SyntaxNode origin)
    {
        var value = LiftParentheses(origin);
        switch (value.Parent)
        {
            case ExpressionStatementSyntax statement:
                return SameExpression(statement.Expression, value);
            case EqualsValueClauseSyntax equals
                when SameExpression(equals.Value, value) &&
                     equals.Parent is VariableDeclaratorSyntax declarator:
                return IsLocalNeverReferenced(declarator);
            case AssignmentExpressionSyntax assignment
                when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                     assignment.Left is IdentifierNameSyntax discard &&
                     discard.Identifier.ValueText == "_" &&
                     IsDiscardIdentifier(discard) &&
                     SameExpression(assignment.Right, value):
                return assignment.Parent is ExpressionStatementSyntax;
            default:
                return false;
        }
    }

    private static bool SameExpression(
        ExpressionSyntax? expression,
        SyntaxNode value)
    {
        return expression != null && ReferenceEquals(
            StripParentheses(expression),
            StripParentheses((ExpressionSyntax)value));
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        for (var depth = 0;
             depth < MaximumTransparentParentheses &&
             expression is ParenthesizedExpressionSyntax parenthesized;
             depth++)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static SyntaxNode LiftParentheses(SyntaxNode syntax)
    {
        for (var depth = 0;
             depth < MaximumTransparentParentheses &&
             syntax.Parent is ParenthesizedExpressionSyntax parenthesized;
             depth++)
        {
            syntax = parenthesized;
        }

        return syntax;
    }

    private bool IsLocalNeverReferenced(VariableDeclaratorSyntax declarator)
    {
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_compilation, declarator.SyntaxTree);
        if (model.GetDeclaredSymbol(declarator) is not ILocalSymbol local)
        {
            return false;
        }

        _usedLocals ??= FindUsedLocals();
        return _usedLocalsComplete && !_usedLocals.Contains(local);
    }

    private bool IsDiscardIdentifier(IdentifierNameSyntax identifier)
    {
        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_compilation, identifier.SyntaxTree);
        return model.GetSymbolInfo(identifier).Symbol == null;
    }

    private HashSet<ILocalSymbol> FindUsedLocals()
    {
        var used = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var visited = 0;
        foreach (var reference in _caller.DeclaringSyntaxReferences)
        {
            var declaration = reference.GetSyntax();
            var model = SharpProof.Frontend.Host.CompilationModelProvider
                .GetSemanticModel(_compilation, declaration.SyntaxTree);
            foreach (var node in declaration.DescendantNodes())
            {
                if (++visited > MaximumSyntaxScanNodes)
                {
                    _usedLocalsComplete = false;
                    return used;
                }
                if (node is IdentifierNameSyntax identifier &&
                    model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local)
                {
                    used.Add(local);
                }
            }
        }

        return used;
    }
}
