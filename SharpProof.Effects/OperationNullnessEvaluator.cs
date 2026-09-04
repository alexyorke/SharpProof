namespace SharpProof.Effects;

internal sealed class OperationNullnessEvaluator
{
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
        return value != null &&
            (value.ConstantValue is { HasValue: true, Value: null } ||
             _abstractFlow?.ProvesNull(origin, value) == true ||
             IsSourceDefinitelyNull(value, origin));
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
            var isNull = result.IsDefinitelyNull;
            var isNonNull = result.IsDefinitelyNonNull;
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

        var aliases = new List<ILocalSymbol> { local.Local };
        bool IsAlias(ILocalSymbol candidate)
        {
            return aliases.Any(alias =>
                SymbolEqualityComparer.Default.Equals(alias, candidate));
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

    internal bool IsProvenNonNull(IOperation? value, IOperation access)
    {
        return IsStaticallyNonNull(value) ||
            value is not null &&
            _abstractFlow?.ProvesNonNull(access, value) == true;
    }
}
