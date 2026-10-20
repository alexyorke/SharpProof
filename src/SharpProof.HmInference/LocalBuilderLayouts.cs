using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.HmInference;

public static partial class Inference
{
    private sealed partial class Session
    {
        private bool InferLocalLayoutCalls(Compilation compilation, MethodDeclarationSyntax proposal, Dictionary<int, INamedTypeSymbol> states,
            int[] active, Dictionary<HmType, string> variables)
        {
            if (compilation is null)
            { throw new ArgumentNullException(nameof(compilation)); }
            if (proposal is null)
            { throw new ArgumentNullException(nameof(proposal)); }
            if (states is null)
            { throw new ArgumentNullException(nameof(states)); }
            if (active is null)
            { throw new ArgumentNullException(nameof(active)); }
            if (variables is null)
            { throw new ArgumentNullException(nameof(variables)); }
            var semantic = compilation.GetSemanticModel(proposal.SyntaxTree);
            var root = semantic.GetDeclaredSymbol(proposal, cancellationToken)
                ?? throw new InferenceException("Cannot resolve generic layout caller.");
            var functions = new List<(SyntaxNode Node, IMethodSymbol Symbol)>();
            var reachable = ReachableLocalLayoutCalls(semantic, proposal);
            var calls = reachable.Calls;
            foreach (var node in proposal.DescendantNodes())
            {
                cancellationToken.ThrowIfCancellationRequested();
                IMethodSymbol? function = node is LocalFunctionStatementSyntax local ? semantic.GetDeclaredSymbol(local, cancellationToken)
                    : node is AnonymousFunctionExpressionSyntax && semantic.GetOperation(node, cancellationToken) is IAnonymousFunctionOperation anonymous ? anonymous.Symbol : null;
                if (function is { IsAsync: true })
                { functions.Add((node, function)); }
            }
            var required = new HashSet<ITypeParameterSymbol>(SymbolEqualityComparer.Default);
            foreach (var index in active)
            {
                if (index < 0 || index >= functions.Count || !states.TryGetValue(index, out var state))
                { throw new InferenceException("Generic local state-machine correlation changed."); }
                if (!reachable.Functions.Contains(functions[index].Symbol.OriginalDefinition))
                { continue; }
                var frame = new List<ITypeParameterSymbol>();
                var owners = new Stack<INamedTypeSymbol>();
                for (var owner = root.ContainingType; owner is not null; owner = owner.ContainingType)
                { owners.Push(owner); }
                foreach (var owner in owners)
                { frame.AddRange(owner.TypeParameters); }
                frame.AddRange(root.TypeParameters);
                foreach (var local in functions[index].Node.AncestorsAndSelf().OfType<LocalFunctionStatementSyntax>().Reverse())
                { frame.AddRange((semantic.GetDeclaredSymbol(local, cancellationToken) ?? throw new InferenceException("Unresolved local generic frame.")).TypeParameters); }
                var containers = new Stack<INamedTypeSymbol>();
                for (var owner = state; owner is not null; owner = owner.ContainingType)
                { containers.Push(owner); }
                var parameters = containers.SelectMany(owner =>
                { if (owner is null) { throw new ArgumentNullException(nameof(owner)); } return owner.TypeParameters; }).ToArray();
                if (parameters.Length != frame.Count)
                { throw new InferenceException("Emitted generic layout frame differs from source."); }
                var mapping = new Dictionary<ITypeParameterSymbol, ITypeParameterSymbol>(SymbolEqualityComparer.Default);
                for (var i = 0; i < parameters.Length; i++)
                { mapping.Add(parameters[i], frame[i]); }
                InspectUnmanagedStorage(state, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default), parameter =>
                {
                    if (parameter is null)
                    { throw new ArgumentNullException(nameof(parameter)); }
                    if (!mapping.TryGetValue(parameter, out var source))
                    { throw new InferenceException("Unresolved emitted generic storage parameter."); }
                    required.Add(source);
                });
            }
            var visited = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
            int previous;
            do
            {
                previous = required.Count;
                foreach (var call in calls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var definition = call.OriginalDefinition;
                    for (var i = 0; i < definition.TypeParameters.Length; i++)
                    {
                        if (!required.Contains(definition.TypeParameters[i]))
                        { continue; }
                        InspectUnmanagedStorage(call.TypeArguments[i], visited, parameter =>
                        { if (parameter is null) { throw new ArgumentNullException(nameof(parameter)); } required.Add(parameter); });
                    }
                }
            } while (previous != required.Count);
            var added = false;
            foreach (var variable in variables)
            {
                var parameter = root.TypeParameters.Single(item =>
                { if (item is null) { throw new ArgumentNullException(nameof(item)); } return item.Name == variable.Value; });
                if (!required.Contains(parameter) || inferredConstraints.Any(requirement =>
                    { if (requirement.Variable is null) { throw new ArgumentException("Expected a constraint owner.", nameof(requirement)); } return ReferenceEquals(ConversionCore(requirement.Variable), variable.Key.Root) && requirement.Keyword == "unmanaged"; }))
                { continue; }
                RequireKeyword(variable.Key, "unmanaged");
                added = true;
            }
            return added;
        }

        private (List<IMethodSymbol> Calls, HashSet<IMethodSymbol> Functions) ReachableLocalLayoutCalls(SemanticModel semantic, MethodDeclarationSyntax proposal)
        {
            if (semantic is null)
            { throw new ArgumentNullException(nameof(semantic)); }
            if (proposal is null)
            { throw new ArgumentNullException(nameof(proposal)); }
            var calls = new List<IMethodSymbol>();
            var graphs = new HashSet<ControlFlowGraph>();
            var functions = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            void VisitGraph(ControlFlowGraph graph)
            {
                if (graph is null)
                { throw new ArgumentNullException(nameof(graph)); }
                cancellationToken.ThrowIfCancellationRequested();
                if (!graphs.Add(graph))
                { return; }
                foreach (var block in graph.Blocks)
                {
                    if (!block.IsReachable)
                    { continue; }
                    foreach (var operation in block.Operations)
                    { VisitOperation(operation, graph); }
                    if (block.BranchValue is { } branch)
                    { VisitOperation(branch, graph); }
                }
            }
            void VisitOperation(IOperation operation, ControlFlowGraph graph)
            {
                if (operation is null)
                { throw new ArgumentNullException(nameof(operation)); }
                if (graph is null)
                { throw new ArgumentNullException(nameof(graph)); }
                cancellationToken.ThrowIfCancellationRequested();
                if (operation is INameOfOperation)
                { return; }
                if (operation is IFlowAnonymousFunctionOperation anonymous)
                {
                    functions.Add(anonymous.Symbol.OriginalDefinition);
                    VisitGraph(graph.GetAnonymousFunctionControlFlowGraph(anonymous, cancellationToken));
                    return;
                }
                var selected = operation switch
                {
                    IInvocationOperation invocation => invocation.TargetMethod,
                    IMethodReferenceOperation reference => reference.Method,
                    _ => null
                };
                if (selected is { MethodKind: MethodKind.LocalFunction })
                {
                    if (selected.IsGenericMethod)
                    { calls.Add(selected); }
                    var definition = selected.OriginalDefinition;
                    if (functions.Add(definition))
                    {
                        ControlFlowGraph? owner = graph;
                        while (owner is not null && !owner.LocalFunctions.Any(function =>
                        {
                            if (function is null)
                            { throw new ArgumentNullException(nameof(function)); }
                            return SymbolEqualityComparer.Default.Equals(function.OriginalDefinition, definition);
                        }))
                        { owner = owner.Parent; }
                        if (owner is null)
                        { throw new InferenceException("Cannot correlate local layout control flow."); }
                        VisitGraph(owner.GetLocalFunctionControlFlowGraph(definition, cancellationToken));
                    }
                }
                foreach (var child in operation.ChildOperations)
                { VisitOperation(child, graph); }
            }
            VisitGraph(ControlFlowGraph.Create(proposal, semantic, cancellationToken)
                ?? throw new InferenceException("Cannot inspect local layout control flow."));
            return (calls, functions);
        }
    }
}
