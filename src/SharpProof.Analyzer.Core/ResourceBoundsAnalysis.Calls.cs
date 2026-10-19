namespace SharpProof.Analyzer;

internal sealed partial class ResourceBoundsAnalysis
{
    private Bound? ArraySize(IOperation operation)
    {
        operation = ArgumentNullGuard.NotNull(operation, nameof(operation));
        if (operation is IParameterReferenceOperation parameter && parameter.Parameter.Type is IArrayTypeSymbol)
        {
            return _arrayArguments.TryGetValue(parameter.Parameter, out var argument) ? argument :
                new(parameter.Parameter.Name + ".Length", maximum: int.MaxValue);
        }
        if (operation is IArrayCreationOperation { DimensionSizes.Length: 1, Initializer: null } array)
        { return Size(array.DimensionSizes[0]); }
        return null;
    }

    private Cost Call(IInvocationOperation invocation, int depth)
    {
        invocation = ArgumentNullGuard.NotNull(invocation, nameof(invocation));
        var target = invocation.TargetMethod;
        target = target.PartialImplementationPart ?? target;
        if (_activeMethods.Contains(target))
        { return Unknown("RecursiveCostUnknown"); }
        if (_compilation is null || target.DeclaringSyntaxReferences.IsEmpty)
        { return MetadataCall(invocation, depth); }
        if (!target.IsStatic || invocation.Instance is not null || target.IsAsync || target.IsAbstract || target.IsExtern ||
            target.IsGenericMethod || target.ContainingType.IsGenericType || target.ReducedFrom is not null ||
            target.MethodKind != MethodKind.Ordinary || target.ReturnsByRef || target.ReturnsByRefReadonly ||
            (!target.ReturnsVoid && !SupportedType(target.ReturnType)) ||
            target.Parameters.Any(parameter => parameter is null ? throw new ArgumentNullException(nameof(parameter)) :
                parameter.RefKind != RefKind.None || !SupportedType(parameter.Type)))
        { return Unknown("UnsupportedCall"); }
        // A source body alone says nothing about a type initializer that may
        // run on a call. Do not silently omit those allocations or callbacks.
        if (!target.ContainingType.StaticConstructors.IsEmpty)
        { return Unknown("CallTypeInitializationUnknown"); }
        if (target.DeclaringSyntaxReferences.Length != 1 ||
            target.DeclaringSyntaxReferences[0].GetSyntax(_cancellation) is not MethodDeclarationSyntax declaration ||
            !_compilation.SyntaxTrees.Contains(declaration.SyntaxTree))
        { return Unknown("MissingCalleeBody"); }
        var model = Frontend.Host.CompilationModelProvider.GetSemanticModel(_compilation, declaration.SyntaxTree);
        var body = declaration.Body is { } block ? model.GetOperation(block, _cancellation) :
            declaration.ExpressionBody is { } arrow ? model.GetOperation(arrow.Expression, _cancellation) : null;
        if (body is null)
        { return Unknown("MissingCalleeBody"); }
        var argumentCosts = Cost.Zero;
        foreach (var argument in invocation.Arguments)
        {
            argumentCosts = argumentCosts.Add(Visit(argument.Value, depth + 1));
            if (_unknown is not null)
            { return Cost.Zero; }
        }
        // Bind symbols, not parameter names. Costs of argument evaluation are
        // charged once above; a callee's repeated uses only read the size.
        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is not { } parameter)
            { return Unknown("UnboundCallArgument"); }
            _scalarArguments.Add(parameter, Size(argument.Value));
            _arrayArguments.Add(parameter, ArraySize(argument.Value));
        }
        _activeMethods.Add(target);
        try
        { return argumentCosts.Add(CallableBody(target, body, depth + 1)); }
        finally
        {
            _activeMethods.Remove(target);
            foreach (var parameter in target.Parameters)
            {
                _scalarArguments.Remove(parameter);
                _arrayArguments.Remove(parameter);
            }
        }
    }
}
