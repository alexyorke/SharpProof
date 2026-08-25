using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace SharpProof.Meta.Analyzers;

internal static class CacheSoundnessRules
{
    private static readonly ImmutableHashSet<string> WriteMethods =
        new[] { "Add", "AddOrUpdate", "GetOrAdd", "Set", "TryAdd", "TryUpdate", "TryWrite", "TryWriteAsync", "Write", "WriteAsync" }
            .ToImmutableHashSet(StringComparer.Ordinal);

    internal static void AnalyzeWrite(OperationAnalysisContext context, IInvocationOperation invocation)
    {
        if (!WriteMethods.Contains(invocation.TargetMethod.Name) ||
            !IsCacheType(invocation.Instance?.Type ?? invocation.TargetMethod.ContainingType) ||
            !invocation.Arguments.Any(argument => IsNonCacheableSemanticAnswer(
                argument.Value, Root(argument.Value),
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default))))
        {
            return;
        }

        Report(context, invocation.Syntax.GetLocation());
    }

    internal static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        if (assignment.Target is not IPropertyReferenceOperation property ||
            !IsCacheType(property.Instance?.Type ?? property.Property.ContainingType) ||
            !IsNonCacheableSemanticAnswer(
                assignment.Value, Root(assignment),
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)))
        {
            return;
        }

        Report(context, assignment.Syntax.GetLocation());
    }

    private static void Report(OperationAnalysisContext context, Location? location)
    {
        context.ReportDiagnostic(Diagnostic.Create(
            MetaDiagnosticDescriptors.NonCacheableSemanticAnswer, location));
    }

    private static bool IsCacheType(ITypeSymbol? type)
    {
        return type?.Name.IndexOf("Cache", StringComparison.Ordinal) >= 0;
    }

    private static IOperation Root(IOperation operation)
    {
        while (operation.Parent != null)
        {
            operation = operation.Parent;
        }
        return operation;
    }

    private static bool IsNonCacheableSemanticAnswer(
        IOperation operation, IOperation root, HashSet<ILocalSymbol> resolving)
    {
        operation = operation switch
        {
            IConversionOperation conversion when conversion.OperatorMethod == null => conversion.Operand,
            IParenthesizedOperation parenthesized => parenthesized.Operand,
            _ => operation
        };
        return operation switch
        {
            IFieldReferenceOperation field
                when field.Field.ContainingType.TypeKind == TypeKind.Enum &&
                     IsSemanticAnswerType(field.Type) => IsNonCacheableName(field.Field.Name),
            IObjectCreationOperation creation =>
                IsSemanticAnswerType(creation.Type) && IsNonCacheableName(creation.Type?.Name),
            ILocalReferenceOperation local => ResolveLocal(local, root, resolving),
            IConditionalOperation conditional =>
                IsNonCacheableSemanticAnswer(conditional.WhenTrue, root, resolving) ||
                conditional.WhenFalse != null &&
                IsNonCacheableSemanticAnswer(conditional.WhenFalse, root, resolving),
            IPropertyReferenceOperation property => ResolveProperty(property),
            IInvocationOperation invocation => ResolveInvocation(invocation),
            _ => IsSemanticAnswerType(operation.Type) &&
                operation.ConstantValue is not { HasValue: true }
        };
    }

    private static bool ResolveLocal(
        ILocalReferenceOperation reference, IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        if (!resolving.Add(reference.Local))
        {
            return true;
        }
        try
        {
            var writes = GetReachingLocalValues(reference, root);
            if (writes.Length == 0)
            {
                return IsSemanticAnswerType(reference.Type);
            }
            return writes.Any(value =>
                IsNonCacheableSemanticAnswer(value, root, resolving));
        }
        finally
        {
            resolving.Remove(reference.Local);
        }
    }

    private static IOperation[] GetReachingLocalValues(
        ILocalReferenceOperation reference,
        IOperation root)
    {
        var graph = CreateControlFlowGraph(root);
        if (graph == null)
        {
            return GetPriorLocalValues(reference, root);
        }

        var target = graph.Blocks.FirstOrDefault(block =>
            BlockOperations(block).Any(operation =>
                operation.DescendantsAndSelf().Any(candidate =>
                    candidate is ILocalReferenceOperation local &&
                    SymbolEqualityComparer.Default.Equals(
                        local.Local,
                        reference.Local) &&
                    candidate.Syntax.SyntaxTree == reference.Syntax.SyntaxTree &&
                    candidate.Syntax.Span == reference.Syntax.Span)));
        if (target == null)
        {
            return GetPriorLocalValues(reference, root);
        }

        var outputs = graph.Blocks.ToDictionary(
            static block => block.Ordinal,
            static _ => new HashSet<IOperation>());
        bool changed;
        do
        {
            changed = false;
            foreach (var block in graph.Blocks.Where(static block => block.IsReachable))
            {
                var input = new HashSet<IOperation>();
                foreach (var predecessor in block.Predecessors)
                {
                    input.UnionWith(outputs[predecessor.Source.Ordinal]);
                }
                var output = TransferLocalValues(
                    block,
                    reference.Local,
                    input,
                    int.MaxValue,
                    root);
                if (!outputs[block.Ordinal].SetEquals(output))
                {
                    outputs[block.Ordinal] = output;
                    changed = true;
                }
            }
        }
        while (changed);

        var reaching = new HashSet<IOperation>();
        foreach (var predecessor in target.Predecessors)
        {
            reaching.UnionWith(outputs[predecessor.Source.Ordinal]);
        }
        return TransferLocalValues(
                target,
                reference.Local,
                reaching,
                reference.Syntax.SpanStart,
                root)
            .ToArray();
    }

    private static ControlFlowGraph? CreateControlFlowGraph(IOperation root)
    {
        try
        {
            return root switch
            {
                IMethodBodyOperation method => ControlFlowGraph.Create(method),
                IConstructorBodyOperation constructor =>
                    ControlFlowGraph.Create(constructor),
                IBlockOperation block => ControlFlowGraph.Create(block),
                _ => null
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    private static HashSet<IOperation> TransferLocalValues(
        BasicBlock block,
        ILocalSymbol local,
        IEnumerable<IOperation> input,
        int before,
        IOperation root)
    {
        var result = new HashSet<IOperation>(input);
        foreach (var value in BlockOperations(block)
                     .SelectMany(static operation =>
                         operation.DescendantsAndSelf())
                     .Where(candidate =>
                         candidate.Syntax.SpanStart < before &&
                         !IsInsideNestedCallable(candidate, root))
                     .Select(candidate => GetLocalWriteValue(candidate, local))
                     .Where(static value => value != null)
                     .Cast<IOperation>()
                     .OrderBy(static value => value.Syntax.SpanStart))
        {
            result.Clear();
            result.Add(value);
        }
        return result;
    }

    private static IEnumerable<IOperation> BlockOperations(BasicBlock block)
    {
        return block.Operations.Concat(
            block.BranchValue == null ? [] : [block.BranchValue]);
    }

    private static IOperation? GetLocalWriteValue(
        IOperation candidate,
        ILocalSymbol local)
    {
        return candidate switch
        {
            IVariableDeclaratorOperation declarator
                when SymbolEqualityComparer.Default.Equals(
                    declarator.Symbol,
                    local) => declarator.Initializer?.Value,
            ISimpleAssignmentOperation
            { Target: ILocalReferenceOperation target } assignment
                when SymbolEqualityComparer.Default.Equals(
                    target.Local,
                    local) => assignment.Value,
            _ => null
        };
    }

    private static IOperation[] GetPriorLocalValues(
        ILocalReferenceOperation reference,
        IOperation root)
    {
        return root.DescendantsAndSelf()
            .Where(candidate =>
                candidate.Syntax.SpanStart < reference.Syntax.SpanStart &&
                !IsInsideNestedCallable(candidate, root))
            .Select(candidate => GetLocalWriteValue(candidate, reference.Local))
            .Where(static value => value != null)
            .Cast<IOperation>()
            .ToArray();
    }

    private static bool IsInsideNestedCallable(IOperation operation, IOperation root)
    {
        for (var current = operation.Parent; current != null && !ReferenceEquals(current, root);
             current = current.Parent)
        {
            if (current is IAnonymousFunctionOperation or ILocalFunctionOperation)
            {
                return true;
            }
        }
        return false;
    }

    private static bool ResolveProperty(IPropertyReferenceOperation property)
    {
        var values = GetReturnedValueNames(property.Property).ToArray();
        return values.Length == 0
            ? IsSemanticAnswerType(property.Type) && IsNonCacheableName(property.Property.Name)
            : values.Any(IsNonCacheableName);
    }

    private static bool ResolveInvocation(IInvocationOperation invocation)
    {
        var values = GetReturnedValueNames(invocation.TargetMethod).ToArray();
        return values.Length == 0
            ? IsSemanticAnswerType(invocation.Type) && IsNonCacheableName(invocation.TargetMethod.Name)
            : values.Any(IsNonCacheableName);
    }

    private static IEnumerable<string> GetReturnedValueNames(ISymbol symbol)
    {
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var syntax = reference.GetSyntax();
            foreach (var expression in syntax.DescendantNodesAndSelf()
                         .OfType<ArrowExpressionClauseSyntax>()
                         .Select(static arrow => arrow.Expression)
                         .Concat(syntax.DescendantNodesAndSelf()
                             .OfType<ReturnStatementSyntax>()
                             .Where(static statement => statement.Expression != null)
                             .Select(static statement => statement.Expression!))
                         .Where(expression => !expression.Ancestors()
                             .TakeWhile(ancestor => !ReferenceEquals(ancestor, syntax))
                             .Any(static ancestor => ancestor is
                                 AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)))
            {
                var name = expression switch
                {
                    MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    ObjectCreationExpressionSyntax creation => creation.Type.ToString(),
                    _ => null
                };
                if (name != null)
                {
                    yield return name;
                }
            }
        }
    }

    private static bool IsSemanticAnswerType(ITypeSymbol? type)
    {
        if (type == null || !IsSharpProofNamespace(type.ContainingNamespace))
        {
            return false;
        }
        return type.Name.IndexOf("Answer", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Result", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Outcome", StringComparison.Ordinal) >= 0;
    }

    private static bool IsSharpProofNamespace(INamespaceSymbol? symbol)
    {
        while (symbol is { IsGlobalNamespace: false, ContainingNamespace: { } parent } &&
               !parent.IsGlobalNamespace)
        {
            symbol = parent;
        }
        return string.Equals(symbol?.Name, "SharpProof", StringComparison.Ordinal);
    }

    private static bool IsNonCacheableName(string? name)
    {
        return name != null &&
               (string.Equals(name, "Unknown", StringComparison.Ordinal) ||
                name.IndexOf("Timeout", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("TimedOut", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Error", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failure", StringComparison.Ordinal) >= 0 ||
                name.IndexOf("Failed", StringComparison.Ordinal) >= 0);
    }
}
