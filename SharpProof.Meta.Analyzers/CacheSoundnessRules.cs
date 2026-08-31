using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
        var root = Root(invocation);
        if (!WriteMethods.Contains(invocation.TargetMethod.Name) ||
            !IsCacheReceiver(
                invocation.Instance,
                invocation.TargetMethod.ContainingType,
                root,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)) ||
            !invocation.Arguments.Any(argument =>
                !IsGuardedCacheableResponse(invocation, argument.Value) &&
                IsNonCacheableSemanticAnswer(
                    argument.Value,
                    root,
                    new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default))))
        {
            return;
        }

        Report(context, invocation.Syntax.GetLocation());
    }

    internal static void AnalyzeAssignment(OperationAnalysisContext context)
    {
        var assignment = (IAssignmentOperation)context.Operation;
        var root = Root(assignment);
        if (!IsCacheAssignmentTarget(assignment.Target, root) ||
            !IsNonCacheableSemanticAnswer(
                assignment.Value,
                root,
                new HashSet<ILocalSymbol>(SymbolEqualityComparer.Default)))
        {
            return;
        }

        Report(context, assignment.Syntax.GetLocation());
    }

    private static bool IsCacheAssignmentTarget(
        IOperation target,
        IOperation root)
    {
        var resolving = new HashSet<ILocalSymbol>(
            SymbolEqualityComparer.Default);
        return target switch
        {
            IPropertyReferenceOperation property => IsCacheReceiver(
                property.Instance,
                property.Property.ContainingType,
                root,
                resolving),
            IFieldReferenceOperation field => IsCacheReceiver(
                field.Instance,
                field.Field.ContainingType,
                root,
                resolving),
            _ => false
        };
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

    private static bool IsGuardedCacheableResponse(
        IInvocationOperation write,
        IOperation value)
    {
        if (!IsWorkerVerifyResponse(value.Type))
        {
            return false;
        }

        for (var current = write.Parent; current != null; current = current.Parent)
        {
            if (current is IConditionalOperation conditional &&
                IsDescendantOf(write, conditional.WhenTrue) &&
                HasCacheableGuard(conditional.Condition, value))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCacheableGuard(
        IOperation condition,
        IOperation value)
    {
        condition = UnwrapValue(condition);

        if (condition is IInvocationOperation
            {
                TargetMethod:
                {
                    IsStatic: true,
                    Name: "IsCacheable",
                    ContainingType: { } containingType
                }
            } invocation &&
            IsCacheType(containingType) &&
            invocation.Arguments.FirstOrDefault(argument =>
                argument.Parameter?.Ordinal == 0) is { } response &&
            IsSameValue(response.Value, value))
        {
            return true;
        }

        return condition is IBinaryOperation
        {
            OperatorKind: BinaryOperatorKind.ConditionalAnd
        } binary &&
            (HasCacheableGuard(binary.LeftOperand, value) ||
             HasCacheableGuard(binary.RightOperand, value));
    }

    private static bool IsSameValue(IOperation left, IOperation right)
    {
        left = UnwrapValue(left);
        right = UnwrapValue(right);
        return left switch
        {
            ILocalReferenceOperation local
                when right is ILocalReferenceOperation other =>
                SymbolEqualityComparer.Default.Equals(
                    local.Local,
                    other.Local),
            IParameterReferenceOperation parameter
                when right is IParameterReferenceOperation other =>
                SymbolEqualityComparer.Default.Equals(
                    parameter.Parameter,
                    other.Parameter),
            _ => false
        };
    }

    private static IOperation UnwrapValue(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation
                { OperatorMethod: null } conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private static bool IsDescendantOf(
        IOperation operation,
        IOperation ancestor)
    {
        for (var current = operation; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCacheReceiver(
        IOperation? operation,
        ITypeSymbol? fallbackType,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        if (IsCacheType(fallbackType) || IsCacheType(operation?.Type))
        {
            return true;
        }

        operation = operation == null ? null : UnwrapValue(operation);
        if (IsCacheType(operation?.Type))
        {
            return true;
        }

        return operation switch
        {
            ILocalReferenceOperation local =>
                ResolveCacheLocal(local, root, resolving),
            IConditionalOperation conditional =>
                IsCacheReceiver(
                    conditional.WhenTrue,
                    null,
                    root,
                    resolving) ||
                conditional.WhenFalse != null &&
                IsCacheReceiver(
                    conditional.WhenFalse,
                    null,
                    root,
                    resolving),
            ICoalesceOperation coalesce =>
                IsCacheReceiver(coalesce.Value, null, root, resolving) ||
                IsCacheReceiver(coalesce.WhenNull, null, root, resolving),
            _ => false
        };
    }

    private static bool ResolveCacheLocal(
        ILocalReferenceOperation reference,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        if (!resolving.Add(reference.Local))
        {
            return false;
        }

        try
        {
            return GetReachingLocalValues(reference, root).Any(value =>
                IsCacheReceiver(value, null, root, resolving));
        }
        finally
        {
            resolving.Remove(reference.Local);
        }
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
        IOperation operation,
        IOperation root,
        HashSet<ILocalSymbol> resolving)
    {
        operation = UnwrapValue(operation);
        return operation switch
        {
            IFieldReferenceOperation field
                when field.Field.ContainingType.TypeKind == TypeKind.Enum &&
                     IsSemanticAnswerType(field.Type) => IsNonCacheableName(field.Field.Name),
            IObjectCreationOperation creation =>
                IsSemanticAnswerType(creation.Type) && IsNonCacheableName(creation.Type?.Name),
            ILocalReferenceOperation local => ResolveLocal(
                local,
                root,
                resolving),
            IConditionalOperation conditional =>
                IsNonCacheableSemanticAnswer(
                    conditional.WhenTrue,
                    root,
                    resolving) ||
                conditional.WhenFalse != null &&
                IsNonCacheableSemanticAnswer(
                    conditional.WhenFalse,
                    root,
                    resolving),
            ISwitchExpressionOperation switchExpression =>
                switchExpression.Arms.Any(arm =>
                    IsNonCacheableSemanticAnswer(
                        arm.Value,
                        root,
                        resolving)),
            ICoalesceOperation coalesce =>
                IsNonCacheableSemanticAnswer(
                    coalesce.Value,
                    root,
                    resolving) ||
                IsNonCacheableSemanticAnswer(
                    coalesce.WhenNull,
                    root,
                    resolving),
            IPropertyReferenceOperation property => ResolveProperty(property),
            IInvocationOperation invocation => ResolveInvocation(invocation),
            _ => IsSemanticAnswerType(operation.Type) &&
                operation.ConstantValue is not { HasValue: true }
        };
    }

    private static bool ResolveLocal(
        ILocalReferenceOperation reference,
        IOperation root,
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
                IsNonCacheableSemanticAnswer(
                    value,
                    root,
                    resolving));
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
                    IsSameLocalReference(candidate, reference))));
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
                    null,
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
                reference,
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
        ILocalReferenceOperation? before,
        IOperation root)
    {
        var result = new HashSet<IOperation>(input);
        foreach (var candidate in BlockOperations(block)
                     .SelectMany(operation =>
                         InEvaluationOrder(operation, root)))
        {
            if (before != null &&
                IsSameLocalReference(candidate, before))
            {
                break;
            }

            var value = GetLocalWriteValue(candidate, local);
            if (value == null)
            {
                continue;
            }

            result.Clear();
            result.Add(value);
        }
        return result;
    }

    private static IEnumerable<IOperation> InEvaluationOrder(
        IOperation operation,
        IOperation root)
    {
        var pending = new Stack<(IOperation Operation, bool ChildrenVisited)>();
        pending.Push((operation, false));
        while (pending.Count != 0)
        {
            var (current, childrenVisited) = pending.Pop();
            if (IsInsideNestedCallable(current, root))
            {
                continue;
            }

            if (childrenVisited)
            {
                yield return current;
                continue;
            }

            pending.Push((current, true));
            foreach (var child in current.ChildOperations.Reverse())
            {
                pending.Push((child, false));
            }
        }
    }

    private static bool IsSameLocalReference(
        IOperation candidate,
        ILocalReferenceOperation reference)
    {
        return candidate is ILocalReferenceOperation local &&
            SymbolEqualityComparer.Default.Equals(
                local.Local,
                reference.Local) &&
            candidate.Syntax.SyntaxTree == reference.Syntax.SyntaxTree &&
            candidate.Syntax.Span == reference.Syntax.Span;
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
            IInvocationOperation invocation =>
                GetRefOrOutWriteValue(invocation, local),
            IDeconstructionAssignmentOperation deconstruction =>
                GetDeconstructionWriteValue(deconstruction, local),
            _ => null
        };
    }

    private static IOperation? GetRefOrOutWriteValue(
        IInvocationOperation invocation,
        ILocalSymbol local)
    {
        if (invocation.TargetMethod.ReducedFrom is
            { Parameters.Length: > 0 } reduced &&
            IsWritableReference(reduced.Parameters[0].RefKind) &&
            FindLocalReference(invocation.Instance, local) is
            { } receiver)
        {
            return receiver;
        }

        foreach (var argument in invocation.Arguments)
        {
            if (argument.Parameter is { } parameter &&
                IsWritableReference(parameter.RefKind) &&
                FindLocalReference(argument.Value, local) is { } reference)
            {
                return reference;
            }
        }

        return null;
    }

    private static bool IsWritableReference(RefKind refKind)
    {
        return refKind is RefKind.Ref or RefKind.Out;
    }

    private static IOperation? GetDeconstructionWriteValue(
        IDeconstructionAssignmentOperation assignment,
        ILocalSymbol local)
    {
        return GetDeconstructionWriteValue(
            UnwrapValue(assignment.Target),
            UnwrapValue(assignment.Value),
            local);
    }

    private static IOperation? GetDeconstructionWriteValue(
        IOperation target,
        IOperation value,
        ILocalSymbol local)
    {
        if (target is ITupleOperation targetTuple &&
            value is ITupleOperation valueTuple &&
            targetTuple.Elements.Length == valueTuple.Elements.Length)
        {
            for (var index = 0; index < targetTuple.Elements.Length; index++)
            {
                var result = GetDeconstructionWriteValue(
                    UnwrapValue(targetTuple.Elements[index]),
                    UnwrapValue(valueTuple.Elements[index]),
                    local);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        return FindLocalReference(target, local) == null
            ? null
            : value;
    }

    private static ILocalReferenceOperation? FindLocalReference(
        IOperation? operation,
        ILocalSymbol local)
    {
        return operation?.DescendantsAndSelf()
            .OfType<ILocalReferenceOperation>()
            .FirstOrDefault(reference =>
                SymbolEqualityComparer.Default.Equals(
                    reference.Local,
                    local));
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
        return IsNonCacheableReturnedValue(
            property.Property,
            property.Type,
            property.Property.Name);
    }

    private static bool ResolveInvocation(IInvocationOperation invocation)
    {
        return IsNonCacheableReturnedValue(
            invocation.TargetMethod,
            invocation.Type,
            invocation.TargetMethod.Name);
    }

    private static bool IsNonCacheableReturnedValue(
        ISymbol symbol,
        ITypeSymbol? returnType,
        string fallbackName)
    {
        var names = GetReturnedValueNames(
            symbol,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default));
        return names.Length == 0
            ? IsSemanticAnswerType(returnType) &&
              IsNonCacheableName(fallbackName)
            : names.Any(IsNonCacheableName);
    }

    private static ImmutableArray<string> GetReturnedValueNames(
        ISymbol symbol,
        HashSet<ISymbol> resolving)
    {
        if (!resolving.Add(symbol))
        {
            return ["Unknown"];
        }

        try
        {
            var names = ImmutableArray.CreateBuilder<string>();
            foreach (var reference in symbol.DeclaringSyntaxReferences)
            {
                var syntax = reference.GetSyntax();
                foreach (var expression in GetReturnExpressions(syntax))
                {
                    names.AddRange(GetExpressionValueNames(
                        expression,
                        symbol,
                        syntax,
                        resolving,
                        new HashSet<string>(StringComparer.Ordinal)));
                }
            }
            return names.ToImmutable();
        }
        finally
        {
            resolving.Remove(symbol);
        }
    }

    private static IEnumerable<ExpressionSyntax> GetReturnExpressions(
        SyntaxNode syntax)
    {
        return syntax.DescendantNodesAndSelf()
            .OfType<ArrowExpressionClauseSyntax>()
            .Select(static arrow => arrow.Expression)
            .Concat(syntax.DescendantNodesAndSelf()
                .OfType<ReturnStatementSyntax>()
                .Where(static statement => statement.Expression != null)
                .Select(static statement => statement.Expression!))
            .Where(expression => !IsInsideNestedCallable(expression, syntax));
    }

    private static ImmutableArray<string> GetExpressionValueNames(
        ExpressionSyntax expression,
        ISymbol owner,
        SyntaxNode syntax,
        HashSet<ISymbol> resolving,
        HashSet<string> resolvingNames)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        switch (expression)
        {
            case ParenthesizedExpressionSyntax parenthesized:
                names.AddRange(GetExpressionValueNames(
                    parenthesized.Expression,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case CastExpressionSyntax cast:
                names.AddRange(GetExpressionValueNames(
                    cast.Expression,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case ConditionalExpressionSyntax conditional:
                names.AddRange(GetExpressionValueNames(
                    conditional.WhenTrue,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                names.AddRange(GetExpressionValueNames(
                    conditional.WhenFalse,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case SwitchExpressionSyntax switchExpression:
                foreach (var arm in switchExpression.Arms)
                {
                    names.AddRange(GetExpressionValueNames(
                        arm.Expression,
                        owner,
                        syntax,
                        resolving,
                        resolvingNames));
                }
                break;
            case BinaryExpressionSyntax binary
                when binary.IsKind(SyntaxKind.CoalesceExpression):
                names.AddRange(GetExpressionValueNames(
                    binary.Left,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                names.AddRange(GetExpressionValueNames(
                    binary.Right,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case IdentifierNameSyntax identifier:
                names.AddRange(GetIdentifierValueNames(
                    identifier,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
                break;
            case MemberAccessExpressionSyntax member:
                names.Add(member.Name.Identifier.ValueText);
                names.AddRange(GetMemberValueNames(
                    member.Name.Identifier.ValueText,
                    owner,
                    resolving));
                break;
            case InvocationExpressionSyntax invocation:
                var name = GetInvokedName(invocation.Expression);
                if (name != null)
                {
                    names.Add(name);
                    names.AddRange(GetMemberValueNames(name, owner, resolving));
                }
                break;
            case ObjectCreationExpressionSyntax creation:
                names.Add(creation.Type.ToString());
                break;
        }
        return names.ToImmutable();
    }

    private static ImmutableArray<string> GetIdentifierValueNames(
        IdentifierNameSyntax identifier,
        ISymbol owner,
        SyntaxNode syntax,
        HashSet<ISymbol> resolving,
        HashSet<string> resolvingNames)
    {
        var name = identifier.Identifier.ValueText;
        if (!resolvingNames.Add(name))
        {
            return ["Unknown"];
        }

        try
        {
            var values = syntax.DescendantNodes()
                .Where(candidate =>
                    candidate.SpanStart < identifier.SpanStart &&
                    !IsInsideNestedCallable(candidate, syntax))
                .Select(candidate => GetSyntacticLocalWrite(candidate, name))
                .Where(static value => value != null)
                .Cast<ExpressionSyntax>()
                .ToArray();
            if (values.Length == 0)
            {
                return [name];
            }

            var names = ImmutableArray.CreateBuilder<string>();
            foreach (var value in values)
            {
                names.AddRange(GetExpressionValueNames(
                    value,
                    owner,
                    syntax,
                    resolving,
                    resolvingNames));
            }
            return names.ToImmutable();
        }
        finally
        {
            resolvingNames.Remove(name);
        }
    }

    private static ImmutableArray<string> GetMemberValueNames(
        string name,
        ISymbol owner,
        HashSet<ISymbol> resolving)
    {
        if (owner.ContainingType == null)
        {
            return [];
        }

        var names = ImmutableArray.CreateBuilder<string>();
        foreach (var member in owner.ContainingType.GetMembers(name))
        {
            if (member is IMethodSymbol or IPropertySymbol)
            {
                names.AddRange(GetReturnedValueNames(member, resolving));
            }
        }
        return names.ToImmutable();
    }

    private static string? GetInvokedName(ExpressionSyntax expression)
    {
        return expression switch
        {
            SimpleNameSyntax simple => simple.Identifier.ValueText,
            MemberAccessExpressionSyntax member =>
                member.Name.Identifier.ValueText,
            MemberBindingExpressionSyntax binding =>
                binding.Name.Identifier.ValueText,
            _ => null
        };
    }

    private static ExpressionSyntax? GetSyntacticLocalWrite(
        SyntaxNode syntax,
        string name)
    {
        return syntax switch
        {
            VariableDeclaratorSyntax declarator
                when declarator.Identifier.ValueText == name =>
                declarator.Initializer?.Value,
            AssignmentExpressionSyntax assignment
                when assignment.Left is IdentifierNameSyntax identifier &&
                     identifier.Identifier.ValueText == name =>
                assignment.Right,
            _ => null
        };
    }

    private static bool IsInsideNestedCallable(
        SyntaxNode node,
        SyntaxNode root)
    {
        return node.Ancestors()
            .TakeWhile(ancestor => !ReferenceEquals(ancestor, root))
            .Any(static ancestor => ancestor is
                AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
    }

    private static bool IsSemanticAnswerType(ITypeSymbol? type)
    {
        if (type == null || !IsSharpProofNamespace(type.ContainingNamespace))
        {
            return false;
        }
        return type.Name.IndexOf("Answer", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Result", StringComparison.Ordinal) >= 0 ||
               type.Name.IndexOf("Outcome", StringComparison.Ordinal) >= 0 ||
               IsWorkerVerifyResponse(type);
    }

    private static bool IsWorkerVerifyResponse(ITypeSymbol? type)
    {
        return type != null &&
               string.Equals(
                   type.Name,
                   "WorkerVerifyResponse",
                   StringComparison.Ordinal) &&
               IsExactNamespace(
                   type.ContainingNamespace,
                   "SharpProof",
                   "Worker",
                   "Protocol");
    }

    private static bool IsExactNamespace(
        INamespaceSymbol? value,
        params string[] expected)
    {
        var current = value;
        for (var index = expected.Length - 1; index >= 0; index--)
        {
            if (current == null ||
                current.IsGlobalNamespace ||
                !string.Equals(
                    current.Name,
                    expected[index],
                    StringComparison.Ordinal))
            {
                return false;
            }

            current = current.ContainingNamespace;
        }

        return current?.IsGlobalNamespace == true;
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
