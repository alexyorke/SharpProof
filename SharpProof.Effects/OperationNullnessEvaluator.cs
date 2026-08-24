namespace SharpProof.Effects;

internal sealed class OperationNullnessEvaluator
{
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

    internal bool IsImplicitLockEnterWithNullValue(IInvocationOperation invocation)
    {
        return invocation.IsImplicit &&
            _monitorType != null &&
            SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.ContainingType.OriginalDefinition,
                _monitorType.OriginalDefinition) &&
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

        foreach (var operation in _root.DescendantsAndSelf()
                     .Where(candidate =>
                         candidate.Syntax.SyntaxTree == origin.Syntax.SyntaxTree &&
                         candidate.Syntax.SpanStart >= declaration.Span.End &&
                         candidate.Syntax.SpanStart < origin.Syntax.SpanStart))
        {
            if (operation is IAssignmentOperation assignment &&
                assignment.Target is ILocalReferenceOperation target &&
                SymbolEqualityComparer.Default.Equals(target.Local, local.Local))
            {
                return false;
            }

            if (operation is IArgumentOperation
                {
                    Parameter.RefKind: not RefKind.None
                } argument &&
                argument.Value is ILocalReferenceOperation argumentValue &&
                SymbolEqualityComparer.Default.Equals(argumentValue.Local, local.Local))
            {
                return false;
            }
        }

        return true;
    }

    internal bool IsProvenNonNull(IOperation? value, IOperation access)
    {
        return value == null ||
            value is IInstanceReferenceOperation ||
            value.Type is { IsValueType: true } ||
            DefiniteOperationFacts.IsDefinitelyNonNull(value) ||
            _abstractFlow?.ProvesNonNull(access, value) == true;
    }
}
