using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SharpProof.Effects;

internal sealed class OperationNullnessEvaluator
{
    internal readonly record struct NullProofs(bool IsNull, bool IsNonNull);

    internal enum NullState
    {
        Unknown,
        Null,
        NonNull
    }

    private readonly ManagedFlowResult? _abstractFlow;
    private readonly INamedTypeSymbol? _monitorType;
    private readonly IOperation _root;
    private readonly EffectAnalysisSession _session;

    internal OperationNullnessEvaluator(
        EffectAnalysisSession session,
        IOperation root,
        ManagedFlowResult? abstractFlow,
        INamedTypeSymbol? monitorType)
    {
        _session = session;
        _root = root;
        _abstractFlow = abstractFlow;
        _monitorType = monitorType;
    }

    internal bool IsProvenNull(IOperation? value, IOperation origin)
    {
        var hasDeconstructionAssignment = value != null &&
            HasDeconstructionAssignmentBefore(value, origin);
        return value != null &&
            (value.ConstantValue is { HasValue: true, Value: null } ||
             (!hasDeconstructionAssignment &&
              _abstractFlow?.ProvesNull(origin, value) == true) ||
             IsSourceDefinitelyNull(value, origin));
    }

    internal NullProofs GetNullProofs(IOperation? value, IOperation origin)
    {
        var isNonNull = IsStaticallyNonNull(value);
        if (value == null)
        {
            return new(IsNull: false, IsNonNull: isNonNull);
        }

        var isNull = value.ConstantValue is { HasValue: true, Value: null };
        var hasDeconstructionAssignment =
            HasDeconstructionAssignmentBefore(value, origin);
        if (_abstractFlow?.TryEvaluate(origin, value, out var result) == true)
        {
            isNonNull |= !hasDeconstructionAssignment &&
                result.IsDefinitelyNonNull;
            isNull |= !hasDeconstructionAssignment &&
                result.IsDefinitelyNull;
        }

        if (!isNull)
        {
            isNull = IsSourceDefinitelyNull(value, origin);
        }

        return new(isNull, isNonNull);
    }

    internal NullState GetNullState(IOperation? value, IOperation origin)
    {
        if (IsStaticallyNonNull(value))
        {
            return NullState.NonNull;
        }

        if (TryGetAbstractNullState(
                value,
                origin,
                preferNull: false,
                out var state))
        {
            return state;
        }

        return value != null &&
            (value.ConstantValue is { HasValue: true, Value: null } ||
             IsSourceDefinitelyNull(value, origin))
            ? NullState.Null
            : NullState.Unknown;
    }

    internal NullState GetNullStatePreferNull(
        IOperation? value,
        IOperation origin)
    {
        if (value != null &&
            value.ConstantValue is { HasValue: true, Value: null })
        {
            return NullState.Null;
        }

        if (TryGetAbstractNullState(
                value,
                origin,
                preferNull: true,
                out var state))
        {
            return state;
        }

        if (value != null && IsSourceDefinitelyNull(value, origin))
        {
            return NullState.Null;
        }

        return IsStaticallyNonNull(value)
            ? NullState.NonNull
            : NullState.Unknown;
    }

    private bool TryGetAbstractNullState(
        IOperation? value,
        IOperation origin,
        bool preferNull,
        out NullState state)
    {
        if (value != null &&
            _abstractFlow?.TryEvaluate(origin, value, out var result) == true)
        {
            var hasDeconstructionAssignment =
                HasDeconstructionAssignmentBefore(value, origin);
            var isNull = result.IsDefinitelyNull &&
                !hasDeconstructionAssignment;
            var isNonNull = result.IsDefinitelyNonNull &&
                !hasDeconstructionAssignment;
            if (preferNull ? isNull : isNonNull)
            {
                state = preferNull ? NullState.Null : NullState.NonNull;
                return true;
            }

            if (preferNull ? isNonNull : isNull)
            {
                state = preferNull ? NullState.NonNull : NullState.Null;
                return true;
            }
        }

        state = NullState.Unknown;
        return false;
    }

    private static bool IsStaticallyNonNull(IOperation? value)
    {
        return value == null ||
            value is IInstanceReferenceOperation ||
            (value.Type is { IsValueType: true } type &&
             !ManagedAbstractValue.IsNullableType(type)) ||
            DefiniteOperationFacts.IsDefinitelyNonNull(value);
    }

    internal bool IsImplicitLockEnterWithNullValue(IInvocationOperation invocation)
    {
        return invocation.IsImplicit &&
            MonitorFacts.IsMonitorMethod(invocation.TargetMethod, _monitorType) &&
            invocation.TargetMethod.Name == "Enter" &&
            invocation.Arguments.Length != 0 &&
            IsProvenNull(invocation.Arguments[0].Value, invocation);
    }

    private bool IsSourceDefinitelyNull(IOperation value, IOperation origin)
    {
        if (value is not ILocalReferenceOperation local ||
            local.Local.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var declaration = local.Local.DeclaringSyntaxReferences[0]
            .GetSyntax();
        if (declaration.SyntaxTree != origin.Syntax.SyntaxTree ||
            declaration.SpanStart >= origin.Syntax.SpanStart)
        {
            return false;
        }

        var model = SharpProof.Frontend.Host.CompilationModelProvider
            .GetSemanticModel(_session.Compilation, declaration.SyntaxTree);
        var declarationOperation = model.GetOperation(declaration);
        var initializer = declarationOperation?.DescendantsAndSelf()
            .OfType<IVariableDeclaratorOperation>()
            .FirstOrDefault(declarator =>
                SymbolEqualityComparer.Default.Equals(
                    declarator.Symbol, local.Local))?.Initializer?.Value;
        if (initializer?.ConstantValue is not { HasValue: true, Value: null })
        {
            return false;
        }

        // The source-order fallback has no way to account for an assignment
        // that reaches the origin through a loop or a backward goto. Let the
        // caller's conservative nullness path handle these control-flow
        // shapes instead of treating the declaration's initializer as still
        // current.
        if (CanReachOriginThroughBackEdge(origin))
        {
            return false;
        }

        var aliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)
        {
            local.Local
        };
        bool IsAlias(ILocalSymbol candidate)
        {
            return aliases.Contains(candidate);
        }

        foreach (var operation in _root.DescendantsAndSelf()
                     .Where(candidate =>
                         candidate.Syntax.SyntaxTree == origin.Syntax.SyntaxTree &&
                         candidate.Syntax.SpanStart >= declaration.Span.End &&
                         candidate.Syntax.SpanStart < origin.Syntax.SpanStart)
                     .OrderBy(static candidate => candidate.Syntax.SpanStart)
                     .ThenByDescending(static candidate => candidate.Syntax.Span.Length))
        {
            if (operation is IVariableDeclaratorOperation
                {
                    Symbol.RefKind: RefKind.Ref,
                    Initializer.Value: { } aliasInitializer
                } aliasDeclarator &&
                DefiniteOperationFacts.UnwrapHarmlessValue(aliasInitializer)
                    is ILocalReferenceOperation aliasedLocal &&
                IsAlias(aliasedLocal.Local))
            {
                aliases.Add(aliasDeclarator.Symbol);
                continue;
            }

            if (operation is IAssignmentOperation assignment &&
                DefiniteOperationFacts.UnwrapHarmlessValue(assignment.Target)
                    is ILocalReferenceOperation target &&
                IsAlias(target.Local))
            {
                return false;
            }

            if (operation is IDeconstructionAssignmentOperation deconstruction &&
                deconstruction.Target.DescendantsAndSelf()
                    .OfType<ILocalReferenceOperation>()
                    .Any(reference => IsAlias(reference.Local)))
            {
                return false;
            }

            if (operation is IArgumentOperation
                {
                    Parameter.RefKind: not RefKind.None
                } argument &&
                DefiniteOperationFacts.UnwrapHarmlessValue(argument.Value)
                    is ILocalReferenceOperation argumentValue &&
                IsAlias(argumentValue.Local))
            {
                return false;
            }

            if (operation is IInvocationOperation
                {
                    TargetMethod.MethodKind: MethodKind.LocalFunction
                })
            {
                return false;
            }
        }

        return true;
    }

    private bool HasDeconstructionAssignmentBefore(
        IOperation value,
        IOperation origin)
    {
        if (value is not ILocalReferenceOperation local ||
            local.Local.DeclaringSyntaxReferences.Length != 1)
        {
            return false;
        }

        var declaration = local.Local.DeclaringSyntaxReferences[0]
            .GetSyntax();
        if (declaration.SyntaxTree != origin.Syntax.SyntaxTree ||
            declaration.SpanStart >= origin.Syntax.SpanStart)
        {
            return false;
        }

        var aliases = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)
        {
            local.Local
        };
        foreach (var operation in _root.DescendantsAndSelf()
                     .Where(candidate =>
                         candidate.Syntax.SyntaxTree == origin.Syntax.SyntaxTree &&
                         candidate.Syntax.SpanStart >= declaration.Span.End &&
                         candidate.Syntax.SpanStart < origin.Syntax.SpanStart)
                     .OrderBy(static candidate => candidate.Syntax.SpanStart)
                     .ThenByDescending(static candidate => candidate.Syntax.Span.Length))
        {
            if (operation is IVariableDeclaratorOperation
                {
                    Symbol.RefKind: RefKind.Ref,
                    Initializer.Value: { } aliasInitializer
                } aliasDeclarator &&
                DefiniteOperationFacts.UnwrapHarmlessValue(aliasInitializer)
                    is ILocalReferenceOperation aliasedLocal &&
                aliases.Contains(aliasedLocal.Local))
            {
                aliases.Add(aliasDeclarator.Symbol);
                continue;
            }

            if (operation is IDeconstructionAssignmentOperation deconstruction &&
                deconstruction.Target.DescendantsAndSelf()
                    .OfType<ILocalReferenceOperation>()
                    .Any(reference => aliases.Contains(reference.Local)))
            {
                return true;
            }
        }

        return false;
    }

    private bool CanReachOriginThroughBackEdge(IOperation origin)
    {
        if (_root.Syntax.SyntaxTree != origin.Syntax.SyntaxTree)
        {
            return false;
        }

        var originSpan = origin.Syntax.Span;
        if (_root.DescendantsAndSelf()
                .OfType<ILoopOperation>()
                .Any(loop =>
                    loop.Syntax.SyntaxTree == origin.Syntax.SyntaxTree &&
                    loop.Syntax.Span.Contains(originSpan)))
        {
            return true;
        }

        return _root.DescendantsAndSelf()
            .OfType<IBranchOperation>()
            .Where(branch => branch.Syntax is GotoStatementSyntax)
            .Any(branch =>
                branch.Syntax.SyntaxTree == origin.Syntax.SyntaxTree &&
                branch.Syntax.SpanStart > originSpan.Start &&
                branch.Target.DeclaringSyntaxReferences.Any(reference =>
                    reference.SyntaxTree == origin.Syntax.SyntaxTree &&
                    reference.GetSyntax().SpanStart < originSpan.Start));
    }

    internal bool IsProvenNonNull(IOperation? value, IOperation access)
    {
        return IsStaticallyNonNull(value) ||
            value is not null &&
            !HasDeconstructionAssignmentBefore(value, access) &&
            _abstractFlow?.ProvesNonNull(access, value) == true;
    }
}
