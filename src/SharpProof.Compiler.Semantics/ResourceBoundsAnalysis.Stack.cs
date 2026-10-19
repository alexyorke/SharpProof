namespace SharpProof.Compiler.Semantics;

internal sealed partial class ResourceBoundsAnalysis
{
    private Cost CallableBody(IMethodSymbol method, IOperation body, int depth)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        body = ArgumentNullGuard.NotNull(body, nameof(body));
        return WithActivation(method, body, CallableBodyCore(method, body, depth));
    }

    private Cost WithActivation(IMethodSymbol method, IOperation? body, Cost cost)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        cost = ArgumentNullGuard.NotNull(cost, nameof(cost));
        return new(cost.LoopIterations, cost.Arrays, cost.Elements, ResourceProgram.Activate(ActivationShape(method, body), cost.Program));
    }

    private ResourceActivationShape? ActivationShape(IMethodSymbol method, IOperation? body)
    {
        method = ArgumentNullGuard.NotNull(method, nameof(method));
        if (!method.IsStatic && !method.ContainingType.IsReferenceType)
        { return null; }
        var variables = ImmutableArray.CreateBuilder<ResourceStorageKind>();
        foreach (var parameter in method.Parameters)
        {
            var storage = FieldStorage(parameter.Type);
            if (parameter.RefKind != RefKind.None || storage == ResourceStorageKind.Unspecified)
            { return null; }
            variables.Add(storage);
        }
        var pending = new Stack<IOperation>();
        if (body != null)
        { pending.Push(body); }
        var locals = new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default);
        var remaining = ResourceExpression.MaximumNodes;
        while (pending.Count != 0)
        {
            _cancellation.ThrowIfCancellationRequested();
            if (--remaining < 0)
            { return null; }
            var current = pending.Pop();
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            { continue; }
            if (current is IVariableDeclaratorOperation variable && locals.Add(variable.Symbol))
            {
                var storage = FieldStorage(variable.Symbol.Type);
                if (variable.Symbol.RefKind != RefKind.None || storage == ResourceStorageKind.Unspecified)
                { return null; }
                variables.Add(storage);
            }
            if (variables.Count > ResourceActivationShape.MaximumVariables)
            { return null; }
            foreach (var child in current.ChildOperations)
            { pending.Push(child); }
        }
        if (variables.Count > ResourceActivationShape.MaximumVariables)
        { return null; }
        return new(variables.ToImmutable(), !method.IsStatic, !method.ReturnsVoid);
    }
}
