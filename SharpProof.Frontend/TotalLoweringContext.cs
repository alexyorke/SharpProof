namespace SharpProof.Frontend;

public sealed class TotalParameterBinding(IParameterSymbol parameter, IrVarId entry, IrVarId current, IrVarId preState)
{
    public IParameterSymbol Parameter { get; } = parameter;
    public IrVarId Entry { get; } = entry;
    public IrVarId Current { get; } = current;
    public IrVarId PreState { get; } = preState;
}

internal enum TotalParameterState { Entry, Current, PreState }

public sealed class GuardedExpression(IrTerm value, IrTerm safeCondition, FrontendSubsetClassification classification)
{
    public IrTerm Value { get; } = value;
    public IrTerm SafeCondition { get; } = safeCondition;
    public FrontendSubsetClassification Classification { get; } = classification;
}

// One callable context is shared by its body and clauses. Entry values are
// immutable inputs; current parameter storage and Old snapshots remain distinct.
public sealed class TotalLoweringContext
{
    private readonly RoslynTypeMapper _types;
    private readonly Dictionary<ISymbol, IrVarId> _locals = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<CaptureId, IrVarId> _captures = [];
    private readonly HashSet<(SyntaxTree Tree, int Start, int Length)> _specificationCalls = [];
    private int _temporary;

    public TotalLoweringContext(IrFactory factory, IMethodSymbol target)
    {
        Factory = ArgumentNullGuard.NotNull(factory, nameof(factory));
        Target = ArgumentNullGuard.NotNull(target, nameof(target));
        if (factory.Semantics != IrExecutionSemantics.Total)
        {
            throw new ArgumentException("Candidate lowering requires Total IR semantics.", nameof(factory));
        }
        _types = new RoslynTypeMapper(factory);
        Parameters = [.. target.Parameters.Select(parameter =>
        {
            var type = _types.GetTypeId(parameter.Type);
            return new TotalParameterBinding(parameter,
                factory.CreateVariable("entry:" + parameter.Ordinal, type),
                factory.CreateVariable("current:" + parameter.Ordinal, type),
                factory.CreateVariable("old:" + parameter.Ordinal, type));
        })];
        Result = target.ReturnsVoid ? null : factory.CreateVariable("result", _types.GetTypeId(target.ReturnType));
    }

    public IrFactory Factory { get; }
    public IMethodSymbol Target { get; }
    public ImmutableArray<TotalParameterBinding> Parameters { get; }
    public IrVarId? Result { get; }
    // The program's initialization reads Entry, so concrete replay must bind
    // these identities themselves before executing the first instruction.
    public ImmutableArray<IrVarId> EntryVariables => [.. Parameters.Select(binding => binding.Entry)];

    internal bool HasScalarSignature => Target.IsStatic && Target.Arity == 0 && !Target.ContainingType.IsGenericType &&
        Target.PartialDefinitionPart == null && Target.PartialImplementationPart == null &&
        !Target.ReturnsByRef && !Target.ReturnsByRefReadonly &&
        Parameters.All(binding => binding.Parameter.RefKind == RefKind.None &&
            CSharpOperationSemantics.IsScalar(binding.Parameter.Type)) &&
        (Target.ReturnsVoid || CSharpOperationSemantics.IsScalar(Target.ReturnType));

    internal bool OwnsBody(IOperation body)
    {
        var model = body.SemanticModel;
        var method = model?.GetDeclaredSymbol(body.Syntax) as IMethodSymbol ??
            model?.GetEnclosingSymbol(body.Syntax.SpanStart) as IMethodSymbol;
        return SymbolEqualityComparer.Default.Equals(Target, method);
    }

    internal IrTypeId Type(ITypeSymbol? type)
    {
        return _types.GetTypeId(type);
    }

    internal IrVarId Variable(ISymbol symbol, TotalParameterState state = TotalParameterState.Current)
    {
        if (symbol is IParameterSymbol parameter)
        {
            var binding = Parameters.Single(item => SymbolEqualityComparer.Default.Equals(item.Parameter, parameter));
            return state switch
            {
                TotalParameterState.Entry => binding.Entry,
                TotalParameterState.PreState => binding.PreState,
                _ => binding.Current
            };
        }
        if (!_locals.TryGetValue(symbol, out var variable))
        {
            var local = (ILocalSymbol)symbol;
            variable = Factory.CreateVariable(local.Name, Type(local.Type));
            _locals.Add(symbol, variable);
        }
        return variable;
    }

    internal IrVarId Capture(CaptureId capture, ITypeSymbol? type)
    {
        if (!_captures.TryGetValue(capture, out var variable))
        {
            variable = Temporary(Type(type));
            _captures.Add(capture, variable);
        }
        return variable;
    }

    internal IrVarId Temporary(IrTypeId type)
    {
        return Factory.CreateVariable("temp:" + _temporary++, type);
    }

    internal OperationId Site(IOperation operation)
    {
        var syntax = operation.Syntax;
        return Factory.CreateOperation(operation.Kind + "@" + syntax.SpanStart,
            new IrSourceSpan(string.IsNullOrEmpty(syntax.SyntaxTree.FilePath) ? "source" : syntax.SyntaxTree.FilePath,
                syntax.SpanStart, syntax.Span.Length));
    }

    internal void ExcludeSpecificationCall(IInvocationOperation invocation)
    {
        var syntax = invocation.Syntax;
        _specificationCalls.Add((syntax.SyntaxTree, syntax.SpanStart, syntax.Span.Length));
    }

    internal bool IsSpecificationOperation(IOperation operation)
    {
        var syntax = operation.Syntax;
        return _specificationCalls.Any(call => call.Tree == syntax.SyntaxTree &&
            call.Start <= syntax.SpanStart && syntax.Span.End <= call.Start + call.Length);
    }

    internal ImmutableArray<FrontendVariableBinding> Variables =>
        [.. Parameters.Select(binding => new FrontendVariableBinding(binding.Parameter, binding.Current))
            .Concat(_locals.Select(pair => new FrontendVariableBinding(pair.Key, pair.Value)))];
    internal ImmutableArray<IrVarId> Captures => [.. _captures.Values.OrderBy(value => value.Value)];
}
