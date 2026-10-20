namespace SharpProof.Frontend;

internal sealed partial class TotalLoweringContext
{
    private readonly ImmutableHashSet<CaptureId>.Builder _managedValueReceiverCaptures = ImmutableHashSet.CreateBuilder<CaptureId>();
    private readonly ImmutableDictionary<CaptureId, IrTerm>.Builder _managedValueCaptureLocations = ImmutableDictionary.CreateBuilder<CaptureId, IrTerm>();
    private readonly ImmutableDictionary<IOperation, IrTerm>.Builder _managedValueLocations = ImmutableDictionary.CreateBuilder<IOperation, IrTerm>();

    internal void TrackManagedValueReceivers(ControlFlowGraph graph)
    {
        graph = RepositoryParameterGuard.NotNull(graph, nameof(graph));
        foreach (var block in graph.Blocks)
        {
            foreach (var root in block.Operations.Concat(block.BranchValue is { } branch ? ImmutableArray.Create(branch) : ImmutableArray<IOperation>.Empty))
            {
                foreach (var invocation in root.DescendantsAndSelf().OfType<IInvocationOperation>())
                {
                    if ((IsConstrainedIntegerComparable(invocation) || CSharpOperationSemantics.IsIntegerCompareTo(invocation.TargetMethod)) && IsVariableComparableReceiver(invocation) &&
                        invocation.Instance is IFlowCaptureReferenceOperation capture)
                    { _managedValueReceiverCaptures.Add(capture.Id); }
                }
            }
        }
    }

    internal bool IsManagedValueReceiverCapture(CaptureId capture /* SPINT001: all values valid - every Roslyn capture token is valid */)
    { return _managedValueReceiverCaptures.Contains(capture); }

    internal bool NeedsManagedValueLocation(IOperation operation)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        return operation.Parent is IFlowCaptureOperation capture && IsManagedValueReceiverCapture(capture.Id) ||
            operation.Parent is IInvocationOperation invocation && invocation.Instance == operation &&
            (IsConstrainedIntegerComparable(invocation) || CSharpOperationSemantics.IsIntegerCompareTo(invocation.TargetMethod)) && IsVariableComparableReceiver(invocation);
    }

    internal void RecordManagedValueLocation(IOperation operation, IrTerm location)
    {
        operation = RepositoryParameterGuard.NotNull(operation, nameof(operation));
        location = RepositoryParameterGuard.NotNull(location, nameof(location));
        if (operation.Parent is IFlowCaptureOperation capture)
        { _managedValueCaptureLocations[capture.Id] = location; }
        else
        { _managedValueLocations[operation] = location; }
    }

    internal IrTerm? ManagedValueReceiverLocation(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        if (!IsVariableComparableReceiver(invocation) || invocation.Instance is not { } receiver)
        { return null; }
        if (CapturedStorage(receiver) is { } storage)
        { return Factory.Variable(storage); }
        if (receiver is IParameterReferenceOperation parameter && parameter.Parameter.RefKind == RefKind.None)
        { return Factory.Variable(Variable(parameter.Parameter)); }
        if (receiver is ILocalReferenceOperation { Local.RefKind: RefKind.None } local)
        { return Factory.Variable(Variable(local.Local)); }
        if (receiver is IFlowCaptureReferenceOperation capture && _managedValueCaptureLocations.TryGetValue(capture.Id, out var captured))
        { return captured; }
        return _managedValueLocations.TryGetValue(receiver, out var location) ? location : null;
    }

    internal bool IsVariableComparableReceiver(IInvocationOperation invocation)
    {
        invocation = RepositoryParameterGuard.NotNull(invocation, nameof(invocation));
        if (invocation.Syntax is not Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax
            { Expression: Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax member })
        { return false; }
        Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax expression = member.Expression;
        while (true)
        {
            if (expression is Microsoft.CodeAnalysis.CSharp.Syntax.ParenthesizedExpressionSyntax parenthesized)
            { expression = parenthesized.Expression; }
            else if (expression is Microsoft.CodeAnalysis.CSharp.Syntax.PostfixUnaryExpressionSyntax suppression &&
                suppression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression))
            { expression = suppression.Operand; }
            else
            { break; }
        }
        var model = invocation.SemanticModel ?? (Compilation is { } compilation
            ? Host.CompilationModelProvider.GetSemanticModel(compilation, expression.SyntaxTree) : null);
        var symbol = model?.GetSymbolInfo(expression).Symbol;
        if (symbol is IParameterSymbol parameter)
        { return parameter.RefKind == RefKind.None; }
        if (symbol is ILocalSymbol local)
        { return local.RefKind == RefKind.None && !local.IsConst; }
        if (symbol is IFieldSymbol field)
        {
            if (field.IsConst)
            { return false; }
            return !field.IsReadOnly || Target.MethodKind == MethodKind.Constructor &&
                SymbolEqualityComparer.Default.Equals(Target.ContainingType, field.ContainingType) &&
                (expression is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax or
                    Microsoft.CodeAnalysis.CSharp.Syntax.MemberAccessExpressionSyntax { Expression: Microsoft.CodeAnalysis.CSharp.Syntax.ThisExpressionSyntax });
        }
        return expression is Microsoft.CodeAnalysis.CSharp.Syntax.ElementAccessExpressionSyntax element &&
            model?.GetTypeInfo(element.Expression).Type is IArrayTypeSymbol;
    }
}
